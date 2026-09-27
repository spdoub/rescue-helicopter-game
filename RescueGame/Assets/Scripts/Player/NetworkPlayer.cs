using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A player's crew member over the network (host-authoritative, like the helicopter).
/// - On the owner's machine: reads WASD / mouse / Space / E / left click and sends it to the host.
/// - On the host: feeds those controls into CrewBody physics and handles the pilot seat.
/// - Everywhere: publishes what CrewVisual needs to animate the body (look, hands, speed, airborne).
///
/// Controls: WASD walk, Shift run, Space jump, mouse look, hold left/right click to reach and grab
/// with that hand (hold W while holding on to climb), E to sit in / leave the pilot seat,
/// F to open/close the nearest cabin door, and at the winch box hold R / Q to reel the hoist in / out.
/// </summary>
[RequireComponent(typeof(CrewBody))]
public class NetworkPlayer : NetworkBehaviour
{
    /// <summary>This machine's own player (null if not in a session).</summary>
    public static NetworkPlayer Local { get; private set; }

    public float mouseSensitivity = 0.12f;
    /// <summary>How far down you can look (degrees) - stops the camera tucking under your chin.</summary>
    public const float MaxLookDown = 80f;

    /// <summary>This player's animated body (registered by CrewVisual; it detaches while ragdolled).</summary>
    public CrewVisual Visual { get; set; }

    public NetworkVariable<bool> SeatedAsPilot = new();
    /// <summary>Strapped into a cabin seat as a passenger (rescued survivors).</summary>
    public NetworkVariable<bool> SeatedPassenger = new();
    public bool IsSeated => SeatedAsPilot.Value || SeatedPassenger.Value;

    /// <summary>Computer-controlled (a survivor): no keyboard, no camera, the brain supplies the controls.</summary>
    public bool IsNpc => brain != null;
    /// <summary>This machine's own human-controlled crew member?</summary>
    public new bool IsLocalPlayer => IsOwner && !IsNpc;
    SurvivorBrain brain;
    public NetworkVariable<bool> OnHelicopter = new();
    public NetworkVariable<bool> KnockedDown = new();
    public NetworkVariable<bool> Grounded = new(true);
    public NetworkVariable<float> MoveSpeed = new();     // walking speed relative to what you stand on
    public NetworkVariable<Vector2> LocalMove = new();   // that movement in body space: x right, y forward
    public NetworkVariable<float> Pitch = new();         // where the head/arms point (for other players)
    public NetworkVariable<bool> ReachLeft = new(), ReachRight = new();
    public NetworkVariable<bool> HoldLeft = new(), HoldRight = new();
    public NetworkVariable<Vector3> HoldPointLeft = new(), HoldPointRight = new();

    /// <summary>Is the grabbing test switch on (or the mouse held) for this hand right now - owner only.</summary>
    public bool LocalReachLeft { get; private set; }
    public bool LocalReachRight { get; private set; }

    // Owner-side look state (world yaw so you keep facing the same way if you fall out).
    public float LookYaw { get; private set; }
    public float LookPitch { get; private set; }

    CrewBody body;
    NetworkHelicopter heli;
    NetworkWinch winch;
    bool jumpQueued;
    float lastHeliYaw;
    PlayerInput latestInput;          // host: most recent controls from the owner
    bool pendingJump;                 // host: a jump press waiting for the next physics tick
    float lastInputTime;
    const float InputTimeout = 0.5f;
    const float SeatReach = 1.5f;

    public static bool ForceSeatForTests;
    public static bool HoldGrabForTests;   // test switch: look down and hold grab the whole time
    public static bool BoardForTests;      // test switch: walk forward and keep jumping (climb aboard)
    public static bool RunForTests;        // test switch: sprint forward

    // ---- Bot control (automated playtests; see PlaytestDirector) ----
    /// <summary>When set, replaces keyboard/mouse movement for this machine's player.</summary>
    public static PlayerInput? BotOverride;
    /// <summary>Jump presses queued by a bot (consumed one per physics tick).</summary>
    public static int BotJumps;
    public void BotLook(float yaw, float pitch) { LookYaw = yaw; LookPitch = Mathf.Clamp(pitch, -85f, MaxLookDown); }
    public void BotToggleSeat() => ToggleSeatRpc(false);

