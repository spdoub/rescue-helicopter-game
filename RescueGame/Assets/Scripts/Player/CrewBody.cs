using UnityEngine;

/// <summary>
/// Physics body for a crew member. Runs on the host only (clients just see the synced result).
///
/// Standing: the body is kept upright and pushed toward "whatever I'm standing on is doing, plus
/// my walk input", so you ride along inside the moving helicopter.
/// Knocked down: if the floor under you lurches too hard (and you're not holding on), you lose
/// balance, tumble freely for a couple of seconds, then get back up.
/// Grabbing: hold to latch your hand onto whatever you're looking at (a loose rope-like joint).
/// Holding on keeps you on your feet.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class CrewBody : MonoBehaviour
{
    [Header("Walking")]
    public float walkSpeed = 3.5f;
    public float groundAccel = 12f;
    public float airAccel = 2f;
    public float jumpSpeed = 4.5f;

    [Header("Balance")]
    [Tooltip("Floor acceleration (m/s²) that knocks you over if you aren't holding on.")]
    public float knockdownAccel = 12f;
    [Tooltip("Floor tilt (degrees) that knocks you over if you aren't holding on.")]
    public float knockdownSlope = 40f;
    public float knockdownTime = 2f;
    public float getUpTime = 0.4f;

    [Header("Grabbing")]
    public float grabReach = 2f;
    public float grabSlack = 0.1f;
    public float grabBreakForce = 6000f;

    [Header("Body")]
    public float eyeHeight = 1.35f;  // above the feet

    public bool IsGrounded { get; private set; }
    public bool IsKnockedDown => knockdownTimer > 0f;
    public bool IsGrabbing => grabJoint != null;
    public Vector3 GrabPoint { get; private set; }
    public Rigidbody GroundBody { get; private set; }
    public bool Seated { get; private set; }

    Rigidbody rb;
    CapsuleCollider capsule;
    ConfigurableJoint grabJoint;
    Rigidbody grabbedBody;
    Vector3 groundPoint;
    Vector3 lastPlatformVel;
    bool hadPlatform;
    float knockdownTimer, getUpTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
    }

    public Vector3 Feet => transform.position + Vector3.down * (capsule.height * 0.5f);
    public Vector3 Eye => Feet + Vector3.up * eyeHeight;

    /// <summary>Called by the host every physics tick with this player's latest controls.</summary>
    public void Simulate(PlayerInput input, float dt)
    {
        if (Seated) return;

        CheckGround(out Vector3 platformVel, out Vector3 floorNormal);
        UpdateGrab(input);
        UpdateBalance(platformVel, floorNormal, dt);

        if (IsKnockedDown) return;

        // Face where the player is looking (yaw only), staying upright.
        rb.MoveRotation(Quaternion.Euler(0f, input.yaw, 0f));

        Vector3 wish = Quaternion.Euler(0f, input.yaw, 0f) * new Vector3(input.moveX, 0f, input.moveZ);
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        Vector3 target = platformVel + wish * walkSpeed;
        Vector3 delta = target - rb.linearVelocity;
        delta.y = 0f;
        float accel = IsGrounded ? groundAccel : airAccel;
        Vector3 push = Vector3.ClampMagnitude(delta * accel, 25f);
        rb.AddForce(push, ForceMode.Acceleration);

        // Feet push back on whatever we're standing on (equal and opposite), so walking around
        // the cabin nudges the helicopter instead of adding energy from nowhere.
        if (IsGrounded && GroundBody != null)
            GroundBody.AddForceAtPosition(-push * rb.mass, groundPoint, ForceMode.Force);

        if (input.jump && IsGrounded)
        {
            Vector3 v = rb.linearVelocity;
            v.y = platformVel.y + jumpSpeed;
            rb.linearVelocity = v;
        }
    }

    void CheckGround(out Vector3 platformVel, out Vector3 floorNormal)
    {
        platformVel = Vector3.zero;
        floorNormal = Vector3.up;
        GroundBody = null;

        float r = capsule.radius * 0.9f;
        Vector3 origin = transform.position;
        float dist = capsule.height * 0.5f - r + 0.15f;
        IsGrounded = false;
        if (IsKnockedDown || !Physics.SphereCast(origin, r, Vector3.down, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore)
            || hit.rigidbody == rb)
        {
            hadPlatform = false;
            return;
        }
        IsGrounded = true;

        floorNormal = hit.normal;
        GroundBody = hit.rigidbody;
        groundPoint = hit.point;
        if (GroundBody != null) platformVel = GroundBody.GetPointVelocity(hit.point);
    }

    void UpdateBalance(Vector3 platformVel, Vector3 floorNormal, float dt)
    {
        if (knockdownTimer > 0f)
        {
            knockdownTimer -= dt;
            if (knockdownTimer <= 0f) getUpTimer = getUpTime;
            return;
        }

        if (getUpTimer > 0f)
        {
            // Swing back upright, then lock rotation again.
            getUpTimer -= dt;
            Quaternion upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, upright, 1f - getUpTimer / getUpTime));
            if (getUpTimer <= 0f) { rb.freezeRotation = true; rb.angularVelocity = Vector3.zero; }
            return;
        }

        // Did the floor lurch hard enough to take our legs out?
        if (IsGrounded && GroundBody != null && hadPlatform && !IsGrabbing)
        {
            Vector3 floorAccel = (platformVel - lastPlatformVel) / dt;
            floorAccel.y = 0f;
            float slope = Vector3.Angle(floorNormal, Vector3.up);
            if (floorAccel.magnitude > knockdownAccel || slope > knockdownSlope)
                KnockDown(-floorAccel * 0.05f);
        }
        hadPlatform = IsGrounded && GroundBody != null;
        lastPlatformVel = platformVel;
    }

    /// <summary>Lose balance and ragdoll-tumble for a bit.</summary>
    public void KnockDown(Vector3 shove)
    {
        if (Seated) return;
        knockdownTimer = knockdownTime;
        rb.freezeRotation = false;
        rb.AddForce(shove, ForceMode.VelocityChange);
        rb.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
    }

    void UpdateGrab(PlayerInput input)
    {
        if (grabJoint == null && grabbedBody != null) grabbedBody = null; // joint snapped

        if (!input.grab)
        {
            Release();
            return;
        }
        if (grabJoint != null)
        {
            GrabPoint = grabbedBody ? grabbedBody.transform.TransformPoint(grabJoint.connectedAnchor) : grabJoint.connectedAnchor;
            return;
        }
        if (IsKnockedDown) return;

        Vector3 dir = Quaternion.Euler(input.pitch, input.yaw, 0f) * Vector3.forward;
        var hits = Physics.RaycastAll(Eye, dir, grabReach, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; RaycastHit? pick = null;
        foreach (var h in hits)
            if (h.rigidbody != rb && h.distance < best) { best = h.distance; pick = h; }
        if (pick == null) return;

        var hit = pick.Value;
        grabbedBody = hit.rigidbody;
        grabJoint = gameObject.AddComponent<ConfigurableJoint>();
        grabJoint.autoConfigureConnectedAnchor = false;
        // Rope from the chest to the grabbed point: starts exactly as long as the reach, so nothing
        // snaps on grab. You can move closer but not further away. The chest is near the body's
        // centre, so turning your head doesn't swing the rope around.
        Vector3 chest = transform.position + Vector3.up * 0.3f;
        grabJoint.anchor = transform.InverseTransformPoint(chest);
        grabJoint.connectedBody = grabbedBody;
        grabJoint.connectedAnchor = grabbedBody ? grabbedBody.transform.InverseTransformPoint(hit.point) : hit.point;
        grabJoint.xMotion = grabJoint.yMotion = grabJoint.zMotion = ConfigurableJointMotion.Limited;
        grabJoint.linearLimit = new SoftJointLimit { limit = Vector3.Distance(chest, hit.point) + grabSlack };
        grabJoint.breakForce = grabBreakForce;
        grabJoint.enableCollision = true;
        GrabPoint = hit.point;
    }

    public void Release()
    {
        if (grabJoint != null) Destroy(grabJoint);
        grabJoint = null;
        grabbedBody = null;
    }

    /// <summary>Strap into a seat: body turns off its collisions and is carried by the seat.</summary>
    public void Sit(Transform seat)
    {
        Release();
        Seated = true;
        knockdownTimer = getUpTimer = 0f;
        capsule.enabled = false;
        rb.isKinematic = true;
        rb.freezeRotation = true;
        FollowSeat(seat);
    }

    public void FollowSeat(Transform seat)
    {
        transform.SetPositionAndRotation(seat.position + Vector3.up * (capsule.height * 0.5f), seat.rotation);
        rb.position = transform.position;
        rb.rotation = transform.rotation;
    }

    public void StandUp(Vector3 position, Rigidbody platform)
    {
        Seated = false;
        transform.SetPositionAndRotation(position + Vector3.up * (capsule.height * 0.5f), Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        rb.position = transform.position;
        rb.isKinematic = false;
        capsule.enabled = true;
        rb.linearVelocity = platform ? platform.GetPointVelocity(position) : Vector3.zero;
        hadPlatform = false;
    }
}
