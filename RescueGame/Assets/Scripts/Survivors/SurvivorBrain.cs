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
    public enum Kind : byte { StrongGrandma, ReclinerGuy, BackseatPilot, DogPerson, LedgeResident }

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

    /// <summary>A fire on their roof (RoofFire draws and spreads it on every machine from this).</summary>
    public struct FireState : INetworkSerializeByMemcpy
    {
        public Vector3 origin, roofMin, roofMax;
        public double start, end; // server time; start 0 = no fire
    }
    public NetworkVariable<FireState> Fire = new();
    /// <summary>Caught in the flames this trip (smokes, loses a star).</summary>
    public NetworkVariable<bool> Singed = new();
    /// <summary>Ledge Resident: where his ledge is (xyz = where he stands, w = which way it faces, degrees).</summary>
    public NetworkVariable<Vector4> LedgeSpot = new();
    /// <summary>Ledge Resident: has he been talked round into being rescued?</summary>
    public NetworkVariable<bool> Agreed = new();
    /// <summary>Where this call is: 0 rooftop, 1 beach, 2 mountain, 3 cliff top, 4 lake shore, 5 forest (for the HUD).</summary>
    public NetworkVariable<byte> Place = new();
    public static readonly string[] PlaceNames = { "the rooftop", "the beach", "the mountain", "the cliff top", "the lake shore", "the forest" };

    /// <summary>Chance a new call is somewhere out on the island instead of a city roof. "-wild 1" forces it (dev builds); playtests default to roofs.</summary>
    public static float WildChance
    {
        get
        {
            if (wildChance < 0f)
            {
                var args = System.Environment.GetCommandLineArgs();
                wildChance = 0.45f;
                for (int i = 0; i < args.Length; i++) if (args[i] == "-scenario") wildChance = 0f;
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-wild" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) wildChance = f;
            }
            return wildChance;
        }
    }
    static float wildChance = -1f;

    /// <summary>Chance a call comes with a roof fire. "-fire 0.5" on the command line sets it;
    /// playtests (which pass -scenario) get none unless they ask.</summary>
    public static float FireChance
    {
        get
        {
            if (fireChance < 0f)
            {
                var args = System.Environment.GetCommandLineArgs();
                fireChance = 0.35f;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-scenario") fireChance = 0f;
                }
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-fire" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) fireChance = f;
            }
            return fireChance;
        }
    }
    static float fireChance = -1f;
    float fireAt = -1f, nextFireRemark;
    RoofFire fire;

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
        public string[] fire; public string singed;
        public string[] refuse; public string agree; // Ledge Resident
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
        fire = new[] { "Is something burning? It's not my scones!", "Hot! Hot! Hurry up, sonny!", "I'm too old to be barbecued!" }, singed = "OW! My cardigan's on fire!",
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
        fire = new[] { "Uh, is the roof supposed to be on fire?", "This chair is VERY flammable!", "Can't move! Reclined! Hurry!" }, singed = "AAH! The footrest!",
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
        fire = new[] { "Fire! Classic. You took too long.", "I'd have been here before the fire started!", "Downwind of the smoke, genius!" }, singed = "My eyebrows! I'm billing you!",
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
        fire = new[] { "Fire! Biscuit, stay away from the fire!", "Hurry! Biscuit hates fire!", "Stop, drop and roll, Biscuit!" }, singed = "Ow ow ow! Hot hot hot!",
    };
    static readonly string[] CallingDog = { "Biscuit! BISCUIT! Come here, boy!", "I'm not leaving without Biscuit!", "He likes things that hold still!", "Biscuit, no! Not the edge!" };

    static readonly Personality LedgeLines = new()
    {
        waiting = new[] { "I didn't call you!", "This is a private ledge!", "Go away, you're scaring the pigeons!" },
        refuse = new[] { "Get that thing away from me!", "No! Shoo! Shoo!", "I live here, you know!" },
        agree = "Fine. FINE. But I'm not enjoying this.",
        hooked = new[] { "Mind the pigeons!", "I was perfectly happy up there!", "This is kidnapping!" },
        yank = new string[0],
        seated = new[] { "Eleven years on that ledge. Eleven!", "The pigeons will be wondering where I am.", "Nobody asked for this.", "My ledge had a better view." },
        rough = new[] { "Oh, marvellous.", "Typical!", "I'm writing to the council!" },
        reviews = new[] { "Five stars. Don't tell anyone I said that.", "Four stars. Still didn't ask for it.", "Three stars. The pigeons will be worried.", "Two stars. I want my ledge back.", "One star. I'm climbing back up tonight." },
        hup = "Hmph!", settled = "Don't talk to me.", arrived = "Right. How do I get back up there?", fell = "Ow! See? This is why I stay put!",
        fire = new[] { "Oh, that's just perfect!", "The ledge was fine until you lot showed up!" }, singed = "My robe! My good robe!",
    };

    Personality L => kind switch { Kind.ReclinerGuy => ReclinerLines, Kind.BackseatPilot => BackseatLines, Kind.DogPerson => DogLines, Kind.LedgeResident => LedgeLines, _ => GrandmaLines };

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
        fire = GetComponent<RoofFire>();
    }

    public override void OnNetworkSpawn()
    {
        heli = FindAnyObjectByType<NetworkHelicopter>();
        winch = heli ? heli.GetComponent<NetworkWinch>() : null;
        home = transform.position;
        stateSince = Time.time;
        if (IsServer) ScheduleFire();
        if (IsServer && kind == Kind.LedgeResident) PlaceOnLedge(); // the city is built by now: find him a ledge
    }

    // ------------------------------------------------------------------ host: one tick of thinking

    public PlayerInput Think()
    {
        var input = new PlayerInput { yaw = transform.eulerAngles.y };
        if (heli == null) return input;
        if (lineUntil > 0f && Time.time > lineUntil) { Line.Value = default; lineUntil = 0f; }
        float t = Time.time - stateSince;
        if (fireAt > 0f && Time.time > fireAt && Stage.Value == State.Waiting) { fireAt = -1f; StartFire(); }
        if (kind != Kind.ReclinerGuy && Stage.Value == State.Waiting) CheckFellOffRoof();

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

        if (kind == Kind.LedgeResident && !Agreed.Value && Refusing(ref input)) return;

        if (HookAvailable(out Vector3 hookPos))
        {
            Vector3 toHook = Flat(hookPos - transform.position);
            float up = hookPos.y - body.Feet.y;
            if (toHook.magnitude < hookNoticeRange && up > -0.3f && up < 3f)
            {
                // Shuffle over to it, arms out, and grab on.
                Aim(ref input, hookPos);
                if (toHook.magnitude > 0.55f && (kind != Kind.LedgeResident || OnLedge(hookPos))) input.moveZ = 1f; // (not off the end of his ledge)
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

        // Fire creeping up: back away from it, into the far corner of the roof.
        if (fire != null && fire.BurningNear(transform.position, 4.5f))
        {
            Vector3 toSafe = Flat(fire.SafestSpot() - transform.position);
            if (toSafe.magnitude > 0.8f && !body.IsKnockedDown) { input.yaw = Yaw(toSafe); input.moveZ = 1f; }
            FireRemark();
            return;
        }
        if (fire != null && fire.Active && Stage.Value == State.Waiting && Time.time > nextFireRemark + 6f) FireRemark();

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
            // Slipped right at the door: the crew grab them and haul them in anyway.
            if (winch.Length.Value < 2.5f && Vector3.Distance(transform.position, winch.TipPosition) < 3.5f) { Enter(State.Boarding); return; }
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
                if (fire != null && fire.Active) FireRemark();
                else if (d < 45f && Time.time > nextRemark) { Say(Pick(L.waiting), 3f); nextRemark = Time.time + 10f; }
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
        if (kind == Kind.LedgeResident)
        {
            if (!PlaceOnLedge()) { reportedAt = Time.time; return; }
            Weather.Instance?.Roll();
            reported = false; deliveredTo = null; RescueStartedAt = -1f; RoughMoments = 0;
            ResetFire();
            Enter(State.Waiting);
            Say(Pick(L.waiting), 3f);
            return;
        }
        byte place = 0;
        bool wild = IslandGenerator.Current != null && Random.value < WildChance && FindWildSpot(out Vector3 spot, out place);
        if (!wild && !FindRoofSpot(out spot)) { reportedAt = Time.time; return; }
        Place.Value = place;
        Weather.Instance?.Roll(); // new call, new weather
        if (kind == Kind.ReclinerGuy && chair != null)
        {
            chair.SetDown(spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), heli ? heli.GetComponent<Rigidbody>() : null);
            reported = false; deliveredTo = null; RescueStartedAt = -1f; RoughMoments = 0;
            ResetFire();
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
        ResetFire();
        Enter(State.Waiting);
        Say(kind == Kind.DogPerson ? Pick(CallingDog) : Pick(L.waiting), 3f);
        if (kind == Kind.DogPerson && dog != null) dog.Relocate(spot + transform.right * 1.5f + Vector3.up * 0.3f);
    }

    /// <summary>Somewhere out on the island: a beach, a mountain shoulder, the cliff top, the lake shore or a forest clearing.</summary>
    bool FindWildSpot(out Vector3 spot, out byte place)
    {
        spot = default; place = 0;
        var isl = IslandGenerator.Current;
        int mask = GameLayers.Gameplay & ~(1 << gameObject.layer);
        for (int attempt = 0; attempt < 120; attempt++)
        {
            int kind = 1 + Random.Range(0, 5);
            float x, z;
            switch (kind)
            {
                case 1: x = Random.Range(-380f, 380f); z = Random.Range(-500f, -320f); break;   // beach
                case 2: x = Random.Range(-300f, 300f); z = Random.Range(300f, 520f); break;     // mountains
                case 3: x = Random.Range(-640f, -480f); z = Random.Range(-220f, 220f); break;   // cliff top
                case 4: x = 470f + Random.Range(-160f, 160f); z = 120f + Random.Range(-160f, 160f); break; // lake
                default: x = Random.Range(-450f, 450f); z = Random.Range(-260f, 280f); break;  // hills / forest
            }
            if (new Vector2(x, z).magnitude < 300f) continue;                // (the city)
            float h = isl.Height(x, z);
            if (h < IslandGenerator.SeaLevel + 1.5f) continue;
            if (kind == 1 && h > IslandGenerator.SeaLevel + 4f) continue;      // beach: right by the water
            if (kind == 2 && h < 60f) continue;                                // mountain: properly up
            if (kind == 4 && Vector2.Distance(new Vector2(x, z), new Vector2(470f, 120f)) > 130f) continue;
            // stand on the real terrain, on something walkable, with the sky open above
            if (!Physics.Raycast(new Vector3(x, h + 30f, z), Vector3.down, out RaycastHit hit, 60f, mask, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.9f) continue;
            Vector3 feet = hit.point + Vector3.up * 0.05f;
            if (Physics.CheckBox(feet + Vector3.up * 1.1f, new Vector3(2.2f, 0.95f, 2.2f), Quaternion.identity, mask, QueryTriggerInteraction.Ignore)) continue;
            if (Physics.Raycast(feet + Vector3.up * 2f, Vector3.up, 30f, mask, QueryTriggerInteraction.Ignore)) continue;
            spot = feet; place = (byte)kind;
            return true;
        }
        return false;
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

    /// <summary>Fell off their roof somehow (knocked over the edge, blown off, a fire...)? After a
    /// few seconds they turn up back on the roof, dusting themselves off, so a rescue never gets
    /// stuck with them lying in the street where the hook can't reach.</summary>
    void CheckFellOffRoof()
    {
        if (body.Feet.y > home.y - 3f) { offRoofSince = -1f; lastOnRoof = body.Feet; return; }
        if (offRoofSince < 0f) { offRoofSince = Time.time; Debug.Log($"[Survivor] {displayName} went over the edge at {lastOnRoof} (fire {(fire != null && fire.Active ? "burning" : "none")}, knocked {body.IsKnockedDown})"); return; }
        if (Time.time - offRoofSince < 5f) return;
        offRoofSince = -1f;
        Vector3 back = fire != null && fire.Active ? fire.SafestSpot() + Vector3.up * 0.05f : home;
        var rb = GetComponent<Rigidbody>();
        body.StandUp(back, null);
        rb.position = transform.position;
        rb.linearVelocity = Vector3.zero;
        var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (nt) nt.Teleport(transform.position, transform.rotation, transform.localScale);
        Say(L.fell, 3f);
        Debug.Log($"[Survivor] {displayName} fell off the roof: back up at {back}");
    }
    float offRoofSince = -1f; Vector3 lastOnRoof;

    // ------------------------------------------------------------------ Ledge Resident (host)

    float patience, nextSwat;
    const float PatienceNeeded = 10f;

    /// <summary>Not having it: swats the hook away and tells the crew off, until they've hovered
    /// close by and patiently for long enough. Returns true while he's still refusing.</summary>
    bool Refusing(ref PlayerInput input)
    {
        Vector3 toHeli = heli.transform.position - transform.position;
        var hrb = heli.GetComponent<Rigidbody>();
        Vector3 hv = hrb.linearVelocity;
        bool close = toHeli.magnitude < 15f, steady = new Vector2(hv.x, hv.z).magnitude < 3f;
        if (close && steady) patience += Time.fixedDeltaTime;
        if (patience >= PatienceNeeded)
        {
            Agreed.Value = true;
            Say(L.agree, 3.5f);
            if (RescueStartedAt < 0f) RescueStartedAt = Time.time;
            return false;
        }
        // Shake a fist at them.
        input.pitch = -40f;
        input.grabRight = Mathf.Repeat(Time.time * 2.2f, 1f) < 0.5f;
        if (HookAvailable(out Vector3 hookPos) && Vector3.Distance(body.Chest, hookPos) < 1.6f && Time.time > nextSwat)
        {
            // Bat it away.
            Vector3 away = Flat(hookPos - body.Chest); if (away.sqrMagnitude < 0.01f) away = Flat(toHeli);
            winch.hook.AddForce(away.normalized * 1.8f + Vector3.up * 0.3f, ForceMode.VelocityChange);
            Say(Pick(L.refuse), 2f);
            nextSwat = Time.time + 1.8f;
            nextRemark = Time.time + 4f;
        }
        else if (close && Time.time > nextRemark) { Say(Pick(L.waiting), 2.5f); nextRemark = Time.time + 6f; }
        return true;
    }

    /// <summary>Is that (flattened) point somewhere he can stand on his ledge?</summary>
    bool OnLedge(Vector3 p)
    {
        var ls = LedgeSpot.Value;
        Vector3 local = Quaternion.Euler(0f, -ls.w, 0f) * Flat(p - new Vector3(ls.x, ls.y, ls.z)); // z = out from the wall, x = along it
        return Mathf.Abs(local.x) < 1.3f && local.z < 1.1f && local.z > -0.3f;
    }

    /// <summary>Put him on a ledge a few metres under the roofline of some building, facing open street.</summary>
    bool PlaceOnLedge()
    {
        if (!FindLedgeSpot(out Vector3 stand, out float facing)) return false;
        LedgeSpot.Value = new Vector4(stand.x, stand.y, stand.z, facing);
        GetComponent<LedgePerch>()?.Rebuild(); // the slab under him now, before he's dropped onto it
        var rb = GetComponent<Rigidbody>();
        body.StandUp(stand + Vector3.up * 0.05f, null);
        transform.rotation = Quaternion.Euler(0f, facing, 0f);
        rb.position = transform.position; rb.rotation = transform.rotation;
        rb.linearVelocity = Vector3.zero;
        var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (nt) nt.Teleport(transform.position, transform.rotation, transform.localScale);
        home = stand;
        patience = 0f; Agreed.Value = false;
        Debug.Log($"[Survivor] {displayName} is on a ledge at {stand}, facing {facing:0}");
        return true;
    }

    bool FindLedgeSpot(out Vector3 stand, out float facing)
    {
        stand = default; facing = 0f;
        var boxes = new System.Collections.Generic.List<BoxCollider>();
        foreach (var c in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
        {
            if (c.isTrigger || c.attachedRigidbody != null || c.gameObject.layer == gameObject.layer) continue;
            var b = c.bounds;
            if (b.max.y < 14f || b.max.y > 34f || b.size.x < 8f || b.size.z < 8f || b.min.y > 1f) continue; // whole buildings, standing on the ground
            float d = Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(transform.position.x, transform.position.z));
            if (d < 40f || d > 160f || new Vector2(b.center.x, b.center.z).magnitude < 30f) continue;
            bool nearDropOff = false;
            foreach (var z in RescueZone.All) if (Vector3.Distance(z.transform.position, b.center) < 35f) nearDropOff = true;
            if (!nearDropOff) boxes.Add(c);
        }
        int mask = GameLayers.Gameplay & ~(1 << gameObject.layer);
        for (int attempt = 0; attempt < 200 && boxes.Count > 0; attempt++)
        {
            var c = boxes[Random.Range(0, boxes.Count)];
            var t = c.transform;
            Vector3 half = Vector3.Scale(c.size, t.lossyScale) * 0.5f, centre = t.TransformPoint(c.center);
            int face = Random.Range(0, 4);
            Vector3 n = face switch { 0 => t.forward, 1 => -t.forward, 2 => t.right, _ => -t.right };
            Vector3 along = Vector3.Cross(Vector3.up, n);
            float depth = face < 2 ? half.z : half.x, width = face < 2 ? half.x : half.z;
            if (width < 4f) continue;
            float top = c.bounds.max.y;
            Vector3 wallPoint = centre + n * depth + along * Random.Range(-width + 3f, width - 3f);
            wallPoint.y = top - 3.4f;
            // the wall really is here (not an alcove or a neighbour in the way)
            if (!Physics.Raycast(wallPoint + n * 1.5f, -n, out RaycastHit wall, 2.5f, mask, QueryTriggerInteraction.Ignore) || wall.collider != c) continue;
            Vector3 s = wall.point + n * 0.45f;
            // open air out front for the helicopter, and nothing overhead between the hook and him
            if (Physics.Raycast(s + Vector3.up * 1f, n, 16f, mask, QueryTriggerInteraction.Ignore)) continue;
            if (Physics.CheckBox(s + n * 2.5f + Vector3.up * 9f, new Vector3(6.5f, 3f, 6.5f), Quaternion.LookRotation(n), mask, QueryTriggerInteraction.Ignore)) continue;
            if (Physics.Raycast(s + Vector3.up * 0.3f, Vector3.up, 9f, mask, QueryTriggerInteraction.Ignore)) continue;
            if (Physics.CheckBox(s + Vector3.up * 1.1f + n * 0.1f, new Vector3(1.6f, 0.9f, 0.35f), Quaternion.LookRotation(n), mask, QueryTriggerInteraction.Ignore)) continue;
            // a sheer drop all round (not a hop onto the neighbour's roof)
            bool sheer = true;
            foreach (var off in new[] { along * 3f, -along * 3f, n * 1.6f, n * 1.6f + along * 2f, n * 1.6f - along * 2f })
                if (Physics.Raycast(s + off + Vector3.up * 0.5f, Vector3.down, 7f, mask, QueryTriggerInteraction.Ignore)) { sheer = false; break; }
            if (!sheer) continue;
            stand = s;
            facing = Quaternion.LookRotation(n).eulerAngles.y;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ roof fire (host)

    static double Now => NetworkManager.Singleton.ServerTime.Time;

    void ScheduleFire() => fireAt = fire != null && Random.value < FireChance ? Time.time + (FireChance >= 1f ? Random.Range(3f, 5f) : Random.Range(12f, 35f)) : -1f; // "-fire 1" (testing): right away

    void ResetFire()
    {
        Fire.Value = default;
        Singed.Value = false;
        ScheduleFire();
    }

    /// <summary>Light up the roof they're standing on, starting some way off from them.</summary>
    void StartFire()
    {
        Vector3 at = chair != null ? chair.transform.position : body.Feet;
        // The roof under them (looking past their own chair, or anything else that moves).
        RaycastHit hit = default; bool found = false; float nearest = float.MaxValue;
        foreach (var h in Physics.RaycastAll(at + Vector3.up * 1f, Vector3.down, 3f, GameLayers.Gameplay & ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
            if (h.rigidbody == null && h.collider is BoxCollider && h.distance < nearest) { nearest = h.distance; hit = h; found = true; }
        if (!found) return;
        var b = hit.collider.bounds;
        if (b.size.x < 6f || b.size.z < 6f) return;
        // Somewhere 6+ m away if the roof allows it (so there's a minute before it reaches them).
        Vector3 origin = at; float best = -1f;
        for (int i = 0; i < 12; i++)
        {
            var p = new Vector3(Random.Range(b.min.x + 1.5f, b.max.x - 1.5f), b.max.y, Random.Range(b.min.z + 1.5f, b.max.z - 1.5f));
            float d = Flat(p - at).magnitude;
            float score = d >= 6f ? 100f - d : d; // the nearest spot that's still 6 m off, else the farthest
            if (score > best) { best = score; origin = p; }
        }
        Fire.Value = new FireState { origin = origin, roofMin = b.min, roofMax = b.max, start = Now, end = double.MaxValue };
        nextFireRemark = 0f;
        FireRemark();
        Debug.Log($"[Fire] {displayName}'s roof caught fire {Flat(origin - at).magnitude:0.0} m away ({b.size.x:0}x{b.size.z:0} m roof)");
    }

    void FireRemark()
    {
        if (Time.time < nextFireRemark || L.fire == null) return;
        Say(Pick(L.fire), 3f);
        nextFireRemark = Time.time + Random.Range(6f, 9f);
        nextRemark = Time.time + 8f;
    }

    /// <summary>Host: the flames got them (RoofFire calls this).</summary>
    public void Burned()
    {
        if (Singed.Value) return;
        Singed.Value = true;
        Debug.Log($"[Fire] {displayName} got singed");
        Say(L.singed, 3f);
        nextFireRemark = Time.time + 5f;
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
        if (s == State.Seated && Fire.Value.start > 0.0 && Fire.Value.end > Now + 12.0)
        {
            var f = Fire.Value; f.end = Now + 8.0; Fire.Value = f; // they're out: the fire brigade can have it
        }
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

    /// <summary>Cabin seats in use (by survivors or crew) on the host.</summary>
    public static readonly System.Collections.Generic.HashSet<Transform> TakenSeats = new();
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
