using UnityEngine;

/// <summary>
/// Makes a crew member look alive. Purely cosmetic and runs on every machine (it only reads the
/// synced state from NetworkPlayer; gameplay physics live in CrewBody on the host).
///
/// - Animation: walk/run blend from the speed relative to what you stand on, flail in the air,
///   seated pose in the pilot seat.
/// - Hands (Human Fall Flat style): arms reach where you look while a mouse button is held and
///   hands land exactly on whatever they're holding. Pilots hold the stick and collective.
/// - Floppiness: arms and head lag and swing with sudden speed changes (jumps, landings, lurches).
/// - Ragdoll: when knocked down the skeleton goes fully limp, loosely tethered to the physics
///   body so it flops around but stays where the player really is.
/// - First person: your own head is hidden from your camera so you see your arms and legs.
/// Lives on the model (same object as the Animator).
/// </summary>
[RequireComponent(typeof(Animator))]
public class CrewVisual : MonoBehaviour
{
    [Header("Floppiness")]
    public float armSwing = 3.5f;       // how much arms react to acceleration
    public float armSpring = 40f;
    public float armDamping = 5f;
    public float headWobble = 1.5f;

    [Header("Ragdoll")]
    public float tetherStrength = 60f;
    public float tetherDamping = 8f;

    [Header("Pilot grips (helicopter local space)")]
    public Vector3 stickGrip = new(0.45f, 1.55f, 2.2f);
    public Vector3 collectiveGrip = new(0.12f, 1.35f, 1.95f);

    public Vector3 HeadPosition => head ? head.position : transform.position + Vector3.up * 1.35f;
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

    static readonly int SpeedId = Animator.StringToHash("Speed");
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
        anim.SetFloat(SpeedId, player.MoveSpeed.Value, 0.1f, Time.deltaTime);
        anim.SetBool(GroundedId, player.Grounded.Value || player.SeatedAsPilot.Value);
        anim.SetBool(SeatedId, player.SeatedAsPilot.Value);
    }

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

        anim.SetLookAtWeight(1f, 0.25f, 0.9f, 0f, 0.6f);
        anim.SetLookAtPosition(Eye + look * 5f);

        var heli = player.SeatedAsPilot.Value ? FindHeli() : null;
        HandIK(AvatarIKGoal.LeftHand, -1f, look,
            player.HoldLeft.Value, player.HoldPointLeft.Value,
            isLocal ? player.LocalReachLeft : player.ReachLeft.Value,
            heli ? heli.TransformPoint(collectiveGrip) : (Vector3?)null);
        HandIK(AvatarIKGoal.RightHand, 1f, look,
            player.HoldRight.Value, player.HoldPointRight.Value,
            isLocal ? player.LocalReachRight : player.ReachRight.Value,
            heli ? heli.TransformPoint(stickGrip) : (Vector3?)null);
    }

    void HandIK(AvatarIKGoal goal, float side, Vector3 look, bool holding, Vector3 holdPoint, bool reaching, Vector3? seatGrip)
    {
        Vector3 target; float weight;
        if (holding) { target = holdPoint; weight = 1f; }
        else if (seatGrip.HasValue) { target = seatGrip.Value; weight = 1f; }
        else if (reaching)
        {
            // Arm out toward where you're looking, a little to its own side.
            Vector3 right = Vector3.Cross(Vector3.up, look).normalized;
            target = Eye + Vector3.down * 0.2f + right * (0.2f * side) + look * 0.65f;
            weight = 1f;
        }
        else { anim.SetIKPositionWeight(goal, 0f); anim.SetIKRotationWeight(goal, 0f); return; }

        anim.SetIKPositionWeight(goal, weight);
        anim.SetIKPosition(goal, target);
        anim.SetIKRotationWeight(goal, 0.6f);
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

            FlopArm(leftUpperArm, player.HoldLeft.Value || (isLocal ? player.LocalReachLeft : player.ReachLeft.Value), swing + flail);
            FlopArm(rightUpperArm, player.HoldRight.Value || (isLocal ? player.LocalReachRight : player.ReachRight.Value) || player.SeatedAsPilot.Value, swing - flail);
            if (neck) neck.rotation = Quaternion.AngleAxis(swing.magnitude * 40f * headWobble, Vector3.Cross(Vector3.up, swing).normalized + Vector3.forward * 1e-4f) * neck.rotation;
        }

        // First person: hide your own head (and helmet) so it doesn't block your camera.
        if (head) head.localScale = isLocal ? Vector3.one * 0.001f : Vector3.one;
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
        swing = swingVel = Vector3.zero;
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
