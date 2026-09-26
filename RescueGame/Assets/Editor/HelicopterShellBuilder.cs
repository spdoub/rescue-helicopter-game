using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Rescue Heli > Build Helicopter Shell.
/// Builds one continuous interior shell for the medevac by shrink-wrapping the inside of the outer
/// hull: rings of rays are fired from the centre line outward and the shell sits a few cm inside
/// where they hit (so there are no seams or gaps). Door and window openings are cut out of the
/// shell, and a frame (jamb) joins each opening's edge to the outer hull. The cut-out pieces are
/// reused: window pieces become glass (so the glass follows the real window shape) and door
/// pieces become the sliding doors. Meshes are saved under Assets/Art/Helicopters/Medevac/Shell.
/// </summary>
public static class HelicopterShellBuilder
{
    const string OutDir = "Assets/Art/Helicopters/Medevac/Shell/";
    const float Inset = 0.04f, Outset = 0.05f;
    const float ZStart = -0.52f, ZEnd = 2.72f, ZStep = 0.1f;
    const int Segs = 36;
    const float FloorY = 0.93f;

    // Openings in helicopter space (x right, y up, z forward).
    public struct Box { public Vector3 min, max; public bool Contains(Vector3 p) => p.x > min.x && p.y > min.y && p.z > min.z && p.x < max.x && p.y < max.y && p.z < max.z; }
    public static readonly Box DoorL = new() { min = new Vector3(-2f, 0.5f, -0.32f), max = new Vector3(-0.5f, 2.28f, 1.40f) };
    public static readonly Box DoorR = new() { min = new Vector3(0.5f, 0.5f, -0.32f), max = new Vector3(2f, 2.28f, 1.40f) };
    public static readonly Box Windscreen = new() { min = new Vector3(-2f, 1.72f, 2.45f), max = new Vector3(2f, 2.42f, 2.9f) };
    public static readonly Box SideWinL = new() { min = new Vector3(-2f, 1.45f, 1.55f), max = new Vector3(-0.5f, 2.25f, 2.45f) };
    public static readonly Box SideWinR = new() { min = new Vector3(0.5f, 1.45f, 1.55f), max = new Vector3(2f, 2.25f, 2.45f) };

    enum Region { Wall, DoorL, DoorR, Glass }

