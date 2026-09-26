using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person camera.
/// In a session: sits at your crew member's eyes, or in the cockpit when you're the seated pilot
/// (NetworkPlayer owns the look direction). Offline: rides in the pilot seat so you can just fly.
/// Esc frees the mouse (for the menu); click to grab it again. V toggles a debug chase view.
/// </summary>
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

    [Header("Debug chase view (V)")]
    public float chaseDistance = 20f;
    public float chaseHeight = 7f;

    float yaw, pitch;
    bool chaseView;
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
        if (kb != null && kb.vKey.wasPressedThisFrame) chaseView = !chaseView;

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

        if (chaseView)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(helicopter.forward, Vector3.up).normalized;
            transform.position = helicopter.position - flatForward * chaseDistance + Vector3.up * chaseHeight;
            transform.LookAt(helicopter.position + Vector3.up * 2f);
            return;
        }

        var me = NetworkPlayer.Local;
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
                var visual = me.Visual; // the model detaches from the body while ragdolled
                Vector3 eyePos = me.GetComponent<CrewBody>().Eye;
                if (visual != null && visual.Ragdolled) eyePos = visual.HeadPosition;
                transform.position = Vector3.Lerp(transform.position, eyePos, visual != null && visual.Ragdolled ? 0.35f : 1f);
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
