using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Grey-box helicopter flight. Physics-based (Rigidbody) but with "assists" so it is
/// easy to fly: it holds altitude when you let go of up/down, and levels itself when you
/// let go of the tilt keys. Tilting the helicopter is what moves it sideways/forwards.
///
/// Keyboard: Space = up, Left Shift = down, W/S = nose down/up (forward/back),
///           A/D = roll left/right, Q/E = turn left/right.
/// Gamepad:  Left stick = tilt, Right stick X = turn, RT/LT = up/down.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class HelicopterController : MonoBehaviour
{
    [Header("Lift (up/down)")]
    [Tooltip("Top climb speed at full 'up' input, in m/s.")]
    public float maxClimbSpeed = 8f;
    [Tooltip("Top descent speed at full 'down' input, in m/s.")]
    public float maxDescentSpeed = 6f;
    [Tooltip("How quickly it reaches the target climb/descent speed. Also acts as altitude hold when you let go.")]
    public float verticalResponse = 2f;

    [Header("Tilt (forward/back/sideways)")]
    [Tooltip("How far the helicopter leans at full input, in degrees. More lean = faster.")]
    public float maxTiltAngle = 25f;
    public float tiltStrength = 40f;
    public float tiltDamping = 12f;
    [Tooltip("Air resistance on horizontal speed. Lower = higher top speed and more drifting.")]
    public float horizontalDrag = 0.18f;
    [Tooltip("Extra braking when you let go of the tilt controls, so it settles into a hover. 0 = realistic drifting.")]
    public float hoverAssistDrag = 0.5f;

    [Header("Turning")]
    [Tooltip("Turn rate at full input, in degrees per second.")]
    public float yawRate = 90f;
    public float yawResponse = 5f;

    [Header("Visuals")]
    public Transform mainRotor;
    public Transform tailRotor;
    public float rotorSpinSpeed = 1500f;

    public bool engineOn = true;

    Rigidbody rb;
    float collective, pitchInput, rollInput, yawInput;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        ReadInput();

        if (engineOn)
        {
            if (mainRotor) mainRotor.Rotate(0f, rotorSpinSpeed * Time.deltaTime, 0f, Space.Self);
            if (tailRotor) tailRotor.Rotate(rotorSpinSpeed * Time.deltaTime, 0f, 0f, Space.Self);
        }
    }

    void ReadInput()
    {
        collective = pitchInput = rollInput = yawInput = 0f;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.isPressed) collective += 1f;
            if (kb.leftShiftKey.isPressed) collective -= 1f;
            if (kb.wKey.isPressed) pitchInput += 1f;
            if (kb.sKey.isPressed) pitchInput -= 1f;
            if (kb.dKey.isPressed) rollInput += 1f;
            if (kb.aKey.isPressed) rollInput -= 1f;
            if (kb.eKey.isPressed) yawInput += 1f;
            if (kb.qKey.isPressed) yawInput -= 1f;
        }

        var pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 left = pad.leftStick.ReadValue();
            pitchInput += left.y;
            rollInput += left.x;
            yawInput += pad.rightStick.ReadValue().x;
            collective += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
        }

        collective = Mathf.Clamp(collective, -1f, 1f);
        pitchInput = Mathf.Clamp(pitchInput, -1f, 1f);
        rollInput = Mathf.Clamp(rollInput, -1f, 1f);
        yawInput = Mathf.Clamp(yawInput, -1f, 1f);
    }

    void FixedUpdate()
    {
        if (!engineOn) return;

        ApplyLift();
        ApplyTilt();
        ApplyYaw();
        ApplyHorizontalDrag();
    }

    void ApplyLift()
    {
        // Rotor pushes along the helicopter's up direction. We scale it so the vertical
        // part always cancels gravity (plus/minus your input), which means leaning over
        // turns the leftover sideways part into movement instead of losing height.
        float upDot = Vector3.Dot(transform.up, Vector3.up);
        if (upDot <= 0.1f) return; // on its side or upside down: rotor can't hold it up

        // Steer vertical speed toward a target (0 when you let go = altitude hold).
        float targetClimb = collective >= 0f ? collective * maxClimbSpeed : collective * maxDescentSpeed;
        float verticalAccel = -Physics.gravity.y + (targetClimb - rb.linearVelocity.y) * verticalResponse;

        float liftAccel = verticalAccel / Mathf.Max(upDot, 0.5f);
        rb.AddForce(transform.up * liftAccel, ForceMode.Acceleration);
    }

    void ApplyTilt()
    {
        // Work out which way "up" should point for the current input, relative to the
        // direction the nose is facing (ignoring its current lean).
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.01f) flatForward = Vector3.forward;
        Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);

        Vector3 desiredUp =
            Quaternion.AngleAxis(pitchInput * maxTiltAngle, flatRight) *
            Quaternion.AngleAxis(-rollInput * maxTiltAngle, flatForward) *
            Vector3.up;

        // Spring toward that orientation, with damping so it doesn't wobble forever.
        Vector3 correction = Vector3.Cross(transform.up, desiredUp) * tiltStrength;
        Vector3 w = rb.angularVelocity;
        Vector3 tiltSpin = w - Vector3.Project(w, Vector3.up);
        rb.AddTorque(correction - tiltSpin * tiltDamping, ForceMode.Acceleration);
    }

    void ApplyYaw()
    {
        float targetYawSpeed = yawInput * yawRate * Mathf.Deg2Rad;
        float currentYawSpeed = Vector3.Dot(rb.angularVelocity, Vector3.up);
        rb.AddTorque(Vector3.up * (targetYawSpeed - currentYawSpeed) * yawResponse, ForceMode.Acceleration);
    }

    void ApplyHorizontalDrag()
    {
        Vector3 v = rb.linearVelocity;
        float drag = horizontalDrag;
        if (Mathf.Approximately(pitchInput, 0f) && Mathf.Approximately(rollInput, 0f))
            drag += hoverAssistDrag;
        rb.AddForce(new Vector3(-v.x, 0f, -v.z) * drag, ForceMode.Acceleration);
    }
}
