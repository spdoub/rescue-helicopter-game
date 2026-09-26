using UnityEngine;

/// <summary>
/// Physics body for a crew member. Runs on the host only (clients see the synced result and
/// CrewVisual makes it look floppy).
///
/// Standing: kept upright and pushed toward "whatever I'm standing on is doing, plus my walk
/// input", so you ride along inside the moving helicopter. Feet push back on the floor.
/// Jumping: coyote time + jump buffering so jumps feel forgiving.
/// Hands (Human Fall Flat style): each mouse button reaches one hand toward where you look and
/// latches onto whatever it touches with a rope-like joint. Hold forward while holding on to haul
/// yourself up (climb into the helicopter, up ledges, onto other players).
/// Knocked down (full ragdoll): hard floor lurches, big landings, or long falls. You tumble,
/// lie there a moment, then get back up. Holding on with a hand keeps you on your feet.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class CrewBody : MonoBehaviour
{
    [Header("Walking")]
    public float walkSpeed = 3.5f;
    public float sprintSpeed = 6f;
    public float groundAccel = 12f;
    public float airAccel = 3f;

    [Header("Steps")]
    [Tooltip("Ledges up to this high are stepped onto automatically while walking.")]
    public float stepHeight = 0.4f;

    [Header("Jumping")]
    public float jumpSpeed = 5.3f;
    [Tooltip("Can still jump this long after walking off an edge.")]
    public float coyoteTime = 0.15f;
    [Tooltip("A jump pressed this long before landing still happens.")]
    public float jumpBuffer = 0.15f;

    [Header("Balance")]
    [Tooltip("Floor acceleration (m/s²) that knocks you over if you aren't holding on.")]
    public float knockdownAccel = 12f;
    [Tooltip("Floor tilt (degrees) that knocks you over if you aren't holding on.")]
    public float knockdownSlope = 40f;
    [Tooltip("Landing faster than this (m/s) knocks you flat.")]
    public float hardLandingSpeed = 9f;
    [Tooltip("Falling for longer than this (s) goes full ragdoll mid-air.")]
    public float longFallTime = 1.1f;
    public float knockdownTime = 2f;
    public float getUpTime = 0.4f;

    [Header("Hands")]
    public float grabReach = 1.6f;
    public float grabSlack = 0.1f;
    public float grabBreakForce = 6000f;
    [Tooltip("How fast holding forward reels you in toward what you're holding (m/s).")]
    public float pullSpeed = 1.6f;
    public float minRope = 0.3f;

    [Header("Body")]
    public float eyeHeight = 1.35f;  // above the feet

    /// <summary>One hand's hold on the world.</summary>
    public class Hand
    {
        public ConfigurableJoint joint;
        public Rigidbody body;
        public float rope;
        public bool Holding => joint != null;
        public Vector3 Point => body ? body.transform.TransformPoint(joint.connectedAnchor) : joint.connectedAnchor;
    }

    public Hand Left { get; } = new();
    public Hand Right { get; } = new();

    public bool IsGrounded { get; private set; }
    public bool IsKnockedDown => knockdownTimer > 0f;
    public bool IsHolding => Left.Holding || Right.Holding;
    public Rigidbody GroundBody { get; private set; }
    public Vector3 PlatformVelocity { get; private set; }
    public bool Seated { get; private set; }

    Rigidbody rb;
    CapsuleCollider capsule;
    Vector3 groundPoint;
    Vector3 lastPlatformVel;
    bool hadPlatform;
    float knockdownTimer, getUpTimer;
    float lastGroundedTime = -10f, lastJumpPressTime = -10f, airTime;
    float lastVerticalSpeed;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
    }

    public Vector3 Feet => transform.position + Vector3.down * (capsule.height * 0.5f);
    public Vector3 Eye => Feet + Vector3.up * eyeHeight;
    public Vector3 Chest => transform.position + Vector3.up * 0.3f;

    /// <summary>Called by the host every physics tick with this player's latest controls.</summary>
    public void Simulate(PlayerInput input, float dt)
    {
        if (Seated) return;

        bool wasGrounded = IsGrounded;
        CheckGround(out Vector3 floorNormal);
        if (input.jump) lastJumpPressTime = Time.time;

        // Landing: how hard did we hit relative to what we landed on?
        if (IsGrounded && !wasGrounded && !IsKnockedDown && lastVerticalSpeed - PlatformVelocity.y < -hardLandingSpeed)
            KnockDown(Vector3.zero);

        airTime = IsGrounded || IsHolding ? 0f : airTime + dt;
        if (!IsKnockedDown && airTime > longFallTime && rb.linearVelocity.y < -6f)
            KnockDown(Vector3.zero);

        var look = Quaternion.Euler(input.pitch, input.yaw, 0f);
        UpdateHand(Left, input.grabLeft, -1f, look, input, dt);
        UpdateHand(Right, input.grabRight, 1f, look, input, dt);
        UpdateBalance(floorNormal, dt);
        lastVerticalSpeed = rb.linearVelocity.y;

        if (IsKnockedDown) return;

        // Face where the player is looking (yaw only), staying upright.
        rb.MoveRotation(Quaternion.Euler(0f, input.yaw, 0f));

        Vector3 wish = Quaternion.Euler(0f, input.yaw, 0f) * new Vector3(input.moveX, 0f, input.moveZ);
        if (wish.sqrMagnitude > 1f) wish.Normalize();
        float speed = input.sprint && IsGrounded ? sprintSpeed : walkSpeed;

        Vector3 target = PlatformVelocity + wish * speed;
        Vector3 delta = target - rb.linearVelocity;
        delta.y = 0f;
        float accel = IsGrounded ? groundAccel : airAccel;
        Vector3 push = Vector3.ClampMagnitude(delta * accel, 25f);
        rb.AddForce(push, ForceMode.Acceleration);

        if (IsGrounded && wish.sqrMagnitude > 0.01f) StepUp(wish.normalized);

        // Feet push back on whatever we're standing on (equal and opposite), so walking around
        // the cabin nudges the helicopter instead of adding energy from nowhere.
        if (IsGrounded && GroundBody != null)
            GroundBody.AddForceAtPosition(-push * rb.mass, groundPoint, ForceMode.Force);

        bool canJump = IsGrounded || Time.time - lastGroundedTime < coyoteTime;
        if (canJump && Time.time - lastJumpPressTime < jumpBuffer)
        {
            Vector3 v = rb.linearVelocity;
            v.y = PlatformVelocity.y + jumpSpeed;
            rb.linearVelocity = v;
            lastJumpPressTime = lastGroundedTime = -10f;
            IsGrounded = false;
        }
    }

    /// <summary>Walking into a low ledge (a kerb, the helipad lip, a step): hop the body up onto it.</summary>
    void StepUp(Vector3 dir)
    {
        int mask = GameLayers.Gameplay & ~(1 << gameObject.layer);
        float reach = capsule.radius + 0.15f;
        Vector3 feet = Feet;
        // Something blocking at ankle height...
        if (!Physics.Raycast(feet + Vector3.up * 0.05f, dir, out RaycastHit low, reach, mask, QueryTriggerInteraction.Ignore)) return;
        if (Vector3.Angle(low.normal, Vector3.up) < 50f) return; // that's a slope, not a step
        // ...but clear above step height...
        if (Physics.Raycast(feet + Vector3.up * (stepHeight + 0.05f), dir, reach + 0.1f, mask, QueryTriggerInteraction.Ignore)) return;
        // ...and there's a floor on top we can stand on.
        Vector3 over = feet + dir * (reach + 0.05f) + Vector3.up * (stepHeight + 0.05f);
        if (!Physics.Raycast(over, Vector3.down, out RaycastHit top, stepHeight + 0.05f, mask, QueryTriggerInteraction.Ignore)) return;
        float rise = top.point.y - feet.y;
        if (rise < 0.02f || rise > stepHeight) return;
        rb.position += Vector3.up * (rise + 0.02f) + dir * 0.05f;
        Vector3 v = rb.linearVelocity; if (v.y < 0f) v.y = 0f; rb.linearVelocity = v;
    }

    void CheckGround(out Vector3 floorNormal)
    {
        floorNormal = Vector3.up;
        GroundBody = null;
        IsGrounded = false;
        if (IsKnockedDown) { hadPlatform = false; PlatformVelocity = Vector3.zero; return; }

        float r = capsule.radius * 0.9f;
        float dist = capsule.height * 0.5f - r + 0.15f;
        foreach (var hit in Physics.SphereCastAll(transform.position, r, Vector3.down, dist, GameLayers.Gameplay, QueryTriggerInteraction.Ignore))
        {
            if (hit.rigidbody == rb || hit.distance <= 0f) continue;
            IsGrounded = true;
            floorNormal = hit.normal;
            GroundBody = hit.rigidbody;
            groundPoint = hit.point;
            break;
        }

        if (!IsGrounded)
        {
            hadPlatform = false;
            return; // keep the last platform velocity for air control / jumping off
        }
        lastGroundedTime = Time.time;
        PlatformVelocity = GroundBody != null ? GroundBody.GetPointVelocity(groundPoint) : Vector3.zero;
    }

    void UpdateBalance(Vector3 floorNormal, float dt)
    {
        if (knockdownTimer > 0f)
        {
            // Stay floppy while still flying through the air.
            bool nearGround = Physics.CheckSphere(transform.position, capsule.height * 0.5f + 0.2f, GameLayers.Gameplay & ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore);
            knockdownTimer -= dt;
            if (!nearGround) knockdownTimer = Mathf.Max(knockdownTimer, 0.6f);
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
        if (IsGrounded && GroundBody != null && hadPlatform && !IsHolding)
        {
            Vector3 floorAccel = (PlatformVelocity - lastPlatformVel) / dt;
            floorAccel.y = 0f;
            float slope = Vector3.Angle(floorNormal, Vector3.up);
            if (floorAccel.magnitude > knockdownAccel || slope > knockdownSlope)
                KnockDown(-floorAccel * 0.05f);
        }
        hadPlatform = IsGrounded && GroundBody != null;
        lastPlatformVel = PlatformVelocity;
    }

    /// <summary>Lose balance and ragdoll-tumble for a bit.</summary>
    public void KnockDown(Vector3 shove)
    {
        if (Seated) return;
        knockdownTimer = knockdownTime;
        getUpTimer = 0f;
        rb.freezeRotation = false;
        rb.AddForce(shove, ForceMode.VelocityChange);
        rb.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
    }

    void UpdateHand(Hand hand, bool held, float side, Quaternion look, PlayerInput input, float dt)
    {
        if (hand.joint == null) hand.body = null; // joint snapped under too much force

        if (!held) { ReleaseHand(hand); return; }

        if (hand.Holding)
        {
            // Hold forward to haul yourself toward the hold (climbing).
            if (input.moveZ > 0.1f && !IsKnockedDown)
            {
                hand.rope = Mathf.Max(minRope, hand.rope - pullSpeed * dt);
                hand.joint.linearLimit = new SoftJointLimit { limit = hand.rope };
                Mantle(hand.Point);
            }
            return;
        }
        if (IsKnockedDown) return;

        // Reach: a ray from the shoulder on this side, along where you're looking.
        Vector3 shoulder = Eye + transform.right * (0.22f * side) + Vector3.down * 0.25f;
        Vector3 dir = look * Vector3.forward;
        float best = float.MaxValue; RaycastHit? pick = null;
        foreach (var h in Physics.SphereCastAll(shoulder, 0.08f, dir, grabReach, GameLayers.Gameplay, QueryTriggerInteraction.Ignore))
            if (h.rigidbody != rb && h.distance > 0f && h.distance < best) { best = h.distance; pick = h; }
        if (pick == null) return;

        var hit = pick.Value;
        hand.body = hit.rigidbody;
        hand.joint = gameObject.AddComponent<ConfigurableJoint>();
        hand.joint.autoConfigureConnectedAnchor = false;
        // Rope from the chest (near the body's centre, so turning doesn't swing it) to the hold.
        // It starts exactly as long as the reach, so nothing snaps on grab.
        hand.joint.anchor = transform.InverseTransformPoint(Chest);
        hand.joint.connectedBody = hand.body;
        hand.joint.connectedAnchor = hand.body ? hand.body.transform.InverseTransformPoint(hit.point) : hit.point;
        hand.joint.xMotion = hand.joint.yMotion = hand.joint.zMotion = ConfigurableJointMotion.Limited;
        hand.rope = Vector3.Distance(Chest, hit.point) + grabSlack;
        hand.joint.linearLimit = new SoftJointLimit { limit = hand.rope };
        hand.joint.breakForce = grabBreakForce;
        hand.joint.enableCollision = true;
    }

    /// <summary>Help the body up and over a hold that's above the feet (getting into the cabin, onto ledges).</summary>
    void Mantle(Vector3 hold)
    {
        if (Feet.y > hold.y + 0.15f) return;
        Vector3 toHold = hold - transform.position; toHold.y = 0f;
        Vector3 lift = Vector3.up * 18f + Vector3.ClampMagnitude(toHold, 1f) * 6f;
        rb.AddForce(lift, ForceMode.Acceleration);
    }

    void ReleaseHand(Hand hand)
    {
        if (hand.joint != null) Destroy(hand.joint);
        hand.joint = null;
        hand.body = null;
    }

    public void ReleaseAll()
    {
        ReleaseHand(Left);
        ReleaseHand(Right);
    }

    /// <summary>Strap into a seat: body turns off its collisions and is carried by the seat.</summary>
    public void Sit(Transform seat)
    {
        ReleaseAll();
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
