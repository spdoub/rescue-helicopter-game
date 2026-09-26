using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A survivor's mind (host only). The survivor has the same floppy physics body as the crew
/// (CrewBody + CrewVisual through an NPC NetworkPlayer); this supplies the controls a player's
/// keyboard would.
///
/// Strong Grandma:
///  Waiting  - faces the helicopter and waves; walks to the rescue hook when it comes down and
///             grabs it with both hands.
///  Hooked   - hangs on. Every few seconds, unless the crew is reeling her in, she yanks the
///             cable down hard enough to drag the helicopter toward her.
///  Boarding - hoisted up to the door: lets go and clambers into the cabin.
///  Seated   - straps into a bench seat and critiques the flying.
///  Delivered- helicopter lands in a RescueZone: she gets out, walks off, rates the trip.
/// </summary>
[RequireComponent(typeof(NetworkPlayer))]
public class SurvivorBrain : NetworkBehaviour
{
    public enum State : byte { Waiting, Hooked, Boarding, Seated, Delivered }

    public string displayName = "Grandma";
    [Tooltip("How far (m, sideways) she'll shuffle over to grab the hook.")]
    public float hookNoticeRange = 4f;
    [Tooltip("Downward pull (N) of each yank on the cable.")]
    public float yankForce = 2600f;
    public float yankSeconds = 0.7f;
    public float yankEvery = 3.5f;

    /// <summary>What she's saying right now (subtitle; empty = quiet).</summary>
    public NetworkVariable<FixedString128Bytes> Line = new();
    public NetworkVariable<State> Stage = new();

    public State Current => Stage.Value;

    CrewBody body;
    NetworkPlayer me;
    NetworkHelicopter heli;
    NetworkWinch winch;
    float stateSince, lineUntil, nextYank, yankUntil, nextRemark;
    bool jumped;
    /// <summary>For the payout: how rough the ride was and when the rescue started.</summary>
    public int RoughMoments { get; private set; }
    public float RescueStartedAt { get; private set; } = -1f;
    Vector3 home;
    Transform seat;
    RescueZone deliveredTo;

    static readonly string[] HookedLines = { "Pull harder, sonny!", "I've carried heavier shopping!", "Is that all you've got?", "Put your back into it!" };
    static readonly string[] YankLines = { "HNNNGH!", "Come down here!", "My turn to pull!" };
    static readonly string[] SeatedLines = { "Ooh, it's like the rollercoaster at Blackpool.", "My Harold flew smoother than this. He was a bus driver.", "Are we nearly there?", "I brought biscuits, don't tell the others." };
    static readonly string[] RoughLines = { "WATCH IT!", "I'll have your licence for this!", "My hip! My good hip!" };

    void Awake()
    {
        body = GetComponent<CrewBody>();
        me = GetComponent<NetworkPlayer>();
    }

    public override void OnNetworkSpawn()
    {
        heli = FindAnyObjectByType<NetworkHelicopter>();
        winch = heli ? heli.GetComponent<NetworkWinch>() : null;
        home = transform.position;
        stateSince = Time.time;
    }

    // ------------------------------------------------------------------ host: one tick of thinking

    public PlayerInput Think()
    {
        var input = new PlayerInput { yaw = transform.eulerAngles.y };
        if (heli == null) return input;
        if (lineUntil > 0f && Time.time > lineUntil) { Line.Value = default; lineUntil = 0f; }
        float t = Time.time - stateSince;

        switch (Stage.Value)
        {
            case State.Waiting: Waiting(ref input); break;
            case State.Hooked: Hooked(ref input); break;
            case State.Boarding: Boarding(ref input, t); break;
            case State.Seated: Seated(); break;
            case State.Delivered: Delivered(ref input, t); break;
        }
        return input;
    }

