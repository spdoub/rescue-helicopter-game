using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A fire breaking out on a survivor's roof (the "changing environment" pressure dial). The host
/// only decides where and when it starts (SurvivorBrain.Fire); every machine then works out the
/// same spread from that: the roof is cut into cells, each catching a little later the further it
/// is from where it started, burning for a while, then leaving a black scorch mark.
///  - Flames, embers, a smoke column that leans with the wind, and a flickering orange glow.
///  - Anyone standing in the flames gets thrown off their feet. The survivor gets singed (a star
///    off the review and less pay) and trails smoke for the rest of the trip.
///  - Hovering over it, the hot air shoves the helicopter up and rattles it about.
///  - Once the survivor is aboard the fire brigade shows up (it dies down within a few seconds).
/// </summary>
[RequireComponent(typeof(SurvivorBrain))]
public class RoofFire : MonoBehaviour
{
    public Material flameMaterial, smokeMaterial;
    [Tooltip("Size (m) of one patch of roof that burns on its own.")]
    public float cellSize = 2.2f;
    [Tooltip("How fast the fire front creeps across the roof (m/s).")]
    public float spreadSpeed = 0.3f;
    [Tooltip("How long one patch burns before it's just scorch.")]
    public float burnSeconds = 70f;
    [Tooltip("Upward push on the helicopter right above the flames (m/s²).")]
    public float updraft = 2f;

    struct Cell { public Vector3 pos; public double ignite; public float rot, size; public bool inner; }

    SurvivorBrain brain;
    readonly List<Cell> cells = new();
    SurvivorBrain.FireState built;
    ParticleSystem flames, smoke;
    Light glow;
    Mesh scorchMesh; GameObject scorch;
    int scorchShown = -1;
    float emitDebt, smokeDebt, trailDebt, nextScan;
    CrewBody[] bodies = new CrewBody[0];
    readonly Dictionary<CrewBody, float> lastBurn = new();
    Vector3 centroid; int burning; float nextDebug;

