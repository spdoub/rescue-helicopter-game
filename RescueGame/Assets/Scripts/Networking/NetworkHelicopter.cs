using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Makes the helicopter multiplayer, host-authoritative:
/// - Only the host runs the flight physics. Everyone else just sees the synced position
///   (NetworkTransform + NetworkRigidbody on the same object).
/// - Whoever is in the pilot seat reads their own keyboard/gamepad and sends it to the host
///   every physics tick. The host feeds it into HelicopterController.
/// - Press P to take the pilot seat. The host starts as pilot.
/// </summary>
[RequireComponent(typeof(HelicopterController))]
public class NetworkHelicopter : NetworkBehaviour
{
    public const ulong NoPilot = ulong.MaxValue;

    /// <summary>Client id of the current pilot (NoPilot if the seat is empty).</summary>
    public NetworkVariable<ulong> PilotClientId = new(NoPilot);

    /// <summary>Test mode: the pilot flies a scripted pattern instead of reading devices (used by automated tests).</summary>
    public static bool Autopilot;

    HelicopterController controller;
    HelicopterInput latestInput;          // host only: most recent input from the pilot
    float lastInputTime;                  // host only: when it arrived (so a lagging pilot doesn't leave the stick held)
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
        {
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnect;
            if (PilotClientId.Value == NoPilot && IsHost)
                PilotClientId.Value = NetworkManager.LocalClientId;
        }
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
        if (PilotClientId.Value == clientId)
        {
            PilotClientId.Value = NoPilot;
            latestInput = HelicopterInput.None;
        }
    }

    void Update()
    {
        if (!IsSpawned) return;

        var kb = Keyboard.current;
        if (kb != null && kb.pKey.wasPressedThisFrame) RequestPilotSeat();
    }

    /// <summary>Ask the host for the pilot seat.</summary>
    public void RequestPilotSeat()
    {
        if (IsSpawned && !IsLocalPilot) RequestPilotSeatRpc();
    }

    void FixedUpdate()
    {
        if (!IsSpawned) return;

        // Pilot: send this tick's controls to the host (the host sends to itself locally).
        if (IsLocalPilot)
        {
            var input = Autopilot ? AutopilotInput(Time.time) : HelicopterInput.ReadDevices();
            if (IsServer) ReceiveInput(input);
            else SubmitInputRpc(input);
        }

        // Host: drive the physics from the pilot's latest controls.
        if (IsServer)
        {
            bool fresh = Time.time - lastInputTime < InputTimeout && PilotClientId.Value != NoPilot;
            controller.SetInput(fresh ? latestInput : HelicopterInput.None);
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

    [Rpc(SendTo.Server)]
    void RequestPilotSeatRpc(RpcParams rpcParams = default)
    {
        // Prototype rule: anyone can grab the stick at any time. (Later: must be sitting in the cockpit.)
        PilotClientId.Value = rpcParams.Receive.SenderClientId;
        latestInput = HelicopterInput.None;
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
