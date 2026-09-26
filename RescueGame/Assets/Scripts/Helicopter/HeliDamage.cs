using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Helicopter damage (host-authoritative health, effects on every machine) - the "aircraft
/// damage" pressure dial from the design doc.
///  - Slamming into things hurts (the harder, the worse). Clipping a wall with the main rotor is
///    the worst: sparks, a violent jolt and lots of damage.
///  - Below 60% it smokes; below 30% it pours black smoke, the controls get shaky and an alarm
///    sounds; at 0 the engine can only manage a slow sinking glide.
///  - Parked on the home helipad it gets patched up.
///  - Upside down on the ground for a few seconds: towed back to the helipad, repaired.
/// </summary>
[RequireComponent(typeof(HelicopterController), typeof(NetworkHelicopter))]
public class HeliDamage : NetworkBehaviour
{
    [Tooltip("Impacts slower than this (m/s) don't hurt.")]
    public float crashSpeed = 4f;
    [Tooltip("Health lost per m/s of impact above crashSpeed.")]
    public float damagePerSpeed = 0.045f;
    [Tooltip("Health lost per second while the rotor is chewing a wall.")]
    public float rotorStrikeDamage = 0.35f;
    public float rotorRadius = 5.2f;
    public float repairSeconds = 25f;
    [Tooltip("Multiplier on all damage taken (armour upgrades lower it).")]
    public float damageScale = 1f;
    public AudioClip crash, rotorStrike, alarm;
    public Material smokeMaterial;

    public NetworkVariable<float> Health = new(1f);

    HelicopterController controller;
    NetworkHelicopter net;
    HeliFuel fuel;
    Rigidbody rb;
    ParticleSystem smoke, sparks;
    AudioSource alarmSource, sfx;
    float health = 1f, flippedSince = -1f, nextStrikeFx, nextCrashFx;
    readonly Collider[] hits = new Collider[16];

    void Awake()
    {
        controller = GetComponent<HelicopterController>();
        net = GetComponent<NetworkHelicopter>();
        fuel = GetComponent<HeliFuel>();
        rb = GetComponent<Rigidbody>();
        smoke = MakeSmoke();
        sparks = MakeSparks();
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.spatialBlend = 1f; sfx.minDistance = 8f; sfx.maxDistance = 200f; sfx.playOnAwake = false;
        var a = new GameObject("DamageAlarm"); a.transform.SetParent(transform, false); a.transform.localPosition = new Vector3(0f, 1.9f, 2.4f);
        alarmSource = a.AddComponent<AudioSource>();
        alarmSource.clip = alarm; alarmSource.loop = true; alarmSource.spatialBlend = 1f; alarmSource.minDistance = 2f; alarmSource.maxDistance = 25f; alarmSource.volume = 0.5f;
    }

    // ------------------------------------------------------------------ host

    void OnCollisionEnter(Collision c)
    {
        if (!IsSpawned || !IsServer) return;
        if (IsCargo(c.rigidbody)) return; // bumping people, survivors, the hook, the chair or the dog isn't a crash
        float v = c.relativeVelocity.magnitude;
        if (v <= crashSpeed) return;
        Hurt((v - crashSpeed) * damagePerSpeed);
        if (Time.time > nextCrashFx) { nextCrashFx = Time.time + 0.3f; HitFxRpc(c.GetContact(0).point, false); }
    }

