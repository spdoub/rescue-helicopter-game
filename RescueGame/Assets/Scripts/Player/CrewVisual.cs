using UnityEngine;

/// <summary>
/// Makes a crew member look alive. Purely cosmetic and runs on every machine (it only reads the
/// synced state from NetworkPlayer; gameplay physics live in CrewBody on the host).
///
/// - Animation: walk/run blend from the speed relative to what you stand on, flail in the air,
///   seated pose in the pilot seat.
/// - Hands (Human Fall Flat style): arms reach where you look while a mouse button is held and
///   hands land exactly on whatever they're holding. Pilots hold the stick and collective.
/// - Floppiness: arms and head lag and swing with sudden speed changes (jumps, landings, lurches),
///   the body leans into acceleration and turns lazily, reaching arms glide and wobble.
/// - Walking backwards plays the walk in reverse; sideways/diagonal moves turn the legs.
/// - Ragdoll: when knocked down the skeleton goes fully limp, loosely tethered to the physics
///   body so it flops around but stays where the player really is.
/// - First person: your own head is hidden from your camera so you see your arms and legs.
/// Lives on the model (same object as the Animator).
/// </summary>
[RequireComponent(typeof(Animator))]
public class CrewVisual : MonoBehaviour
{
    [Header("Floppiness")]
    public float armSwing = 5.5f;       // how much arms react to acceleration
    public float armSpring = 30f;
    public float armDamping = 7f;       // lower = more overshoot and wobble
    public float headWobble = 2.2f;

    [Header("Body")]
    [Tooltip("How quickly the body turns to face where you look (lower = lazier).")]
    public float turnSpeed = 9f;
    [Tooltip("Degrees of lean per m/s² of acceleration (leans into starts, stops and turns).")]
    public float leanPerAccel = 1.0f;
    [Tooltip("How quickly the lean follows (higher = snappier).")]
    public float leanStiffness = 40f;
    public float maxLean = 16f;
    [Tooltip("Legs turn toward sideways/diagonal movement by up to this much.")]
    public float maxLegTurn = 90f;
    [Tooltip("Ground speed (m/s) the run animation's feet cover at normal playback. Faster movement speeds it up so feet don't slide.")]
    public float runAnimSpeed = 4.7f;

    [Header("Hands")]
    [Tooltip("How fast arms raise/lower when you start/stop reaching.")]
    public float reachBlendSpeed = 7f;
    [Tooltip("How fast a reaching hand follows where you look (lower = floatier).")]
    public float handFollowSpeed = 12f;
    public float handWobble = 0.03f;

    [Header("First-person eyes (from the head bone)")]
    public float eyeUp = 0.1f;
    public float eyeForward = 0.2f;


    [Header("Ragdoll")]
    public float tetherStrength = 60f;
    public float tetherDamping = 8f;

    [Header("Pilot grips (helicopter local space)")]
    public Vector3 stickGrip = new(0.45f, 1.55f, 2.2f);
    public Vector3 collectiveGrip = new(0.12f, 1.35f, 1.95f);

    public Vector3 HeadPosition => head ? head.position : transform.position + Vector3.up * 1.35f;

    /// <summary>Where your eyes are this frame: just in front of the animated head.</summary>
    public Vector3 EyePosition { get; private set; }

    /// <summary>Show your own head too (set by the camera for the front/outside views).</summary>
    public static bool ShowOwnHead;
    public bool Ragdolled { get; private set; }

    Animator anim;
    NetworkPlayer player;
    CrewBody body;
    Transform root;          // the player's physics object (this model's parent normally)
    Transform head, neck, leftUpperArm, rightUpperArm, hips;
    Rigidbody[] bones;
    Rigidbody hipsBody;
    Vector3 localOffset;
    Quaternion localRotation;

    Vector3 lastRootPos, rootVel, lastRootVel;
    Vector3 swing, swingVel;          // spring state for floppy arms/head (world-space offset)
    bool isLocal;

    float bodyYaw, legTurn;           // smoothed facing and leg direction
    Vector3 lean, leanVel;            // spring state for leaning into acceleration

