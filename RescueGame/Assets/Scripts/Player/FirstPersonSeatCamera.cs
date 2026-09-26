using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person camera.
/// In a session: sits at your crew member's eyes, or in the cockpit when you're the seated pilot
/// (NetworkPlayer owns the look direction). Offline: rides in the pilot seat so you can just fly.
/// Esc frees the mouse (for the menu); click to grab it again.
/// V cycles views: first person -> front view (camera in front of you, looking back at your
/// character, for checking animations) -> outside view of the helicopter -> first person.
/// Runs after CrewVisual so it follows the animated head for this frame.
/// </summary>
[DefaultExecutionOrder(100)]
public class FirstPersonSeatCamera : MonoBehaviour
{
    public Transform helicopter;

    [Header("Seats (helicopter local space)")]
    public Vector3 pilotEye = new(0.45f, 1.85f, 2.05f);
    public Vector3 crewEye = new(-0.45f, 1.8f, 0.2f);

    [Header("Look")]
    public float mouseSensitivity = 0.12f;
    public float maxYaw = 150f;
    public float maxPitch = 80f;

    [Header("First person")]
    [Tooltip("How far in front of the body's centre line your eyes sit.")]
    public float eyeForward = 0.2f;

    [Header("Front view (V)")]
    public float frontDistance = 2.4f;
    public float frontHeight = 0.35f;

    [Header("Helicopter chase view (V)")]
    public float chaseDistance = 20f;
    public float chaseHeight = 7f;

