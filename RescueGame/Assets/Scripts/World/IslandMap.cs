using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A paper map of the island, painted from the real terrain when the level loads (so it always matches),
/// pinned up in two places on the helicopter: the co-pilot's side of the dash and the cabin bulkhead.
/// Live pins: a red helicopter marker, a yellow pin per open call, a white cross for the hospital.
/// Purely local (each machine draws its own).
/// </summary>
public class IslandMap : MonoBehaviour
{
    public Material paperMaterial;   // Stylized Lit template (unlit-ish); the texture is set at runtime
    public Material pinMaterial;

    const int W = 256, H = 192;
    Texture2D tex;
    readonly List<(Transform quad, float size)> maps = new();
    readonly List<Transform> pins = new();
    Transform heli;

    void Start()
    {
        heli = transform;
        var isl = IslandGenerator.Current;
        if (isl == null) return;
        tex = Paint(isl);
        // dash, co-pilot side (leaning back with the panel), and the left cabin bulkhead
        Place("MapDash", new Vector3(-0.62f, 1.50f, 2.60f), Quaternion.Euler(18f, 0f, 0f), 0.26f);   // on the panel, leaning back, facing the seats
        Place("MapWall", new Vector3(-0.65f, 1.95f, 1.42f), Quaternion.Euler(0f, 0f, 0f), 0.42f);     // on the bulkhead's cabin face
    }