    [MenuItem("Tools/Rescue Heli/Build Helicopter Shell")]
    public static void Build()
    {
        var heli = GameObject.Find("Helicopter").transform;
        var hull = heli.Find("Model");
        var hullMesh = hull.GetComponent<MeshFilter>().sharedMesh;

        // ---- measure the hull from the inside
        var probe = new GameObject("HullProbe");
        probe.transform.SetPositionAndRotation(hull.position, hull.rotation);
        probe.transform.localScale = hull.lossyScale;
        var mc = probe.AddComponent<MeshCollider>(); mc.sharedMesh = hullMesh;
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        Physics.SyncTransforms();

        int rings = Mathf.RoundToInt((ZEnd - ZStart) / ZStep) + 1;
        var inner = new Vector3[rings, Segs];
        var outer = new Vector3[rings, Segs];
        var dirs = new Vector3[Segs];
        for (int s = 0; s < Segs; s++) { float a = s * Mathf.PI * 2f / Segs; dirs[s] = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f); }
        var D = new float[rings, Segs];
        for (int r = 0; r < rings; r++)
        {
            float z = ZStart + r * ZStep;
            var c = new Vector3(0f, CentreY(z), z);
            var dist = new float[Segs];
            for (int s = 0; s < Segs; s++)
            {
                dist[s] = OuterSkin(mc, heli.TransformPoint(c), heli.TransformDirection(dirs[s]), 1.45f);
            }
            for (int s = 0; s < Segs; s++) if (dist[s] < 0f) dist[s] = Neighbour(dist, s);
            for (int s = 0; s < Segs; s++) D[r, s] = dist[s];
        }
        // Smooth out bumps from the hull's inner detail: median across neighbours, then a gentle
        // blur along the length. Take the smaller of raw/smoothed so the shell never pokes out.
        var M = new float[rings, Segs];
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < Segs; s++)
            {
                var w = new List<float>();
                for (int dr = -1; dr <= 1; dr++) for (int ds = -1; ds <= 1; ds++)
                {
                    int rr = Mathf.Clamp(r + dr, 0, rings - 1);
                    w.Add(D[rr, (s + ds + Segs) % Segs]);
                }
                w.Sort(); M[r, s] = w[w.Count / 2];
            }
        // then a wider average for a smooth, even lining (the 4 cm inset absorbs the difference)
        var A = new float[rings, Segs];
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < Segs; s++)
            {
                float sum = 0f; int n = 0;
                for (int dr = -2; dr <= 2; dr++) for (int ds = -2; ds <= 2; ds++)
                {
                    int rr = r + dr; if (rr < 0 || rr >= rings) continue;
                    sum += M[rr, (s + ds + Segs) % Segs]; n++;
                }
                A[r, s] = Mathf.Min(sum / n, M[r, s] + 0.02f);
            }
        M = A;
        // Clean cross-section: a rounded rectangle per ring (floor to headlining, wall to wall), sized
        // to the hull and smoothed along the length, then trimmed wherever the hull is narrower.
        var W = new float[rings]; var H = new float[rings];
        for (int r = 0; r < rings; r++)
        {
            float z = ZStart + r * ZStep, cy = CentreY(z);
            // robust sizes: median sideways width, highest of the near-vertical roof rays
            var ws = new List<float>();
            for (int s = 0; s < Segs; s++)
                if (Mathf.Abs(dirs[s].y) < 0.35f) ws.Add(Mathf.Abs(dirs[s].x * D[r, s]));
            ws.Sort();
            float w = ws.Count > 0 ? ws[ws.Count / 2] : 0.9f;
            float roof = Mathf.Max(D[r, 0], Mathf.Max(D[r, 1], D[r, Segs - 1]) * dirs[1].y);
            W[r] = Mathf.Min(0.95f, w - Inset);
            H[r] = Mathf.Min(z < 1.45f ? 2.62f : 2.3f, cy + roof - Inset);
        }
        W = SmoothMin(W); H = SmoothMin(H);
        for (int r = 0; r < rings; r++)
        {
            float z = ZStart + r * ZStep;
            var c = new Vector3(0f, CentreY(z), z);
            float midY = (FloorY + H[r]) / 2f, halfH = (H[r] - FloorY) / 2f;
            for (int s = 0; s < Segs; s++)
            {
                float t = SuperEllipseHit(c, dirs[s], W[r], halfH, midY, 4f);
                // walls: trim where the hull is genuinely narrower (smoothed); ceiling: trust the fitted roof
                if (dirs[s].y < 0.5f) t = Mathf.Min(t, M[r, s] - Inset);
                Vector3 pi = c + dirs[s] * Mathf.Max(0.15f, t);
                if (pi.y < FloorY) pi.y = FloorY;
                inner[r, s] = pi;
                outer[r, s] = c + dirs[s] * (D[r, s] + Outset);
            }
        }
        Physics.queriesHitBackfaces = backfaces;
        Object.DestroyImmediate(probe);

        // ---- classify each quad of the shell
        var region = new Region[rings - 1, Segs];
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < Segs; s++)
            {
                Vector3 m = (inner[r, s] + inner[r + 1, s] + inner[r, (s + 1) % Segs] + inner[r + 1, (s + 1) % Segs]) / 4f;
                region[r, s] = DoorL.Contains(m) ? Region.DoorL : DoorR.Contains(m) ? Region.DoorR
                             : (Windscreen.Contains(m) || SideWinL.Contains(m) || SideWinR.Contains(m)) ? Region.Glass : Region.Wall;
            }

        // ---- shell: walls (cream in the cabin, red in the cockpit) + ceiling (grey)
        var shell = new MeshBuilder(4);
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < Segs; s++)
            {
                if (region[r, s] != Region.Wall) continue;
                Vector3 a = inner[r, s], b = inner[r, (s + 1) % Segs], cc = inner[r + 1, (s + 1) % Segs], d = inner[r + 1, s];
                Vector3 mid = (a + cc) / 2f;
                int sub = mid.y > 2.2f && Mathf.Abs(mid.x) < 0.7f ? 2 : mid.z < 1.45f ? 0 : 1;
                shell.Quad(sub, a, b, cc, d, InwardOf(mid));
            }
        // end caps (rear wall and nose)
        Cap(shell, inner, 0, true);

        // ---- jambs: wherever a wall quad meets an opening, bridge inner -> outer
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < Segs; s++)
            {
                if (region[r, s] != Region.Wall) continue;
                // neighbour across the edge between ring r and r+1 at segment s+1 (the "right" edge)
                Jamb(shell, region, inner, outer, r, s, r, (s + 1) % Segs, rings, true);
                Jamb(shell, region, inner, outer, r, s, r, (s - 1 + Segs) % Segs, rings, true);
                if (r + 1 < rings - 1) Jamb(shell, region, inner, outer, r, s, r + 1, s, rings, false);
                if (r - 1 >= 0) Jamb(shell, region, inner, outer, r, s, r - 1, s, rings, false);
            }

        // ---- glass (mid-thickness of the window openings)
        var glass = new MeshBuilder(1);
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < Segs; s++)
            {
                if (region[r, s] != Region.Glass) continue;
                Vector3 G(int rr, int ss) => Vector3.Lerp(inner[rr, ss % Segs], outer[rr, ss % Segs], 0.45f);
                Vector3 a = G(r, s), b = G(r, s + 1), cc = G(r + 1, s + 1), d = G(r + 1, s);
                glass.Quad(0, a, b, cc, d, InwardOf((a + cc) / 2f));
            }

        // ---- doors: the cut-out door pieces, pushed just outside the hull, with a window
        Mesh Door(Region side, out Bounds bounds)
        {
            var mb = new MeshBuilder(2);
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < Segs; s++)
                {
                    if (region[r, s] != side) continue;
                    Vector3 O(int rr, int ss) => outer[rr, ss % Segs] + Out(outer[rr, ss % Segs]) * 0.02f;
                    Vector3 I(int rr, int ss) => outer[rr, ss % Segs] - Out(outer[rr, ss % Segs]) * 0.02f;
                    Vector3 a = O(r, s), b = O(r, s + 1), cc = O(r + 1, s + 1), d = O(r + 1, s);
                    Vector3 mid = (a + cc) / 2f;
                    bool window = mid.y > 1.72f && mid.y < 2.12f && mid.z > -0.05f && mid.z < 1.1f;
                    mb.Quad(window ? 1 : 0, a, b, cc, d, -InwardOf(mid));                          // outside face
                    if (!window) mb.Quad(0, I(r, s), I(r, s + 1), I(r + 1, s + 1), I(r + 1, s), InwardOf(mid)); // inside face
                    else mb.Quad(1, I(r, s), I(r, s + 1), I(r + 1, s + 1), I(r + 1, s), InwardOf(mid));
                    // door edges
                    foreach (var (nr, ns, e0, e1) in new[] { (r, s + 1, (r, s + 1), (r + 1, s + 1)), (r, s - 1, (r + 1, s), (r, s)), (r + 1, s, (r + 1, s + 1), (r + 1, s)), (r - 1, s, (r, s), (r, s + 1)) })
                    {
                        bool inside = nr >= 0 && nr < rings - 1 && region[nr, (ns + Segs) % Segs] == side;
                        if (inside) continue;
                        Vector3 p0 = O(e0.Item1, e0.Item2), p1 = O(e1.Item1, e1.Item2), q0 = I(e0.Item1, e0.Item2), q1 = I(e1.Item1, e1.Item2);
                        mb.Quad(0, p0, p1, q1, q0, Vector3.Cross(p1 - p0, Out(p0)).normalized);
                    }
                }
            var m = mb.ToMesh("Door_" + side);
            // overlap the opening by ~5 cm on every edge (like a real sliding door) so no slit shows when closed
            var vs = m.vertices; var bc = m.bounds.center; var bs = m.bounds.size;
            float sy = (bs.y + 0.10f) / bs.y, sz = (bs.z + 0.10f) / bs.z;
            for (int i = 0; i < vs.Length; i++) vs[i] = new Vector3(vs[i].x, bc.y + (vs[i].y - bc.y) * sy, bc.z + (vs[i].z - bc.z) * sz);
            m.vertices = vs; m.RecalculateBounds();
            bounds = m.bounds;
            return m;
        }

        Directory.CreateDirectory(OutDir);
        var shellMesh = Save(shell.ToMesh("HeliShell"), "HeliShell");
        var glassMesh = Save(glass.ToMesh("HeliGlass"), "HeliGlass");
        var doorL = Save(Door(Region.DoorL, out var bl), "DoorL");
        var doorR = Save(Door(Region.DoorR, out var br), "DoorR");

        // ---- scene objects
        var old = heli.Find("Shell"); if (old) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("Shell").transform; root.SetParent(heli, false);
        string mats = "Assets/Materials/Interior/";
        var mCream = AssetDatabase.LoadAssetAtPath<Material>(mats + "Mat_PadCream.mat");
        var mRed = AssetDatabase.LoadAssetAtPath<Material>(mats + "Mat_PadRed.mat");
        var mGrey = AssetDatabase.LoadAssetAtPath<Material>(mats + "Mat_Grey.mat");
        var mHull = AssetDatabase.LoadAssetAtPath<Material>(mats + "Mat_HullRed.mat");
        var mGlass = AssetDatabase.LoadAssetAtPath<Material>(mats + "Glass.mat");
        Obj("InnerShell", root, shellMesh, new[] { mCream, mRed, mGrey, mHull }).AddComponent<CameraBlocker>().mesh = shellMesh;
        Obj("Glass", root, glassMesh, new[] { mGlass }).GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (var (name, mesh, b, sign) in new[] { ("Door_L", doorL, bl, -1f), ("Door_R", doorR, br, 1f) })
        {
            var d = Obj(name, root, mesh, new[] { mHull, mGlass });
            var col = d.AddComponent<BoxCollider>();
            col.center = b.center + new Vector3(-sign * 0.02f, 0f, 0f);
            col.size = new Vector3(0.08f, b.size.y, b.size.z);
            var sd = d.AddComponent<SlidingDoor>();
            sd.left = sign < 0f;
        }
        TuckInteriorInsideHull(heli, hull, TuckApply);

        // shell & jambs block people too (thin boxes are handled by the existing hull colliders)
        var clip = hull.GetComponent<HullInteriorClip>();
        if (clip) { Object.DestroyImmediate(clip); }
        clip = hull.gameObject.AddComponent<HullInteriorClip>();
        clip.zones = new[]
        {
            HullCut(DoorL), HullCut(DoorR), HullCut(Windscreen), HullCut(SideWinL), HullCut(SideWinR),
        };
        EditorUtility.SetDirty(clip);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(heli.gameObject.scene);
        Debug.Log($"[Shell] rings {rings}, shell tris {shellMesh.triangles.Length / 3}, glass tris {glassMesh.triangles.Length / 3}");
    }

    public static bool TuckApply = true;  // true = move the vertices, false = only report
    const string InteriorFbx = "Assets/Art/Helicopters/Medevac/Interior/Interior.fbx";

    /// <summary>
    /// Some Blender interior pieces (rear wall pads, bulkhead frame) are wider than the hull where the
    /// hull tapers, so they poke out through the outside skin. For each vertex, cast from the cabin
    /// centre line toward it; if the hull's outer skin is nearer than the vertex, pull the vertex back
    /// to just inside the skin. No skin in that direction = an opening (window/door), left alone.
    /// Always starts from the original FBX meshes, so rebuilding is repeatable.
    /// </summary>
    static void TuckInteriorInsideHull(Transform heli, Transform hull, bool apply)
    {
        var originals = new Dictionary<string, Mesh>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(InteriorFbx)) if (o is Mesh m) originals[m.name] = m;
        var interior = heli.Find("Interior");
        if (interior == null) { Debug.LogWarning("[Shell] no Interior under Helicopter"); return; }
        var probe = new GameObject("HullProbe");
        probe.transform.SetPositionAndRotation(hull.position, hull.rotation);
        probe.transform.localScale = hull.lossyScale;
        var col = probe.AddComponent<MeshCollider>(); col.sharedMesh = hull.GetComponent<MeshFilter>().sharedMesh;
        bool backfaces = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true; Physics.SyncTransforms();
        string log = "";
        foreach (var mf in interior.GetComponentsInChildren<MeshFilter>(true))
        {
            string key = mf.sharedMesh != null ? mf.sharedMesh.name.Replace("Tucked_", "") : null;
            if (key == null || !originals.TryGetValue(key, out var src)) continue;
            var toHeli = heli.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var toMesh = toHeli.inverse;
            var v = src.vertices; int moved = 0; float worst = 0f;
            bool bulkhead = mf.name.StartsWith("Bulk");
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = toHeli.MultiplyPoint3x4(v[i]), q = p;
                if (p.z > 0.2f && !bulkhead) continue;                 // only the tapering rear cabin + bulkhead frame
                // sideways (keeps walls vertical and the floor flat)
                if (Mathf.Abs(p.x) > 0.05f)
                {
                    Vector3 side = new Vector3(Mathf.Sign(p.x), 0f, 0f);
                    float skin = OuterSkin(col, heli.TransformPoint(new Vector3(0f, p.y, p.z)), heli.TransformDirection(side), 1.5f);
                    if (skin > 0f && Mathf.Abs(p.x) > skin - 0.03f) q.x = Mathf.Sign(p.x) * (skin - 0.03f);
                }
                // under the roof
                float roof = OuterSkin(col, heli.TransformPoint(new Vector3(q.x, 1.72f, p.z)), heli.up, 1.5f);
                if (roof > 0f && p.y > 1.72f + roof - 0.03f) q.y = 1.72f + roof - 0.03f;
                if (q == p) continue;
                worst = Mathf.Max(worst, (q - p).magnitude);
                v[i] = toMesh.MultiplyPoint3x4(q);
                moved++;
            }
            if (!apply || moved == 0) { if (mf.sharedMesh != src) mf.sharedMesh = src; if (moved > 0) log += $"{mf.name} ({moved}v, {worst * 100f:F0}cm)  "; continue; }
            var tucked = Object.Instantiate(src); tucked.name = "Tucked_" + src.name;
            tucked.vertices = v; tucked.RecalculateBounds(); tucked.RecalculateNormals();
            mf.sharedMesh = Save(tucked, "Tucked_" + src.name);
            var mc = mf.GetComponent<MeshCollider>(); if (mc) mc.sharedMesh = mf.sharedMesh;
            log += $"{mf.name} ({moved}v, {worst * 100f:F0}cm)  ";
        }
        Physics.queriesHitBackfaces = backfaces;
        Object.DestroyImmediate(probe);
        Debug.Log("[Shell] outside the hull" + (apply ? " (tucked)" : "") + ": " + (log.Length > 0 ? log : "nothing"));
    }

    static HullInteriorClip.Zone HullCut(Box b)
    {
        // hull cut a little smaller than the shell cut so the jambs always cover the hull's cut edge
        const float k = 0.07f;
        return new HullInteriorClip.Zone { min = new Vector3(b.min.x, Mathf.Max(b.min.y, 0.9f) + k, b.min.z + k), max = new Vector3(b.max.x, b.max.y - k, b.max.z - k) };
    }

    static float CentreY(float z) => 1.72f;

    /// <summary>
    /// The Meshy hull has hidden geometry inside it, so the first thing a ray hits is often junk.
    /// Keep going and return the distance to the LAST surface within maxDist (the outer skin).
    /// </summary>
    static float OuterSkin(Collider col, Vector3 origin, Vector3 dir, float maxDist)
    {
        float travelled = 0f, last = -1f;
        for (int i = 0; i < 40 && travelled < maxDist; i++)
        {
            if (!col.Raycast(new Ray(origin + dir * travelled, dir), out RaycastHit hit, maxDist - travelled)) break;
            travelled += hit.distance;
            last = travelled;
            travelled += 0.002f;
        }
        return last;
    }

    /// <summary>Distance along dir from c to a superellipse |x/a|^n + |(y-cy)/b|^n = 1 (c is inside it).
    /// The lower half is square (straight walls down to a flat floor) so no rounded corner ramps over the floor.</summary>
    static float SuperEllipseHit(Vector3 c, Vector3 dir, float a, float b, float cy, float n)
    {
        float lo = 0f, hi = 3f;
        for (int i = 0; i < 30; i++)
        {
            float t = (lo + hi) / 2f;
            Vector3 p = c + dir * t;
            float f = p.y < cy ? Mathf.Max(Mathf.Abs(p.x / a), Mathf.Abs((p.y - cy) / b))
                               : Mathf.Pow(Mathf.Abs(p.x / a), n) + Mathf.Pow(Mathf.Abs((p.y - cy) / b), n);
            if (f < 1f) lo = t; else hi = t;
        }
        return lo;
    }

    /// <summary>Along the length: a running minimum over 5 rings, then a gentle average (never grows).</summary>
    static float[] SmoothMin(float[] v)
    {
        int n = v.Length; var m = new float[n]; var o = new float[n];
        for (int i = 0; i < n; i++) { float x = v[i]; for (int k = -2; k <= 2; k++) x = Mathf.Min(x, v[Mathf.Clamp(i + k, 0, n - 1)]); m[i] = x; }
        for (int i = 0; i < n; i++) { float sum = 0; for (int k = -1; k <= 1; k++) sum += m[Mathf.Clamp(i + k, 0, n - 1)]; o[i] = Mathf.Min(v[i], sum / 3f); }
        return o;
    }

    static float Neighbour(float[] d, int s)
    {
        for (int k = 1; k < d.Length; k++)
        {
            float a = d[(s + k) % d.Length], b = d[(s - k + d.Length) % d.Length];
            if (a > 0 && b > 0) return (a + b) / 2f;
            if (a > 0) return a; if (b > 0) return b;
        }
        return 1f;
    }

    static Vector3 InwardOf(Vector3 p) => -Out(p);
    static Vector3 Out(Vector3 p) { var v = new Vector3(p.x, p.y - CentreY(p.z), 0f); return v.sqrMagnitude < 1e-6f ? Vector3.up : v.normalized; }

    static void Cap(MeshBuilder mb, Vector3[,] inner, int r, bool rear)
    {
        int segs = inner.GetLength(1);
        Vector3 c = Vector3.zero; for (int s = 0; s < segs; s++) c += inner[r, s]; c /= segs;
        Vector3 inward = rear ? Vector3.forward : Vector3.back;
        for (int s = 0; s < segs; s++) mb.Tri(0, c, inner[r, s], inner[r, (s + 1) % segs], inward);
    }

    static void Jamb(MeshBuilder mb, Region[,] region, Vector3[,] inner, Vector3[,] outer, int r, int s, int nr, int ns, int rings, bool acrossSegs)
    {
        if (nr < 0 || nr >= rings - 1) return;
        if (region[nr, ns] == Region.Wall) return;
        int segs = inner.GetLength(1);
        // shared edge vertices
        (int, int) e0, e1;
        if (acrossSegs)
        {
            int edgeS = ns == (s + 1) % segs ? (s + 1) % segs : s;
            e0 = (r, edgeS); e1 = (r + 1, edgeS);
        }
        else
        {
            int edgeR = nr > r ? r + 1 : r;
            e0 = (edgeR, s); e1 = (edgeR, (s + 1) % segs);
        }
        Vector3 a = inner[e0.Item1, e0.Item2], b = inner[e1.Item1, e1.Item2];
        Vector3 c = outer[e1.Item1, e1.Item2], d = outer[e0.Item1, e0.Item2];
        // face the jamb toward the opening
        Vector3 wallMid = (inner[r, s] + inner[r + 1, (s + 1) % segs]) / 2f;
        Vector3 edgeMid = (a + b) / 2f;
        mb.Quad(3, a, b, c, d, (edgeMid - wallMid).normalized);
        // and the back side: where the hull tapers away behind an opening the jamb is seen from outside
        mb.Quad(3, a, b, c, d, (wallMid - edgeMid).normalized);
    }

    static Mesh Save(Mesh m, string name)
    {
        string p = OutDir + name + ".asset";
        // Replace outright (copying into the old asset leaves the GPU drawing a stale copy).
        AssetDatabase.DeleteAsset(p);
        m.name = name;
        AssetDatabase.CreateAsset(m, p);
        return AssetDatabase.LoadAssetAtPath<Mesh>(p);
    }

    static GameObject Obj(string name, Transform parent, Mesh mesh, Material[] mats)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        var use = new Material[mesh.subMeshCount];
        for (int i = 0; i < use.Length; i++) use[i] = mats[Mathf.Min(i, mats.Length - 1)];
        r.sharedMaterials = use;
        return go;
    }

    /// <summary>Flat-shaded mesh builder; each quad/triangle gets its own vertices, wound to face 'facing'.</summary>
    class MeshBuilder
    {
        readonly List<Vector3> v = new(); readonly List<Vector3> n = new(); readonly List<Vector2> uv = new();
        readonly List<int>[] subs;
        public MeshBuilder(int subCount) { subs = new List<int>[subCount]; for (int i = 0; i < subCount; i++) subs[i] = new List<int>(); }

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(normal, facing) < 0f) { (b, c) = (c, b); normal = -normal; }
            normal.Normalize();
            int i = v.Count;
            // Unity shows the side that Cross(b - a, c - a) points to
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(normal); n.Add(normal); n.Add(normal);
            uv.Add(new Vector2(a.z, a.y)); uv.Add(new Vector2(b.z, b.y)); uv.Add(new Vector2(c.z, c.y));
            subs[sub].Add(i); subs[sub].Add(i + 1); subs[sub].Add(i + 2);
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            Tri(sub, a, b, c, facing);
            Tri(sub, a, c, d, facing);
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