    public enum View { FirstPerson, Front, HeliChase, Side }
    public static View ForcedView = (View)(-1); // test screenshots only
    View view = View.FirstPerson;
    public static View CurrentView { get; private set; }
    float yaw, pitch;
    float bobHeight;
    Vector3 headLean;
    NetworkHelicopter netHeli;
    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.nearClipPlane = 0.05f;
    }

    void Start()
    {
        if (helicopter) netHeli = helicopter.GetComponent<NetworkHelicopter>();
    }

    /// <summary>Offline, or in a session and in the pilot seat.</summary>
    bool SittingInPilotSeat => netHeli == null || !netHeli.IsSpawned || netHeli.IsLocalPilot;

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked && !MouseOverMenu(mouse))
            SetCursorLocked(true);
        if (kb != null && kb.vKey.wasPressedThisFrame) view = (View)(((int)view + 1) % 3);
        if ((int)ForcedView >= 0) view = ForcedView;
        CurrentView = view;
        CrewVisual.ShowOwnHead = view != View.FirstPerson;

        if (mouse != null && Cursor.lockState == CursorLockMode.Locked && NetworkPlayer.Local == null)
        {
            Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
            yaw = Mathf.Clamp(yaw + d.x, -maxYaw, maxYaw);
            pitch = Mathf.Clamp(pitch - d.y, -maxPitch, maxPitch);
        }
    }

    void LateUpdate()
    {
        if (!helicopter) return;

        var me = NetworkPlayer.Local;

        if (view == View.Front && me != null)
        {
            // Stand in front of your character and look back at it (turns as you turn).
            Vector3 body = me.Visual != null && me.Visual.Ragdolled ? me.Visual.HeadPosition : me.transform.position;
            Vector3 facing = Quaternion.Euler(0f, me.LookYaw, 0f) * Vector3.forward;
            // Pull in closer if a wall or the helicopter is in the way.
            Vector3 focus = body + Vector3.up * 0.1f;
            Vector3 offset = facing * frontDistance + Vector3.up * frontHeight;
            float dist = offset.magnitude;
            foreach (var hit in Physics.SphereCastAll(focus, 0.2f, offset.normalized, dist, GameLayers.Gameplay & ~(1 << GameLayers.Crew), QueryTriggerInteraction.Ignore))
                if (hit.distance > 0f) dist = Mathf.Min(dist, hit.distance - 0.05f);
            transform.position = focus + offset.normalized * Mathf.Max(dist, 0.6f);
            transform.LookAt(focus);
            return;
        }

        if (view == View.Side && me != null)
        {
            // Test recordings: watch your character from the side (good for feet and wobble).
            Vector3 body = me.Visual != null && me.Visual.Ragdolled ? me.Visual.HeadPosition : me.transform.position;
            Vector3 right = Quaternion.Euler(0f, me.LookYaw, 0f) * Vector3.right;
            transform.position = body + right * 3.2f + Vector3.up * 0.2f;
            transform.LookAt(body + Vector3.down * 0.1f);
            return;
        }

        if (view == View.HeliChase)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(helicopter.forward, Vector3.up).normalized;
            transform.position = helicopter.position - flatForward * chaseDistance + Vector3.up * chaseHeight;
            transform.LookAt(helicopter.position + Vector3.up * 2f);
            return;
        }

        if (me != null)
        {
            if (me.SeatedAsPilot.Value)
            {
                // Strapped in: the cockpit banks and pitches with you.
                transform.position = helicopter.TransformPoint(pilotEye);
                transform.rotation = helicopter.rotation * Quaternion.Euler(me.LookPitch, Mathf.DeltaAngle(helicopter.eulerAngles.y, me.LookYaw), 0f);
            }
            else
            {
                // On your feet: eyes at head height, horizon kept level.
                // Knocked flat: ride along with the floppy ragdoll's head.
                // Eyes ride on the animated head (so the body stays lined up underneath you);
                // the model detaches from the body while ragdolled.
                // In front of the face: horizontally we follow the animated head (so when the body
                // leans into a run the camera stays ahead of the chest, never inside it), smoothed;
                // vertically the steady eye height plus a smoothed walking bob.
                var visual = me.Visual;
                Vector3 steady = me.GetComponent<CrewBody>().Eye;
                if (visual != null && visual.Ragdolled)
                    transform.position = Vector3.Lerp(transform.position, visual.EyePosition, 0.35f);
                else
                {
                    Vector3 facing = Quaternion.Euler(0f, me.LookYaw, 0f) * Vector3.forward;
                    Vector3 headFlat = visual != null ? visual.HeadBonePosition : steady;
                    Vector3 lean = headFlat - steady; lean.y = 0f;
                    headLean = Vector3.Lerp(headLean, lean, 1f - Mathf.Exp(-14f * Time.deltaTime));
                    float bob = visual != null ? visual.EyePosition.y - steady.y : 0f;
                    bobHeight = Mathf.Lerp(bobHeight, bob, 1f - Mathf.Exp(-10f * Time.deltaTime));
                    Vector3 want = steady + headLean + facing * eyeForward + Vector3.up * bobHeight;
                    // (if the eye point itself got shoved into a thin wall, look from the body's centre instead)
                    Vector3 from = Physics.CheckSphere(steady, 0.06f, GameLayers.CameraSolid, QueryTriggerInteraction.Ignore) ? me.transform.position : steady;
                    transform.position = me.OnHelicopter.Value || InsideCabin(helicopter, steady) ? KeepOutOfWallsRiding(helicopter, from, want) : KeepOutOfWalls(from, want);
                }
                transform.rotation = Quaternion.Euler(me.LookPitch, me.LookYaw, 0f);
            }
            return;
        }

        Vector3 eye = SittingInPilotSeat ? pilotEye : crewEye;
        transform.position = helicopter.TransformPoint(eye);
        transform.rotation = helicopter.rotation * Quaternion.Euler(pitch, yaw, 0f);
    }

    /// <summary>
    /// Riding inside the moving helicopter: on the host its walls sit at the last physics step while
    /// what you see is smoothed ahead of that (up to ~15 cm at speed), so do the wall check in the
    /// helicopter's physics frame and bring the answer back to the smoothed one.
    /// </summary>
    public static Vector3 KeepOutOfWallsRiding(Transform heli, Vector3 from, Vector3 to)
    {
        Matrix4x4 toPhys = RenderToPhysics(heli);
        Vector3 hit = KeepOutOfWalls(toPhys.MultiplyPoint3x4(from), toPhys.MultiplyPoint3x4(to));
        return toPhys.inverse.MultiplyPoint3x4(hit);
    }

    /// <summary>Is this point inside the helicopter's cabin/cockpit volume (riding it, even with your feet off the floor)?</summary>
    public static bool InsideCabin(Transform heli, Vector3 world)
    {
        if (heli == null) return false;
        Vector3 l = heli.InverseTransformPoint(world);
        return Mathf.Abs(l.x) < 1.25f && l.y > 0.6f && l.y < 3.1f && l.z > -0.8f && l.z < 3.2f;
    }

    /// <summary>Maps a point on the helicopter as drawn to where the same point is for physics right now.</summary>
    public static Matrix4x4 RenderToPhysics(Transform heli)
    {
        var rb = heli ? heli.GetComponent<Rigidbody>() : null;
        if (rb == null || rb.isKinematic || rb.interpolation == RigidbodyInterpolation.None) return Matrix4x4.identity;
        return Matrix4x4.TRS(rb.position, rb.rotation, Vector3.one) * heli.worldToLocalMatrix;
    }

    /// <summary>A raycast from something drawn riding the helicopter (a foot, a hand): done where the
    /// helicopter is for physics, with the hit brought back to where it's drawn.</summary>
    public static bool RaycastRiding(Transform heli, Vector3 origin, Vector3 dir, out RaycastHit hit, float dist, int mask)
    {
        Matrix4x4 toPhys = RenderToPhysics(heli);
        if (!Physics.Raycast(toPhys.MultiplyPoint3x4(origin), toPhys.MultiplyVector(dir), out hit, dist, mask, QueryTriggerInteraction.Ignore)) return false;
        Matrix4x4 back = toPhys.inverse;
        hit.point = back.MultiplyPoint3x4(hit.point);
        hit.normal = back.MultiplyVector(hit.normal);
        return true;
    }

    /// <summary>Pull the camera back toward your head if it would poke into a wall or the helicopter.</summary>
    static Vector3 KeepOutOfWalls(Vector3 from, Vector3 to)
    {
        const float radius = 0.1f;
        Vector3 d = to - from; float len = d.magnitude;
        if (len < 1e-4f) return to;
        Vector3 result = to;
        if (Physics.SphereCast(from, radius, d / len, out RaycastHit hit, len, GameLayers.CameraSolid, QueryTriggerInteraction.Ignore))
            result = from + d / len * Mathf.Max(0f, hit.distance - 0.01f);
        // Safety net (the cast misses things it starts inside, e.g. a thin handle while being thrown
        // around a lurching cabin): never finish inside something solid.
        if (Physics.CheckSphere(result, 0.06f, GameLayers.CameraSolid, QueryTriggerInteraction.Ignore)) result = from;
        return result;
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // The prototype menu lives in the top-left corner; don't steal clicks meant for it.
    static bool MouseOverMenu(Mouse mouse)
    {
        Vector2 p = mouse.position.ReadValue();
        return p.x < 320 && p.y > Screen.height - 250;
    }
}