    void FixedUpdate()
    {
        if (!IsSpawned || !IsServer) return;

        // Main rotor clipping something: sample the blade tips' circle and the hub.
        Transform hub = controller.mainRotor != null ? controller.mainRotor : transform;
        Vector3 centre = hub.position;
        for (int i = 0; i < 9; i++)
        {
            Vector3 p = i == 8 ? centre : centre + Quaternion.AngleAxis(i * 45f, transform.up) * (transform.forward * rotorRadius);
            int n = Physics.OverlapSphereNonAlloc(p, 0.35f, hits, GameLayers.Gameplay, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                var col = hits[k];
                if (col.attachedRigidbody == rb || IsCargo(col.attachedRigidbody)) continue;
                Hurt(rotorStrikeDamage * Time.fixedDeltaTime);
                Vector3 away = (centre - col.ClosestPoint(p)); away.y = 0f;
                rb.AddForceAtPosition(away.normalized * 6f + Vector3.up * 1f, p, ForceMode.VelocityChange); // the blades bounce off
                rb.AddTorque(Random.onUnitSphere * 2f, ForceMode.VelocityChange);
                if (Time.time > nextStrikeFx) { nextStrikeFx = Time.time + 0.25f; HitFxRpc(col.ClosestPoint(p), true); }
                break;
            }
        }

        // Patched up while parked on the home pad.
        if (controller.IsLanded && fuel != null && fuel.Refuelling && health < 1f)
            health = Mathf.Min(1f, health + Time.fixedDeltaTime / repairSeconds);

        // Shaky controls when badly hurt.
        if (health < 0.3f && !controller.IsLanded)
            rb.AddTorque(Random.insideUnitSphere * (0.3f - health) * 6f, ForceMode.Acceleration);
        controller.damagePower = health > 0f ? 1f : 0.3f;

        // Flipped over on the ground: tow it home.
        bool flipped = Vector3.Dot(transform.up, Vector3.up) < 0.3f && rb.linearVelocity.magnitude < 1.5f;
        if (flipped) { if (flippedSince < 0f) flippedSince = Time.time; }
        else flippedSince = -1f;
        if (flippedSince > 0f && Time.time - flippedSince > 6f) TowHome();

        if (Mathf.Abs(health - Health.Value) > 0.005f || (health != Health.Value && (health == 0f || health == 1f))) Health.Value = health;
    }

    void Hurt(float amount) => health = Mathf.Max(0f, health - amount * damageScale);

    void TowHome()
    {
        flippedSince = -1f;
        Vector3 pad = fuel != null ? fuel.homePad : Vector3.zero;
        Vector3 pos = pad + Vector3.up * 0.3f;
        Quaternion rot = Quaternion.identity;
        rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        rb.position = pos; rb.rotation = rot;
        transform.SetPositionAndRotation(pos, rot);
        GetComponent<Unity.Netcode.Components.NetworkTransform>()?.Teleport(pos, rot, transform.localScale);
        health = 1f;
    }

    bool IsCargo(Rigidbody other)
    {
        if (other == null) return false;
        var w = GetComponent<NetworkWinch>();
        return other.GetComponent<CrewBody>() || other.GetComponent<Recliner>() || other.GetComponent<Dog>() || (w != null && other == w.hook);
    }

    [Rpc(SendTo.Everyone)]
    void HitFxRpc(Vector3 point, bool rotor)
    {
        sparks.transform.position = point;
        sparks.Emit(rotor ? 40 : 15);
        var clip = rotor ? rotorStrike : crash;
        if (clip) AudioSource.PlayClipAtPoint(clip, point, rotor ? 1f : 0.8f);
    }

    // ------------------------------------------------------------------ everyone: smoke + alarm

    void Update()
    {
        float h = IsSpawned ? Health.Value : 1f;
        var em = smoke.emission;
        em.rateOverTime = h < 0.6f ? Mathf.Lerp(0f, 45f, (0.6f - h) / 0.6f) : 0f;
        var main = smoke.main;
        main.startColor = Color.Lerp(new Color(0.25f, 0.23f, 0.22f, 0.7f), new Color(0.6f, 0.58f, 0.55f, 0.5f), h / 0.6f);
        bool alarmOn = h < 0.3f;
        if (alarmOn && !alarmSource.isPlaying && alarmSource.clip) alarmSource.Play();
        if (!alarmOn && alarmSource.isPlaying) alarmSource.Stop();
    }

    ParticleSystem MakeSmoke()
    {
        var go = new GameObject("DamageSmoke");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 3.0f, -0.9f); // engine exhaust
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.maxParticles = 300;
        main.gravityModifier = -0.08f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.3f;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.5f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var em = ps.emission; em.rateOverTime = 0f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = smokeMaterial;
        ps.Play();
        return ps;
    }

    ParticleSystem MakeSparks()
    {
        var go = new GameObject("ImpactSparks");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 11f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor = new Color(1f, 0.8f, 0.35f, 1f);
        main.gravityModifier = 1.2f;
        main.maxParticles = 200;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.1f;
        var em = ps.emission; em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = smokeMaterial;
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f;
        ps.Play();
        return ps;
    }
}
