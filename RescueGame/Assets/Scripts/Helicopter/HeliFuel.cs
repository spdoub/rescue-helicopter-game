using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Fuel is the mission clock (host-authoritative). It burns while the helicopter is off the
/// ground and refills when it sits on the home helipad or at a drop-off pad. Run dry and the engine
/// loses power: the helicopter can't climb and sinks slowly, so you'd better land somewhere.
/// </summary>
[RequireComponent(typeof(HelicopterController))]
public class HeliFuel : NetworkBehaviour
{
    [Tooltip("Minutes of flying on a full tank.")]
    public float flightMinutes = 6f;
    [Tooltip("Seconds to refill from empty while parked on a pad.")]
    public float refillSeconds = 20f;
    [Tooltip("Home helipad centre (world) and radius.")]
    public Vector3 homePad = Vector3.zero;
    public float padRadius = 10f;

    /// <summary>0..1</summary>
    public NetworkVariable<float> Fuel = new(1f);

    public float SecondsLeft => Fuel.Value * flightMinutes * 60f;
    public bool Refuelling { get; private set; }

    HelicopterController controller;
    float fuel = 1f;

    void Awake() => controller = GetComponent<HelicopterController>();

    void FixedUpdate()
    {
        if (!IsSpawned || !IsServer) return;
        Refuelling = controller.IsLanded && OnPad();
        if (Refuelling) fuel = Mathf.Min(1f, fuel + Time.fixedDeltaTime / refillSeconds);
        else if (!controller.IsLanded) fuel = Mathf.Max(0f, fuel - Time.fixedDeltaTime / (flightMinutes * 60f));
        // (the tank is tracked here; everyone else gets it rounded, a few times a second)
        if (Mathf.Abs(fuel - Fuel.Value) > 0.0005f || (fuel != Fuel.Value && (fuel == 0f || fuel == 1f))) Fuel.Value = fuel;
        float f = fuel;
        // Sputter as it runs dry, then lose most of the power.
        controller.power = f > 0f ? 1f : 0.35f;
    }

    bool OnPad()
    {
        Vector3 p = transform.position;
        if (new Vector2(p.x - homePad.x, p.z - homePad.z).magnitude < padRadius) return true;
        foreach (var z in RescueZone.All) if (z.Contains(p)) return true;
        return false;
    }
}
