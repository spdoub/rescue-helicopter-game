using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The pilot's energy drink (the PRD's consumable "background texture"). Caffeine drains over a flight;
/// run low and the controls go sluggish and the pilot's view drifts. The can on the dash refills it
/// (look at it, press F) with a few seconds of over-caffeinated jitters first. Host-owned level, synced;
/// the effects are applied where the host builds the helicopter's controls each tick.
/// </summary>
public class PilotBuzz : NetworkBehaviour
{
    [Tooltip("Minutes from a full can to running on empty.")]
    public float minutesPerCan = 4.5f;
    public float lowBelow = 0.25f, sluggish = 0.62f, jitterSeconds = 6f, jitterAmount = 0.35f;
    public AudioClip gulp;

    public NetworkVariable<float> Caffeine = new(1f);
    public NetworkVariable<float> JitterUntil = new(-100f);

    public bool Low => Caffeine.Value < lowBelow;
    public bool Jittery => IsSpawned && NetworkManager.ServerTime.Time < JitterUntil.Value;
    public static PilotBuzz Instance { get; private set; }

    Transform can; float level = 1f;
    NetworkHelicopter heli; HelicopterController ctrl;

    void Awake() { Instance = this; heli = GetComponent<NetworkHelicopter>(); ctrl = GetComponent<HelicopterController>(); }
    public override void OnDestroy() { if (Instance == this) Instance = null; base.OnDestroy(); }

    /// <summary>Where the can sits (the cockpit's M_Can marker), for the "look at it" test.</summary>
    public Vector3 CanPosition
    {
        get
        {
            if (can == null) can = FindDeep(transform, "M_Can");
            return can ? can.position : transform.TransformPoint(new Vector3(0.82f, 1.54f, 2.42f));
        }
    }

    /// <summary>Is this camera looking at the can from close by?</summary>
    public bool Aimed(Camera cam)
    {
        if (cam == null) return false;
        Vector3 to = CanPosition - cam.transform.position;
        return to.magnitude < 1.3f && Vector3.Angle(cam.transform.forward, to) < 12f;
    }

    void FixedUpdate()
    {
        if (!IsSpawned || !IsServer) return;
        if (ctrl != null && !ctrl.IsLanded && heli != null && heli.PilotClientId.Value != NetworkHelicopter.NoPilot)
            level = Mathf.Max(0f, level - Time.fixedDeltaTime / (minutesPerCan * 60f));
        if (Mathf.Abs(level - Caffeine.Value) > 0.005f || (level == 0f && Caffeine.Value != 0f)) Caffeine.Value = level;
    }

    /// <summary>Host: the multiplier and noise to fold into this tick's applied controls.</summary>
    public HelicopterInput Apply(HelicopterInput input)
    {
        if (!IsSpawned) return input;
        if (level < lowBelow)
        {
            float k = Mathf.Lerp(sluggish, 1f, level / lowBelow);
            input.collective *= k; input.pitch *= k; input.roll *= k; input.yaw *= k;
        }
        if (Jittery)
        {
            float j = jitterAmount * Mathf.Clamp01((float)(JitterUntil.Value - NetworkManager.ServerTime.Time) / jitterSeconds);
            float t = Time.time * 9f;
            input.pitch = Mathf.Clamp(input.pitch + (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * j, -1f, 1f);
            input.roll = Mathf.Clamp(input.roll + (Mathf.PerlinNoise(t, 4.7f) - 0.5f) * j, -1f, 1f);
            input.yaw = Mathf.Clamp(input.yaw + (Mathf.PerlinNoise(t, 8.1f) - 0.5f) * j * 0.6f, -1f, 1f);
        }
        return input;
    }

    [Rpc(SendTo.Server)]
    public void DrinkRpc(RpcParams p = default)
    {
        level = 1f; Caffeine.Value = 1f;
        JitterUntil.Value = (float)NetworkManager.ServerTime.Time + jitterSeconds;
        GulpRpc();
    }

    /// <summary>Host: a survivor drank it (empties the can for the pilot).</summary>
    public void StolenByNpc() { if (!IsServer) return; level = 0f; Caffeine.Value = 0f; GulpRpc(); }

    [Rpc(SendTo.Everyone)]
    void GulpRpc() { if (gulp) AudioSource.PlayClipAtPoint(gulp, CanPosition, 0.9f); }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
        return null;
    }
}
