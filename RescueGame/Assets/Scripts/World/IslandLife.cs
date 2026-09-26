using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Life out on the island (built at load on every machine; motion runs off the shared clock):
///  - boats: a fishing boat bobbing by the pier, a sailboat tacking round the bay
///  - sheep grazing the hills near the lodge road, a few cows by the lake
///  - chimney smoke at the cabins and the lodge
///  - seagulls wheeling over the beach
///  - the lighthouse lamp turning, with a beam (it's golden hour, so it's just come on)
/// </summary>
[DefaultExecutionOrder(-400)]
public class IslandLife : MonoBehaviour
{
    public Material paintMaterial, smokeMaterial, beamMaterial;

    System.Random rnd;
    IslandGenerator isl;
    Transform fishingBoat, sailBoat, lampHead, beam;
    Vector3 fishingAt;
    readonly List<(Transform t, Vector3 at, float phase)> sheep = new();
    readonly List<(Transform t, float r, float phase)> gulls = new();
    Vector3 gullCentre;

    static readonly Color Hull = new(0.86f, 0.84f, 0.78f), HullTrim = new(0.2f, 0.4f, 0.6f), Sail = new(0.97f, 0.96f, 0.9f), Wood = new(0.5f, 0.34f, 0.22f),
                          Wool = new(0.92f, 0.9f, 0.85f), Face = new(0.15f, 0.13f, 0.12f), Cow = new(0.3f, 0.2f, 0.16f), CowPatch = new(0.95f, 0.93f, 0.9f),
                          Gull = new(0.95f, 0.95f, 0.97f), Lamp = new(1f, 0.95f, 0.75f);

    static double Now => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

    void Start()
    {
        isl = IslandGenerator.Current;
        if (isl == null) return;
        rnd = new System.Random(isl.seed + 99);
        BuildBoats();
        BuildAnimals();
        BuildSmoke();
        BuildGulls();
        BuildLighthouse();
    }

    // ------------------------------------------------------------------ boats

    void BuildBoats()
    {
        fishingAt = new Vector3(84f, IslandGenerator.SeaLevel, -450f);
        fishingBoat = Boat("FishingBoat", 6.5f, 2.4f, false);
        sailBoat = Boat("Sailboat", 7.5f, 2.2f, true);
    }

    Transform Boat(string name, float L, float W, bool sail)
    {
        var b = new GameObject(name).transform; b.SetParent(transform, false);
        Part(b, PrimitiveType.Cube, "Hull", new Vector3(0f, 0.4f, 0f), new Vector3(W, 0.9f, L), Hull);
        Part(b, PrimitiveType.Cube, "Bow", new Vector3(0f, 0.45f, L / 2f + 0.5f), new Vector3(W * 0.5f, 0.8f, 1.2f), Hull).transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        Part(b, PrimitiveType.Cube, "Trim", new Vector3(0f, 0.86f, 0f), new Vector3(W + 0.06f, 0.12f, L + 0.06f), HullTrim);
        Part(b, PrimitiveType.Cube, "Deck", new Vector3(0f, 0.9f, 0f), new Vector3(W - 0.3f, 0.06f, L - 0.3f), Wood);
        if (sail)
        {
            Part(b, PrimitiveType.Cylinder, "Mast", new Vector3(0f, 4.4f, 0.4f), new Vector3(0.14f, 3.6f, 0.14f), Wood);
            var s = Part(b, PrimitiveType.Cube, "Sail", new Vector3(0.05f, 4.6f, -1.2f), new Vector3(0.04f, 5.2f, 2.8f), Sail);
            s.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            var jib = Part(b, PrimitiveType.Cube, "Jib", new Vector3(0.05f, 3.8f, 2.0f), new Vector3(0.04f, 3.6f, 1.8f), Sail);
            jib.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        }
        else
        {
            Part(b, PrimitiveType.Cube, "Wheelhouse", new Vector3(0f, 1.7f, -1.0f), new Vector3(1.8f, 1.5f, 2.0f), HullTrim);
            Part(b, PrimitiveType.Cube, "Wheelhouse windows", new Vector3(0f, 1.95f, -1.0f), new Vector3(1.85f, 0.5f, 2.05f), new Color(0.3f, 0.4f, 0.5f));
            Part(b, PrimitiveType.Cylinder, "Chimney", new Vector3(0.5f, 2.8f, -1.6f), new Vector3(0.22f, 0.5f, 0.22f), Face);
            Part(b, PrimitiveType.Cylinder, "Mast", new Vector3(0f, 3.0f, 1.6f), new Vector3(0.1f, 2.2f, 0.1f), Wood);
            Part(b, PrimitiveType.Cube, "Boom", new Vector3(0f, 4.8f, 0.6f), new Vector3(0.1f, 0.1f, 3.6f), Wood).transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
        }
        return b;
    }

