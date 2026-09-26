using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The island the city sits on, generated when the level loads (seeded: the same on every machine,
/// nothing stored). Low-poly faceted terrain, ~1.6 x 1.2 km:
///  - the city on a flat coastal plain in the middle (the existing asphalt ground plane covers it)
///  - a long sandy beach along the south coast, with a pier
///  - sea-cliffs on the west coast (a high plateau that drops straight into the sea), a lighthouse
///  - mountains along the north, snow on the tops, a ski lodge in a high valley
///  - rolling hills and forest between, a lake in the east with a fishing cabin
/// Materials are Stylized Lit flat colours (grass/sand/rock/snow in two shades each for a patchy look).
/// </summary>
[DefaultExecutionOrder(-550)]
public class IslandGenerator : MonoBehaviour
{
    public int seed = 20260926;
    [Tooltip("Grass A/B, Sand, Rock A/B, Snow, Water, Trunk, Leaves A/B/C, Wood, Stone, Roof, Asphalt, Paint, Cable (17 materials, in this order)")]
    public Material[] mats = new Material[17];
    public float halfX = 800f, halfZ = 600f, cell = 10f;
    public const float SeaLevel = -1.5f;

    const int GrassA = 0, GrassB = 1, Sand = 2, RockA = 3, RockB = 4, Snow = 5, Water = 6, Trunk = 7, LeafA = 8, LeafB = 9, LeafC = 10, Wood = 11, Stone = 12, RoofM = 13, Asphalt = 14, Paint = 15, Cable = 16;
    const int MatCount = 17;

    public static IslandGenerator Current { get; private set; }
    System.Random rnd;

    void Awake()
    {
        Current = this;
        if (Application.isPlaying) Generate();
    }

    public void Generate()
    {
        rnd = new System.Random(seed + 1);
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
        BuildTerrain();
        BuildSea();
        BuildForest();
        BuildLandmarks();
        BuildRoads();
        BuildGondola();
        if (!Application.isPlaying)
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (t != transform) t.gameObject.hideFlags = HideFlags.DontSave;
    }

    // ------------------------------------------------------------------ the shape of the land

    static float Noise(float x, float z, float scale, float ox) => Mathf.PerlinNoise(x / scale + ox, z / scale + ox * 0.37f);
    static float Fbm(float x, float z, float scale, float ox)
    {
        return Noise(x, z, scale, ox) * 0.55f + Noise(x, z, scale * 0.45f, ox + 9.1f) * 0.3f + Noise(x, z, scale * 0.18f, ox + 17.3f) * 0.15f;
    }
    static float Smooth(float a, float b, float t) => Mathf.SmoothStep(a, b, Mathf.Clamp01(t));

    /// <summary>Terrain height at a point (metres). Sea is anything below SeaLevel.</summary>
    public float Height(float x, float z)
    {
        // island outline: a lumpy oval, sea beyond it
        float nx = x / halfX, nz = z / halfZ;
        float r = Mathf.Sqrt(nx * nx + nz * nz);
        float edge = 0.86f + (Fbm(x, z, 420f, 3.3f) - 0.5f) * 0.22f;
        float land = Smooth(1f, 0f, (r - edge + 0.10f) / 0.12f);            // 1 inland .. 0 at the coast
        float baseH = -9f + land * 12f;                                        // sea floor -9, coast rises to ~3

        // gentle hills everywhere inland
        float hills = (Fbm(x, z, 260f, 1.7f) - 0.35f) * 45f;
        float h = baseH + Mathf.Max(0f, hills) * land;

        // the city plain: dead flat at 0 out to 240 m, blended out by 300 m
        float cityR = Mathf.Sqrt(x * x + z * z);
        float cityK = Smooth(1f, 0f, (cityR - 240f) / 60f);
        h = Mathf.Lerp(h, 0f, cityK);

        // mountains along the north: a ridge, tallest in the middle
        float mk = Smooth(0f, 1f, (z - 200f) / 200f) * Smooth(1f, 0f, (Mathf.Abs(x) - 380f) / 220f) * land;
        float ridge = (Fbm(x, z, 210f, 7.7f) * 0.7f + 0.3f) * 230f * mk;
        ridge *= Smooth(0.35f, 1f, (z - 240f) / 220f);
        h += ridge * (1f - cityK);

        // the west cliffs: a plateau at ~55 m that ends in a sheer drop into the sea
        float ck = Smooth(0f, 1f, (-x - 420f) / 120f) * Smooth(1f, 0f, (Mathf.Abs(z) - 260f) / 140f);
        float plateau = 55f + (Fbm(x, z, 120f, 4.4f) - 0.5f) * 14f;
        float cliffEdge = 0.80f + (Noise(x, z, 160f, 8.8f) - 0.5f) * 0.06f;
        float cliffDrop = Smooth(1f, 0f, (r - cliffEdge) / 0.025f);         // sharp
        float cliffH = Mathf.Lerp(-12f, plateau, cliffDrop);
        h = Mathf.Lerp(h, cliffH, ck * (1f - cityK));

        // the south beach: a broad, gentle sand slope down to the sea
        float bk = Smooth(0f, 1f, (-z - 230f) / 120f) * Smooth(1f, 0f, (Mathf.Abs(x) - 420f) / 160f);
        float beach = Mathf.Lerp(3.5f, -6f, Mathf.Clamp01((-z - 300f) / 220f)) + (Noise(x, z, 60f, 2.2f) - 0.5f) * 1.2f;
        h = Mathf.Lerp(h, beach, bk * (1f - cityK));

        // the lake in the east: a bowl below sea level
        float lk = Smooth(0f, 1f, 1f - Vector2.Distance(new Vector2(x, z), new Vector2(470f, 120f)) / 110f);
        h = Mathf.Lerp(h, -9f, lk * lk);
        return h;
    }

    int MaterialFor(Vector3 a, Vector3 b, Vector3 c, float nz)
    {
        Vector3 n = Vector3.Cross(b - a, c - a).normalized; if (n.y < 0f) n = -n;
        float y = (a.y + b.y + c.y) / 3f, slope = Vector3.Angle(n, Vector3.up);
        float cx = (a.x + b.x + c.x) / 3f, cz = (a.z + b.z + c.z) / 3f;
        bool patch = Noise(cx, cz, 38f, 5.5f) > 0.5f;
        if (y > 150f + (patch ? 10f : 0f)) return slope > 55f ? RockB : Snow;
        if (slope > 36f) return patch ? RockA : RockB;
        if (y < SeaLevel + 5.5f) return y < SeaLevel - 3f ? RockB : Sand;
        if (y > 120f) return patch ? RockA : GrassB;
        return patch ? GrassA : GrassB;
    }

    Mesh terrainMesh;
    public bool OnLand(float x, float z) => Height(x, z) > SeaLevel + 0.5f;

    void BuildTerrain()
    {
        int nx = Mathf.RoundToInt(halfX * 2f / cell), nz = Mathf.RoundToInt(halfZ * 2f / cell);
        var grid = new Vector3[nx + 1, nz + 1];
        for (int i = 0; i <= nx; i++)
            for (int k = 0; k <= nz; k++)
            {
                float x = -halfX + i * cell, z = -halfZ + k * cell;
                // jitter the grid a little (except the outer ring) for an organic low-poly look
                float jx = (i > 0 && i < nx) ? (Noise(x, z, 17f, 1.1f) - 0.5f) * cell * 0.6f : 0f;
                float jz = (k > 0 && k < nz) ? (Noise(x, z, 17f, 2.9f) - 0.5f) * cell * 0.6f : 0f;
                float h = Height(x + jx, z + jz);
                // keep the city plain exactly flat and slightly under the asphalt plane
                if (Mathf.Sqrt(x * x + z * z) < 235f) h = -0.08f;
                grid[i, k] = new Vector3(x + jx, h, z + jz);
            }
        var kit = new Kit(MatCount);
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < nz; k++)
            {
                Vector3 a = grid[i, k], b = grid[i + 1, k], c = grid[i + 1, k + 1], d = grid[i, k + 1];
                // skip cells that are entirely under the city's ground plane (it covers them)
                if (Mathf.Abs(a.x) < 290f && Mathf.Abs(a.z) < 290f && Mathf.Abs(c.x) < 290f && Mathf.Abs(c.z) < 290f
                    && a.y < 0f && b.y < 0f && c.y < 0f && d.y < 0f && Mathf.Sqrt(a.x * a.x + a.z * a.z) < 235f && Mathf.Sqrt(c.x * c.x + c.z * c.z) < 235f) continue;
                // deep sea floor: not drawn (the sea is opaque enough to hide it), so no square rim shows from the air
                if (a.y < SeaLevel - 4f && b.y < SeaLevel - 4f && c.y < SeaLevel - 4f && d.y < SeaLevel - 4f) continue;
                bool diag = ((i + k) & 1) == 0;
                if (diag) { kit.Tri(MaterialFor(a, b, c, 0), a, b, c); kit.Tri(MaterialFor(a, c, d, 0), a, c, d); }
                else { kit.Tri(MaterialFor(a, b, d, 0), a, b, d); kit.Tri(MaterialFor(b, c, d, 0), b, c, d); }
            }
        var go = new GameObject("Terrain"); go.transform.SetParent(transform, false);
        terrainMesh = kit.ToMesh("IslandTerrain");
        go.AddComponent<MeshFilter>().sharedMesh = terrainMesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        go.AddComponent<MeshCollider>().sharedMesh = terrainMesh;
        go.layer = 0;
    }

    void BuildSea()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Sea"; go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(0f, SeaLevel, 0f);
        go.transform.localScale = new Vector3(halfX * 2f * 4f / 10f, 1f, halfZ * 2f * 5f / 10f);   // out to the horizon haze
        go.GetComponent<Renderer>().sharedMaterial = mats[Water];
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(go.GetComponent<Collider>());
        go.AddComponent<Sea>();
    }

    // ------------------------------------------------------------------ trees

    void BuildForest()
    {
        var chunks = new Dictionary<(int, int), Kit>();
        int count = 0;
        for (float x = -halfX + 20f; x < halfX - 20f; x += 9f)
            for (float z = -halfZ + 20f; z < halfZ - 20f; z += 9f)
            {
                float px = x + (float)(rnd.NextDouble() - 0.5) * 8f, pz = z + (float)(rnd.NextDouble() - 0.5) * 8f;
                if (Mathf.Sqrt(px * px + pz * pz) < 275f) continue;                  // not in the city
                float h = Height(px, pz);
                if (h < SeaLevel + 6f || h > 140f) continue;                             // not on the beach or up in the snow
                float slope = SlopeAt(px, pz);
                if (slope > 30f) continue;
                float density = Fbm(px, pz, 180f, 12.5f);                              // forest belts
                if (density < 0.52f || rnd.NextDouble() > (density - 0.5f) * 2.4f) continue;
                var key = (Mathf.FloorToInt(px / 160f), Mathf.FloorToInt(pz / 160f));
                if (!chunks.TryGetValue(key, out var kit)) chunks[key] = kit = new Kit(MatCount);
                Tree(kit, new Vector3(px, h - 0.3f, pz), h > 95f);
                count++;
            }
        foreach (var kv in chunks)
        {
            var go = new GameObject($"Forest_{kv.Key.Item1}_{kv.Key.Item2}"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = kv.Value.ToMesh("Forest");
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        }
    }

    float SlopeAt(float x, float z)
    {
        float d = 4f;
        Vector3 n = Vector3.Cross(new Vector3(0f, Height(x, z + d) - Height(x, z - d), 2f * d), new Vector3(2f * d, Height(x + d, z) - Height(x - d, z), 0f)).normalized;
        return Vector3.Angle(n.y < 0 ? -n : n, Vector3.up);
    }

    void Tree(Kit kit, Vector3 at, bool alpine)
    {
        float s = 0.8f + (float)rnd.NextDouble() * 0.7f;
        int leaf = alpine ? LeafC : (rnd.Next(3) == 0 ? LeafB : LeafA);
        if (alpine || rnd.NextDouble() < 0.35)
        {
            // conifer: trunk + two stacked cones
            kit.Cylinder(Trunk, at, 0.25f * s, 2.2f * s, 5);
            kit.Cone(leaf, at + Vector3.up * 1.6f * s, 2.4f * s, 4.5f * s, 6);
            kit.Cone(leaf, at + Vector3.up * 4.2f * s, 1.7f * s, 3.6f * s, 6);
        }
        else
        {
            // broadleaf: trunk + a lumpy blob
            kit.Cylinder(Trunk, at, 0.3f * s, 3.2f * s, 5);
            kit.Blob(leaf, at + Vector3.up * 4.6f * s, 3.0f * s, rnd);
        }
    }

    // ------------------------------------------------------------------ landmarks

    void BuildLandmarks()
    {
        var kit = new Kit(MatCount);
        // lighthouse on the cliff point (south-west)
        Vector3 lh = LandNear(-560f, -300f);
        kit.Cylinder(Stone, lh, 4.5f, 2f, 10);
        kit.Cylinder(Stone, lh + Vector3.up * 2f, 3.2f, 16f, 10);
        kit.Cylinder(Stone, lh + Vector3.up * 18f, 3.8f, 1.2f, 10);
        kit.Cylinder(Water, lh + Vector3.up * 19.2f, 2.6f, 3f, 8);                    // the lamp room (glassy)
        kit.Cone(RoofM, lh + Vector3.up * 22.2f, 3.4f, 2.6f, 10);
        for (int i = 0; i < 3; i++) kit.Box(RoofM, lh + Vector3.up * (5f + i * 5f), new Vector3(7f, 1.2f, 7f)); // red bands
        // a pier out from the beach
        Vector3 pier = new Vector3(60f, 0f, -360f); pier.y = Height(pier.x, pier.z);
        for (int i = 0; i < 14; i++)
        {
            Vector3 p = pier + Vector3.forward * (-i * 6f);
            float top = Mathf.Max(Height(p.x, p.z), SeaLevel) + 2.4f;
            kit.Box(Wood, new Vector3(p.x, top, p.z), new Vector3(5f, 0.3f, 6.2f));
            foreach (var sx in new[] { -2.2f, 2.2f }) kit.Cylinder(Trunk, new Vector3(p.x + sx, top - 6f, p.z), 0.25f, 6f, 5);
            foreach (var sx in new[] { -2.4f, 2.4f }) kit.Box(Wood, new Vector3(p.x + sx, top + 0.6f, p.z), new Vector3(0.12f, 0.9f, 6.2f));
        }
        Vector3 hut = pier + Vector3.forward * -84f; hut.y = Mathf.Max(Height(hut.x, hut.z), SeaLevel) + 2.55f;
        Cabin(kit, hut, 6f, 5f, 3f, 0f);
        // the ski lodge in a mountain valley (north), a couple of cabins by the lake and in the forest
        Cabin(kit, LandNear(60f, 330f), 14f, 9f, 4.5f, 0f);
        Cabin(kit, LandNear(400f, 60f), 6f, 5f, 3f, 20f);
        Cabin(kit, LandNear(-300f, 380f), 6f, 5f, 3f, -30f);
        Cabin(kit, LandNear(520f, -200f), 7f, 5f, 3f, 45f);
        // boulders scattered on the slopes
        for (int i = 0; i < 90; i++)
        {
            float x = (float)(rnd.NextDouble() - 0.5) * halfX * 1.9f, z = (float)(rnd.NextDouble() - 0.5) * halfZ * 1.9f;
            float h = Height(x, z);
            if (h < SeaLevel + 2f || Mathf.Sqrt(x * x + z * z) < 280f || SlopeAt(x, z) < 12f) continue;
            kit.Blob(RockA, new Vector3(x, h + 0.5f, z), 2f + (float)rnd.NextDouble() * 4f, rnd);
        }
        var go = new GameObject("Landmarks"); go.transform.SetParent(transform, false);
        var mesh = kit.ToMesh("Landmarks");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    Vector3 LandNear(float x, float z)
    {
        // nudge inland until it's on land and not too steep
        for (int i = 0; i < 40; i++)
        {
            float h = Height(x, z);
            if (h > SeaLevel + 2f && SlopeAt(x, z) < 22f) return new Vector3(x, h, z);
            x *= 0.96f; z *= 0.96f;
        }
        return new Vector3(x, Mathf.Max(Height(x, z), SeaLevel + 1f), z);
    }

    void Cabin(Kit kit, Vector3 at, float w, float d, float h, float yawDeg)
    {
        var q = Quaternion.Euler(0f, yawDeg, 0f);
        kit.BoxRot(Stone, at + Vector3.up * 0.4f, new Vector3(w + 0.6f, 0.8f, d + 0.6f), q);      // footing
        kit.BoxRot(Wood, at + Vector3.up * (0.8f + h / 2f), new Vector3(w, h, d), q);            // walls
        kit.BoxRot(RoofM, at + Vector3.up * (0.8f + h + 0.7f), new Vector3(w + 1.2f, 1.4f, d + 1.2f), q); // fat roof slab
        kit.BoxRot(RoofM, at + Vector3.up * (0.8f + h + 1.9f), new Vector3(w * 0.5f, 1.2f, d + 0.6f), q); // ridge
        kit.BoxRot(Stone, at + q * new Vector3(w * 0.3f, 0.8f + h + 2.2f, 0f), new Vector3(1f, 2.6f, 1f), q); // chimney
        kit.BoxRot(Water, at + q * new Vector3(0f, 0.8f + h * 0.55f, d / 2f + 0.02f), new Vector3(w * 0.4f, h * 0.4f, 0.05f), q); // window
    }

    // ------------------------------------------------------------------ roads

    /// <summary>A country road from the city's edge out to each landmark: a ribbon that hugs the terrain
    /// (0.15 m above it), with a dashed centre line, a low stone wall here and there, and a lay-by at the end.</summary>
    void BuildRoads()
    {
        var kit = new Kit(MatCount);
        // routes: city edge -> waypoints (the terrain gives the height)
        var routes = new[]
        {
            new[] { new Vector2(60f, -240f), new Vector2(60f, -300f), new Vector2(64f, -348f) },                                      // south: to the pier
            new[] { new Vector2(40f, 240f), new Vector2(50f, 290f), new Vector2(60f, 320f) },                                         // north: to the lodge
            new[] { new Vector2(240f, 60f), new Vector2(330f, 70f), new Vector2(400f, 60f) },                                         // east: to the lake cabin
            new[] { new Vector2(-240f, -60f), new Vector2(-380f, -140f), new Vector2(-470f, -230f), new Vector2(-545f, -290f) },        // west: to the lighthouse
        };
        foreach (var r in routes) Road(kit, r, 6f);
        var go = new GameObject("Roads"); go.transform.SetParent(transform, false);
        var mesh = kit.ToMesh("Roads");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    void Road(Kit kit, Vector2[] pts, float width)
    {
        // resample the polyline every 6 m with a Catmull-Rom curve between the waypoints
        var path = new List<Vector2>();
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector2 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Length - 1)];
            int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 6f));
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)n, t2 = t * t, t3 = t2 * t;
                path.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
            }
        }
        path.Add(pts[pts.Length - 1]);
        Vector3 P(Vector2 q, float side, float lift)
        {
            // smoothed height across the road's width so it doesn't tilt with every terrain facet
            float h = 0f; int c = 0;
            for (float dx = -3f; dx <= 3f; dx += 3f) for (float dz = -3f; dz <= 3f; dz += 3f) { h += Height(q.x + dx, q.y + dz); c++; }
            return new Vector3(q.x, Mathf.Max(h / c, SeaLevel + 0.6f) + lift, q.y);
        }
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 a = path[i], b = path[i + 1];
            Vector2 dir = (b - a).normalized, side = new Vector2(-dir.y, dir.x) * (width / 2f);
            Vector3 a0 = P(a - side, 0, 0.15f), a1 = P(a + side, 0, 0.15f), b0 = P(b - side, 0, 0.15f), b1 = P(b + side, 0, 0.15f);
            kit.Quad(Asphalt, a0, a1, b1, b0, Vector3.up);
            // shoulders (a thin sand/gravel edge so the asphalt doesn't float above grass facets)
            Vector2 s2 = side * 1.5f;
            kit.Quad(Sand, P(a - s2, 0, 0.05f), a0 + Vector3.down * 0.02f, b0 + Vector3.down * 0.02f, P(b - s2, 0, 0.05f), Vector3.up);
            kit.Quad(Sand, a1 + Vector3.down * 0.02f, P(a + s2, 0, 0.05f), P(b + s2, 0, 0.05f), b1 + Vector3.down * 0.02f, Vector3.up);
            if (i % 2 == 0)
            {
                Vector2 m = (a + b) / 2f, ds = new Vector2(-dir.y, dir.x) * 0.12f, dl = dir * 1.6f;
                kit.Quad(Paint, P(m - dl - ds, 0, 0.17f), P(m - dl + ds, 0, 0.17f), P(m + dl + ds, 0, 0.17f), P(m + dl - ds, 0, 0.17f), Vector3.up);
            }
            // a low dry-stone wall along one side on some stretches
            if ((i / 9) % 3 == 1)
            {
                Vector3 w0 = P(a + side * 1.35f, 0, 0f), w1 = P(b + side * 1.35f, 0, 0f);
                kit.BoxRot(Stone, (w0 + w1) / 2f + Vector3.up * 0.45f, new Vector3(0.5f, 0.9f, Vector3.Distance(w0, w1) + 0.1f), Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y)));
            }
        }
        // a signpost at the start
        Vector3 s0 = P(path[0], 0, 0f) + new Vector3(width / 2f + 1.5f, 0f, 0f);
        kit.Cylinder(Trunk, s0, 0.08f, 2.4f, 5);
        kit.Box(Wood, s0 + Vector3.up * 2.1f, new Vector3(1.2f, 0.3f, 0.06f));
    }

    // ------------------------------------------------------------------ the gondola (a mountain rescue waiting to happen)

    /// <summary>A cable car up the mountain: a base station by the lodge, pylons up the slope, a top station near
    /// the snow, and one cabin hanging mid-span. The cabin is where a future rescue takes place.</summary>
    void BuildGondola()
    {
        var kit = new Kit(MatCount);
        Vector2 a = new Vector2(80f, 330f), b = new Vector2(130f, 470f);
        Vector3 A = new Vector3(a.x, Height(a.x, a.y), a.y), B = new Vector3(b.x, Height(b.x, b.y), b.y);
        // stations
        foreach (var (p, name) in new[] { (A, "Base"), (B, "Top") })
        {
            kit.Box(Stone, p + Vector3.up * 1.5f, new Vector3(9f, 3f, 7f));
            kit.Box(RoofM, p + Vector3.up * 3.6f, new Vector3(10f, 1.2f, 8f));
            kit.Box(Metal_(), p + Vector3.up * 6.5f, new Vector3(0.6f, 6f, 0.6f));   // the wheel tower
            kit.Cylinder(Stone, p + Vector3.up * 8.5f, 1.6f, 0.4f, 12);               // bull wheel
        }
        // pylons every ~45 m, cable hanging between them (sagging), and the cabin on the middle span
        int n = Mathf.CeilToInt(Vector3.Distance(A, B) / 45f);
        var tops = new List<Vector3>();
        tops.Add(A + Vector3.up * 8.5f);
        for (int i = 1; i < n; i++)
        {
            float t = i / (float)n;
            Vector2 q = Vector2.Lerp(a, b, t);
            Vector3 g = new Vector3(q.x, Height(q.x, q.y), q.y);
            float hgt = 14f + Mathf.Sin(t * Mathf.PI) * 6f;
            kit.Box(Metal_(), g + Vector3.up * hgt / 2f, new Vector3(1.2f, hgt, 1.2f));
            kit.Box(Metal_(), g + Vector3.up * (hgt - 0.5f), new Vector3(5f, 0.5f, 0.8f));  // cross arm
            kit.Box(Stone, g + Vector3.up * 0.6f, new Vector3(3f, 1.2f, 3f));
            tops.Add(g + Vector3.up * hgt);
        }
        tops.Add(B + Vector3.up * 8.5f);
        for (int i = 0; i < tops.Count - 1; i++)
        {
            Vector3 p0 = tops[i], p1 = tops[i + 1];
            int segs = 8;
            for (int k = 0; k < segs; k++)
            {
                float t0 = k / (float)segs, t1 = (k + 1) / (float)segs;
                Vector3 c0 = Vector3.Lerp(p0, p1, t0) + Vector3.down * (Mathf.Sin(t0 * Mathf.PI) * 2.5f);
                Vector3 c1 = Vector3.Lerp(p0, p1, t1) + Vector3.down * (Mathf.Sin(t1 * Mathf.PI) * 2.5f);
                foreach (var sx in new[] { -1.8f, 1.8f })
                {
                    Vector3 off = Vector3.Cross(Vector3.up, (p1 - p0).normalized) * sx;
                    Vector3 mid = (c0 + c1) / 2f + off;
                    kit.BoxRot(Cable, mid, new Vector3(0.12f, 0.12f, Vector3.Distance(c0, c1)), Quaternion.LookRotation(c1 - c0));
                }
            }
            if (i == (tops.Count - 1) / 2)
            {
                // the cabin, hanging from the cable at mid-span (a bit of a tilt: it's stuck)
                Vector3 hang = Vector3.Lerp(p0, p1, 0.5f) + Vector3.down * 2.5f + Vector3.Cross(Vector3.up, (p1 - p0).normalized) * 1.8f;
                var q = Quaternion.LookRotation(p1 - p0) * Quaternion.Euler(0f, 0f, 6f);
                kit.BoxRot(Metal_(), hang + Vector3.down * 1.2f, new Vector3(0.3f, 2.4f, 0.3f), q);          // hanger arm
                kit.BoxRot(RoofM, hang + Vector3.down * 3.6f, new Vector3(2.6f, 2.6f, 3.2f), q);              // cabin body
                kit.BoxRot(Water, hang + Vector3.down * 3.3f, new Vector3(2.7f, 1.0f, 3.3f), q);              // window band
                kit.BoxRot(Metal_(), hang + Vector3.down * 2.35f, new Vector3(2.8f, 0.15f, 3.4f), q);         // roof rail
                GondolaCabin = hang + Vector3.down * 3.6f;
            }
        }
        var go = new GameObject("Gondola"); go.transform.SetParent(transform, false);
        var mesh = kit.ToMesh("Gondola");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }
    /// <summary>Where the stuck gondola cabin is (world), for a future rescue.</summary>
    public Vector3 GondolaCabin { get; private set; }
    static int Metal_() => Stone; // (grey stone doubles as painted steel at this scale)

    // ------------------------------------------------------------------ mesh kit

    public class Kit
    {
        readonly List<Vector3> v = new(); readonly List<Vector3> n = new(); readonly List<Vector2> uv = new();
        readonly List<int>[] subs;
        public Kit(int count) { subs = new List<int>[count]; for (int i = 0; i < count; i++) subs[i] = new List<int>(); }

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (nrm.sqrMagnitude < 1e-12f) return;
            if (nrm.y < 0f) { (b, c) = (c, b); nrm = -nrm; }
            AddTri(sub, a, b, c, nrm.normalized);
        }
        void AddTri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 nrm)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); n.Add(nrm); n.Add(nrm); n.Add(nrm);
            uv.Add(new Vector2(a.x, a.z) / 8f); uv.Add(new Vector2(b.x, b.z) / 8f); uv.Add(new Vector2(c.x, c.z) / 8f);
            subs[sub].Add(i); subs[sub].Add(i + 1); subs[sub].Add(i + 2);
        }
        public void TriFacing(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (nrm.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(nrm, facing) < 0f) { (b, c) = (c, b); nrm = -nrm; }
            AddTri(sub, a, b, c, nrm.normalized);
        }
        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing) { TriFacing(sub, a, b, c, facing); TriFacing(sub, a, c, d, facing); }
        public void Box(int sub, Vector3 c, Vector3 s) => BoxRot(sub, c, s, Quaternion.identity);
        public void BoxRot(int sub, Vector3 c, Vector3 s, Quaternion q)
        {
            Vector3 X = q * Vector3.right * s.x / 2f, Y = q * Vector3.up * s.y / 2f, Z = q * Vector3.forward * s.z / 2f;
            void F(Vector3 nrm, Vector3 u, Vector3 w) { Vector3 o = c + nrm; Quad(sub, o - u - w, o + u - w, o + u + w, o - u + w, nrm); }
            F(X, Y, Z); F(-X, Y, Z); F(Y, X, Z); F(-Y, X, Z); F(Z, X, Y); F(-Z, X, Y);
        }
        public void Cylinder(int sub, Vector3 bottom, float r, float h, int segs)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r, p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r;
                Quad(sub, bottom + p0, bottom + p1, bottom + p1 + Vector3.up * h, bottom + p0 + Vector3.up * h, (p0 + p1).normalized);
                TriFacing(sub, bottom + Vector3.up * h, bottom + p0 + Vector3.up * h, bottom + p1 + Vector3.up * h, Vector3.up);
            }
        }
        public void Cone(int sub, Vector3 bottom, float r, float h, int segs)
        {
            Vector3 apex = bottom + Vector3.up * h;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                Vector3 p0 = bottom + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r, p1 = bottom + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r;
                TriFacing(sub, p0, p1, apex, ((p0 + p1) / 2f - bottom).normalized * h + Vector3.up * r);
            }
        }
        /// <summary>A lumpy low-poly ball (tree crown, boulder): an icosphere-ish octahedron subdivided once and jittered.</summary>
        public void Blob(int sub, Vector3 c, float r, System.Random rnd)
        {
            var pts = new List<Vector3>();
            int rings = 3, segs = 6;
            var grid = new Vector3[rings + 1, segs];
            for (int i = 0; i <= rings; i++)
            {
                float t = i / (float)rings, phi = t * Mathf.PI;
                for (int k = 0; k < segs; k++)
                {
                    float th = k * Mathf.PI * 2f / segs + i * 0.5f;
                    float rr = r * (0.8f + (float)rnd.NextDouble() * 0.4f);
                    grid[i, k] = c + new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th)) * rr;
                }
            }
            for (int i = 0; i < rings; i++)
                for (int k = 0; k < segs; k++)
                {
                    Vector3 a = grid[i, k], b = grid[i, (k + 1) % segs], cc = grid[i + 1, (k + 1) % segs], d = grid[i + 1, k];
                    Vector3 mid = (a + b + cc + d) / 4f;
                    Quad(sub, a, b, cc, d, mid - c);
                }
        }
        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = subs.Length;
            for (int i = 0; i < subs.Length; i++) m.SetTriangles(subs[i], i);
            m.RecalculateBounds();
            return m;
        }
    }
}

/// <summary>The sea: a gentle bob and a slow drift of the water surface.</summary>
public class Sea : MonoBehaviour
{
    void Update()
    {
        var p = transform.position;
        transform.position = new Vector3(p.x, IslandGenerator.SeaLevel + Mathf.Sin(Time.time * 0.6f) * 0.12f, p.z);
    }
}
