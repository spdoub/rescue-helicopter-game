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
///
/// Recliner Guy (never leaves his armchair):
///  Waiting  - sits in his recliner complaining about the noise; the hook latches onto the chair's strap.
///  Hooked   - chair and man (about 150 kg) hang on the cable: the helicopter really feels it.
///  Boarding - winched up, the whole chair swings into the cabin and gets strapped down.
///  Seated   - critiques the flying from his recliner.
///  Delivered- the chair is set down on the hospital pad, him still in it.
/// </summary>
[RequireComponent(typeof(NetworkPlayer))]
public class SurvivorBrain : NetworkBehaviour
{
    public enum State : byte { Waiting, Hooked, Boarding, Seated, Delivered, Meddling }
    public enum Kind : byte { StrongGrandma, ReclinerGuy, BackseatPilot, DogPerson }

    public Kind kind = Kind.StrongGrandma;
    public string displayName = "Grandma";
    [Tooltip("Recliner Guy's chair.")]
    public Recliner chair;
    [Tooltip("Dog Person's dog.")]
    public Dog dog;
    [Tooltip("Extra pay on delivery (heavier, needier survivors pay more).")]
    public int payBonus;
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

    class Personality
    {
        public string[] waiting, hooked, yank, seated, rough, reviews; // reviews: 5 stars .. 1 star
        public string hup, settled, arrived, fell;
        // Backseat Pilot: critiques that match what the helicopter is doing, and the takeover bit
        public string[] fast, bank, low, hover, takeover, givesUp, satisfied;
    }

    static readonly Personality GrandmaLines = new()
    {
        waiting = new[] { "Yoo-hoo! Down here!" },
        hooked = new[] { "Pull harder, sonny!", "I've carried heavier shopping!", "Is that all you've got?", "Put your back into it!" },
        yank = new[] { "HNNNGH!", "Come down here!", "My turn to pull!" },
        seated = new[] { "Ooh, it's like the rollercoaster at Blackpool.", "My Harold flew smoother than this. He was a bus driver.", "Are we nearly there?", "I brought biscuits, don't tell the others." },
        rough = new[] { "WATCH IT!", "I'll have your licence for this!", "My hip! My good hip!" },
        reviews = new[] { "Lovely boys. Five stars. Would get winched again.", "Very nice. Bit bumpy. Four stars.", "I've had worse. I've had better. Three stars.", "My hip will never forgive you. Two stars.", "One star. I'm telling the newspaper." },
        hup = "HUP!", settled = "Right. Seatbelt. Where's the tea?", arrived = "Well! That was an experience.", fell = "OOF! Again, but better.",
    };

    static readonly Personality ReclinerLines = new()
    {
        waiting = new[] { "Hey! Keep it down, I'm watching the game!", "I'm not going anywhere without my chair.", "You're blocking the signal!" },
        hooked = new[] { "Careful with the chair! It's vintage.", "Is this covered by insurance?", "Don't mind me. Just reclining." },
        yank = new string[0],
        seated = new[] { "Got any snacks back here?", "Nice view. Still prefer the TV.", "Can you turn it up? No, the radio.", "This chair's been in three floods. Never left it." },
        rough = new[] { "MY CHAIR!", "Hey! Watch the upholstery!", "I spilled my drink!" },
        reviews = new[] { "Five stars. Didn't spill a drop.", "Four stars. Chair's got a new squeak.", "Three stars. Missed the second half.", "Two stars. My remote fell out.", "One star. I'm suing. From this chair." },
        hup = "Whoa whoa whoa!", settled = "Yeah, this'll do. Wake me when we land.", arrived = "Right here's fine. Don't touch the chair.", fell = "Aw, come on!",
    };