    void Awake()
    {
        brain = GetComponent<SurvivorBrain>();
        flames = MakeSystem("RoofFlames", flameMaterial, 1500);
        smoke = MakeSystem("RoofSmoke", smokeMaterial, 900);
        var flameMain = flames.main; flameMain.gravityModifier = -0.25f;
        var fr = flames.GetComponent<ParticleSystemRenderer>();
        fr.renderMode = ParticleSystemRenderMode.Stretch; fr.velocityScale = 0.28f; fr.lengthScale = 1.1f; // licks of flame, stretched upward
        var smokeMain = smoke.main; smokeMain.gravityModifier = -0.02f;
        var sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.2f));
        var fz = flames.sizeOverLifetime; fz.enabled = true; fz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.1f)));
        var fc = flames.colorOverLifetime; fc.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.92f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.3f), new GradientColorKey(new Color(0.75f, 0.12f, 0.04f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.1f), new GradientAlphaKey(0.55f, 0.5f), new GradientAlphaKey(0f, 1f) });
        fc.color = g;
        var sc = smoke.colorOverLifetime; sc.enabled = true;
        var gs = new Gradient();
        gs.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        sc.color = gs;

        var l = new GameObject("FireGlow");
        glow = l.AddComponent<Light>();
        glow.type = LightType.Point; glow.color = new Color(1f, 0.55f, 0.2f); glow.range = 22f; glow.shadows = LightShadows.None;
        glow.enabled = false;

        scorch = new GameObject("RoofScorch");
        scorch.AddComponent<MeshFilter>().sharedMesh = scorchMesh = new Mesh { name = "Scorch" };
        var mr = scorch.AddComponent<MeshRenderer>();
        mr.sharedMaterial = smokeMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void OnDestroy()
    {
        if (flames) Destroy(flames.gameObject);
        if (smoke) Destroy(smoke.gameObject);
        if (glow) Destroy(glow.gameObject);
        if (scorch) Destroy(scorch);
    }

    static double Now => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

    public bool Active => brain.IsSpawned && brain.Fire.Value.start > 0.0;
    bool Burning(in Cell c, double now) => now >= c.ignite && now < c.ignite + burnSeconds && now < brain.Fire.Value.end;

    // ------------------------------------------------------------------ queries (host brain uses these)

    /// <summary>Is anything burning within <paramref name="r"/> metres (flat distance) of p?</summary>
    public bool BurningNear(Vector3 p, float r)
    {
        if (!Active) return false;
        double now = Now;
        foreach (var c in cells)
            if (Burning(c, now) && Flat(c.pos - p).sqrMagnitude < (r + cellSize * 0.5f) * (r + cellSize * 0.5f)) return true;
        return false;
    }

    /// <summary>Where on the roof the fire will reach last (to back away to).</summary>
    public Vector3 SafestSpot()
    {
        // The last patch to catch, preferring ones with roof all round them (nobody backs off a roof).
        Cell best = default; double latest = double.MinValue; bool inner = false;
        foreach (var c in cells)
            if ((c.inner && !inner) || (c.inner == inner && c.ignite > latest)) { latest = c.ignite; best = c; inner = c.inner; }
        return best.pos;
    }

    /// <summary>Solid roof at the same height 1.8 m out in every direction (so not near an edge,
    /// whichever way the building is turned).</summary>
    static bool RoofAllRound(Vector3 p)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 q = p + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 1.8f;
            if (!Physics.Raycast(q + Vector3.up * 1.5f, Vector3.down, out RaycastHit h, 2.5f, GameLayers.Gameplay, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(h.point.y - p.y) > 0.3f) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ every machine

    void Rebuild(SurvivorBrain.FireState f)
    {
        built = f;
        cells.Clear();
        scorchShown = -1;
        if (f.start <= 0.0) return;
        var rng = new System.Random(Mathf.RoundToInt(f.origin.x * 73f + f.origin.z * 31f));
        Vector3 size = f.roofMax - f.roofMin;
        int nx = Mathf.Clamp(Mathf.RoundToInt(size.x / cellSize), 1, 24), nz = Mathf.Clamp(Mathf.RoundToInt(size.z / cellSize), 1, 24);
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < nz; k++)
            {
                Vector3 p = new Vector3(f.roofMin.x + (i + 0.5f) * size.x / nx, f.roofMax.y, f.roofMin.z + (k + 0.5f) * size.z / nz);
                // Only where there's actually roof to burn (not over an air-con box or off an edge).
                if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 2f, GameLayers.Gameplay, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.9f
                    || hit.rigidbody != null || Mathf.Abs(hit.point.y - f.roofMax.y) > 0.25f) continue; // the roof itself, not a parapet or a prop
                p.y = hit.point.y;
                float d = Flat(p - f.origin).magnitude;
                double delay = d / spreadSpeed * (0.75 + rng.NextDouble() * 0.6);
                cells.Add(new Cell { pos = p, ignite = f.start + delay, rot = (float)rng.NextDouble() * 360f, size = cellSize * (1.3f + (float)rng.NextDouble() * 0.5f), inner = RoofAllRound(p) });
            }
    }

    void Update()
    {
        if (!brain.IsSpawned) return;
        var f = brain.Fire.Value;
        if (f.start != built.start) Rebuild(f);
        double now = Now;

        burning = 0; centroid = Vector3.zero; int charred = 0;
        foreach (var c in cells)
        {
            if (now >= c.ignite) charred++;
            if (!Burning(c, now)) continue;
            burning++; centroid += c.pos;
        }
        if (burning > 0) centroid /= burning;
        Emit(now);
        if (charred != scorchShown) BuildScorch(now, charred);

        if (Debug.isDebugBuild && burning > 0 && Time.time > nextDebug) { nextDebug = Time.time + 10f; Debug.Log($"[Fire] {name}: {burning}/{cells.Count} burning, flames {flames.particleCount}, smoke {smoke.particleCount}, smoke mat {(smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial ? smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial.name : "NONE")}"); }
        glow.enabled = burning > 0;
        if (burning > 0)
        {
            glow.transform.position = centroid + Vector3.up * 2.5f;
            float flicker = Mathf.PerlinNoise(Time.time * 7f, 0.3f) * 0.6f + 0.7f;
            glow.intensity = Mathf.Min(6f, 1.5f + burning * 0.15f) * flicker;
        }

        // Singed: a little smoke trail off them for the rest of the trip.
        if (brain.Singed.Value)
        {
            trailDebt += Time.deltaTime * 6f;
            var ep = new ParticleSystem.EmitParams { startColor = new Color(0.25f, 0.24f, 0.23f, 0.55f), startSize = 0.45f, startLifetime = 2.2f };
            for (; trailDebt >= 1f; trailDebt--)
            {
                ep.position = transform.position + Vector3.up * 0.6f + Random.insideUnitSphere * 0.2f;
                ep.velocity = Vector3.up * 0.8f + Wind * 0.5f;
                smoke.Emit(ep, 1);
            }
        }
    }

    static Vector3 Wind => Weather.Instance != null && Weather.Instance.IsSpawned ? Weather.Instance.Wind.Value : Vector3.zero;

    void Emit(double now)
    {
        if (burning == 0) { emitDebt = smokeDebt = 0f; return; }
        Vector3 wind = Wind;
        // Busy fires get a few less flames per patch so a whole roof ablaze stays cheap.
        float perCell = Mathf.Lerp(22f, 10f, Mathf.Clamp01(burning / 80f));
        emitDebt += burning * perCell * Time.deltaTime;
        smokeDebt += Mathf.Max(3f, burning * 1.6f) * Time.deltaTime; // even a small fire makes a visible plume
        int n = (int)emitDebt, s = (int)smokeDebt;
        emitDebt -= n; smokeDebt -= s;
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < n; i++)
        {
            var c = PickBurning(now);
            float age = (float)(now - c.ignite), life = Mathf.Min(1f, age / 4f) * Mathf.Clamp01((float)(c.ignite + burnSeconds - now) / 8f); // grows in, dies down
            if (Random.value > 0.15f + life) continue;
            bool ember = Random.value < 0.06f;
            ep.position = c.pos + new Vector3(Random.Range(-1f, 1f) * cellSize * 0.5f, 0.2f, Random.Range(-1f, 1f) * cellSize * 0.5f);
            ep.velocity = Vector3.up * (ember ? Random.Range(1.5f, 3f) : Random.Range(2f, 4f)) + wind * (ember ? 0.5f : 0.25f) + Random.insideUnitSphere * 0.5f;
            ep.startSize = ember ? Random.Range(0.05f, 0.1f) : Random.Range(0.45f, 1.0f) * (0.6f + life * 0.6f);
            ep.startLifetime = ember ? Random.Range(1.5f, 2.5f) : Random.Range(0.35f, 0.75f);
            ep.rotation = Random.Range(-15f, 15f);
            ep.startColor = Color.white;
            flames.Emit(ep, 1);
        }
        var sp = new ParticleSystem.EmitParams();
        for (int i = 0; i < s; i++)
        {
            var c = PickBurning(now);
            sp.position = c.pos + Vector3.up * 1.6f + Random.insideUnitSphere * 0.6f;
            sp.velocity = Vector3.up * Random.Range(2.5f, 4f) + wind * 0.7f;
            sp.startSize = Random.Range(2f, 3.2f);
            sp.startLifetime = Random.Range(5f, 7.5f);
            float grey = Random.Range(0.16f, 0.3f);
            sp.startColor = new Color(grey, grey * 0.95f, grey * 0.9f, 0.75f);
            sp.rotation = Random.Range(0f, 360f);
            smoke.Emit(sp, 1);
        }
    }

    Cell PickBurning(double now)
    {
        for (int tries = 0; tries < 12; tries++)
        {
            var c = cells[Random.Range(0, cells.Count)];
            if (Burning(c, now)) return c;
        }
        foreach (var c in cells) if (Burning(c, now)) return c;
        return cells[0];
    }

    void BuildScorch(double now, int count)
    {
        scorchShown = count;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var tri = new List<int>();
        int k = 0;
        foreach (var c in cells)
        {
            if (now < c.ignite) continue;
            var rot = Quaternion.Euler(0f, c.rot, 0f);
            float h = c.size * 0.5f, y = 0.015f + (k++ % 7) * 0.002f; // staggered a hair so overlapping blotches don't flicker
            int b = v.Count;
            v.Add(c.pos + rot * new Vector3(-h, y, -h)); v.Add(c.pos + rot * new Vector3(-h, y, h));
            v.Add(c.pos + rot * new Vector3(h, y, h)); v.Add(c.pos + rot * new Vector3(h, y, -h));
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
            var dark = new Color(0.05f, 0.04f, 0.035f, 0.85f);
            col.Add(dark); col.Add(dark); col.Add(dark); col.Add(dark);
            tri.Add(b); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b); tri.Add(b + 2); tri.Add(b + 3);
        }
        scorchMesh.Clear();
        scorchMesh.SetVertices(v); scorchMesh.SetUVs(0, uv); scorchMesh.SetColors(col); scorchMesh.SetTriangles(tri, 0);
        scorchMesh.RecalculateBounds();
        scorchMesh.RecalculateNormals();
    }

    void FixedUpdate()
    {
        if (!Active || burning == 0) return;
        double now = Now;
        var f = brain.Fire.Value;

        // Hot air: right above the flames the helicopter gets shoved up and shaken (host flies it).
        if (NetworkManager.Singleton.IsServer)
        {
            var heli = FindHeli();
            if (heli && !heli.isKinematic)
            {
                Vector3 d = heli.position - centroid;
                float flat = Flat(d).magnitude, up = d.y;
                float spread = Mathf.Sqrt(burning) * cellSize * 0.5f + 3f;
                if (flat < spread + 4f && up > 0f && up < 35f)
                {
                    float k = Mathf.Clamp01(1f - flat / (spread + 4f)) * Mathf.Clamp01(1f - up / 35f) * Mathf.Clamp01(burning / 12f)
                            * (HeliUpgrades.Instance ? HeliUpgrades.Instance.PushScale : 1f);
                    // a steady lift plus slow, rolling bumps (Perlin, not per-tick noise, so it rocks rather than rattles)
                    float t = Time.time * 0.8f;
                    Vector3 bump = new Vector3(Mathf.PerlinNoise(t, 1.7f) - 0.5f, Mathf.PerlinNoise(t, 5.3f) - 0.5f, Mathf.PerlinNoise(t, 9.1f) - 0.5f);
                    heli.AddForce((Vector3.up + bump * 1.2f) * updraft * k, ForceMode.Acceleration);
                    heli.AddTorque(bump * 0.35f * k, ForceMode.Acceleration);
                }
            }
        }

        // Standing in it: thrown clear (whoever simulates that body does this).
        if (Time.time > nextScan) { nextScan = Time.time + 1.5f; bodies = FindObjectsByType<CrewBody>(FindObjectsSortMode.None); }
        foreach (var b in bodies)
        {
            if (!b) continue;
            var rb = b.GetComponent<Rigidbody>();
            bool isMe = b.gameObject == gameObject;
            bool chairBound = isMe && brain.chair != null;
            Vector3 feet = chairBound ? brain.chair.transform.position : b.Feet;
            if ((rb.isKinematic && !chairBound) || (b.Seated && !chairBound)) continue;
            if (b.IsHolding) continue;                                     // hanging on to the hook (or anything): swinging clear of it
            if (isMe && brain.Current != SurvivorBrain.State.Waiting) continue; // on their way out already
            if (feet.x < f.roofMin.x - 0.5f || feet.x > f.roofMax.x + 0.5f || feet.z < f.roofMin.z - 0.5f || feet.z > f.roofMax.z + 0.5f) continue;
            if (Mathf.Abs(feet.y - f.roofMax.y) > 1.2f) continue;
            if (!InFlames(feet, now)) continue;
            if (lastBurn.TryGetValue(b, out float last) && Time.time - last < 2.5f) continue;
            lastBurn[b] = Time.time;
            // Thrown out of the flames: the survivor toward the far (safe) corner so the fire can't
            // chase them off the edge; anyone else just away from the heart of the fire, gently.
            Vector3 away = isMe ? Flat(SafestSpot() - feet) : Flat(feet - centroid);
            float push = isMe ? Mathf.Min(3f, away.magnitude * 1.1f) : 2.2f; // (a hop that lands at the safe spot, not past it)
            if (away.sqrMagnitude < 0.01f) away = Random.onUnitSphere;
            away = Flat(away).normalized;
            if (!chairBound) b.KnockDown(away * push + Vector3.up * 3.5f);
            if (isMe && NetworkManager.Singleton.IsServer) brain.Burned();
        }
    }

    bool InFlames(Vector3 p, double now)
    {
        float r = cellSize * 0.55f;
        foreach (var c in cells)
            if (Burning(c, now) && now - c.ignite > 2.0 && Mathf.Abs(c.pos.x - p.x) < r && Mathf.Abs(c.pos.z - p.z) < r) return true;
        return false;
    }

    Rigidbody heliRb;
    Rigidbody FindHeli()
    {
        if (heliRb == null) { var h = FindAnyObjectByType<NetworkHelicopter>(); if (h) heliRb = h.GetComponent<Rigidbody>(); }
        return heliRb;
    }

    ParticleSystem MakeSystem(string name, Material mat, int max)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.playOnAwake = false;
        var em = ps.emission; em.rateOverTime = 0f;
        var shape = ps.shape; shape.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