    void Waiting(ref PlayerInput input)
    {
        Vector3 toHeli = Flat(heli.transform.position - transform.position);
        if (toHeli.sqrMagnitude > 0.01f) input.yaw = Yaw(toHeli);

        if (HookAvailable(out Vector3 hookPos))
        {
            Vector3 toHook = Flat(hookPos - transform.position);
            float up = hookPos.y - body.Feet.y;
            if (toHook.magnitude < hookNoticeRange && up > -0.3f && up < 3f)
            {
                // Shuffle over to it, arms out, and grab on.
                Aim(ref input, hookPos);
                if (toHook.magnitude > 0.55f) input.moveZ = 1f;
                bool inReach = Vector3.Distance(body.Chest, hookPos) < body.grabReach;
                input.grabLeft = input.grabRight = inReach;
                if (HoldingHook())
                {
                    Enter(State.Hooked);
                    if (RescueStartedAt < 0f) RescueStartedAt = Time.time;
                    Say(Pick(HookedLines), 3f);
                    nextYank = Time.time + yankEvery;
                }
                return;
            }
        }

        // Not reachable yet: wave at the helicopter when it's around.
        if (toHeli.magnitude < 70f && !body.IsKnockedDown)
        {
            input.pitch = -65f;
            input.grabRight = Mathf.Repeat(Time.time * 1.6f, 1f) < 0.5f;
            if (Time.time > nextRemark && toHeli.magnitude < 40f) { Say("Yoo-hoo! Down here!", 2.5f); nextRemark = Time.time + 9f; }
        }
    }

    void Hooked(ref PlayerInput input)
    {
        Vector3 hookPos = winch.hook.position;
        Aim(ref input, hookPos);
        input.grabLeft = input.grabRight = true;
        body.ShortenGrip(0.35f, 1.2f, Time.fixedDeltaTime); // hug the hook: haul in to arm's length

        if (!HoldingHook())
        {
            // Lost her grip (or it snapped): fall, dust off, wait again.
            Enter(State.Waiting);
            Say("OOF! Again, but better.", 3f);
            return;
        }

        // The Strong Grandma bit: she hauls the cable (and the helicopter) down toward her
        // unless the crew is actively reeling her in.
        bool reelingIn = winch.Motion.Value > 0;
        if (!reelingIn && winch.Length.Value > 1.5f && Time.time > nextYank)
        {
            yankUntil = Time.time + yankSeconds;
            nextYank = Time.time + yankEvery;
            Say(Pick(YankLines), 1.5f);
        }
        if (Time.time < yankUntil && !reelingIn)
            winch.hook.AddForce(Vector3.down * yankForce, ForceMode.Force);

        // Hoisted right up to the door: time to get in.
        if (winch.Length.Value < 0.9f)
        {
            if (Vector3.Distance(transform.position, winch.TipPosition) < 3.2f) Enter(State.Boarding);
        }
    }

    // Boarding swing: from where she hangs, up to door height, then in and down into her seat.
    Transform swing;
    Vector3 swingFrom, swingMid, swingTo;
    const float SwingTime = 1.1f;

