using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Wind (host-authoritative, synced): a prevailing breeze plus gusts that wander in strength and
/// direction. It shoves the helicopter, swings the hook (and whoever or whatever is hanging on
/// it), and even nudges people standing on the rooftops. Each new rescue call rolls the weather:
/// usually calm or breezy, sometimes properly gusty. Everyone sees wind streaks and a HUD readout.
/// </summary>
public class Weather : NetworkBehaviour
{
    public enum Kind : byte { Calm, Breezy, Gusty }

    [Tooltip("Base wind speed (m/s) for calm / breezy / gusty.")]
    public Vector3 baseSpeeds = new(1.5f, 5f, 8f);
    [Tooltip("How much gusts add on top (m/s) for calm / breezy / gusty.")]
    public Vector3 gustSpeeds = new(1f, 4f, 9f);
    [Tooltip("How hard the wind pushes the helicopter (acceleration per m/s of wind).")]
    public float heliPush = 0.12f;
    public float hangingPush = 0.18f, peoplePush = 0.15f;
    public Material streakMaterial;

    public NetworkVariable<Vector3> Wind = new();
    public NetworkVariable<Kind> Current = new(Kind.Breezy);

    public static Weather Instance { get; private set; }

    float heading;          // prevailing direction (degrees)
    float nextScan; Recliner[] chairs = new Recliner[0]; CrewBody[] bodies = new CrewBody[0];
    ParticleSystem streaks;
    Rigidbody heliRb;
    NetworkWinch winch;

    void Awake()
    {
        Instance = this;
        heliRb = GetComponent<Rigidbody>();
        winch = GetComponent<NetworkWinch>();
        streaks = MakeStreaks();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) { heading = Random.Range(0f, 360f); Current.Value = Kind.Breezy; }
    }

    /// <summary>Host: new weather for a new call.</summary>
    public void Roll()
    {
        float r = Random.value;
        Current.Value = r < 0.5f ? Kind.Calm : r < 0.85f ? Kind.Breezy : Kind.Gusty;
        heading = Random.Range(0f, 360f);
    }

    void FixedUpdate()
    {
        if (!IsSpawned || !IsServer) return;
        int k = (int)Current.Value;
        float t = Time.time;
        float gust = Mathf.Pow(Mathf.PerlinNoise(t * 0.12f, 3.7f), 2f) * 1.6f;           // slow swells
        float flurry = Mathf.PerlinNoise(t * 0.9f, 8.1f) * 0.4f;                          // quick flutters
        float speed = baseSpeeds[k] + gustSpeeds[k] * (gust + flurry);
        float dir = heading + (Mathf.PerlinNoise(t * 0.05f, 1.3f) - 0.5f) * 70f;
        Vector3 w = Quaternion.Euler(0f, dir, 0f) * Vector3.forward * speed;
        if ((w - Wind.Value).sqrMagnitude > 0.04f) Wind.Value = w;

        // Push: the helicopter (more when it's high and exposed), the hook, anything hanging, people on roofs.
        if (heliRb && !heliRb.isKinematic && !GetComponent<HelicopterController>().IsLanded)
            heliRb.AddForce(w * heliPush, ForceMode.Acceleration);
        if (winch && winch.hook && winch.Length.Value > 1f) winch.hook.AddForce(w * hangingPush, ForceMode.Acceleration);
        if (Time.time > nextScan) { nextScan = Time.time + 2f; chairs = FindObjectsByType<Recliner>(FindObjectsSortMode.None); bodies = FindObjectsByType<CrewBody>(FindObjectsSortMode.None); }
        foreach (var r in chairs)
            if (r && r.Latched) r.GetComponent<Rigidbody>().AddForce(w * hangingPush, ForceMode.Acceleration);
        foreach (var b in bodies)
        {
            if (!b) continue;
            var rb = b.GetComponent<Rigidbody>();
            if (rb.isKinematic || b.Seated) continue;
            bool up = rb.position.y > 5f && !(heliRb && b.GroundBody == heliRb); // exposed on a roof or dangling, not in the cabin
            if (up) rb.AddForce(w * (b.IsHolding ? hangingPush : peoplePush), ForceMode.Acceleration);
        }
    }

    void Update()
    {
        // Streaks around the camera, flying with the wind; more of them the stronger it blows.
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 w = IsSpawned ? Wind.Value : Vector3.zero;
        streaks.transform.position = cam.transform.position - w.normalized * 6f;
        var main = streaks.main;
        main.startSpeed = w.magnitude * 1.4f + 2f;
        streaks.transform.rotation = w.sqrMagnitude > 0.01f ? Quaternion.LookRotation(w) : Quaternion.identity;
        var em = streaks.emission;
        em.rateOverTime = Mathf.Clamp01((w.magnitude - 3f) / 10f) * 60f;
    }

    ParticleSystem MakeStreaks()
    {
        var go = new GameObject("WindStreaks");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 1.2f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = new Color(1f, 0.96f, 0.88f, 0.35f);
        main.maxParticles = 300;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(16f, 8f, 2f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.12f; r.lengthScale = 2f;
        r.sharedMaterial = streakMaterial;
        var em = ps.emission; em.rateOverTime = 0f;
        ps.Play();
        return ps;
    }

    public override void OnDestroy() { if (Instance == this) Instance = null; if (streaks) Destroy(streaks.gameObject); base.OnDestroy(); }
}
