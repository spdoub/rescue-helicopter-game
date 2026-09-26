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
    public static bool BoardForTests;      // test switch: walk forward and keep jumping (climb aboard)

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
            if (!Physics.Raycast(above, Vector3.down, out RaycastHit ground, 20f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (ground.rigidbody != null) continue; // landed on the helicopter or a person, not the ground

            Vector3 feet = ground.point + Vector3.up * 0.05f;
            bool blocked = false;
            foreach (var c in Physics.OverlapCapsule(feet + Vector3.up * 0.3f, feet + Vector3.up * 1.2f, 0.28f, ~0, QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody != myRb && c.attachedRigidbody != null) { blocked = true; break; }
            if (!blocked) return feet;
        }
        return heli.transform.TransformPoint(new Vector3(-3f, 0f, 1f)) + Vector3.up * 0.3f;
    }

    [Rpc(SendTo.Owner)]
    void FaceRpc(float yaw)
    {
        LookYaw = yaw;
        LookPitch = 0f;
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
