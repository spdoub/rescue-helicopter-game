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

    [Header("Front view (V)")]
    public float frontDistance = 2.4f;
    public float frontHeight = 0.35f;

    [Header("Helicopter chase view (V)")]
    public float chaseDistance = 20f;
    public float chaseHeight = 7f;

    public enum View { FirstPerson, Front, HeliChase }
    public static View ForcedView = (View)(-1); // test screenshots only
    View view = View.FirstPerson;
    float yaw, pitch;
    Vector3 bobOffset;
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
                // Head bob is smoothed relative to the body so it feels loose, not jittery.
                var visual = me.Visual;
                Vector3 steady = me.GetComponent<CrewBody>().Eye;
                Vector3 eyePos = visual != null ? visual.EyePosition : steady;
                if (visual != null && visual.Ragdolled)
                    transform.position = Vector3.Lerp(transform.position, eyePos, 0.35f);
                else
                {
                    bobOffset = Vector3.Lerp(bobOffset, eyePos - steady, 1f - Mathf.Exp(-12f * Time.deltaTime));
                    transform.position = steady + bobOffset;
                }
                transform.rotation = Quaternion.Euler(me.LookPitch, me.LookYaw, 0f);
            }
            return;
        }

        Vector3 eye = SittingInPilotSeat ? pilotEye : crewEye;
        transform.position = helicopter.TransformPoint(eye);
        transform.rotation = helicopter.rotation * Quaternion.Euler(pitch, yaw, 0f);
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