    void Boarding(ref PlayerInput input, float t)
    {
        if (swing == null)
        {
            seat = FreeSeat();
            if (seat == null) { Enter(State.Hooked); return; } // cabin full: keep hanging on
            Say("HUP!", 1.2f);
            float half = body.transform.position.y - body.Feet.y;
            swingFrom = heli.transform.InverseTransformPoint(transform.position);
            swingTo = heli.transform.InverseTransformPoint(seat.position) + Vector3.up * half;
            swingMid = new Vector3(Mathf.Max(swingFrom.x, 1.3f) * 0.8f, heli.cabinFloorHeight + half + 0.45f, (swingFrom.z + swingTo.z) * 0.5f);
            swing = new GameObject("BoardingSwing").transform;
            swing.SetParent(heli.transform, false);
            swing.localRotation = Quaternion.Inverse(heli.transform.rotation) * transform.rotation;
            body.Sit(swing); // carried along the path (no collisions on the way in)
        }

        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / SwingTime));
        Vector3 p = Vector3.Lerp(Vector3.Lerp(swingFrom, swingMid, k), Vector3.Lerp(swingMid, swingTo, k), k);
        swing.localPosition = p + Vector3.down * (body.transform.position.y - body.Feet.y); // FollowSeat adds the half-height back
        swing.localRotation = Quaternion.Slerp(swing.localRotation, Quaternion.Inverse(heli.transform.rotation) * seat.rotation, k);
        body.FollowSeat(swing);

        if (t >= SwingTime)
        {
            Destroy(swing.gameObject); swing = null;
            me.SetPassengerSeat(seat);
            Enter(State.Seated);
            Say("Right. Seatbelt. Where's the tea?", 3.5f);
            nextRemark = Time.time + 12f;
        }
    }

    void Seated()
    {
        var rb = heli.GetComponent<Rigidbody>();
        if (rb.angularVelocity.magnitude > 1.1f && Time.time > nextRemark - 6f)
        {
            Say(Pick(RoughLines), 2f);
            RoughMoments++;
            nextRemark = Time.time + 8f;
        }
        else if (Time.time > nextRemark)
        {
            Say(Pick(SeatedLines), 4f);
            nextRemark = Time.time + 14f;
        }

        // Landed at a drop-off? Out she gets.
        foreach (var zone in RescueZone.All)
        {
            if (!zone.Contains(heli.transform.position) || rb.linearVelocity.magnitude > 0.8f) continue;
            if (!heli.GetComponent<HelicopterController>().IsLanded) continue;
            float side = Mathf.Sign(heli.transform.InverseTransformPoint(zone.exitPoint ? zone.exitPoint.position : zone.transform.position).x);
            me.SetPassengerSeat(null, heli.transform.TransformPoint(new Vector3(side * 1.9f, 0f, 0.5f)) + Vector3.up * 0.1f);
            ReleaseSeat(seat); seat = null;
            deliveredTo = zone;
            Enter(State.Delivered);
            Say("Well! That was an experience.", 3f);
            break;
        }
    }

    void Delivered(ref PlayerInput input, float t)
    {
        if (deliveredTo == null) return;
        Vector3 exit = deliveredTo.exitPoint ? deliveredTo.exitPoint.position : deliveredTo.transform.position;
        Vector3 to = Flat(exit - transform.position);
        if (to.magnitude > 0.8f && t < 20f) { input.yaw = Yaw(to); input.moveZ = 1f; }
        if (!reported && (to.magnitude <= 0.8f || t > 6f))
        {
            reported = true;
            deliveredTo.Deliver(this);
        }
    }
    bool reported;

    // ------------------------------------------------------------------ helpers

    bool HookAvailable(out Vector3 pos)
    {
        pos = default;
        if (winch == null || winch.hook == null || winch.Length.Value < 1.2f) return false;
        pos = winch.hook.position;
        return true;
    }

    bool HoldingHook()
    {
        var hook = winch != null ? winch.hook : null;
        return hook != null && ((body.Left.Holding && body.Left.body == hook) || (body.Right.Holding && body.Right.body == hook));
    }

    void Aim(ref PlayerInput input, Vector3 target)
    {
        Vector3 d = target - body.Eye;
        Vector3 flat = Flat(d);
        if (flat.sqrMagnitude > 0.0001f) input.yaw = Yaw(flat);
        input.pitch = Mathf.Clamp(-Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg, -85f, NetworkPlayer.MaxLookDown);
    }

    void Enter(State s)
    {
        // On the hook she can't snag on the helicopter's belly (getting wedged under the hull
        // while the winch hauls would just rip her off the hook).
        SetHeliCollision(s != State.Hooked);
        Stage.Value = s;
        stateSince = Time.time;
        jumped = false;
    }

    public void Say(string text, float seconds)
    {
        Line.Value = new FixedString128Bytes(text);
        lineUntil = Time.time + seconds;
    }

    bool heliCollision = true;
    void SetHeliCollision(bool on)
    {
        if (heli == null || on == heliCollision) return;
        heliCollision = on;
        var mine = GetComponent<Collider>();
        foreach (var c in heli.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mine, c, !on);
    }

    static readonly System.Collections.Generic.HashSet<Transform> TakenSeats = new();
    Transform FreeSeat()
    {
        for (int k = 0; k < 3; k++)
        {
            var s = FindDeep(heli.transform, "M_BenchSeat_" + k);
            if (s != null && TakenSeats.Add(s)) return s;
        }
        return null;
    }
    static void ReleaseSeat(Transform s) { if (s) TakenSeats.Remove(s); }

    public override void OnNetworkDespawn() => ReleaseSeat(seat);

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static float Yaw(Vector3 flat) => Quaternion.LookRotation(flat).eulerAngles.y;
    static string Pick(string[] a) => a[Random.Range(0, a.Length)];

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
        return null;
    }
}
