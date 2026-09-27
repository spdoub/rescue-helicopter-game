using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Makes the helicopter multiplayer, host-authoritative:
/// - Only the host runs the flight physics. Everyone else just sees the synced position
///   (NetworkTransform + NetworkRigidbody on the same object).
/// - Whoever is in the pilot seat reads their own keyboard/gamepad and sends it to the host
///   every physics tick. The host feeds it into HelicopterController.
/// - You fly by sitting in the pilot seat (NetworkPlayer handles walking up and pressing E).
/// </summary>
[RequireComponent(typeof(HelicopterController))]
public class NetworkHelicopter : NetworkBehaviour
{
    public const ulong NoPilot = ulong.MaxValue;

    /// <summary>Client id of the current pilot (NoPilot if the seat is empty).</summary>
    public NetworkVariable<ulong> PilotClientId = new(NoPilot);

    /// <summary>The controls being applied (collective, pitch, roll, yaw), so everyone sees the stick move.</summary>
    public NetworkVariable<Vector4> ControlsSync = new();

    /// <summary>Cabin doors (start open so people can climb in).</summary>
    public NetworkVariable<bool> DoorLeftOpen = new(true), DoorRightOpen = new(true);
    public bool DoorOpen(bool left) => left ? DoorLeftOpen.Value : DoorRightOpen.Value;

    void Start()
    {
        // Dev builds: "-doorsclosed" starts with both doors shut (screenshots, tests).
        if (Debug.isDebugBuild && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-doorsclosed") >= 0)
            foreach (var d in GetComponentsInChildren<SlidingDoor>()) d.OfflineToggle();
    }

    /// <summary>Host: someone who isn't the pilot is grabbing the controls (added on top of the pilot's).</summary>
    public void MeddleFor(HelicopterInput meddle, float seconds) { Meddle = meddle; meddleUntil = Time.time + seconds; }
    HelicopterInput Meddle; float meddleUntil;

    /// <summary>Host only.</summary>
    public void ToggleDoor(bool left)
    {
        if (left) DoorLeftOpen.Value = !DoorLeftOpen.Value; else DoorRightOpen.Value = !DoorRightOpen.Value;
    }

    /// <summary>Test mode: the pilot flies a scripted pattern instead of reading devices (used by automated tests).</summary>
    public static bool Autopilot;

    /// <summary>Automated playtests: flight controls as a function of seconds since the pilot sat down.</summary>
    public static System.Func<float, HelicopterInput> BotPilot;

    [Header("Cabin")]
    public Transform pilotSeat;       // where the pilot's feet go when seated
    public Transform pilotSeatExit;   // where they stand after getting up
    public float cabinFloorHeight = 0.95f;

    HelicopterController controller;
    HelicopterInput latestInput;          // host only: most recent input from the pilot
    float lastInputTime;                  // host only: when it arrived (so a lagging pilot doesn't leave the stick held)
    float pilotSince;                     // when the current pilot sat down (autopilot pattern starts from here)
    const float InputTimeout = 0.5f;

    public bool IsLocalPilot => IsSpawned && PilotClientId.Value == NetworkManager.LocalClientId;

    void Awake()
    {
        controller = GetComponent<HelicopterController>();
    }

    public override void OnNetworkSpawn()
    {
        controller.useLocalInput = false;
        controller.simulate = IsServer;

        if (IsServer)
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnect;
        if (IsServer && Debug.isDebugBuild && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-doorsclosed") >= 0)
        { DoorLeftOpen.Value = false; DoorRightOpen.Value = false; }
        PilotClientId.OnValueChanged += (_, _) => pilotSince = Time.time;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnect;

        // Back to single-player behaviour when the session ends.
        controller.useLocalInput = true;
        controller.simulate = true;
    }

    void HandleClientDisconnect(ulong clientId)
    {
        if (PilotClientId.Value == clientId) ClearPilot();
    }

    /// <summary>Host only: put this client in the pilot seat.</summary>
    public void SetPilot(ulong clientId)
    {
        PilotClientId.Value = clientId;
        latestInput = HelicopterInput.None;
    }

    /// <summary>Host only: empty the pilot seat (the helicopter holds a hover on its own).</summary>
    public void ClearPilot()
    {
        PilotClientId.Value = NoPilot;
        latestInput = HelicopterInput.None;
    }

    void FixedUpdate()
    {
        if (!IsSpawned) return;

        // Pilot: send this tick's controls to the host (the host sends to itself locally).
        if (IsLocalPilot)
        {
            float since = Time.time - pilotSince;
            var input = BotPilot != null ? BotPilot(since) : Autopilot ? AutopilotInput(since) : HelicopterInput.ReadDevices();
            if (IsServer) ReceiveInput(input);
            else SubmitInputRpc(input);
        }

        // Host: drive the physics from the pilot's latest controls.
        if (IsServer)
        {
            bool fresh = Time.time - lastInputTime < InputTimeout && PilotClientId.Value != NoPilot;
            var applied = fresh ? latestInput : HelicopterInput.None;
            // A backseat pilot yanking on the controls from beside the pilot seat.
            if (Time.time < meddleUntil)
            {
                applied.collective = Mathf.Clamp(applied.collective + Meddle.collective, -1f, 1f);
                applied.pitch = Mathf.Clamp(applied.pitch + Meddle.pitch, -1f, 1f);
                applied.roll = Mathf.Clamp(applied.roll + Meddle.roll, -1f, 1f);
                applied.yaw = Mathf.Clamp(applied.yaw + Meddle.yaw, -1f, 1f);
            }
            var buzz = GetComponent<PilotBuzz>(); if (buzz) applied = buzz.Apply(applied);
            controller.SetInput(applied);
            controller.hoverAutopilot = PilotClientId.Value == NoPilot && Time.time >= meddleUntil;
            var c = new Vector4(applied.collective, applied.pitch, applied.roll, applied.yaw);
            if ((c - ControlsSync.Value).sqrMagnitude > 0.0004f) ControlsSync.Value = c;
        }
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void SubmitInputRpc(HelicopterInput input, RpcParams rpcParams = default)
    {
        // Ignore controls from anyone who isn't in the pilot seat.
        if (rpcParams.Receive.SenderClientId != PilotClientId.Value) return;
        ReceiveInput(input);
    }

    void ReceiveInput(HelicopterInput input)
    {
        latestInput = input;
        lastInputTime = Time.time;
    }

    /// <summary>Scripted test flight: climb, fly forward, turn, hover.</summary>
    static HelicopterInput AutopilotInput(float t)
    {
        var i = new HelicopterInput();
        float phase = t % 20f;
        if (phase < 4f) i.collective = 1f;
        else if (phase < 10f) i.pitch = 1f;
        else if (phase < 13f) i.yaw = 1f;
        else if (phase < 16f) i.roll = 1f;
        return i;
    }
}
