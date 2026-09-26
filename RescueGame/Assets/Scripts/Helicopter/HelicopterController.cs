using UnityEngine;

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
    [Tooltip("How quickly it reaches the target climb/descent speed.")]
    public float verticalResponse = 2f;
    [Tooltip("Let go of up/down: how firmly it holds that height (per metre off).")]
    public float altitudeHoldStrength = 1.2f;

    [Header("Tilt (forward/back/sideways)")]
    [Tooltip("How far the helicopter leans at full input, in degrees. More lean = faster.")]
    public float maxTiltAngle = 25f;
    public float tiltStrength = 40f;
    public float tiltDamping = 12f;
    [Tooltip("Air resistance on horizontal speed. Lower = higher top speed and more drifting.")]
    public float horizontalDrag = 0.18f;
    [Tooltip("Extra braking when you let go of the tilt controls, so it settles into a hover. 0 = realistic drifting.")]
    public float hoverAssistDrag = 0.5f;
    [Tooltip("Nobody in the pilot seat: the hover autopilot also holds its spot against the wind (so a solo player can go winch).")]
    public bool hoverAutopilot;
    public float autopilotHoldStrength = 0.6f;

    [Header("Turning")]
    [Tooltip("Turn rate at full input, in degrees per second.")]
    public float yawRate = 90f;
    public float yawResponse = 5f;

    [Header("Ground")]
    [Tooltip("How far below the skids still counts as 'on the ground'.")]
    public float groundCheckDistance = 0.3f;

    [Header("Visuals")]
    public Transform mainRotor;
    public Transform tailRotor;
    public float rotorSpinSpeed = 1500f;

    public bool engineOn = true;

    [Header("Engine power")]
    [Tooltip("1 = full power. Out of fuel drops this so the helicopter sinks (see HeliFuel).")]
    [Range(0f, 1f)] public float power = 1f;
    [Tooltip("Engine damage multiplier (see HeliDamage).")]
    [Range(0f, 1f)] public float damagePower = 1f;

    [Header("Networking")]
    [Tooltip("Read the keyboard/gamepad on this machine. Turned off when NetworkHelicopter feeds input from the pilot.")]
    public bool useLocalInput = true;
    [Tooltip("Run the flight physics on this machine. Only the host does this in multiplayer.")]
    public bool simulate = true;

    Rigidbody rb;
    float collective, pitchInput, rollInput, yawInput;
    bool holding; float holdAltitude;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>Set the controls from outside (e.g. the pilot's input arriving over the network).</summary>
    /// <summary>The controls currently being applied (for debugging / HUD).</summary>
    public HelicopterInput CurrentInput => new() { collective = collective, pitch = pitchInput, roll = rollInput, yaw = yawInput };

    public void SetInput(HelicopterInput input)
    {
        collective = input.collective;
        pitchInput = input.pitch;
        rollInput = input.roll;
        yawInput = input.yaw;
    }

    void Update()
    {
        if (useLocalInput) SetInput(HelicopterInput.ReadDevices());

        if (engineOn)
        {
            // (about the helicopter's own axes, whatever way the imported pivots happen to point)
            if (mainRotor) mainRotor.Rotate(transform.up, rotorSpinSpeed * Time.deltaTime, Space.World);
            if (tailRotor) tailRotor.Rotate(transform.right, rotorSpinSpeed * 1.6f * Time.deltaTime, Space.World);
        }
    }

    /// <summary>True when the skids are resting on something.</summary>
    public bool IsLanded { get; private set; }

    void FixedUpdate()
    {
        if (!simulate) { IsLanded = CheckLanded(); return; } // (keep this true on every machine, not just the host)
        if (!engineOn) return;

        // Sitting on the ground with nobody pulling up: engine idles and the full weight rests
        // on the skids (otherwise the hover assist makes it weightless and it slides around).
        IsLanded = CheckLanded();
        if (IsLanded && collective <= 0.05f) return;

        ApplyLift();
        ApplyTilt();
        ApplyYaw();
        ApplyHorizontalDrag();
    }

    bool CheckLanded()
    {
        Vector3 origin = transform.position + transform.up * 0.3f;
        foreach (var hit in Physics.RaycastAll(origin, -transform.up, 0.3f + groundCheckDistance, GameLayers.Gameplay, QueryTriggerInteraction.Ignore))
            if (hit.rigidbody != rb) return true;
        return false;
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
        // Altitude hold: let go of up/down and it stays at that height, even with people or a
        // survivor dragging on it (the lift below only cancels the helicopter's own weight).
        if (Mathf.Abs(collective) < 0.05f)
        {
            if (!holding) { holding = true; holdAltitude = rb.position.y; }
            targetClimb = Mathf.Clamp((holdAltitude - rb.position.y) * altitudeHoldStrength, -2f, 2f);
        }
        else holding = false;
        // Losing power: can't climb, and the best it can do is a slow sinking glide.
        float p = power * damagePower;
        if (p < 1f) targetClimb = Mathf.Min(targetClimb * p, -3.5f * (1f - p));
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

        // Hover autopilot: lean back toward where it was left (a gentle spring, capped, so
        // someone dangling on the cable or a big gust can still move it a bit).
        if (hoverAutopilot && !IsLanded)
        {
            if (!holdingSpot) { holdingSpot = true; holdSpot = rb.position; }
            Vector3 off = holdSpot - rb.position; off.y = 0f;
            if (off.magnitude > 15f) holdSpot = rb.position + off.normalized * 15f; // dragged far away: hold the new spot instead
            Vector3 pull = Vector3.ClampMagnitude(off * autopilotHoldStrength - new Vector3(v.x, 0f, v.z) * 0.8f, 3f);
            rb.AddForce(pull, ForceMode.Acceleration);
        }
        else holdingSpot = false;
    }
    bool holdingSpot; Vector3 holdSpot;
}