    class HandState { public float weight; public Vector3 pos; public bool init; }
    readonly HandState leftHand = new(), rightHand = new();

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int DirectionId = Animator.StringToHash("Direction");
    static readonly int GroundedId = Animator.StringToHash("Grounded");
    static readonly int SeatedId = Animator.StringToHash("Seated");

    void Awake()
    {
        anim = GetComponent<Animator>();
        player = GetComponentInParent<NetworkPlayer>();
        if (player) player.Visual = this;
        body = GetComponentInParent<CrewBody>();
        root = transform.parent;
        localOffset = transform.localPosition;
        localRotation = transform.localRotation;

        head = anim.GetBoneTransform(HumanBodyBones.Head);
        neck = anim.GetBoneTransform(HumanBodyBones.Neck);
        hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        leftUpperArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightUpperArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);

        bones = GetComponentsInChildren<Rigidbody>();
        hipsBody = hips ? hips.GetComponent<Rigidbody>() : null;
        SetRagdollPhysics(false);
        lastRootPos = root.position;
        bodyYaw = root.eulerAngles.y;
    }

    void Update()
    {
        // The player left while we were a detached ragdoll: clean up the orphaned body.
        if (root == null) { Destroy(gameObject); return; }
        if (player == null) return;
        isLocal = player.IsOwner;

        bool knocked = player.IsSpawned && player.KnockedDown.Value;
        if (knocked && !Ragdolled) EnterRagdoll();
        else if (!knocked && Ragdolled) ExitRagdoll();

        if (Ragdolled) return;
        float dt = Time.deltaTime;

        // Which way are the legs going, relative to where the body faces?
        Vector2 move = player.LocalMove.Value;       // x = right, y = forward (m/s)
        float speed = move.magnitude;
        float direction = 1f, targetLegTurn = 0f;
        if (speed > 0.3f && !player.SeatedAsPilot.Value)
        {
            float angle = Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg;     // 0 = forward, ±180 = back
            if (Mathf.Abs(angle) <= 100f) targetLegTurn = angle;
            else { direction = -1f; targetLegTurn = angle - 180f * Mathf.Sign(angle); } // walk backwards
            targetLegTurn = Mathf.Clamp(targetLegTurn, -maxLegTurn, maxLegTurn);
        }
        legTurn = Mathf.LerpAngle(legTurn, targetLegTurn, 1f - Mathf.Exp(-8f * dt));

        anim.SetFloat(SpeedId, speed, 0.15f, dt);
        // Playback rate: backwards plays in reverse; faster than the run animation's own pace speeds it up.
        anim.SetFloat(DirectionId, direction * Mathf.Max(1f, speed / runAnimSpeed), 0.1f, dt);
        anim.SetBool(GroundedId, player.Grounded.Value || player.SeatedAsPilot.Value);
        anim.SetBool(SeatedId, player.SeatedAsPilot.Value);

        // Lazy turning: the body swings round to face where you look instead of snapping.
        float rootYaw = root.eulerAngles.y;
        bodyYaw = player.SeatedAsPilot.Value ? rootYaw : Mathf.LerpAngle(bodyYaw, rootYaw, 1f - Mathf.Exp(-turnSpeed * dt));
        if (Mathf.Abs(Mathf.DeltaAngle(bodyYaw, rootYaw)) > 100f) bodyYaw = rootYaw;

        // Lean into speeding up / stopping / turning.
        Vector3 accel = Vector3.ClampMagnitude(new Vector3(lastAccel.x, 0f, lastAccel.z), 25f);
        Vector3 leanTarget = player.Grounded.Value && !player.SeatedAsPilot.Value ? accel * leanPerAccel : Vector3.zero;
        // Critically damped: leans in once and settles, no rocking back and forth.
        leanVel += ((leanTarget - lean) * leanStiffness - leanVel * 2f * Mathf.Sqrt(leanStiffness)) * dt;
        lean += leanVel * dt;
        lean = Vector3.ClampMagnitude(lean, maxLean);
        Vector3 leanAxis = Vector3.Cross(Vector3.up, lean);   // tip toward the acceleration
        Quaternion tilt = leanAxis.sqrMagnitude > 1e-6f ? Quaternion.AngleAxis(lean.magnitude, leanAxis.normalized) : Quaternion.identity;
        transform.rotation = tilt * Quaternion.Euler(0f, bodyYaw + legTurn, 0f);
    }

    Vector3 lastAccel;

    // ---------------------------------------------------------------- where the player looks

    Vector3 LookDirection()
    {
        float yaw = isLocal ? player.LookYaw : root.eulerAngles.y;
        float pitch = isLocal ? player.LookPitch : player.Pitch.Value;
        return Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
    }

    Vector3 Eye => body ? body.Eye : root.position + Vector3.up * 0.6f;

    // ---------------------------------------------------------------- hands & head (IK)

    void OnAnimatorIK(int layer)
    {
        if (player == null || !player.IsSpawned) return;
        Vector3 look = LookDirection();

        anim.SetLookAtWeight(1f, 0.3f, 0.9f, 0f, 0.6f);
        anim.SetLookAtPosition(Eye + look * 5f);

        var heli = player.SeatedAsPilot.Value ? FindHeli() : null;
        HandIK(AvatarIKGoal.LeftHand, leftHand, -1f, look,
            player.HoldLeft.Value, player.HoldPointLeft.Value,
            isLocal ? player.LocalReachLeft : player.ReachLeft.Value,
            heli ? heli.TransformPoint(collectiveGrip) : (Vector3?)null);
        HandIK(AvatarIKGoal.RightHand, rightHand, 1f, look,
            player.HoldRight.Value, player.HoldPointRight.Value,
            isLocal ? player.LocalReachRight : player.ReachRight.Value,
            heli ? heli.TransformPoint(stickGrip) : (Vector3?)null);
    }

    void HandIK(AvatarIKGoal goal, HandState hand, float side, Vector3 look, bool holding, Vector3 holdPoint, bool reaching, Vector3? seatGrip)
    {
        float dt = Time.deltaTime;
        Vector3 target = hand.pos;
        float targetWeight = 1f, follow = handFollowSpeed;
        if (holding) { target = holdPoint; follow = 25f; }
        else if (seatGrip.HasValue) { target = seatGrip.Value; follow = 30f; }
        else if (reaching)
        {
            // Arm out toward where you're looking, a little to its own side, with a loose wobble.
            Vector3 right = Vector3.Cross(Vector3.up, look).normalized;
            target = Eye + Vector3.down * 0.2f + right * (0.2f * side) + look * 0.65f;
            float t = Time.time * 3.1f + side;
            target += (right * Mathf.Sin(t) + Vector3.up * Mathf.Sin(t * 1.37f)) * handWobble;
        }
        else targetWeight = 0f; // let the animation carry it (arms hang out of view)

        // Glide rather than snap: blend the arm in/out and let the hand trail its target.
        if (!hand.init || hand.weight < 0.01f) { hand.pos = target; hand.init = true; }
        hand.weight = Mathf.MoveTowards(hand.weight, targetWeight, reachBlendSpeed * dt);
        if (targetWeight > 0f) hand.pos = Vector3.Lerp(hand.pos, target, 1f - Mathf.Exp(-follow * dt));

        float w = Mathf.SmoothStep(0f, 1f, hand.weight);
        anim.SetIKPositionWeight(goal, w);
        anim.SetIKPosition(goal, hand.pos);
        anim.SetIKRotationWeight(goal, w * 0.5f);
        anim.SetIKRotation(goal, Quaternion.LookRotation(look, Vector3.up));
    }

    Transform heliCache;
    Transform FindHeli()
    {
        if (heliCache == null)
        {
            var h = FindAnyObjectByType<NetworkHelicopter>();
            if (h) heliCache = h.transform;
        }
        return heliCache;
    }

    // ---------------------------------------------------------------- floppiness + first person

    void LateUpdate()
    {
        if (player == null) return;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        rootVel = (root.position - lastRootPos) / dt;
        Vector3 accel = (rootVel - lastRootVel) / dt;
        lastAccel = Vector3.Lerp(lastAccel, accel, 1f - Mathf.Exp(-12f * dt)); // smoothed, for leaning
        lastRootPos = root.position;
        lastRootVel = rootVel;

        if (!Ragdolled)
        {
            // Spring that gets flung opposite to acceleration: arms and head lag, overshoot, settle.
            accel = Vector3.ClampMagnitude(accel, 60f);
            Vector3 force = -accel * 0.01f - swing * armSpring - swingVel * armDamping;
            swingVel += force * dt;
            swing += swingVel * dt;
            swing = Vector3.ClampMagnitude(swing, 0.6f);

            bool airborne = player.IsSpawned && !player.Grounded.Value && !player.SeatedAsPilot.Value;
            Vector3 flail = airborne ? new Vector3(Mathf.Sin(Time.time * 13f), Mathf.Sin(Time.time * 9f + 1f), 0f) * 0.12f : Vector3.zero;

            FlopArm(leftUpperArm, leftHand.weight > 0.5f, swing + flail);
            FlopArm(rightUpperArm, rightHand.weight > 0.5f, swing - flail);
            if (neck) neck.rotation = Quaternion.AngleAxis(swing.magnitude * 40f * headWobble, Vector3.Cross(Vector3.up, swing).normalized + Vector3.forward * 1e-4f) * neck.rotation;
        }

        // Eyes: a little up and forward from the head bone (which sits at the base of the skull),
        // so what's left of the hidden head is behind the camera, not in front of it.
        if (head)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(Ragdolled ? head.forward : root.forward, Vector3.up).normalized;
            EyePosition = head.position + Vector3.up * eyeUp + fwd * eyeForward;
        }
        else if (body) EyePosition = body.Eye;

        // First person: hide your own head (and helmet) so it doesn't block your camera.
        if (head) head.localScale = isLocal && !ShowOwnHead ? Vector3.one * 0.001f : Vector3.one;
    }

    void FlopArm(Transform upperArm, bool busy, Vector3 offset)
    {
        if (upperArm == null || busy || offset.sqrMagnitude < 1e-6f) return;
        Vector3 axis = Vector3.Cross(Vector3.down, offset);
        if (axis.sqrMagnitude < 1e-6f) return;
        upperArm.rotation = Quaternion.AngleAxis(offset.magnitude * 90f * armSwing / 3.5f, axis.normalized) * upperArm.rotation;
    }

    // ---------------------------------------------------------------- ragdoll

    void EnterRagdoll()
    {
        Ragdolled = true;
        anim.enabled = false;
        transform.SetParent(null, true); // simulate in world space, not dragged by the tumbling capsule
        SetRagdollPhysics(true);
        foreach (var b in bones) b.linearVelocity = rootVel;
    }

    void ExitRagdoll()
    {
        Ragdolled = false;
        SetRagdollPhysics(false);
        transform.SetParent(root, false);
        transform.localPosition = localOffset;
        transform.localRotation = localRotation;
        anim.enabled = true;
        swing = swingVel = lean = leanVel = Vector3.zero;
        bodyYaw = root.eulerAngles.y;
    }

    void SetRagdollPhysics(bool on)
    {
        foreach (var b in bones)
        {
            b.isKinematic = !on;
            b.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            var c = b.GetComponent<Collider>();
            if (c) c.enabled = on;
        }
    }

    void FixedUpdate()
    {
        if (!Ragdolled || hipsBody == null || root == null) return;
        // Keep the limp body roughly where the real (networked) body is.
        Vector3 target = root.position;
        Vector3 f = (target - hipsBody.position) * tetherStrength - (hipsBody.linearVelocity - rootVel) * tetherDamping;
        hipsBody.AddForce(Vector3.ClampMagnitude(f, 200f), ForceMode.Acceleration);
    }
}
