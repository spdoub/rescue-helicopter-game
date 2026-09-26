using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Automated playtesting. Does nothing unless started with command-line switches:
///   -scenario name      a bot plays a scripted scenario (walk, board, jumpoff, passenger, chaos, idle)
///   -pilotbot name      if this machine ends up flying, a bot flies a pattern (hover, tour, aggressive)
///   -seed n             random seed for chaos
///   -duration s         quit after this many seconds (default 30)
///   -out dir            write report.txt, telemetry.csv (and frames/ if recording) here
///   -record             save a screenshot every frame (needs a window; time runs in fixed 1/fps steps)
///   -recfps n           recording frame rate (default 15)
///   -recview fp|front|side|chase   camera used while recording (default side)
///
/// While running it watches for problems and writes them to report.txt:
/// errors in the log, anything falling through the world or going NaN, the helicopter spinning or
/// flipping by itself, the camera inside your own body or inside scenery, boots sinking into the
/// ground, feet sliding while planted, and players stuck while trying to move.
/// </summary>
[DefaultExecutionOrder(-100)]
public class PlaytestDirector : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    string scenario, pilotBot, outDir;
    float duration = 30f;
    bool record;
    int recFps = 15;
    System.Random rng = new(1);
    float doorSide = -1f; // which door (heli-local x sign) the bot last pressed F at

    float startTime;
    int frame;
    StreamWriter telemetry;
    readonly Dictionary<string, Issue> issues = new();
    readonly List<string> timeline = new();

    class Issue { public string kind; public int count; public float first; public string detail; }

    // bot state
    readonly Queue<Step> steps = new();
    Step current;
    float stepStart;
    float StepTime => Time.time - stepStart;

    // check state
    float heliSpinTime, stuckTime, spawnedAt = -1f;
    Vector3 lastLeftFoot, lastRightFoot;
    float slideSum; int slideSamples;

    public static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-scenario") < 0 && Array.IndexOf(args, "-pilotbot") < 0 && Array.IndexOf(args, "-out") < 0) return;
        var go = new GameObject("PlaytestDirector");
        DontDestroyOnLoad(go);
        go.AddComponent<PlaytestDirector>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBoot() => Boot();

    static string Arg(string key, string fallback = null)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, key);
        return i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : fallback;
    }

    static bool Flag(string key) => Array.IndexOf(Environment.GetCommandLineArgs(), key) >= 0;

    void Awake()
    {
        scenario = Arg("-scenario");
        pilotBot = Arg("-pilotbot");
        outDir = Arg("-out", Path.Combine(Application.temporaryCachePath, "playtest"));
        duration = float.Parse(Arg("-duration", "30"), Inv);
        record = Flag("-record");
        recFps = int.Parse(Arg("-recfps", "15"), Inv);
        rng = new System.Random(int.Parse(Arg("-seed", "1"), Inv));
        Directory.CreateDirectory(outDir);
        if (record) Directory.CreateDirectory(Path.Combine(outDir, "frames"));

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60; // steady, realistic frame rate for measurements
        if (record)
        {
            Time.captureFramerate = recFps; // game time advances exactly 1/fps per rendered frame
            string v = Arg("-recview", "side");
            FirstPersonSeatCamera.ForcedView = v switch
            {
                "fp" => FirstPersonSeatCamera.View.FirstPerson,
                "front" => FirstPersonSeatCamera.View.Front,
                "chase" => FirstPersonSeatCamera.View.HeliChase,
                _ => FirstPersonSeatCamera.View.Side,
            };
        }

        if (pilotBot != null) NetworkHelicopter.BotPilot = PilotPattern(pilotBot);
        Application.logMessageReceived += OnLog;

        telemetry = new StreamWriter(Path.Combine(outDir, "telemetry.csv"));
        telemetry.WriteLine("t,unixms,role,heliX,heliY,heliZ,heliPitch,heliYaw,heliRoll,heliSpin,pilot," +
                            "px,py,pz,grounded,knocked,seated,onheli,camX,camY,camZ,lfX,lfY,lfZ,rfX,rfY,rfZ,speed," +
                            "winchLen,winchMotion,hookX,hookY,hookZ,survState,sX,sY,sZ,nearWinch,hull");
        startTime = Time.time;
        BuildScenario();
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        NetworkPlayer.BotOverride = null;
    }

    void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            string first = msg.Split('\n')[0];
            if (first.Contains("Screenshot")) return;
            Report("log-error", first.Length > 160 ? first.Substring(0, 160) : first);
        }
    }

    void Report(string kind, string detail)
    {
        float t = Time.time - startTime;
        if (!issues.TryGetValue(kind + detail, out var i))
        {
            issues[kind + detail] = i = new Issue { kind = kind, first = t, detail = detail };
            timeline.Add($"{t:F2}s  {kind}: {detail}");
        }
        i.count++;
    }

    // ------------------------------------------------------------------ main loop

    void Update()
    {
        float t = Time.time - startTime;
        if (t > duration) { Finish(); return; }

        if (record && frame % 1 == 0)
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "frames", $"f{frame:0000}.png"));
        frame++;

        RunBot();
        Check();
        Log(t);
    }

    NetworkPlayer Me => NetworkPlayer.Local;
    NetworkHelicopter heliCache;
    NetworkHelicopter Heli => heliCache ? heliCache : heliCache = FindAnyObjectByType<NetworkHelicopter>();

    // ------------------------------------------------------------------ bot

    abstract class Step
    {
        public float timeout = 20f;
        public bool expectTimeout; // running out of time is the point (e.g. walking into a closed door)
        public abstract bool Tick(PlaytestDirector d, float elapsed); // true = done
        public virtual string Name => GetType().Name;
    }

    class Act : Step
    {
        readonly float secs; readonly PlayerInput input; readonly float turnRate, pitch; readonly string label;
        public Act(string label, float secs, float moveX = 0, float moveZ = 0, bool sprint = false, float turnRate = 0, float pitch = float.NaN, bool grabL = false, bool grabR = false, sbyte winch = 0)
        { this.label = label; this.secs = secs; input = new PlayerInput { moveX = moveX, moveZ = moveZ, sprint = sprint, grabLeft = grabL, grabRight = grabR, winch = winch }; this.turnRate = turnRate; this.pitch = pitch; }
        public override string Name => label;
        public override bool Tick(PlaytestDirector d, float e)
        {
            NetworkPlayer.BotOverride = input;
            if (turnRate != 0 || !float.IsNaN(pitch))
                d.Me.BotLook(d.Me.LookYaw + turnRate * Time.deltaTime, float.IsNaN(pitch) ? d.Me.LookPitch : pitch);
            return e >= secs;
        }
    }

    class Face : Step
    {
        readonly float offset;
        public Face(float offset) { this.offset = offset; }
        public override string Name => $"turn {offset:F0}";
        public override bool Tick(PlaytestDirector d, float e) { d.Me.BotLook(d.Me.LookYaw + offset, 0f); return true; }
    }

    class PressDoor : Step
    {
        public override string Name => "press F (door)";
        public override bool Tick(PlaytestDirector d, float e)
        {
            d.doorSide = Mathf.Sign(d.Heli.transform.InverseTransformPoint(d.Me.transform.position).x);
            d.Me.TryToggleNearestDoor(); return true;
        }
    }

    /// <summary>At the winch box: hold on to it with the left hand (like a real operator in a moving
    /// helicopter) and hold the winch key (+1 in, -1 out, 0 just wait) until a condition is met.</summary>
    class WinchUntil : Step
    {
        readonly string label; readonly sbyte dir; readonly Func<PlaytestDirector, bool> cond;
        public WinchUntil(string label, sbyte dir, Func<PlaytestDirector, bool> cond, float timeout) { this.label = label; this.dir = dir; this.cond = cond; this.timeout = timeout; }
        public override string Name => label;
        public override bool Tick(PlaytestDirector d, float e)
        {
            var w = d.Heli.GetComponent<NetworkWinch>();
            Vector3 to = w.ControlPoint - d.Me.GetComponent<CrewBody>().Eye;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.0001f)
                d.Me.BotLook(Quaternion.LookRotation(flat).eulerAngles.y, -Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
            bool done = cond(d);
            NetworkPlayer.BotOverride = new PlayerInput { winch = done ? (sbyte)0 : dir, grabLeft = true };
            return done;
        }
    }

    /// <summary>Do something once (a key press, a measurement).</summary>
    class Lambda : Step
    {
        readonly string label; readonly Action<PlaytestDirector> act;
        public Lambda(string label, Action<PlaytestDirector> act) { this.label = label; this.act = act; }
        public override string Name => label;
        public override bool Tick(PlaytestDirector d, float e) { act(d); return true; }
    }

    /// <summary>Walk up to someone, grab them with both hands and back off (drag them) until a condition holds.</summary>
    class Chase : Step
    {
        readonly string label; readonly Func<PlaytestDirector, Transform> who; readonly Func<PlaytestDirector, bool> cond;
        public Chase(string label, Func<PlaytestDirector, Transform> who, Func<PlaytestDirector, bool> cond, float timeout) { this.label = label; this.who = who; this.cond = cond; this.timeout = timeout; }
        public override string Name => label;
        public override bool Tick(PlaytestDirector d, float e)
        {
            if (cond(d)) { NetworkPlayer.BotOverride = default(PlayerInput); return true; }
            var t = who(d);
            Vector3 to = t.position + Vector3.up * 0.2f - d.Me.GetComponent<CrewBody>().Eye;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.0001f) d.Me.BotLook(Quaternion.LookRotation(flat).eulerAngles.y, -Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
            var body = d.Me.GetComponent<CrewBody>();
            bool holding = body.Left.Holding || body.Right.Holding;
            NetworkPlayer.BotOverride = new PlayerInput { moveZ = holding ? -1f : (flat.magnitude > 0.9f ? 1f : 0f), grabLeft = true, grabRight = true };
            return false;
        }
    }

    class Jump : Step
    {
        public override bool Tick(PlaytestDirector d, float e) { NetworkPlayer.BotJumps = 1; return true; }
    }

    class Seat : Step
    {
        public override bool Tick(PlaytestDirector d, float e) { NetworkPlayer.BotOverride = default(PlayerInput); d.Me.BotToggleSeat(); return true; }
    }

    class Until : Step
    {
        readonly Func<PlaytestDirector, bool> cond; readonly string label;
        public Until(string label, Func<PlaytestDirector, bool> cond, float timeout) { this.label = label; this.cond = cond; this.timeout = timeout; }
        public override string Name => label;
        public override bool Tick(PlaytestDirector d, float e) { NetworkPlayer.BotOverride = default(PlayerInput); return cond(d); }
    }

    /// <summary>Walk toward a point, facing it, hopping when blocked.</summary>
    class GoTo : Step
    {
        readonly Func<PlaytestDirector, Vector3> target; readonly float within; readonly string label; float lastProgress; float bestDist = 1e9f;
        public GoTo(string label, Func<PlaytestDirector, Vector3> target, float within, float timeout = 15f) { this.label = label; this.target = target; this.within = within; this.timeout = timeout; }
        public override string Name => label;
        public override bool Tick(PlaytestDirector d, float e)
        {
            Vector3 to = target(d) - d.Me.transform.position; float up = to.y; to.y = 0f;
            float dist = to.magnitude;
            if (dist < within) { NetworkPlayer.BotOverride = default(PlayerInput); return true; }
            float yaw = Quaternion.LookRotation(to).eulerAngles.y;
            d.Me.BotLook(Mathf.MoveTowardsAngle(d.Me.LookYaw, yaw, 360f * Time.deltaTime), 10f);
            NetworkPlayer.BotOverride = new PlayerInput { moveZ = 1f };
            if (dist < bestDist - 0.05f) { bestDist = dist; lastProgress = e; }
            if (e - lastProgress > 0.6f || (up > 0.4f && dist < 1.6f)) { NetworkPlayer.BotJumps = 1; lastProgress = e; }
            return false;
        }
    }

    class Chaos : Step
    {
        float next; PlayerInput cur; float turn; float pitch;
        public Chaos(float secs) { timeout = secs; }
        public override bool Tick(PlaytestDirector d, float e)
        {
            var r = d.rng;
            if (e >= next)
            {
                next = e + 0.2f + (float)r.NextDouble() * 0.9f;
                cur = new PlayerInput
                {
                    moveX = r.Next(3) - 1, moveZ = r.Next(3) - 1, sprint = r.Next(3) == 0,
                    grabLeft = r.Next(4) == 0, grabRight = r.Next(4) == 0,
                };
                turn = ((float)r.NextDouble() * 2f - 1f) * 400f;
                pitch = ((float)r.NextDouble() * 2f - 1f) * 85f;
                if (r.Next(3) == 0) NetworkPlayer.BotJumps = 1;
                if (r.Next(25) == 0) d.Me.BotToggleSeat();
            }
            NetworkPlayer.BotOverride = cur;
            d.Me.BotLook(d.Me.LookYaw + turn * Time.deltaTime, Mathf.MoveTowards(d.Me.LookPitch, pitch, 200f * Time.deltaTime));
            return false; // runs until its timeout
        }
    }

    Vector3 DoorPoint(float side) => Heli.transform.TransformPoint(new Vector3(side * 1.25f, 1f, 0.6f));

    void BuildScenario()
    {
        steps.Clear();
        void Add(Step s) => steps.Enqueue(s);
        Add(new Until("wait for spawn", d => d.Me != null && d.Me.IsSpawned && d.Heli != null && d.Heli.IsSpawned, 15f));
        Add(new Act("settle", 1f));
        Func<PlaytestDirector, Vector3> door = d => d.DoorPoint(Mathf.Sign(d.Heli.transform.InverseTransformPoint(d.Me.transform.position).x));
        Func<PlaytestDirector, Vector3> cabin = d => d.Heli.transform.TransformPoint(new Vector3(
            0.3f * Mathf.Sign(d.Heli.transform.InverseTransformPoint(d.Me.transform.position).x), 1.4f, 0.6f));

        switch (scenario)
        {
            case "walk":
                Add(new Face(180f)); // face away from the helicopter: open ground
                Add(new Act("settle", 0.8f));
                Add(new Act("walk forward", 2f, moveZ: 1));
                Add(new Act("stop", 1f));
                Add(new Act("walk back", 2f, moveZ: -1));
                Add(new Act("stop", 1f));
                Add(new Act("strafe right", 2f, moveX: 1));
                Add(new Act("strafe left", 2f, moveX: -1));
                Add(new Act("diagonal", 2f, moveX: 1, moveZ: 1));
                Add(new Act("sprint", 2.5f, moveZ: 1, sprint: true));
                Add(new Act("stop from sprint", 1.5f));
                Add(new Act("turn in place", 1.5f, turnRate: 180));
                Add(new Jump()); Add(new Act("jump", 1.2f));
                Add(new Jump()); Add(new Act("running jump", 1.5f, moveZ: 1, sprint: true));
                Add(new Act("look down walking", 2f, moveZ: 1, pitch: 80));
                Add(new Act("look down sprinting", 2f, moveZ: 1, sprint: true, pitch: 80));
                Add(new Act("reach both", 1.5f, pitch: 0, grabL: true, grabR: true));
                Add(new Act("idle", 2f));
                break;

            case "board":
                Add(new GoTo("walk to door", door, 0.6f));
                Add(new GoTo("climb in", cabin, 0.35f, 8f));
                Add(new Until("inside cabin", d => d.Me.OnHelicopter.Value, 3f));
                Add(new GoTo("walk to cockpit", d => d.Heli.pilotSeat.position, 0.9f, 8f));
                Add(new Seat());
                Add(new Until("seated", d => d.Me.SeatedAsPilot.Value, 3f));
                Add(new Act("fly", 12f));
                Add(new Seat());
                Add(new Act("stand up", 2f));
                break;

            case "jumpoff": // host crew: board and wait at the door while someone else takes off, then jump
                Add(new GoTo("walk to door", door, 0.6f));
                Add(new GoTo("climb in", cabin, 0.35f, 8f));
                Add(new Until("inside cabin", d => d.Me.OnHelicopter.Value, 3f));
                Add(new Act("step in", 0.6f, moveX: 0.6f));
                Add(new Until("airborne 6m", d => d.Heli.transform.position.y > 6f, 30f));
                Add(new GoTo("walk out the door", d => d.Heli.transform.TransformPoint(new Vector3(
                    Mathf.Sign(d.Heli.transform.InverseTransformPoint(d.Me.transform.position).x) * 4f, 1f, 0.6f)), 0.3f, 4f));
                Add(new Act("falling", 5f));
                break;

            case "winch": // board, go to the winch box, let the hook down, reel it back up
            {
                NetworkWinch W(PlaytestDirector d) => d.Heli.GetComponent<NetworkWinch>();
                Add(new GoTo("walk to door", door, 0.6f));
                Add(new GoTo("climb in", cabin, 0.35f, 8f));
                Add(new Until("inside cabin", d => d.Me.OnHelicopter.Value, 3f));
                Add(new GoTo("to the winch box", d => d.Heli.transform.TransformPoint(new Vector3(0.45f, 1f, 1.0f)), 0.25f, 6f));
                Add(new Until("at the winch box", d => d.Me.NearWinch, 2f));
                Add(new Act("hold Q: let the hook down", 4f, winch: -1));
                Add(new Until("cable out 5 m", d => W(d).Length.Value > 5f, 1f));
                Add(new Act("hold R: reel in", 4.5f, winch: 1));
                Add(new Until("hook back up", d => W(d).Length.Value < 0.5f, 2f));
                Add(new Until("hook stowed at the hoist", d => Vector3.Distance(W(d).HookEyePosition, W(d).TipPosition) < 0.6f, 3f));
                break;
            }

            case "upgrade": // walk to the upgrade board with $5,000, buy the winch motor, then the stabiliser twice (second one: can't afford)
            {
                Transform B() => UpgradeBoard.Instance.transform;
                int Cash() => RescueZone.All[0].Money.Value;
                HeliUpgrades U() => HeliUpgrades.Instance;
                Vector3 CardAt(int i) => B().TransformPoint(new Vector3(i % 2 == 0 ? -0.64f : 0.64f, i < 2 ? 1.77f : 1.14f, -0.045f));
                void LookAtCard(PlaytestDirector d, int i)
                {
                    Vector3 to = CardAt(i) - d.Me.GetComponent<CrewBody>().Eye, flat = new Vector3(to.x, 0f, to.z);
                    d.Me.BotLook(Quaternion.LookRotation(flat).eulerAngles.y, -Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
                }
                Add(new Until("board and upgrades exist", d => UpgradeBoard.Instance != null && U() != null && U().IsSpawned && RescueZone.All.Count > 0, 5f));
                Add(new Lambda("company gets $5,000", d => RescueZone.All[0].Money.Value = 5000));
                Add(new GoTo("walk to the board", d => B().position - B().forward * 1.7f, 0.4f, 20f));
                Add(new Lambda("look at WINCH MOTOR", d => LookAtCard(d, 0)));
                Add(new Act("settle", 0.6f));
                Add(new Until("aiming at the winch card", d => UpgradeBoard.Instance.Aimed(Camera.main) == 0, 2f));
                Add(new Lambda("press F", d => d.Me.Use()));
                Add(new Until("winch motor fitted, $1,200 paid", d => U().WinchLevel.Value == 1 && Cash() == 3800, 3f));
                Add(new Lambda("look at STABILISER", d => LookAtCard(d, 3)));
                Add(new Act("settle", 0.6f));
                Add(new Lambda("press F", d => d.Me.Use()));
                Add(new Until("stabiliser fitted, $1,400 paid", d => U().StabiliserLevel.Value == 1 && Cash() == 2400, 3f));
                Add(new Lambda("press F again", d => d.Me.Use()));
                Add(new Act("wait", 1.5f));
                Add(new Until("can't afford level II: nothing changes", d => U().StabiliserLevel.Value == 1 && Cash() == 2400 && U().LastPurchase.Value.ToString().StartsWith("Can't afford"), 2f));
                Add(new Face(180f));
                Add(new Act("look at the helicopter", 2f));
                break;
            }

            case "rescue_crew": // winch operator for a full rescue (someone else flies with the "rescue" pilot bot)
                RescueRounds(SurvivorBrain.Kind.StrongGrandma, 2, door, cabin);
                break;

            case "rescue_ledge": // Ledge Resident: hover by his ledge until he stops refusing, then winch him (pilot bot "rescue-ledge")
                RescueRounds(SurvivorBrain.Kind.LedgeResident, 1, door, cabin);
                break;

            case "rescue_recliner": // same, for Recliner Guy (pilot bot "rescue-recliner")
                RescueRounds(SurvivorBrain.Kind.ReclinerGuy, 1, door, cabin);
                break;

            case "backseat_meddle": // pick up the Backseat Pilot, hover; when he goes for the controls, grab him and pull him back
            {
                SurvivorBrain G() => SurvivorBrain.Find(SurvivorBrain.Kind.BackseatPilot);
                RescueRounds(SurvivorBrain.Kind.BackseatPilot, 0, door, cabin);  // board + winch box only
                NetworkWinch W(PlaytestDirector d) => d.Heli.GetComponent<NetworkWinch>();
                Add(new WinchUntil("over the survivor", 0, d => { var g = G(); Vector3 tip = W(d).TipPosition, gp = g.transform.position; tip.y = gp.y = 0f; return Vector3.Distance(tip, gp) < 2f && d.Heli.transform.position.y > gp.y + 4f; }, 60f));
                Add(new WinchUntil("let the hook down", -1, d => W(d).HookEyePosition.y < G().transform.position.y + 0.6f || G().Current != SurvivorBrain.State.Waiting, 20f));
                Add(new WinchUntil("survivor hooked", 0, d => G().Current == SurvivorBrain.State.Hooked, 25f));
                Add(new WinchUntil("reel them up", 1, d => G().Current == SurvivorBrain.State.Seated, 40f));
                Add(new WinchUntil("he goes for the controls", 0, d => G().Current == SurvivorBrain.State.Meddling, 60f));
                Add(new Chase("grab him and pull him back", d => G().transform, d => G().Current == SurvivorBrain.State.Seated, 20f));
                break;
            }

            case "rescue_dogperson": // Dog Person: first the dog (hold the hook low and still), then her (pilot bot "rescue-dogperson")
            {
                SurvivorBrain G() => SurvivorBrain.Find(SurvivorBrain.Kind.DogPerson);
                NetworkWinch W(PlaytestDirector d) => d.Heli.GetComponent<NetworkWinch>();
                RescueRounds(SurvivorBrain.Kind.DogPerson, 0, door, cabin);  // board + winch box
                Add(new WinchUntil("over the roof", 0, d => { var g = G(); Vector3 tip = W(d).TipPosition, gp = g.transform.position; tip.y = gp.y = 0f; return Vector3.Distance(tip, gp) < 2f && d.Heli.transform.position.y > gp.y + 4f; }, 60f));
                Add(new WinchUntil("lower the hook to dog height", -1, d => W(d).HookEyePosition.y < G().transform.position.y + 0.1f, 20f));
                Add(new WinchUntil("hold it still until Biscuit bites", 0, d => G().dog.Current == Dog.State.Latched, 60f));
                Add(new WinchUntil("reel Biscuit up", 1, d => G().dog.Aboard, 30f));
                Add(new WinchUntil("lower the hook to her", -1, d => W(d).HookEyePosition.y < G().transform.position.y + 0.6f || G().Current != SurvivorBrain.State.Waiting, 20f));
                Add(new WinchUntil("she grabs the hook", 0, d => G().Current == SurvivorBrain.State.Hooked, 25f));
                Add(new WinchUntil("reel her up", 1, d => G().Current == SurvivorBrain.State.Boarding || G().Current == SurvivorBrain.State.Seated, 40f));
                Add(new WinchUntil("delivered to the hospital", 0, d => G().Current == SurvivorBrain.State.Delivered, 150f));
                Add(new WinchUntil("payout", 0, d => RescueZone.All.Count > 0 && RescueZone.All[0].Rescued.Value >= 1, 20f));
                break;
            }

            case "rescue_backseat": // same, for Backseat Pilot (pilot bot "rescue-backseat")
                RescueRounds(SurvivorBrain.Kind.BackseatPilot, 1, door, cabin);
                break;

            case "solo_hover": // one player: take off, climb, get out of the seat - the helicopter should hover on its own
            {
                Vector3 leftAt = default;
                Add(new GoTo("walk to door", door, 0.6f));
                Add(new GoTo("climb in", cabin, 0.35f, 8f));
                Add(new Until("inside cabin", d => d.Me.OnHelicopter.Value, 3f));
                Add(new GoTo("to the pilot seat", d => d.Heli.pilotSeat.position, 0.6f, 8f) { expectTimeout = true });
                Add(new Lambda("sit down (E)", d => d.Me.BotToggleSeat()));
                Add(new Until("seated", d => d.Me.SeatedAsPilot.Value, 3f));
                Add(new Lambda("pilot: climb", d => NetworkHelicopter.BotPilot = t => new HelicopterInput { collective = t < 3.5f ? 1f : 0f }));
                Add(new Act("climbing", 7f));
                Add(new Lambda("get up (E)", d => { d.Me.BotToggleSeat(); leftAt = d.Heli.transform.position; }));
                Add(new Until("out of the seat", d => !d.Me.SeatedAsPilot.Value, 3f));
                Add(new Act("hovering with nobody flying", 20f));
                Add(new Lambda("measure drift", d =>
                {
                    Vector3 now = d.Heli.transform.position;
                    float drift = new Vector2(now.x - leftAt.x, now.z - leftAt.z).magnitude, sink = leftAt.y - now.y;
                    Debug.Log($"[Playtest] solo hover: drift {drift:F1} m, height change {-sink:F1} m in 20 s");
                    if (drift > 6f || sink > 3f) d.Report("unpiloted-drift", $"drifted {drift:F1} m, sank {sink:F1} m in 20 s");
                }));
                break;
            }

            case "crash": // fly flat out into the city until something gets hit; the hull must take damage (and recover on the pad)
            {
                Add(new GoTo("walk to door", door, 0.6f));
                Add(new GoTo("climb in", cabin, 0.35f, 8f));
                Add(new Until("inside cabin", d => d.Me.OnHelicopter.Value, 3f));
                Add(new GoTo("to the pilot seat", d => d.Heli.pilotSeat.position, 0.6f, 8f) { expectTimeout = true });
                Add(new Lambda("sit down (E)", d => d.Me.BotToggleSeat()));
                Add(new Until("seated", d => d.Me.SeatedAsPilot.Value, 3f));
                Add(new Lambda("pilot: up a bit, then full speed ahead", d => NetworkHelicopter.BotPilot = t => new HelicopterInput { collective = t < 1.5f ? 1f : 0f, pitch = t > 2f ? 1f : 0f }));
                Add(new Until("hit something", d => d.Heli.GetComponent<HeliDamage>().Health.Value < 0.95f, 40f));
                Add(new Lambda("report damage", d => Debug.Log($"[Playtest] crash: hull {d.Heli.GetComponent<HeliDamage>().Health.Value:P0} after impact")));
                break;
            }

            case "passenger": // stand in the cabin (holding on half the time) while someone flies hard
                Add(new GoTo("walk to door", door, 0.6f));
                Add(new GoTo("climb in", cabin, 0.35f, 8f));
                Add(new Until("inside cabin", d => d.Me.OnHelicopter.Value, 3f));
                Add(new Act("step in", 0.8f, moveX: 0.6f));
                Add(new Act("stand", 10f));
                Add(new Act("hold on", 10f, pitch: 40, grabL: true));
                Add(new Act("wander", 8f, moveZ: 0.5f, turnRate: 60));
                break;

            case "exitjump": // climb in, then jump out of the doorway several times: must never be knocked down
                for (int k = 0; k < 3; k++)
                {
                    Add(new GoTo("walk to door", door, 0.6f));
                    Add(new GoTo("climb in", cabin, 0.35f, 8f));
                    Add(new Until("inside cabin", d => d.Me.OnHelicopter.Value, 3f));
                    Add(new Act("settle", 0.5f));
                    Add(new GoTo("run to the doorway", d => d.Heli.transform.TransformPoint(new Vector3(
                        Mathf.Sign(d.Heli.transform.InverseTransformPoint(d.Me.transform.position).x) * 0.8f, 1f, 0.6f)), 0.25f, 4f));
                    Add(new Jump());
                    Add(new GoTo("leap out", d => d.Heli.transform.TransformPoint(new Vector3(
                        Mathf.Sign(d.Heli.transform.InverseTransformPoint(d.Me.transform.position).x) * 3.5f, 0f, 0.6f)), 0.4f, 3f));
                    Add(new Act("land", 1.5f));
                }
                break;

            case "door": // close the door from inside, try to walk out (must stay in), open it, walk out
                Add(new GoTo("walk to door", door, 0.6f));
                Add(new GoTo("climb in", cabin, 0.35f, 8f));
                Add(new Until("inside cabin", d => d.Me.OnHelicopter.Value, 3f));
                Add(new PressDoor());
                Add(new Act("door closing", 1.8f));
                Add(new Until("door closed", d => System.Array.Exists(FindObjectsByType<SlidingDoor>(FindObjectsSortMode.None), x => !x.IsOpen), 2f));
                Add(new GoTo("try to walk out (blocked)", d => d.Heli.transform.TransformPoint(new Vector3(d.doorSide * 3f, 1f, 0.6f)), 0.3f, 3f) { expectTimeout = true });
                Add(new Until("still inside", d => d.Me.OnHelicopter.Value, 1f));
                Add(new PressDoor());
                Add(new Act("door opening", 1.8f));
                Add(new GoTo("walk out", d => d.Heli.transform.TransformPoint(new Vector3(d.doorSide * 3f, 0f, 0.6f)), 0.4f, 5f));
                break;

            case "chaos":
                Add(new Chaos(duration));
                break;

            default: // idle / pilot-only machines
                break;
        }
    }

    void RunBot()
    {
        if (scenario == null) return;
        if (current == null)
        {
            if (steps.Count == 0) { NetworkPlayer.BotOverride = default(PlayerInput); return; }
            current = steps.Dequeue();
            stepStart = Time.time;
            timeline.Add($"{Time.time - startTime:F2}s  > {current.Name}");
        }
        if (Me == null && current is not Until) return;
        float e = Time.time - stepStart;
        bool done;
        try { done = current.Tick(this, e); }
        catch (Exception ex) { Report("bot-error", current.Name + ": " + ex.Message); done = true; }
        if (!done && e > current.timeout)
        {
            if (current is not Chaos && !current.expectTimeout) Report("bot-timeout", $"'{current.Name}' did not complete in {current.timeout:F0}s");
            done = true;
        }
        if (done) current = null;
    }

    /// <summary>Winch operator: board, hold the winch box, then for each call: wait until over the
    /// survivor, lower the hook to them, wait for them to be hooked, reel in, wait for delivery.</summary>
    SurvivorBrain.Kind trackKind = SurvivorBrain.Kind.StrongGrandma;

    void RescueRounds(SurvivorBrain.Kind kind, int rounds, Func<PlaytestDirector, Vector3> door, Func<PlaytestDirector, Vector3> cabin)
    {
        void Add(Step s) => steps.Enqueue(s);
        trackKind = kind; // telemetry follows the survivor being rescued
        NetworkWinch W(PlaytestDirector d) => d.Heli.GetComponent<NetworkWinch>();
        SurvivorBrain G() => SurvivorBrain.Find(kind);
        float LowerTo(SurvivorBrain g) => g.chair != null ? g.chair.liftPoint.position.y + 0.4f : g.transform.position.y + 0.6f;
        Add(new GoTo("walk to door", door, 0.6f));
        Add(new GoTo("climb in", cabin, 0.35f, 8f));
        Add(new Until("inside cabin", d => d.Me.OnHelicopter.Value, 3f));
        Add(new GoTo("to the winch box", d => d.Heli.transform.TransformPoint(new Vector3(0.45f, 1f, 1.0f)), 0.25f, 6f));
        Add(new Until("at the winch box", d => d.Me.NearWinch, 2f));
        for (int round = 1; round <= rounds; round++)
        {
            int r = round;
            Add(new WinchUntil("over the survivor", 0, d =>
            {
                var g = G(); if (g == null) return false;
                Vector3 tip = W(d).TipPosition, gp = g.chair != null ? g.chair.liftPoint.position : g.transform.position; tip.y = gp.y = 0f;
                return Vector3.Distance(tip, gp) < 2f && d.Heli.transform.position.y > g.transform.position.y + 4f;
            }, 60f));
            Add(new WinchUntil("let the hook down", -1, d => W(d).HookEyePosition.y < LowerTo(G()) || G().Current != SurvivorBrain.State.Waiting, 20f));
            Add(new WinchUntil("survivor hooked", 0, d => G().Current == SurvivorBrain.State.Hooked, 25f));
            Add(new WinchUntil("let them settle", 0, d => d.StepTime > 1.5f, 3f));
            Add(new WinchUntil("reel them up", 1, d => G().Current == SurvivorBrain.State.Boarding || G().Current == SurvivorBrain.State.Seated, 40f));
            Add(new WinchUntil("aboard", 0, d => G().Current == SurvivorBrain.State.Seated, 20f));
            Add(new WinchUntil("delivered to the hospital", 0, d => G().Current == SurvivorBrain.State.Delivered, 150f));
            Add(new WinchUntil($"payout #{r}", 0, d => RescueZone.All.Count > 0 && RescueZone.All[0].Rescued.Value >= r, 20f));
            if (r < rounds) Add(new WinchUntil("next call comes in", 0, d => G().Current == SurvivorBrain.State.Waiting, 40f));
        }
    }

    static Func<float, HelicopterInput> PilotPattern(string name) => name switch
    {
        "hover" => t => new HelicopterInput { collective = t < 3f ? 1f : 0f },
        "tour" => t =>
        {
            var i = new HelicopterInput();
            float p = t % 24f;
            if (p < 3f) i.collective = 1f;
            else if (p < 8f) i.pitch = 1f;
            else if (p < 11f) i.yaw = 1f;
            else if (p < 15f) i.pitch = 0.6f;
            else if (p < 17f) i.roll = 1f;
            else if (p < 20f) i.pitch = -1f;
            return i;
        },
        "aggressive" => t =>
        {
            var i = new HelicopterInput();
            if (t < 3f) { i.collective = 1f; return i; }
            i.pitch = Mathf.Sin(t * 1.3f);
            i.roll = Mathf.Sin(t * 0.9f + 1f);
            i.yaw = Mathf.Sin(t * 0.7f + 2f);
            i.collective = Mathf.Sin(t * 0.5f) * 0.5f;
            return i;
        },
        "takeoff-later" => t => new HelicopterInput { collective = t > 6f && t < 9f ? 1f : 0f },
        "rescue" => new RescuePilot().Tick,
        "rescue-recliner" => new RescuePilot(SurvivorBrain.Kind.ReclinerGuy).Tick,
        "rescue-backseat" => new RescuePilot(SurvivorBrain.Kind.BackseatPilot).Tick,
        "rescue-dogperson" => new RescuePilot(SurvivorBrain.Kind.DogPerson).Tick,
        "rescue-ledge" => new RescuePilot(SurvivorBrain.Kind.LedgeResident).Tick,
        "rescue-backseat-linger" => new RescuePilot(SurvivorBrain.Kind.BackseatPilot, 75f).Tick,
        _ => t => default,
    };

    // ------------------------------------------------------------------ checks

    void Check()
    {
        int gameplay = GameLayers.Gameplay & ~(1 << GameLayers.Crew);

        foreach (var p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            Vector3 pos = p.transform.position;
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y)) Report("nan", $"player {p.OwnerClientId} position is NaN");
            else if (pos.y < -5f) Report("fell-through-world", $"player {p.OwnerClientId} at y={pos.y:F1}");
        }

        var heli = Heli;
        if (heli != null && heli.IsSpawned)
        {
            var hrb = heli.GetComponent<Rigidbody>();
            if (hrb.position.y < -3f) Report("fell-through-world", "helicopter below ground");
            if (Vector3.Dot(heli.transform.up, Vector3.up) < 0.2f) Report("heli-flipped", $"up.y={heli.transform.up.y:F2}");
            bool noPilot = heli.PilotClientId.Value == NetworkHelicopter.NoPilot;
            if (NetworkManager.Singleton.IsServer && noPilot && hrb.angularVelocity.magnitude > 1.2f) heliSpinTime += Time.deltaTime; else heliSpinTime = 0f;
            if (heliSpinTime > 0.7f) Report("heli-spins-unpiloted", $"angular speed {hrb.angularVelocity.magnitude:F1} rad/s with nobody flying");
        }

        var me = Me;
        if (me == null || !me.IsSpawned) { spawnedAt = -1f; return; }
        if (spawnedAt < 0f) spawnedAt = Time.time;
        if (Time.time - spawnedAt < 1.5f) return;        // positions still syncing in
        if (Application.isBatchMode) return;             // headless copies don't animate: body checks need a window
        var cam = Camera.main;
        var vis = me.Visual;
        bool normal = !me.SeatedAsPilot.Value && !me.KnockedDown.Value && vis != null && !vis.Ragdolled;

        if (cam != null && normal && FirstPersonSeatCamera.CurrentView == FirstPersonSeatCamera.View.FirstPerson)
        {
            Vector3 c = cam.transform.position;
            Vector3 chest = vis.BonePosition(HumanBodyBones.Chest), hips = vis.BonePosition(HumanBodyBones.Hips);
            float d = DistanceToSegment(c, hips, chest);
            if (d < 0.2f) Report("camera-inside-body", $"camera {d:F2} m from own spine (look pitch {me.LookPitch:F0})");
            // (riding: compare against the helicopter's walls where they are for physics, like the camera does)
            bool riding = Heli && (me.OnHelicopter.Value || FirstPersonSeatCamera.InsideCabin(Heli.transform, me.transform.position));
            Vector3 cp = riding ? FirstPersonSeatCamera.RenderToPhysics(Heli.transform).MultiplyPoint3x4(c) : c;
            foreach (var col in Physics.OverlapSphere(cp, 0.04f, gameplay, QueryTriggerInteraction.Ignore))
                Report("camera-in-scenery", col.name);
        }

        if (vis != null && normal && me.Grounded.Value)
        {
            foreach (var (bone, isLeft) in new[] { (HumanBodyBones.LeftFoot, true), (HumanBodyBones.RightFoot, false) })
            {
                Vector3 f = vis.BonePosition(bone);
                RaycastHit hit;
                bool found = me.OnHelicopter.Value && Heli
                    ? FirstPersonSeatCamera.RaycastRiding(Heli.transform, f + Vector3.up * 0.5f, Vector3.down, out hit, 1f, gameplay)
                    : Physics.Raycast(f + Vector3.up * 0.5f, Vector3.down, out hit, 1f, gameplay, QueryTriggerInteraction.Ignore);
                if (found)
                {
                    float sink = hit.point.y - (f.y - 0.06f);
                    if (sink > 0.05f) Report("boot-in-ground", $"{(isLeft ? "left" : "right")} foot {sink:F2} m under the surface");
                }
                // foot slide: a planted foot (at the bottom of its stride, barely moving vertically)
                // should not move relative to the floor
                ref Vector3 last = ref (isLeft ? ref lastLeftFoot : ref lastRightFoot);
                float groundY = hit.collider != null ? hit.point.y : f.y;
                bool planted = f.y - groundY < 0.17f && Mathf.Abs(f.y - last.y) < 0.01f;
                if (last != Vector3.zero && planted && Time.deltaTime > 0f)
                {
                    Vector3 v = (f - last) / Time.deltaTime - me.GetComponent<CrewBody>().PlatformVelocity; v.y = 0f;
                    if (me.MoveSpeed.Value > 0.5f) { slideSum += v.magnitude; slideSamples++; }
                }
                last = f;
            }
        }

        if (NetworkPlayer.BotOverride.HasValue && normal)
        {
            var b = NetworkPlayer.BotOverride.Value;
            Vector3 wishDir = Quaternion.Euler(0f, me.LookYaw, 0f) * new Vector3(b.moveX, 0f, b.moveZ);
            bool wall = wishDir.sqrMagnitude > 0.01f && Physics.Raycast(me.transform.position, wishDir.normalized, 0.8f, gameplay, QueryTriggerInteraction.Ignore);
            bool wantsMove = Mathf.Abs(b.moveX) + Mathf.Abs(b.moveZ) > 0.5f && !wall; // walking into a wall is not "stuck"
            if (wantsMove && me.MoveSpeed.Value < 0.2f && !me.HoldLeft.Value && !me.HoldRight.Value) stuckTime += Time.deltaTime; else stuckTime = 0f;
            if (stuckTime > 2f) { Report("player-stuck", $"trying to move but not moving (near {Where(me.transform.position)})"); stuckTime = 0f; }
        }
    }

    string Where(Vector3 p)
    {
        if (Heli == null) return F(p);
        Vector3 l = Heli.transform.InverseTransformPoint(p);
        return $"heli-local ({l.x:F1},{l.y:F1},{l.z:F1})";
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector3.Distance(p, a + ab * t);
    }

    static string F(Vector3 v) => string.Format(Inv, "({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
    static string C(float f) => f.ToString("F3", Inv);

    void Log(float t)
    {
        var sb = new StringBuilder();
        var nm = NetworkManager.Singleton;
        string role = nm == null ? "none" : nm.IsHost ? "host" : nm.IsClient ? "client" : "offline";
        sb.Append(C(t)).Append(',').Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Append(',').Append(role);
        var h = Heli;
        if (h != null)
        {
            var hp = h.transform.position; var he = h.transform.eulerAngles;
            sb.Append(',').Append(C(hp.x)).Append(',').Append(C(hp.y)).Append(',').Append(C(hp.z))
              .Append(',').Append(C(Mathf.DeltaAngle(0, he.x))).Append(',').Append(C(he.y)).Append(',').Append(C(Mathf.DeltaAngle(0, he.z)))
              .Append(',').Append(C(h.GetComponent<Rigidbody>().angularVelocity.magnitude))
              .Append(',').Append(h.IsSpawned ? h.PilotClientId.Value.ToString() : "-");
        }
        else sb.Append(",,,,,,,,");
        var me = Me;
        if (me != null)
        {
            var p = me.transform.position; var cam = Camera.main ? Camera.main.transform.position : Vector3.zero;
            Vector3 lf = me.Visual ? me.Visual.BonePosition(HumanBodyBones.LeftFoot) : Vector3.zero;
            Vector3 rf = me.Visual ? me.Visual.BonePosition(HumanBodyBones.RightFoot) : Vector3.zero;
            sb.Append(',').Append(C(p.x)).Append(',').Append(C(p.y)).Append(',').Append(C(p.z))
              .Append(',').Append(me.Grounded.Value ? 1 : 0).Append(',').Append(me.KnockedDown.Value ? 1 : 0)
              .Append(',').Append(me.SeatedAsPilot.Value ? 1 : 0).Append(',').Append(me.OnHelicopter.Value ? 1 : 0)
              .Append(',').Append(C(cam.x)).Append(',').Append(C(cam.y)).Append(',').Append(C(cam.z))
              .Append(',').Append(C(lf.x)).Append(',').Append(C(lf.y)).Append(',').Append(C(lf.z))
              .Append(',').Append(C(rf.x)).Append(',').Append(C(rf.y)).Append(',').Append(C(rf.z))
              .Append(',').Append(C(me.MoveSpeed.Value));
        }
        else sb.Append(",,,,,,,,,,,,,,,,,");
        // winch + survivor
        var w = h != null ? h.GetComponent<NetworkWinch>() : null;
        if (w != null && w.IsSpawned && w.hook != null)
        {
            var hk = w.hook.position;
            sb.Append(',').Append(C(w.Length.Value)).Append(',').Append(w.Motion.Value)
              .Append(',').Append(C(hk.x)).Append(',').Append(C(hk.y)).Append(',').Append(C(hk.z));
        }
        else sb.Append(",,,,,");
        var sv = SurvivorBrain.Find(trackKind) ?? FindAnyObjectByType<SurvivorBrain>();
        if (sv != null && sv.IsSpawned)
        {
            var sp = sv.transform.position;
            sb.Append(',').Append(sv.Current).Append(',').Append(C(sp.x)).Append(',').Append(C(sp.y)).Append(',').Append(C(sp.z));
        }
        else sb.Append(",,,,");
        sb.Append(',').Append(me != null && me.NearWinch ? 1 : 0);
        var dmg = h != null ? h.GetComponent<HeliDamage>() : null;
        sb.Append(',').Append(dmg != null && dmg.IsSpawned ? C(dmg.Health.Value) : "");
        telemetry.WriteLine(sb.ToString());
    }

    bool finished;
    void Finish()
    {
        if (finished) return;
        finished = true;
        if (slideSamples > 20)
        {
            float slide = slideSum / slideSamples;
            timeline.Add($"metric  planted-foot slide {slide:F2} m/s (lower is better; under ~0.4 reads as planted)");
            // (informational only: tools/footslide.py gives the reliable per-phase measurement)
        }
        var sb = new StringBuilder();
        var nm = NetworkManager.Singleton;
        sb.AppendLine($"PLAYTEST scenario={scenario ?? "-"} pilotbot={pilotBot ?? "-"} role={(nm && nm.IsHost ? "host" : nm && nm.IsClient ? "client" : "offline")} duration={duration}s frames={frame}");
        sb.AppendLine(issues.Count == 0 ? "RESULT: no problems detected" : $"RESULT: {issues.Count} problem type(s)");
        foreach (var i in issues.Values) sb.AppendLine($"ISSUE {i.kind} x{i.count} first at {i.first:F2}s: {i.detail}");
        sb.AppendLine("--- timeline ---");
        foreach (var l in timeline) sb.AppendLine(l);
        File.WriteAllText(Path.Combine(outDir, "report.txt"), sb.ToString());
        telemetry.Flush(); telemetry.Close();
        Application.Quit();
    }
}