    // ------------------------------------------------------------------ animals

    void BuildAnimals()
    {
        // sheep on the grassy shoulder below the lodge and on the hills west of the city
        foreach (var (cx, cz, n) in new[] { (20f, 275f, 9), (-330f, 120f, 7), (300f, -140f, 6) })
            for (int i = 0; i < n; i++)
            {
                float x = cx + (float)(rnd.NextDouble() - 0.5) * 40f, z = cz + (float)(rnd.NextDouble() - 0.5) * 40f;
                float h = isl.Height(x, z);
                if (h < IslandGenerator.SeaLevel + 3f) continue;
                var s = new GameObject("Sheep").transform; s.SetParent(transform, false);
                Part(s, PrimitiveType.Sphere, "Body", new Vector3(0f, 0.55f, 0f), new Vector3(0.9f, 0.75f, 1.2f), Wool);
                Part(s, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.62f, 0.7f), new Vector3(0.35f, 0.35f, 0.45f), Face);
                foreach (var (lx, lz) in new[] { (-0.25f, 0.35f), (0.25f, 0.35f), (-0.25f, -0.35f), (0.25f, -0.35f) })
                    Part(s, PrimitiveType.Cylinder, "Leg", new Vector3(lx, 0.2f, lz), new Vector3(0.12f, 0.2f, 0.12f), Face);
                s.SetPositionAndRotation(new Vector3(x, h, z), Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
                sheep.Add((s, s.position, (float)rnd.NextDouble() * 10f));
            }
        // cows by the lake
        for (int i = 0; i < 5; i++)
        {
            float x = 400f + (float)(rnd.NextDouble() - 0.5) * 50f, z = 40f + (float)(rnd.NextDouble() - 0.5) * 40f;
            float h = isl.Height(x, z);
            if (h < IslandGenerator.SeaLevel + 2f) continue;
            var c = new GameObject("Cow").transform; c.SetParent(transform, false);
            Part(c, PrimitiveType.Cube, "Body", new Vector3(0f, 1.0f, 0f), new Vector3(1.1f, 1.0f, 2.2f), Cow);
            Part(c, PrimitiveType.Cube, "Patch", new Vector3(0.2f, 1.05f, -0.3f), new Vector3(0.8f, 0.9f, 0.9f), CowPatch);
            Part(c, PrimitiveType.Cube, "Head", new Vector3(0f, 1.2f, 1.35f), new Vector3(0.6f, 0.6f, 0.7f), Cow);
            foreach (var (lx, lz) in new[] { (-0.35f, 0.7f), (0.35f, 0.7f), (-0.35f, -0.7f), (0.35f, -0.7f) })
                Part(c, PrimitiveType.Cylinder, "Leg", new Vector3(lx, 0.3f, lz), new Vector3(0.2f, 0.3f, 0.2f), Cow);
            c.SetPositionAndRotation(new Vector3(x, h, z), Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
        }
    }

    // ------------------------------------------------------------------ smoke

    void BuildSmoke()
    {
        // chimney positions: the cabins are placed by LandNear() in IslandGenerator; recompute the same spots
        foreach (var (x, z, w, yaw) in new[] { (60f, 330f, 14f, 0f), (400f, 60f, 6f, 20f), (-300f, 380f, 6f, -30f), (520f, -200f, 7f, 45f) })
        {
            Vector3 at = LandNear(x, z);
            Vector3 chimney = at + Quaternion.Euler(0f, yaw, 0f) * new Vector3(w * 0.3f, 0.8f + (w > 10f ? 4.5f : 3f) + 3.5f, 0f);
            Smoke(chimney, 0.35f, 5f);
        }
    }

    Vector3 LandNear(float x, float z)
    {
        for (int i = 0; i < 40; i++)
        {
            float h = isl.Height(x, z);
            if (h > IslandGenerator.SeaLevel + 2f && SlopeAt(x, z) < 22f) return new Vector3(x, h, z);
            x *= 0.96f; z *= 0.96f;
        }
        return new Vector3(x, Mathf.Max(isl.Height(x, z), IslandGenerator.SeaLevel + 1f), z);
    }
    float SlopeAt(float x, float z)
    {
        float d = 4f;
        Vector3 n = Vector3.Cross(new Vector3(0f, isl.Height(x, z + d) - isl.Height(x, z - d), 2f * d), new Vector3(2f * d, isl.Height(x + d, z) - isl.Height(x - d, z), 0f)).normalized;
        return Vector3.Angle(n.y < 0 ? -n : n, Vector3.up);
    }

    void Smoke(Vector3 at, float size, float rate)
    {
        var go = new GameObject("ChimneySmoke"); go.transform.SetParent(transform, false); go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f); main.startSize = new ParticleSystem.MinMaxCurve(size, size * 2f); main.maxParticles = 80;
        main.startColor = new Color(0.8f, 0.78f, 0.76f, 0.45f); main.gravityModifier = -0.04f;
        var em = ps.emission; em.rateOverTime = rate;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.2f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 3f));
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = 1.2f; vel.z = 0.4f;  // drifting off in the breeze
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = smokeMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
    }

    // ------------------------------------------------------------------ gulls

    void BuildGulls()
    {
        gullCentre = new Vector3(20f, 22f, -380f);
        for (int i = 0; i < 9; i++)
        {
            var b = new GameObject("Gull").transform; b.SetParent(transform, false);
            Part(b, PrimitiveType.Cube, "Body", Vector3.zero, new Vector3(0.14f, 0.1f, 0.4f), Gull);
            Part(b, PrimitiveType.Cube, "WingL", new Vector3(-0.38f, 0f, 0f), new Vector3(0.7f, 0.03f, 0.22f), Gull).transform.localRotation = Quaternion.Euler(0f, 0f, 10f);
            Part(b, PrimitiveType.Cube, "WingR", new Vector3(0.38f, 0f, 0f), new Vector3(0.7f, 0.03f, 0.22f), Gull).transform.localRotation = Quaternion.Euler(0f, 0f, -10f);
            gulls.Add((b, 14f + (float)rnd.NextDouble() * 22f, (float)rnd.NextDouble() * 6.28f));
        }
    }

    // ------------------------------------------------------------------ lighthouse

    void BuildLighthouse()
    {
        // same spot the generator used
        Vector3 lh = LandNear(-560f, -300f);
        lampHead = new GameObject("LighthouseLamp").transform; lampHead.SetParent(transform, false);
        lampHead.position = lh + Vector3.up * 20.7f;
        Part(lampHead, PrimitiveType.Sphere, "Lamp", Vector3.zero, Vector3.one * 1.2f, Lamp);
        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(lampHead, false); light.type = LightType.Spot; light.range = 420f; light.spotAngle = 14f; light.intensity = 9f; light.color = new Color(1f, 0.95f, 0.8f);
        light.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
        light.shadows = LightShadows.None;
        // a visible beam cone (the searchlight's beam material)
        beam = new GameObject("Beam").transform; beam.SetParent(lampHead, false);
        var mf = beam.gameObject.AddComponent<MeshFilter>(); mf.sharedMesh = Cone(60, 220f, 1.2f, 22f);
        var mr = beam.gameObject.AddComponent<MeshRenderer>(); mr.sharedMaterial = beamMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.localRotation = Quaternion.Euler(4f, 0f, 0f);
    }

    static Mesh Cone(int segs, float length, float r0, float r1)
    {
        var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
        for (int i = 0; i <= segs; i++)
        {
            float a = i * Mathf.PI * 2f / segs;
            v.Add(new Vector3(Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, 0f)); uv.Add(new Vector2(i / (float)segs, 0f));
            v.Add(new Vector3(Mathf.Cos(a) * r1, Mathf.Sin(a) * r1, length)); uv.Add(new Vector2(i / (float)segs, 1f));
        }
        for (int i = 0; i < segs; i++) { int b = i * 2; t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b + 1); t.Add(b + 3); t.Add(b + 2); }
        var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (isl == null) return;
        double T = Now;
        float sea = IslandGenerator.SeaLevel;
        if (fishingBoat)
        {
            float bob = Mathf.Sin((float)T * 1.1f) * 0.18f, roll = Mathf.Sin((float)T * 0.9f + 1f) * 4f;
            fishingBoat.SetPositionAndRotation(fishingAt + Vector3.up * (0.1f + bob), Quaternion.Euler(Mathf.Sin((float)T * 0.7f) * 2f, 95f, roll));
        }
        if (sailBoat)
        {
            // a slow figure-of-eight round the bay south of the beach
            float u = (float)(T * 0.045);
            Vector3 c = new Vector3(-120f, sea + 0.1f, -560f);
            Vector3 p = c + new Vector3(Mathf.Sin(u) * 140f, 0f, Mathf.Sin(2f * u) * 55f);
            Vector3 p2 = c + new Vector3(Mathf.Sin(u + 0.01f) * 140f, 0f, Mathf.Sin(2f * (u + 0.01f)) * 55f);
            Vector3 fwd = p2 - p; fwd.y = 0f;
            sailBoat.SetPositionAndRotation(p + Vector3.up * (Mathf.Sin((float)T * 1.3f) * 0.12f), Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 0f, 12f + Mathf.Sin((float)T * 0.8f) * 3f));
        }
        foreach (var (t, at, phase) in sheep)
        {
            // graze: mostly still, the odd little hop
            float k = Mathf.Repeat((float)T * 0.35f + phase, 7f);
            t.position = at + Vector3.up * (k < 0.3f ? Mathf.Sin(k / 0.3f * Mathf.PI) * 0.25f : 0f);
        }
        for (int i = 0; i < gulls.Count; i++)
        {
            var (t, r, phase) = gulls[i];
            float a = (float)T * 0.35f * (i % 2 == 0 ? 1f : -1f) + phase;
            Vector3 p = gullCentre + new Vector3(Mathf.Cos(a) * r, Mathf.Sin((float)T * 0.9f + phase) * 3f + i * 0.7f, Mathf.Sin(a) * r);
            Vector3 fwd = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * (i % 2 == 0 ? 1f : -1f);
            t.SetPositionAndRotation(p, Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 0f, (i % 2 == 0 ? -1f : 1f) * 18f));
            float flap = Mathf.Sin((float)T * 5f + phase) * 25f;
            t.GetChild(1).localRotation = Quaternion.Euler(0f, 0f, 10f + flap);
            t.GetChild(2).localRotation = Quaternion.Euler(0f, 0f, -10f - flap);
        }
        if (lampHead) lampHead.rotation = Quaternion.Euler(0f, (float)(T * 24.0 % 360.0), 0f);
    }

    GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 local, Vector3 scale, Color colour)
    {
        var p = GameObject.CreatePrimitive(type);
        p.name = name; Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false); p.transform.localPosition = local; p.transform.localScale = scale;
        var r = p.GetComponent<Renderer>();
        if (paintMaterial) r.sharedMaterial = paintMaterial;
        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", colour); r.SetPropertyBlock(mpb);
        return p;
    }
}
