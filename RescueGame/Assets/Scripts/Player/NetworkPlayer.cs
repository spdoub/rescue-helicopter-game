using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A player's crew member over the network (host-authoritative, like the helicopter).
/// - On the owner's machine: reads WASD / mouse / Space / E / left click and sends it to the host.
/// - On the host: feeds those controls into CrewBody physics and handles the pilot seat.
/// - Everywhere: shows the grab arm so everyone can see who's holding what.
///
/// Controls: WASD walk, Space jump, mouse look, hold left click to grab, E to sit in / leave the pilot seat.
/// </summary>
[RequireComponent(typeof(CrewBody))]
public class NetworkPlayer : NetworkBehaviour
{
    /// <summary>This machine's own player (null if not in a session).</summary>
    public static NetworkPlayer Local { get; private set; }

    public float mouseSensitivity = 0.12f;
    public LineRenderer grabArm;

    public NetworkVariable<bool> SeatedAsPilot = new();
    public NetworkVariable<bool> OnHelicopter = new();
    public NetworkVariable<bool> KnockedDown = new();
    public NetworkVariable<bool> Grabbing = new();
    public NetworkVariable<Vector3> GrabPoint = new();

    // Owner-side look state (world yaw so you keep facing the same way if you fall out).
    public float LookYaw { get; private set; }
    public float LookPitch { get; private set; }

    CrewBody body;
    NetworkHelicopter heli;
    bool jumpQueued;
    float lastHeliYaw;
    PlayerInput latestInput;          // host: most recent controls from the owner
    bool pendingJump;                 // host: a jump press waiting for the next physics tick
    float lastInputTime;
    const float InputTimeout = 0.5f;
    const float SeatReach = 1.8f;

    public static bool ForceSeatForTests;
    public static bool HoldGrabForTests;   // test switch: look down and hold grab the whole time

    void Awake()
    {
        body = GetComponent<CrewBody>();
    }

    public override void OnNetworkSpawn()
    {
        heli = FindAnyObjectByType<NetworkHelicopter>();
        GetComponent<Rigidbody>().isKinematic = !IsServer;

        if (IsOwner)
        {
            Local = this;
            LookYaw = heli ? heli.transform.eulerAngles.y : 0f;
            lastHeliYaw = LookYaw;
        }

        if (IsServer && heli != null)
            body.StandUp(FindSpawnSpot(), heli.GetComponent<Rigidbody>());
    }

    // Cabin standing spots (helicopter local space, feet on the floor), away from the open doors.
    static readonly Vector2[] SpawnSlots = { new(-0.4f, 0.3f), new(0.4f, 0.3f), new(-0.4f, 1.1f), new(0.4f, 1.1f), new(0f, 0.7f) };

    Vector3 FindSpawnSpot()
    {
        foreach (var slot in SpawnSlots)
        {
            Vector3 feet = heli.transform.TransformPoint(new Vector3(slot.x, heli.cabinFloorHeight + 0.05f, slot.y));
            Vector3 up = heli.transform.up;
            bool blocked = false;
            foreach (var c in Physics.OverlapCapsule(feet + up * 0.3f, feet + up * 1.2f, 0.28f, ~0, QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody != GetComponent<Rigidbody>() && c.GetComponentInParent<CrewBody>() != null) { blocked = true; break; }
            if (!blocked) return feet;
        }
        return heli.transform.TransformPoint(new Vector3(0f, heli.cabinFloorHeight + 0.05f, 0.7f));
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this) Local = null;
        if (IsServer && heli != null && SeatedAsPilot.Value) heli.ClearPilot();
    }

    void Update()
    {
        if (grabArm) UpdateGrabArm();
        if (!IsOwner) return;

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
            LookPitch = Mathf.Clamp(LookPitch - d.y, -85f, 85f);
        }

        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame && !SeatedAsPilot.Value) jumpQueued = true;
            if (kb.eKey.wasPressedThisFrame) ToggleSeatRpc(false);
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

        if (IsOwner)
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
        var mouse = Mouse.current;
        i.grab = looking && mouse != null && mouse.leftButton.isPressed && !SeatedAsPilot.Value;
        if (HoldGrabForTests) { i.grab = true; i.pitch = 60f; }
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

        var input = Time.time - lastInputTime < InputTimeout ? latestInput : new PlayerInput { yaw = transform.eulerAngles.y };
        input.jump = pendingJump;
        pendingJump = false;
        body.Simulate(input, Time.fixedDeltaTime);

        bool onHeli = heli != null && body.GroundBody != null && body.GroundBody.gameObject == heli.gameObject;
        if (OnHelicopter.Value != onHeli) OnHelicopter.Value = onHeli;
        if (KnockedDown.Value != body.IsKnockedDown) KnockedDown.Value = body.IsKnockedDown;
        if (Grabbing.Value != body.IsGrabbing) Grabbing.Value = body.IsGrabbing;
        if (body.IsGrabbing) GrabPoint.Value = body.GrabPoint;
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

        bool inReach = Vector3.Distance(body.Eye, heli.pilotSeat.position) < SeatReach;
        bool allowForce = force && Debug.isDebugBuild;
        if ((inReach || allowForce) && heli.PilotClientId.Value == NetworkHelicopter.NoPilot && (!body.IsKnockedDown || allowForce))
        {
            SeatedAsPilot.Value = true;
            heli.SetPilot(OwnerClientId);
            body.Sit(heli.pilotSeat);
        }
    }

    void UpdateGrabArm()
    {
        bool show = Grabbing.Value && !SeatedAsPilot.Value;
        grabArm.enabled = show;
        if (!show) return;
        grabArm.SetPosition(0, transform.position + transform.right * 0.25f + Vector3.up * 0.4f);
        grabArm.SetPosition(1, GrabPoint.Value);
    }
}