    static readonly Personality BackseatLines = new()
    {
        waiting = new[] { "Finally! I've been timing you. Terrible.", "You're approaching too fast!", "Wind's from the west, genius!" },
        hooked = new[] { "Winch speed is way too slow!", "I'd have done this in half the time.", "Don't jerk the cable! Smooth!" },
        yank = new string[0],
        seated = new[] { "In my day we flew with our knees.", "I have eleven hours in a simulator, you know.", "Check your mirrors. Helicopters have mirrors, right?" },
        rough = new[] { "Easy! EASY!", "Who taught you to fly? A washing machine?", "I'm writing this down." },
        fast = new[] { "Too fast! TOO FAST!", "This isn't a race, hotshot!" },
        bank = new[] { "Easy on the bank angle, rookie.", "Level the wings! Or... blades!" },
        low = new[] { "Pull up! PULL UP!", "You're scraping the rooftops!" },
        hover = new[] { "You call that a hover?", "Steady! Steady! Steady!" },
        takeover = new[] { "Move over, let me show you how it's done!", "My aircraft!", "I've got it, I've got it!" },
        givesUp = new[] { "Fine! FINE. Your funeral.", "Unhand me! I'm a professional!" },
        satisfied = new[] { "See? That's how it's done.", "You're welcome." },
        reviews = new[] { "Five stars. I taught you well.", "Four stars. Almost as good as me.", "Three stars. Needed more of my input.", "Two stars. I'm reporting this to someone important.", "One star. I'd have crashed less." },
        hup = "Hup! Textbook!", settled = "Right. I'll be supervising.", arrived = "Adequate landing. Adequate.", fell = "That was the winch's fault!",
    };

    static readonly Personality DogLines = new()
    {
        waiting = new[] { "Is he okay? Is he scared? He looks scared.", "Biscuit's safe! Now me, please!" },
        hooked = new[] { "Is Biscuit alright up there?", "Don't let go of him!", "Faster, it's past his dinner time!" },
        yank = new string[0],
        seated = new[] { "Who's a good boy? You are!", "He's never flown before. Neither have I.", "Biscuit, stop licking the pilot." },
        rough = new[] { "BISCUIT!", "Careful! He gets sick!", "Oh no, oh no, oh no!" },
        reviews = new[] { "Five stars! Biscuit loved it!", "Four stars. He only threw up once.", "Three stars. Biscuit is unsettled.", "Two stars. Biscuit bit someone. I don't blame him.", "One star. Biscuit will remember this." },
        hup = "Coming, Biscuit!", settled = "Biscuit! Oh, you brave boy!", arrived = "Come on Biscuit, walkies!", fell = "Whoops! I'm okay!",
    };
    static readonly string[] CallingDog = { "Biscuit! BISCUIT! Come here, boy!", "I'm not leaving without Biscuit!", "He likes things that hold still!", "Biscuit, no! Not the edge!" };

    Personality L => kind switch { Kind.ReclinerGuy => ReclinerLines, Kind.BackseatPilot => BackseatLines, Kind.DogPerson => DogLines, _ => GrandmaLines };

    /// <summary>The survivor of this kind in the scene (null if none).</summary>
    public static SurvivorBrain Find(Kind k)
    {
        foreach (var s in FindObjectsByType<SurvivorBrain>(FindObjectsSortMode.None)) if (s.kind == k) return s;
        return null;
    }

    /// <summary>What they say about the trip (1-5 stars).</summary>
    public string Review(int stars) => L.reviews[Mathf.Clamp(5 - stars, 0, 4)];

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

        if (kind == Kind.ReclinerGuy) { ReclinerThink(t); return input; }