    void Awake()
    {
        body = GetComponent<CrewBody>();
        brain = GetComponent<SurvivorBrain>();
    }

    public override void OnNetworkSpawn()
    {
        heli = FindAnyObjectByType<NetworkHelicopter>();
        winch = heli ? heli.GetComponent<NetworkWinch>() : null;
        GetComponent<Rigidbody>().isKinematic = !IsServer;

        if (IsLocalPlayer)
        {
            Local = this;
            LookYaw = heli ? heli.transform.eulerAngles.y : 0f;
            lastHeliYaw = LookYaw;
        }

        if (IsServer && heli != null && !IsNpc)
        {
            // Start outside on the ground, facing the helicopter's side door.
            Vector3 feet = FindSpawnSpot();
            body.StandUp(feet, null);
            Vector3 toHeli = heli.transform.position - feet; toHeli.y = 0f;
            float yaw = Quaternion.LookRotation(toHeli).eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            FaceRpc(yaw);
        }
    }

    // Standing spots on the ground beside the helicopter's side doors (helicopter local x, z).
    static readonly Vector2[] SpawnSlots = { new(-2.6f, 0.5f), new(2.6f, 0.5f), new(-2.6f, 1.6f), new(2.6f, 1.6f), new(-3.6f, 1.0f), new(3.6f, 1.0f) };