    void Place(string name, Vector3 local, Quaternion rot, float width)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name; DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(heli, false);
        go.transform.localPosition = local; go.transform.localRotation = rot;
        go.transform.localScale = new Vector3(width, width * H / (float)W, 1f);
        var r = go.GetComponent<Renderer>();
        var m = new Material(paperMaterial); m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white); m.SetFloat("_FlatShading", 0f); m.SetFloat("_ShadowStrength", 0.3f);
        m.SetColor("_EmissionColor", Color.white * 0.25f);
        r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // a frame of tape at the corners
        foreach (var (cx, cy) in new[] { (-0.46f, 0.46f), (0.46f, 0.46f), (-0.46f, -0.46f), (0.46f, -0.46f) })
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Quad); t.name = "Tape"; DestroyImmediate(t.GetComponent<Collider>());
            t.transform.SetParent(go.transform, false); t.transform.localPosition = new Vector3(cx, cy, -0.002f);   // (in front of the paper, on the visible side)
            t.transform.localRotation = Quaternion.Euler(0f, 0f, cx * cy > 0f ? 45f : -45f); t.transform.localScale = new Vector3(0.22f, 0.08f, 1f);
            var tr = t.GetComponent<Renderer>(); tr.sharedMaterial = pinMaterial; var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.66f, 1f)); tr.SetPropertyBlock(mpb);
        }
        maps.Add((go.transform, width));
        Debug.Log($"[Map] {name} at heli-local {local} (tex {(tex ? tex.width : 0)}px)");
    }

    /// <summary>Paint the island: sea, sand, grass, rock, snow from the terrain heights; the city as a grey grid; hospital cross; helipad H.</summary>
    Texture2D Paint(IslandGenerator isl)
    {
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[W * H];
        Color sea = new(0.55f, 0.66f, 0.7f), paper = new(0.93f, 0.88f, 0.76f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float wx = (x / (float)W - 0.5f) * isl.halfX * 2.1f, wz = (y / (float)H - 0.5f) * isl.halfZ * 2.1f;
                float h = isl.Height(wx, wz);
                Color c;
                if (h < IslandGenerator.SeaLevel) c = Color.Lerp(sea, sea * 0.85f, Mathf.Clamp01(-h / 10f));
                else if (h < IslandGenerator.SeaLevel + 5.5f) c = new Color(0.9f, 0.82f, 0.6f);
                else if (h > 150f) c = new Color(0.97f, 0.97f, 0.98f);
                else if (h > 100f) c = new Color(0.6f, 0.52f, 0.44f);
                else c = Color.Lerp(new Color(0.55f, 0.68f, 0.38f), new Color(0.48f, 0.56f, 0.32f), h / 100f);
                // contour-ish shading: darken every 25 m band edge
                if (h > IslandGenerator.SeaLevel + 5.5f && Mathf.Repeat(h, 25f) < 1.6f) c *= 0.82f;
                if (Mathf.Sqrt(wx * wx + wz * wz) < 235f)
                {
                    c = new Color(0.72f, 0.7f, 0.66f);
                    if (Mathf.Repeat(wx + 12f, 24f) < 3.5f || Mathf.Repeat(wz + 12f, 24f) < 3.5f) c = new Color(0.55f, 0.53f, 0.5f);   // the street grid
                }
                // a paper tint + grain
                c = Color.Lerp(c, paper, 0.18f) * (0.97f + Mathf.PerlinNoise(x * 0.31f, y * 0.29f) * 0.06f);
                px[y * W + x] = c;
            }
        // hospital cross at (-28, -40), helipad H at the origin
        Stamp(px, -28f, -40f, isl, new Color(0.85f, 0.15f, 0.12f), cross: true);
        Stamp(px, 0f, 0f, isl, new Color(0.15f, 0.15f, 0.2f), cross: false);
        t.SetPixels32(px); t.Apply();
        return t;
    }

    static void Stamp(Color32[] px, float wx, float wz, IslandGenerator isl, Color c, bool cross)
    {
        int cx = Mathf.RoundToInt((wx / (isl.halfX * 2.1f) + 0.5f) * W), cy = Mathf.RoundToInt((wz / (isl.halfZ * 2.1f) + 0.5f) * H);
        for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++)
        {
            bool on = cross ? (Mathf.Abs(dx) <= 1 || Mathf.Abs(dy) <= 1) : (Mathf.Abs(dx) == 2 || dy == 0);
            if (!on) continue;
            int x = cx + dx, y = cy + dy; if (x < 0 || y < 0 || x >= W || y >= H) continue;
            px[y * W + x] = c;
        }
    }

    void Update()
    {
        if (tex == null || maps.Count == 0) return;
        var isl = IslandGenerator.Current;
        // pins: the helicopter + every survivor waiting for pickup
        var wanted = new List<(Vector3 world, Color c, float size)> { (heli.position, new Color(0.9f, 0.15f, 0.1f), 0.05f) };
        foreach (var s in FindObjectsByType<SurvivorBrain>(FindObjectsSortMode.InstanceID))
            if (s.IsSpawned && (s.Current == SurvivorBrain.State.Waiting || s.Current == SurvivorBrain.State.Hooked))
                wanted.Add((s.transform.position, new Color(1f, 0.84f, 0.2f), 0.045f));
        int need = wanted.Count * maps.Count;
        while (pins.Count < need)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Quad); p.name = "Pin"; DestroyImmediate(p.GetComponent<Collider>());
            p.GetComponent<Renderer>().sharedMaterial = pinMaterial; p.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pins.Add(p.transform);
        }
        for (int i = 0; i < pins.Count; i++) pins[i].gameObject.SetActive(i < need);
        int k = 0;
        foreach (var (quad, width) in maps)
            foreach (var (world, c, size) in wanted)
            {
                var pin = pins[k++];
                pin.SetParent(quad, false);
                float u = Mathf.Clamp(world.x / (isl.halfX * 2.1f), -0.5f, 0.5f), v = Mathf.Clamp(world.z / (isl.halfZ * 2.1f), -0.5f, 0.5f);
                pin.localPosition = new Vector3(u, v, -0.003f);
                pin.localRotation = Quaternion.identity;
                pin.localScale = new Vector3(size / width, size / width * (W / (float)H), 1f) * (1f + (c.r > 0.85f && c.g < 0.5f ? Mathf.Sin(Time.time * 6f) * 0.15f : 0f));
                var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", c); pin.GetComponent<Renderer>().SetPropertyBlock(mpb);
            }
    }
}