        switch (Stage.Value)
        {
            case State.Waiting: Waiting(ref input); break;
            case State.Hooked: Hooked(ref input); break;
            case State.Boarding: Boarding(ref input, t); break;
            case State.Seated: if (kind == Kind.BackseatPilot) BackseatSeated(); else Seated(); break;
            case State.Delivered: Delivered(ref input, t); break;
            case State.Meddling: Meddling(ref input, t); break;
        }
        return input;
    }

    void Waiting(ref PlayerInput input)
    {
        Vector3 toHeli = Flat(heli.transform.position - transform.position);
        if (toHeli.sqrMagnitude > 0.01f) input.yaw = Yaw(toHeli);

        // Dog Person won't even look at the hook until Biscuit is safely aboard.
        if (kind == Kind.DogPerson && dog != null && !dog.Aboard)
        {
            Vector3 toDog = Flat(dog.transform.position - transform.position);
            if (toDog.sqrMagnitude > 0.01f) input.yaw = Yaw(toDog);
            if (toDog.magnitude > 2.5f) input.moveZ = 0.7f;   // trailing after him, hopelessly
            if (Time.time > nextRemark && toHeli.magnitude < 60f) { Say(Pick(CallingDog), 3f); nextRemark = Time.time + 7f; }
            return;
        }

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
                    Say(Pick(L.hooked), 3f);
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
            if (Time.time > nextRemark && toHeli.magnitude < 40f) { Say(Pick(L.waiting), 2.5f); nextRemark = Time.time + 9f; }
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
            Say(L.fell, 3f);
            return;
        }

        // The Strong Grandma bit: she hauls the cable (and the helicopter) down toward her
        // unless the crew is actively reeling her in.
        bool reelingIn = winch.Motion.Value > 0;
        if (L.yank.Length > 0 && !reelingIn && winch.Length.Value > 1.5f && Time.time > nextYank)
        {
            yankUntil = Time.time + yankSeconds;
            nextYank = Time.time + yankEvery;
            Say(Pick(L.yank), 1.5f);
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
            Say(L.hup, 1.2f);
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
            Say(L.settled, 3.5f);
            nextRemark = Time.time + 12f;
        }
    }

    void Seated()
    {
        Remarks();
        if (!LandedAtDropOff(out var zone)) return;
        float side = Mathf.Sign(heli.transform.InverseTransformPoint(zone.exitPoint ? zone.exitPoint.position : zone.transform.position).x);
        me.SetPassengerSeat(null, heli.transform.TransformPoint(new Vector3(side * 1.9f, 0f, 0.5f)) + Vector3.up * 0.1f);
        ReleaseSeat(seat); seat = null;
        deliveredTo = zone;
        Enter(State.Delivered);
        Say(L.arrived, 3f);
        if (kind == Kind.DogPerson && dog != null)
            dog.FollowPerson(transform, heli.transform.TransformPoint(new Vector3(side * 2.2f, 0.3f, 0.9f)));
    }

    /// <summary>In-flight commentary: complaints when it's rough (which costs stars), chatter otherwise.</summary>
    void Remarks()
    {
        var rb = heli.GetComponent<Rigidbody>();
        if (rb.angularVelocity.magnitude > 1.1f && Time.time > nextRemark - 6f)
        {
            Say(Pick(L.rough), 2f);
            RoughMoments++;
            nextRemark = Time.time + 8f;
        }
        else if (Time.time > nextRemark)
        {
            Say(Pick(L.seated), 4f);
            nextRemark = Time.time + 14f;
        }
    }

    bool LandedAtDropOff(out RescueZone zone)
    {
        zone = null;
        var rb = heli.GetComponent<Rigidbody>();
        if (rb.linearVelocity.magnitude > 0.8f || !heli.GetComponent<HelicopterController>().IsLanded) return false;
        foreach (var z in RescueZone.All) if (z.Contains(heli.transform.position)) { zone = z; return true; }
        return false;
    }

    // ------------------------------------------------------------------ Backseat Pilot

    float nextTakeover;

    /// <summary>Critiques whatever the pilot is doing right now, and every so often goes to "help".</summary>
    void BackseatSeated()
    {
        if (nextTakeover == 0f) nextTakeover = Time.time + Random.Range(25f, 40f);
        if (Time.time > nextRemark)
        {
            var rb = heli.GetComponent<Rigidbody>();
            Vector3 v = rb.linearVelocity; float speed = new Vector2(v.x, v.z).magnitude;
            float bank = Vector3.Angle(heli.transform.up, Vector3.up);
            bool low = !heli.GetComponent<HelicopterController>().IsLanded &&
                       Physics.Raycast(heli.transform.position, Vector3.down, 4f, GameLayers.Gameplay & ~(1 << heli.gameObject.layer), QueryTriggerInteraction.Ignore);
            string[] pick = low ? L.low : bank > 18f ? L.bank : speed > 14f ? L.fast
                          : (speed < 2f && rb.angularVelocity.magnitude > 0.3f) ? L.hover : L.seated;
            if (pick == L.bank || pick == L.low) RoughMoments++;
            Say(Pick(pick), 3f);
            nextRemark = Time.time + Random.Range(7f, 11f);
        }

        if (LandedAtDropOff(out _)) { Seated(); return; } // same way out as everyone else

        // Takeover: unbuckle and march to the cockpit (only when airborne; he's not an idiot).
        if (Time.time > nextTakeover && !heli.GetComponent<HelicopterController>().IsLanded)
        {
            Vector3 standAt = seat.position + heli.transform.forward * 0.45f + Vector3.up * 0.05f;
            me.SetPassengerSeat(null, standAt);
            Enter(State.Meddling);
            Say(Pick(L.takeover), 3f);
        }
    }

    /// <summary>Out of his seat: walks up to the pilot and yanks the controls until someone drags
    /// him back (grabs him) or he's "shown them how it's done".</summary>
    void Meddling(ref PlayerInput input, float t)
    {
        var myRb = GetComponent<Rigidbody>();
        bool heldBack = false;
        foreach (var b in FindObjectsByType<CrewBody>(FindObjectsSortMode.None))
            if (b != body && ((b.Left.Holding && b.Left.body == myRb) || (b.Right.Holding && b.Right.body == myRb))) heldBack = true;

        Vector3 cockpit = heli.transform.TransformPoint(new Vector3(0.15f, heli.cabinFloorHeight, 1.55f));
        Vector3 to = Flat(cockpit - transform.position);
        bool arrived = to.magnitude < 0.9f;
        bool done = heldBack || t > 14f || (arrived && t - arrivedAt > 6f && arrivedAt > 0f) || LandedAtDropOff(out _);
        if (!done)
        {
            input.yaw = Yaw(to.sqrMagnitude > 0.01f ? to : heli.transform.forward);
            if (!arrived) input.moveZ = 1f;
            else
            {
                if (arrivedAt <= 0f) arrivedAt = t;
                input.grabLeft = input.grabRight = false;
                input.pitch = 35f; // reaching for the stick
                if (Time.time > nextMeddle)
                {
                    heli.MeddleFor(new HelicopterInput { pitch = Random.Range(-0.7f, 0.7f), roll = Random.Range(-0.8f, 0.8f), yaw = Random.Range(-0.6f, 0.6f), collective = Random.Range(-0.3f, 0.4f) }, 0.6f);
                    nextMeddle = Time.time + 0.5f;
                    if (Random.value < 0.25f) Say(Pick(L.takeover), 1.5f);
                }
            }
            return;
        }
        // Back to his seat (on foot), then buckle up again.
        if (returningSince <= 0f) { returningSince = Time.time; Say(heldBack ? Pick(L.givesUp) : Pick(L.satisfied), 3f); }
        Vector3 toSeat = Flat(seat.position - transform.position);
        bool landed = LandedAtDropOff(out _);
        if (toSeat.magnitude > 0.5f && Time.time - returningSince < 6f && !landed)
        {
            input.yaw = Yaw(toSeat);
            input.moveZ = 1f;
            return;
        }
        me.SetPassengerSeat(seat);
        arrivedAt = 0f; returningSince = 0f;
        nextTakeover = Time.time + Random.Range(30f, 50f);
        Enter(State.Seated);
    }
    float arrivedAt, nextMeddle, returningSince;

    // ------------------------------------------------------------------ Recliner Guy

    Vector3 chairFrom, chairMid, chairTo; Quaternion chairRotFrom;
    static readonly Vector3 CabinChairSpot = new(-0.4f, 0.95f, 0.66f);   // helicopter space: left side of the cabin, facing forward, clear of the winch box
    const float ChairSwingTime = 1.4f;

    void ReclinerThink(float t)
    {
        if (chair == null) return;
        if (!me.SeatedPassenger.Value && Stage.Value != State.Boarding) me.SetPassengerSeat(chair.seatPoint); // always in the chair
        var hrb = heli.GetComponent<Rigidbody>();

        switch (Stage.Value)
        {
            case State.Waiting:
            {
                float d = Vector3.Distance(heli.transform.position, chair.transform.position);
                if (d < 45f && Time.time > nextRemark) { Say(Pick(L.waiting), 3f); nextRemark = Time.time + 10f; }
                if (HookAvailable(out _) && Vector3.Distance(winch.hook.transform.TransformPoint(chair.hookCatch), chair.liftPoint.position) < 1.3f)
                {
                    chair.Latch(winch.hook);
                    chair.IgnoreHeli(hrb, true);
                    if (RescueStartedAt < 0f) RescueStartedAt = Time.time;
                    Enter(State.Hooked);
                    Say(Pick(L.hooked), 3f);
                    nextRemark = Time.time + 8f;
                }
                break;
            }
            case State.Hooked:
                if (!chair.Latched) { Enter(State.Waiting); Say(L.fell, 2.5f); break; }
                if (Time.time > nextRemark) { Say(Pick(L.hooked), 3f); nextRemark = Time.time + 9f; }
                if (winch.Length.Value < 0.9f && Vector3.Distance(chair.transform.position, winch.TipPosition) < 4.5f)
                {
                    chairFrom = heli.transform.InverseTransformPoint(chair.transform.position);
                    chairRotFrom = Quaternion.Inverse(heli.transform.rotation) * chair.transform.rotation;
                    chairTo = CabinChairSpot;
                    chairMid = new Vector3(Mathf.Max(chairFrom.x, 1.4f) * 0.85f, heli.cabinFloorHeight + 0.5f, (chairFrom.z + chairTo.z) * 0.5f);
                    chair.Unlatch();
                    chair.IgnoreCrew(true); // swings in past whoever is at the door instead of bowling them over
                    Enter(State.Boarding);
                    Say(L.hup, 1.5f);
                }
                break;
            case State.Boarding:
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / ChairSwingTime));
                Vector3 p = Vector3.Lerp(Vector3.Lerp(chairFrom, chairMid, k), Vector3.Lerp(chairMid, chairTo, k), k);
                Quaternion r = Quaternion.Slerp(chairRotFrom, Quaternion.identity, k);
                chair.Carry(heli.transform.TransformPoint(p), heli.transform.rotation * r);
                if (t >= ChairSwingTime)
                {
                    chair.MountIn(hrb);
                    chair.IgnoreCrew(false);
                    Enter(State.Seated);
                    Say(L.settled, 3.5f);
                    nextRemark = Time.time + 12f;
                }
                break;
            }
            case State.Seated:
                Remarks();
                if (LandedAtDropOff(out var zone))
                {
                    float side = Mathf.Sign(heli.transform.InverseTransformPoint(zone.exitPoint ? zone.exitPoint.position : zone.transform.position).x);
                    Vector3 spot = heli.transform.TransformPoint(new Vector3(side * 2.6f, 0f, 0.4f));
                    if (Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out RaycastHit g, 6f, GameLayers.Gameplay & ~(1 << heli.gameObject.layer), QueryTriggerInteraction.Ignore)) spot = g.point;
                    chair.SetDown(spot + Vector3.up * 0.02f, Quaternion.LookRotation(Flat(spot - heli.transform.position)), hrb);
                    deliveredTo = zone;
                    Enter(State.Delivered);
                    Say(L.arrived, 3.5f);
                }
                break;
            case State.Delivered:
                if (!reported && t > 3f) { reported = true; reportedAt = Time.time; deliveredTo.Deliver(this); }
                if (reported && Time.time - reportedAt > NextCallDelay) NewCall();
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
            reportedAt = Time.time;
            deliveredTo.Deliver(this);
        }
        // A while later the phone rings again: she's stuck on another roof somewhere.
        if (reported && Time.time - reportedAt > NextCallDelay) NewCall();
    }
    bool reported;
    float reportedAt;
    public const float NextCallDelay = 15f;
    /// <summary>Seconds until the next call (for the HUD), or -1 if none is pending.</summary>
    public float NextCallIn => Stage.Value == State.Delivered && reported ? Mathf.Max(0f, NextCallDelay - (Time.time - reportedAt)) : -1f;

    /// <summary>Host: put her on a different flat roof and start a fresh rescue.</summary>
    void NewCall()
    {
        if (!FindRoofSpot(out Vector3 spot)) { reportedAt = Time.time; return; }
        if (kind == Kind.ReclinerGuy && chair != null)
        {
            chair.SetDown(spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), heli ? heli.GetComponent<Rigidbody>() : null);
            reported = false; deliveredTo = null; RescueStartedAt = -1f; RoughMoments = 0;
            Enter(State.Waiting);
            Say(Pick(L.waiting), 3f);
            return;
        }
        var rb = GetComponent<Rigidbody>();
        body.StandUp(spot, null);
        rb.position = transform.position;
        rb.linearVelocity = Vector3.zero;
        var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (nt) nt.Teleport(transform.position, transform.rotation, transform.localScale); // jump, don't slide, on everyone's screen
        home = spot;
        reported = false; deliveredTo = null;
        RescueStartedAt = -1f; RoughMoments = 0;
        Enter(State.Waiting);
        Say(kind == Kind.DogPerson ? Pick(CallingDog) : Pick(L.waiting), 3f);
        if (kind == Kind.DogPerson && dog != null) dog.Relocate(spot + transform.right * 1.5f + Vector3.up * 0.3f);
    }

    /// <summary>A clear spot on a flat roof 50-150 m from where she is, 6-26 m up, away from the hospital.</summary>
    bool FindRoofSpot(out Vector3 spot)
    {
        spot = default;
        var roofs = new System.Collections.Generic.List<Collider>();
        foreach (var c in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
        {
            if (c.isTrigger || c.attachedRigidbody != null) continue;
            var b = c.bounds;
            if (b.max.y < 6f || b.max.y > 26f || b.size.x < 9f || b.size.z < 9f) continue;
            float d = Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(transform.position.x, transform.position.z));
            if (d < 50f || d > 150f) continue;
            bool nearDropOff = false;
            foreach (var z in RescueZone.All) if (Vector3.Distance(z.transform.position, b.center) < 30f) nearDropOff = true;
            if (nearDropOff) continue;
            roofs.Add(c);
        }
        for (int attempt = 0; attempt < 150 && roofs.Count > 0; attempt++)
        {
            var b = roofs[Random.Range(0, roofs.Count)].bounds;
            Vector3 p = new Vector3(Random.Range(b.min.x + 2.5f, b.max.x - 2.5f), b.max.y + 3f, Random.Range(b.min.z + 2.5f, b.max.z - 2.5f));
            if (!Physics.Raycast(p, Vector3.down, out RaycastHit hit, 4f, GameLayers.Gameplay, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(hit.point.y - b.max.y) > 0.05f || hit.normal.y < 0.95f) continue;       // the roof itself, not a prop
            Vector3 feet = hit.point + Vector3.up * 0.05f;
            // room to stand and for the hook to come down: nothing solid within 2.2 m, clear sky above
            // (a box from just above the roof surface up to head height, so the roof itself doesn't count)
            bool blocked = Physics.CheckBox(feet + Vector3.up * 1.1f, new Vector3(2.2f, 0.95f, 2.2f), Quaternion.identity, GameLayers.Gameplay & ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore);
            if (blocked || Physics.Raycast(feet + Vector3.up * 2f, Vector3.up, 30f, GameLayers.Gameplay, QueryTriggerInteraction.Ignore)) continue;
            spot = feet;
            return true;
        }
        return false;
    }

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
        for (int k = 2; k >= 0; k--)  // right side first: Recliner Guy's chair takes the left of the cabin
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