    Vector3 FindSpawnSpot()
    {
        var myRb = GetComponent<Rigidbody>();
        foreach (var slot in SpawnSlots)
        {
            Vector3 above = heli.transform.TransformPoint(new Vector3(slot.x, 0f, slot.y)) + Vector3.up * 5f;
            if (!Physics.Raycast(above, Vector3.down, out RaycastHit ground, 20f, GameLayers.Gameplay, QueryTriggerInteraction.Ignore)) continue;
            if (ground.rigidbody != null) continue; // landed on the helicopter or a person, not the ground

            Vector3 feet = ground.point + Vector3.up * 0.05f;
            bool blocked = false;
            foreach (var c in Physics.OverlapCapsule(feet + Vector3.up * 0.3f, feet + Vector3.up * 1.2f, 0.28f, GameLayers.Gameplay, QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody != myRb && c.attachedRigidbody != null) { blocked = true; break; }
            if (!blocked) return feet;
        }
        return heli.transform.TransformPoint(new Vector3(-3f, 0f, 1f)) + Vector3.up * 0.3f;
    }

    /// <summary>Test screenshots: point the camera.</summary>
    public void SetLookForTests(float pitch) => LookPitch = Mathf.Min(pitch, MaxLookDown);

    [Rpc(SendTo.Owner)]
    void FaceRpc(float yaw)
    {
        LookYaw = RunForTests ? yaw + 180f : yaw; // test run heads away from the helicopter
        LookPitch = 0f;
    }

    public override void OnNetworkDespawn()
    {
        if (passengerSeat != null) SurvivorBrain.TakenSeats.Remove(passengerSeat);
        OnNetworkDespawnInner();
    }

    void OnNetworkDespawnInner()
    {
        if (Local == this) Local = null;
        if (IsServer && heli != null && SeatedAsPilot.Value) heli.ClearPilot();
    }

    void Update()
    {
        if (!IsLocalPlayer) return;

        var kb = Keyboard.current;
        var mouse = Mouse.current;

        // Turn with the helicopter while riding in it, so the cabin doesn't spin under you.
        if (heli != null)
        {
            float heliYaw = heli.transform.eulerAngles.y;
            if (OnHelicopter.Value || SeatedAsPilot.Value) LookYaw += Mathf.DeltaAngle(lastHeliYaw, heliYaw);
            lastHeliYaw = heliYaw;
        }

        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
            LookYaw += d.x;
            LookPitch = Mathf.Clamp(LookPitch - d.y, -85f, MaxLookDown);
        }

        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame && !SeatedAsPilot.Value) jumpQueued = true;
            if (kb.eKey.wasPressedThisFrame) ToggleSeatRpc(false);
            if (kb.fKey.wasPressedThisFrame) Use();
        }
        if (ForceSeatForTests && IsSpawned && heli != null && heli.IsSpawned)
        {
            ForceSeatForTests = false;
            ToggleSeatRpc(true);
        }
    }

    void FixedUpdate()
    {
        if (!IsSpawned) return;

        if (IsServer && IsNpc) ReceiveInput(brain.Think());
        else if (IsOwner)
        {
            var input = ReadInput();
            if (IsServer) ReceiveInput(input);
            else SubmitInputRpc(input);
        }

        if (IsServer) ServerSimulate();
    }

    PlayerInput ReadInput()
    {
        var i = new PlayerInput { yaw = LookYaw, pitch = LookPitch, jump = jumpQueued };
        jumpQueued = false;

        var kb = Keyboard.current;
        bool looking = Cursor.lockState == CursorLockMode.Locked || Application.isBatchMode;
        if (kb != null && !SeatedAsPilot.Value)
        {
            if (kb.wKey.isPressed) i.moveZ += 1f;
            if (kb.sKey.isPressed) i.moveZ -= 1f;
            if (kb.dKey.isPressed) i.moveX += 1f;
            if (kb.aKey.isPressed) i.moveX -= 1f;
        }
        if (kb != null) i.sprint = kb.leftShiftKey.isPressed && !SeatedAsPilot.Value;
        var mouse = Mouse.current;
        bool free = looking && mouse != null && !SeatedAsPilot.Value;
        if (kb != null && NearWinch && !SeatedAsPilot.Value)
            i.winch = (sbyte)((kb.rKey.isPressed ? 1 : 0) - (kb.qKey.isPressed ? 1 : 0));
        i.grabLeft = free && mouse.leftButton.isPressed;
        i.grabRight = free && mouse.rightButton.isPressed;
        if (HoldGrabForTests) { i.grabLeft = i.grabRight = true; i.pitch = 60f; }
        LocalReachLeft = i.grabLeft;
        LocalReachRight = i.grabRight;
        if (BotOverride.HasValue)
        {
            var b = BotOverride.Value;
            i.moveX = b.moveX; i.moveZ = b.moveZ; i.sprint = b.sprint;
            i.grabLeft = b.grabLeft; i.grabRight = b.grabRight; i.winch = b.winch;
            LocalReachLeft = i.grabLeft; LocalReachRight = i.grabRight;
        }
        if (BotJumps > 0) { BotJumps--; i.jump = true; }
        if (RunForTests) { i.moveZ = 1f; i.sprint = true; }
        if (BoardForTests && !OnHelicopter.Value) { i.moveZ = 1f; i.jump = Time.time % 1f < Time.fixedDeltaTime; }
        return i;
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void SubmitInputRpc(PlayerInput input) => ReceiveInput(input);

    void ReceiveInput(PlayerInput input)
    {
        pendingJump |= input.jump;
        latestInput = input;
        lastInputTime = Time.time;
    }

    void ServerSimulate()
    {
        if (SeatedAsPilot.Value && heli != null)
        {
            body.FollowSeat(heli.pilotSeat);
            return;
        }
        if (SeatedPassenger.Value && passengerSeat != null)
        {
            body.FollowSeat(passengerSeat);
            return;
        }

        var input = Time.time - lastInputTime < InputTimeout ? latestInput : new PlayerInput { yaw = transform.eulerAngles.y };
        input.jump = pendingJump;
        pendingJump = false;
        body.Simulate(input, Time.fixedDeltaTime);
        if (input.winch != 0 && winch != null && !body.IsKnockedDown && winch.InReach(body.Eye)) winch.Operate(input.winch);

        bool onHeli = heli != null && body.GroundBody != null && body.GroundBody.gameObject == heli.gameObject;
        Set(OnHelicopter, onHeli);
        Set(KnockedDown, body.IsKnockedDown);
        Set(Grounded, body.IsGrounded);
        Set(ReachLeft, input.grabLeft);
        Set(ReachRight, input.grabRight);
        Set(HoldLeft, body.Left.Holding);
        Set(HoldRight, body.Right.Holding);
        if (body.Left.Holding) HoldPointLeft.Value = body.Left.Point;
        if (body.Right.Holding) HoldPointRight.Value = body.Right.Point;
        if (Mathf.Abs(Pitch.Value - input.pitch) > 1f) Pitch.Value = input.pitch;

        Vector3 rel = GetComponent<Rigidbody>().linearVelocity - body.PlatformVelocity; rel.y = 0f;
        float speed = Mathf.Round(rel.magnitude * 10f) / 10f;
        if (!Mathf.Approximately(MoveSpeed.Value, speed)) MoveSpeed.Value = speed;
        Vector3 local = Quaternion.Euler(0f, -transform.eulerAngles.y, 0f) * rel;
        var move2 = new Vector2(Mathf.Round(local.x * 10f) / 10f, Mathf.Round(local.z * 10f) / 10f);
        if ((LocalMove.Value - move2).sqrMagnitude > 0.01f) LocalMove.Value = move2;
    }

    static void Set(NetworkVariable<bool> v, bool value)
    {
        if (v.Value != value) v.Value = value;
    }

    Transform passengerSeat;

    /// <summary>Host: strap into a cabin seat (null = get up and stand at 'standAt').</summary>
    public void SetPassengerSeat(Transform seat, Vector3 standAt = default)
    {
        if (seat != null)
        {
            passengerSeat = seat;
            body.Sit(seat);
            SeatedPassenger.Value = true;
        }
        else if (SeatedPassenger.Value)
        {
            passengerSeat = null;
            SeatedPassenger.Value = false;
            body.StandUp(standAt, heli ? heli.GetComponent<Rigidbody>() : null);
        }
    }

    public const float DoorReach = 2.2f;

    /// <summary>Owner: open/close whichever cabin door you're standing next to.</summary>
    public void TryToggleNearestDoor()
    {
        SlidingDoor best = null; float bestD = DoorReach;
        foreach (var d in FindObjectsByType<SlidingDoor>(FindObjectsSortMode.None))
        {
            float dist = Vector3.Distance(body.Eye, d.HandlePoint);
            if (dist < bestD) { bestD = dist; best = d; }
        }
        if (best != null) ToggleDoorRpc(best.left);
    }

    /// <summary>Standing at the winch box? (for the on-screen hint and the R/Q keys)</summary>
    public bool NearWinch => winch != null && winch.InReach(body.Eye);

    /// <summary>The seat E would take right now: the pilot's (pilot = true) or a cabin seat, whichever is nearer your feet; null if none in reach.</summary>
    public Transform SeatInReach(out bool pilot)
    {
        pilot = false;
        if (heli == null || SeatedAsPilot.Value || SeatedPassenger.Value) return null;
        Transform best = null; float bestD = SeatReach;
        foreach (var n in new[] { "M_BenchSeat_0", "M_BenchSeat_1", "M_BenchSeat_2", "M_CoPilotSeat" })
        {
            var s = FindDeep(heli.transform, n);
            if (s == null) continue;
            float d = Vector3.Distance(body.Feet, s.position);
            if (d < bestD) { bestD = d; best = s; }
        }
        float dp = heli.pilotSeat ? Vector3.Distance(body.Feet, heli.pilotSeat.position) : 99f;
        if (dp < SeatReach && (best == null || dp < bestD)) { pilot = true; return heli.pilotSeat; }
        return best;
    }

    /// <summary>Close enough to the pilot's seat, and it's the nearest seat? (for the on-screen prompt)</summary>
    public bool NearPilotSeat => SeatInReach(out bool p) != null && p;

    /// <summary>The nearest cabin seat you could sit in (bench seats, the co-pilot's seat), or null.</summary>
    public Transform NearestCabinSeat() { var s = SeatInReach(out bool p); return p ? null : s; }

    /// <summary>The door you're next to (for the prompt), or null.</summary>
    public SlidingDoor NearestDoor()
    {
        SlidingDoor best = null; float bestD = DoorReach;
        foreach (var d in FindObjectsByType<SlidingDoor>(FindObjectsSortMode.None))
        {
            float dist = Vector3.Distance(body.Eye, d.HandlePoint);
            if (dist < bestD) { bestD = dist; best = d; }
        }
        return best;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
        return null;
    }

    /// <summary>Is there a door within reach? (for the on-screen hint)</summary>
    public bool NearDoor
    {
        get
        {
            foreach (var d in FindObjectsByType<SlidingDoor>(FindObjectsSortMode.None))
                if (Vector3.Distance(body.Eye, d.HandlePoint) < DoorReach) return true;
            return false;
        }
    }

    /// <summary>F is "use": buy the upgrade card you're looking at, otherwise the nearest door.</summary>
    public void Use()
    {
        int card = UpgradeBoard.Instance ? UpgradeBoard.Instance.Aimed(Camera.main) : -1;
        if (card >= 0) { BuyUpgradeRpc((byte)card); return; }
        if (PilotBuzz.Instance && PilotBuzz.Instance.Aimed(Camera.main)) { PilotBuzz.Instance.DrinkRpc(); return; }
        TryToggleNearestDoor();
    }

    [Rpc(SendTo.Server)]
    void BuyUpgradeRpc(byte card)
    {
        var board = UpgradeBoard.Instance;
        if (board == null || HeliUpgrades.Instance == null || card > 3) return;
        if (Vector3.Distance(body.Eye, board.transform.position + Vector3.up * 1.45f) > board.reach + 2f) return;
        HeliUpgrades.Instance.TryBuy((HeliUpgrades.Kind)card, OwnerClientId);
    }

    [Rpc(SendTo.Server)]
    void ToggleDoorRpc(bool left)
    {
        if (heli == null) return;
        var d = System.Array.Find(FindObjectsByType<SlidingDoor>(FindObjectsSortMode.None), x => x.left == left);
        if (d != null && Vector3.Distance(body.Eye, d.HandlePoint) < DoorReach + 0.5f) heli.ToggleDoor(left);
    }

    [Rpc(SendTo.Server)]
    void ToggleSeatRpc(bool force)
    {
        if (heli == null) return;

        if (SeatedAsPilot.Value)
        {
            SeatedAsPilot.Value = false;
            heli.ClearPilot();
            body.StandUp(heli.pilotSeatExit.position, heli.GetComponent<Rigidbody>());
            return;
        }
        // Sitting in a cabin seat: E gets you up again, onto the cabin floor in front of it.
        if (SeatedPassenger.Value)
        {
            var seat = passengerSeat;
            Vector3 standAt = seat != null && seat.name == "M_CoPilotSeat" ? heli.transform.TransformPoint(new Vector3(0f, 0.97f, 1.2f))
                            : seat != null ? seat.position + seat.forward * 0.75f + Vector3.up * 0.05f : heli.pilotSeatExit.position;
            if (seat != null) SurvivorBrain.TakenSeats.Remove(seat);
            SetPassengerSeat(null, standAt);
            return;
        }

        var chosen = SeatInReach(out bool wantPilot);
        bool inReach = chosen != null && wantPilot;
        bool allowForce = force && Debug.isDebugBuild;
        if ((inReach || allowForce) && heli.PilotClientId.Value == NetworkHelicopter.NoPilot && (!body.IsKnockedDown || allowForce))
        {
            SeatedAsPilot.Value = true;
            heli.SetPilot(OwnerClientId);
            body.Sit(heli.pilotSeat);
            return;
        }
        // Otherwise a free cabin seat nearby: strap in (out of the way of the ragdoll chaos).
        var cabinSeat = NearestCabinSeat();
        if (cabinSeat != null && !body.IsKnockedDown && !SurvivorBrain.TakenSeats.Contains(cabinSeat))
        {
            SurvivorBrain.TakenSeats.Add(cabinSeat);
            SetPassengerSeat(cabinSeat);
        }
    }
}
