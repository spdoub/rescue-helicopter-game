using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the warm brick city of concept 3 (ArtSource/Concept/concept_3_rooftop.png) when the level
/// loads - on every machine, from the same seed, so everyone gets the identical city without
/// storing any meshes. Every Environment/Block_N keeps its collider and footprint (so gameplay
/// doesn't change) and gets a brick building: windows with frames and sills, stone trim, a
/// cornice and a parapet, and a roof with water towers, TV antennas, AC units, vents, stair huts
/// and skylights. Filler buildings on a street grid, a hazy skyline, lane markings, street lamps
/// and pavements fill the rest. (Set up by Tools/Rescue Heli/Build City, which also previews it.)
/// </summary>
[DefaultExecutionOrder(-500)] // before anything looks for rooftops
public class CityGenerator : MonoBehaviour
{
    public int seed = 20260926;
    [Tooltip("Brick_Red, Brick_Brown, Brick_Tan")]
    public Material[] brickMats = new Material[3];
    [Tooltip("TrimLight, TrimDark, WindowGlass, WindowFrame, RoofTar, WindowLit, RoofMetal, TankWood, Accent")]
    public Material[] sharedMats = new Material[9];
    public Material roadPaint, pavement;

    static readonly string[] BrickNames = { "Brick_Red", "Brick_Brown", "Brick_Tan" };
    static CityGenerator current;
    public static CityGenerator Current => current;
    public const float StreetPitch = 24f;

    void Awake()
    {
        if (Application.isPlaying) Generate();
    }

    /// <summary>(Re)build the city under this object.</summary>
    public void Generate()
    {
        current = this;
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
        Build(transform);
        if (!Application.isPlaying)
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (t != transform) t.gameObject.hideFlags = HideFlags.DontSave; // preview only
    }

    /// <summary>Materials for a building of this brick (submesh order).</summary>
    public Material[] MatsFor(string brick)
    {
        var m = new Material[10];
        m[0] = brickMats[Mathf.Max(0, System.Array.IndexOf(BrickNames, brick))];
        for (int i = 0; i < 9; i++) m[i + 1] = sharedMats[i];
        return m;
    }


    const float FloorH = 3.0f, GroundFloorH = 3.6f;

    // submeshes / materials
    const int Brick = 0, TrimLight = 1, TrimDark = 2, Glass = 3, Frame = 4, Roof = 5, GlassLit = 6, Metal = 7, Wood = 8, Accent = 9;
    static readonly List<Vector3> clearSpots = new();   // survivors' pickup spots: keep their roofs clear around them

    static void Build(Transform city)
    {
        var rnd = new System.Random(current.seed);
        string[] bricks = BrickNames;
        var env = GameObject.Find("Environment").transform;

        // The survivors' pickup roofs keep a clear landing/hoisting area around them.
        clearSpots.Clear();
        foreach (var sb in FindObjectsByType<SurvivorBrain>(FindObjectsSortMode.InstanceID)) clearSpots.Add(sb.transform.position);
        foreach (var rc in FindObjectsByType<Recliner>(FindObjectsSortMode.InstanceID)) clearSpots.Add(rc.transform.position);
        foreach (var dg in FindObjectsByType<Dog>(FindObjectsSortMode.InstanceID)) clearSpots.Add(dg.transform.position);
        Vector3 keepClear = Vector3.one * 9999f;

        int n = 0;
        foreach (Transform b in env)
        {
            if (!b.name.StartsWith("Block_")) continue;
            var r = b.GetComponent<Renderer>(); if (r) r.enabled = false;
            Vector3 size = b.localScale;
            Vector3 basePos = b.position - Vector3.up * size.y / 2f;
            var style = bricks[rnd.Next(bricks.Length)];
            MakeBuilding(city, b.name.Replace("Block_", "Building_"), basePos, Quaternion.Euler(0f, b.eulerAngles.y, 0f), size.x, size.z, size.y, style, rnd, keepClear);
            n++;
        }

        Fill(city, env, rnd, keepClear);
        Skyline(city, rnd);
        Streets(city, env);
    }

    // ------------------------------------------------------------------ materials

    static Material[] Materials(string brick) => current.MatsFor(brick);

    // ------------------------------------------------------------------ one building

    /// <summary>One brick building (with its colliders). Uses the given generator's materials.</summary>
    public static GameObject MakeBuildingWith(CityGenerator gen, Transform parent, string name, Vector3 basePos, Quaternion rot, float w, float d, float h, string brick, System.Random rnd, bool bodyCollider, int forceShopFace)
    {
        current = gen;
        return MakeBuilding(parent, name, basePos, rot, w, d, h, brick, rnd, Vector3.one * 9999f, bodyCollider, forceShopFace);
    }

    static GameObject MakeBuilding(Transform parent, string name, Vector3 basePos, Quaternion rot, float w, float d, float h, string brick, System.Random rnd, Vector3 keepClear, bool bodyCollider = false, int forceShopFace = -1)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(basePos, rot);
        var mb = new Kit(10);
        var cols = new List<(Vector3 c, Vector3 s)>();
        float hw = w / 2f, hd = d / 2f;

        // four facades: (origin corner, along, outward normal, length)
        var faces = new[]
        {
            (new Vector3(-hw, 0, -hd), Vector3.right, Vector3.back, w),
            (new Vector3(hw, 0, -hd), Vector3.forward, Vector3.right, d),
            (new Vector3(hw, 0, hd), Vector3.left, Vector3.forward, w),
            (new Vector3(-hw, 0, hd), Vector3.back, Vector3.left, d),
        };
        int shopFace = forceShopFace >= 0 ? forceShopFace : w >= d ? (rnd.Next(2) == 0 ? 0 : 2) : (rnd.Next(2) == 0 ? 1 : 3);
        for (int f = 0; f < 4; f++)
        {
            var (o, along, outN, len) = faces[f];
            // wall
            mb.Quad(Brick, o, o + along * len, o + along * len + Vector3.up * h, o + Vector3.up * h, outN);
            // base band, string course, cornice
            Band(mb, TrimDark, o, along, outN, len, 0f, 0.6f, 0.08f);
            if (h > GroundFloorH + 1.5f) Band(mb, TrimLight, o, along, outN, len, GroundFloorH - 0.1f, 0.18f, 0.06f);
            Band(mb, TrimLight, o, along, outN, len, h - 0.4f, 0.4f, 0.22f);

            // windows
            int nCols = Mathf.Max(1, Mathf.FloorToInt((len - 1.4f) / 2.5f));
            float step = len / nCols;
            int floors = Mathf.Max(0, Mathf.FloorToInt((h - GroundFloorH - 0.9f) / FloorH));
            for (int c = 0; c < nCols; c++)
            {
                float x = step * (c + 0.5f);
                if (f == shopFace && h > 5f)
                    Window(mb, o + along * x + Vector3.up * 0.75f, along, outN, Mathf.Min(step - 0.5f, 2.4f), 2.3f, rnd.NextDouble() < 0.3, true);
                else if (h > 4.5f)
                    Window(mb, o + along * x + Vector3.up * 1.2f, along, outN, 1.0f, 1.5f, rnd.NextDouble() < 0.12, false);
                for (int fl = 0; fl < floors; fl++)
                    Window(mb, o + along * x + Vector3.up * (GroundFloorH + 0.7f + fl * FloorH), along, outN, 1.05f, 1.6f, rnd.NextDouble() < 0.3, false);
            }
        }

        // a fire escape zig-zagging up one side (not the shop side), on some of the taller buildings
        if (h > 9f && rnd.NextDouble() < 0.45)
        {
            int f = (shopFace + 1 + rnd.Next(3)) % 4;
            var (o, along, outN, len) = faces[f];
            if (len >= 8f) FireEscape(mb, o, along, outN, len, h, rnd);
        }
        // the shop's sign above its windows, and now and then a hanging sign on a bracket
        if (h > 5f)
        {
            var (o, along, outN, len) = faces[shopFace];
            ShopSign(mb, o, along, outN, len, rnd);
        }
        // rooftop billboard on some of the taller ones
        if (h > 12f && bodyCollider && rnd.NextDouble() < 0.18)
            Billboard(mb, w, d, h, rnd);

        // roof and parapet (walkable roof stays at h, like the grey-box collider)
        mb.Quad(Roof, new Vector3(-hw, h + 0.005f, -hd), new Vector3(hw, h + 0.005f, -hd), new Vector3(hw, h + 0.005f, hd), new Vector3(-hw, h + 0.005f, hd), Vector3.up);
        const float pH = 0.85f, pT = 0.25f;
        foreach (var (c, s) in new[] {
            (new Vector3(0, h + pH / 2, -hd + pT / 2), new Vector3(w, pH, pT)), (new Vector3(0, h + pH / 2, hd - pT / 2), new Vector3(w, pH, pT)),
            (new Vector3(-hw + pT / 2, h + pH / 2, 0), new Vector3(pT, pH, d - 2 * pT)), (new Vector3(hw - pT / 2, h + pH / 2, 0), new Vector3(pT, pH, d - 2 * pT)) })
        {
            mb.Box(Brick, c, s);
            mb.Box(TrimLight, c + Vector3.up * (pH / 2 + 0.04f), new Vector3(s.x + 0.1f, 0.08f, s.z + 0.1f));
            cols.Add((c, s));
        }

        // roof clutter
        Vector3 clearLocal = Quaternion.Inverse(rot) * (keepClear - basePos);
        foreach (var spot in clearSpots)
        {
            Vector3 l = Quaternion.Inverse(rot) * (spot - basePos);
            if (Mathf.Abs(l.y - h) < 3f && Mathf.Abs(l.x) < w && Mathf.Abs(l.z) < d) { clearLocal = l; break; }
        }
        RoofProps(mb, cols, w, d, h, rnd, new Vector2(clearLocal.x, clearLocal.z), Mathf.Abs(clearLocal.y - h) < 3f ? 5.5f : 0f);

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = Save(mb.ToMesh(name), name);
        go.AddComponent<MeshRenderer>().sharedMaterials = Materials(brick);
        var colRoot = new GameObject("Colliders").transform; colRoot.SetParent(go.transform, false);
        if (bodyCollider) cols.Add((new Vector3(0f, h / 2f, 0f), new Vector3(w, h, d)));
        foreach (var (c, s) in cols)
        {
            var bc = colRoot.gameObject.AddComponent<BoxCollider>();
            bc.center = c; bc.size = s;
        }
        return go;
    }

    static readonly string[] ShopWords = { "DELI", "PIZZA", "LAUNDRY", "CAFE", "BAR", "SHOES", "BOOKS", "PAWN", "DINER", "MARKET", "BAGELS", "TATTOO", "RADIO", "HOTEL", "NAILS", "FLORIST", "TACOS", "VIDEO", "LIQUOR", "DONUTS", "REPAIR", "NOODLES", "BARBER", "CHECKS", "SUBS", "OPTICS", "TOYS", "BEDS", "GRILL", "GYM" };
    static readonly string[] BillWords = { "EAT", "SALE", "SODA", "FLY", "WOW", "OPEN", "NOW", "CALL", "DRINK", "LUCKY" };

    /// <summary>Block letters (5x7 pixel font) as little boxes standing proud of a surface facing outN, centred on 'centre'.</summary>
    static void Letters(Kit mb, int sub, string text, Vector3 centre, Vector3 along, Vector3 outN, float height, float thick)
    {
        float px = height / 7f;
        float total = (text.Length * 6 - 1) * px;
        Vector3 start = centre - along * (total / 2f);
        for (int ci = 0; ci < text.Length; ci++)
        {
            if (!Font.TryGetValue(text[ci], out var rows)) continue;
            for (int r = 0; r < 7; r++)
            {
                string row = rows[r];
                int c = 0;
                while (c < 5)
                {
                    if (row[c] == '1')
                    {
                        int c0 = c; while (c < 5 && row[c] == '1') c++;
                        int n = c - c0;
                        Vector3 p = start + along * ((ci * 6 + c0 + n / 2f) * px) + Vector3.up * ((3 - r) * px) + outN * (thick / 2f);
                        mb.Box(sub, p, Abs(along * (n * px * 0.95f)) + Vector3.up * (px * 0.92f) + Abs(outN * thick));
                    }
                    else c++;
                }
            }
        }
    }

    static readonly Dictionary<char, string[]> Font = new()
    {
        ['A'] = new[] { ".111.", "1...1", "1...1", "11111", "1...1", "1...1", "1...1" },
        ['B'] = new[] { "1111.", "1...1", "1...1", "1111.", "1...1", "1...1", "1111." },
        ['C'] = new[] { ".1111", "1....", "1....", "1....", "1....", "1....", ".1111" },
        ['D'] = new[] { "1111.", "1...1", "1...1", "1...1", "1...1", "1...1", "1111." },
        ['E'] = new[] { "11111", "1....", "1....", "1111.", "1....", "1....", "11111" },
        ['F'] = new[] { "11111", "1....", "1....", "1111.", "1....", "1....", "1...." },
        ['G'] = new[] { ".1111", "1....", "1....", "1..11", "1...1", "1...1", ".1111" },
        ['H'] = new[] { "1...1", "1...1", "1...1", "11111", "1...1", "1...1", "1...1" },
        ['I'] = new[] { "11111", "..1..", "..1..", "..1..", "..1..", "..1..", "11111" },
        ['K'] = new[] { "1...1", "1..1.", "1.1..", "11...", "1.1..", "1..1.", "1...1" },
        ['L'] = new[] { "1....", "1....", "1....", "1....", "1....", "1....", "11111" },
        ['M'] = new[] { "1...1", "11.11", "1.1.1", "1...1", "1...1", "1...1", "1...1" },
        ['N'] = new[] { "1...1", "11..1", "1.1.1", "1..11", "1...1", "1...1", "1...1" },
        ['O'] = new[] { ".111.", "1...1", "1...1", "1...1", "1...1", "1...1", ".111." },
        ['P'] = new[] { "1111.", "1...1", "1...1", "1111.", "1....", "1....", "1...." },
        ['Q'] = new[] { ".111.", "1...1", "1...1", "1...1", "1.1.1", "1..1.", ".11.1" },
        ['R'] = new[] { "1111.", "1...1", "1...1", "1111.", "1.1..", "1..1.", "1...1" },
        ['S'] = new[] { ".1111", "1....", "1....", ".111.", "....1", "....1", "1111." },
        ['T'] = new[] { "11111", "..1..", "..1..", "..1..", "..1..", "..1..", "..1.." },
        ['U'] = new[] { "1...1", "1...1", "1...1", "1...1", "1...1", "1...1", ".111." },
        ['V'] = new[] { "1...1", "1...1", "1...1", "1...1", "1...1", ".1.1.", "..1.." },
        ['W'] = new[] { "1...1", "1...1", "1...1", "1.1.1", "1.1.1", "11.11", "1...1" },
        ['X'] = new[] { "1...1", "1...1", ".1.1.", "..1..", ".1.1.", "1...1", "1...1" },
        ['Y'] = new[] { "1...1", "1...1", ".1.1.", "..1..", "..1..", "..1..", "..1.." },
        ['Z'] = new[] { "11111", "....1", "...1.", "..1..", ".1...", "1....", "11111" },
        ['J'] = new[] { "..111", "...1.", "...1.", "...1.", "...1.", "1..1.", ".11.." },
    };

    /// <summary>Sign box above the shop windows with the shop's name in block letters; sometimes a hanging sign too.</summary>
    static void ShopSign(Kit mb, Vector3 o, Vector3 along, Vector3 outN, float len, System.Random rnd)
    {
        string word = ShopWords[rnd.Next(ShopWords.Length)];
        int boxSub = new[] { Accent, TrimDark, Wood }[rnd.Next(3)];
        int letterSub = boxSub == Accent ? TrimLight : boxSub == Wood ? TrimLight : GlassLit;
        float signLen = Mathf.Min(len - 1f, Mathf.Max(3.2f, word.Length * 0.42f + 1.2f));
        float y = 3.62f, hgt = 0.6f;
        Vector3 c = o + along * (len / 2f) + Vector3.up * (y + hgt / 2f) + outN * 0.08f;
        mb.Box(boxSub, c, Abs(along * signLen) + Vector3.up * hgt + Abs(outN * 0.16f));
        mb.Box(TrimLight, c + outN * 0.02f, Abs(along * (signLen + 0.08f)) + Vector3.up * (hgt + 0.08f) + Abs(outN * 0.04f)); // frame behind
        Letters(mb, letterSub, word, c + outN * 0.08f, along, outN, 0.36f, 0.03f);
        if (rnd.NextDouble() < 0.5)
        {
            // hanging sign on a bracket near one end
            Vector3 b = o + along * (len * 0.2f) + Vector3.up * 3.3f;
            mb.Box(Metal, b + outN * 0.45f, Abs(along * 0.05f) + Vector3.up * 0.05f + Abs(outN * 0.9f));
            Vector3 sc = b + outN * 0.7f - Vector3.up * 0.42f;
            mb.Box(boxSub, sc, Abs(along * 0.06f) + Vector3.up * 0.7f + Abs(outN * 0.7f));
            Letters(mb, letterSub, word.Length > 4 ? word.Substring(0, 4) : word, sc + along * 0.035f, outN, along, 0.22f, 0.02f);
            Letters(mb, letterSub, word.Length > 4 ? word.Substring(0, 4) : word, sc - along * 0.035f, -outN, -along, 0.22f, 0.02f);
        }
    }

    /// <summary>Landings with railings up one wall, straight flights between them, a ladder at the bottom.</summary>
    static void FireEscape(Kit mb, Vector3 o, Vector3 along, Vector3 outN, float len, float h, System.Random rnd)
    {
        float x0 = 1.2f + (float)rnd.NextDouble() * (len - 6.5f);
        int floors = Mathf.FloorToInt((h - GroundFloorH - 1f) / FloorH);
        Vector3 up = Vector3.up;
        for (int fl = 0; fl <= floors; fl++)
        {
            float y = GroundFloorH + fl * FloorH - 0.35f;
            if (y > h - 1f) break;
            Vector3 lc = o + along * (x0 + 1.2f) + up * y + outN * 0.55f;
            mb.Box(Metal, lc, Abs(along * 2.4f) + up * 0.06f + Abs(outN * 1.05f));                       // landing
            mb.Box(Metal, lc + outN * 0.5f + up * 0.5f, Abs(along * 2.4f) + up * 0.04f + Abs(outN * 0.04f)); // front rail
            foreach (var e in new[] { -1.18f, 1.18f })
            {
                mb.Box(Metal, lc + along * e + outN * 0.5f + up * 0.25f, Abs(along * 0.04f) + up * 0.5f + Abs(outN * 0.04f));
                mb.Box(Metal, lc + along * e + up * 0.5f, Abs(along * 0.04f) + up * 0.04f + Abs(outN * 1.0f));   // side rail
            }
            // brackets into the wall
            foreach (var e in new[] { -0.9f, 0.9f }) mb.Box(Metal, lc + along * e - up * 0.25f - outN * 0.2f, Abs(along * 0.05f) + up * 0.05f + Abs(outN * 0.7f));
            if (fl < floors && GroundFloorH + (fl + 1) * FloorH - 0.35f <= h - 1f)
            {
                // a flight up to the next landing, beside this one
                Vector3 a = o + along * (x0 + 2.4f) + up * y + outN * 0.55f;
                Vector3 b = a + along * 1.9f + up * FloorH;
                Vector3 mid = (a + b) / 2f, dir = (b - a).normalized;
                var q = Quaternion.LookRotation(outN, Vector3.Cross(outN, dir));
                // (BoxRot's local x follows the first axis of q's basis: build it explicitly)
                Vector3 X = dir * ((b - a).magnitude / 2f), Y = Vector3.Cross(outN, dir).normalized * 0.04f, Z = outN * 0.4f;
                StairBox(mb, Metal, mid, X, Y, Z);
                mb.Box(Metal, mid + outN * 0.35f, Abs(along * 0.04f) + up * (FloorH + 0.6f) + Abs(outN * 0.04f)); // handrail post
            }
            if (fl == 0) mb.Box(Metal, lc - up * 1.3f + outN * 0.3f, Abs(along * 0.5f) + up * 2.4f + Abs(outN * 0.05f)); // drop ladder
        }
    }

    static void StairBox(Kit mb, int sub, Vector3 c, Vector3 X, Vector3 Y, Vector3 Z)
    {
        void F(Vector3 nrm, Vector3 u, Vector3 w) { Vector3 o = c + nrm; mb.Quad(sub, o - u - w, o + u - w, o + u + w, o - u + w, nrm); }
        F(X, Y, Z); F(-X, Y, Z); F(Y, X, Z); F(-Y, X, Z); F(Z, X, Y); F(-Z, X, Y);
    }

    /// <summary>A big rooftop sign on two posts.</summary>
    static void Billboard(Kit mb, float w, float d, float h, System.Random rnd)
    {
        bool alongX = w >= d;
        Vector3 along = alongX ? Vector3.right : Vector3.forward, outN = alongX ? Vector3.back : Vector3.left;
        float span = Mathf.Min(alongX ? w : d, 7f) - 1f;
        Vector3 c = Vector3.up * (h + 2.6f) - outN * ((alongX ? d : w) / 2f - 1.2f);
        foreach (var e in new[] { -span / 2f + 0.4f, span / 2f - 0.4f })
            mb.Box(Metal, c + along * e - Vector3.up * 1.1f, Abs(along * 0.14f) + Vector3.up * 3f + Abs(outN * 0.14f));   // (from the roof up to the panel)
        int bg = new[] { Accent, TrimLight, Wood }[rnd.Next(3)];
        mb.Box(bg, c, Abs(along * span) + Vector3.up * 2.2f + Abs(outN * 0.1f));
        mb.Box(TrimDark, c - outN * 0.02f, Abs(along * (span + 0.16f)) + Vector3.up * 2.36f + Abs(outN * 0.06f));
        string word = BillWords[rnd.Next(BillWords.Length)];
        Letters(mb, bg == TrimLight ? TrimDark : TrimLight, word, c + outN * 0.06f, along, outN, Mathf.Min(1.3f, span / (word.Length * 0.9f)), 0.04f);
        mb.Box(Metal, c + Vector3.up * 1.3f + outN * 0.3f, Abs(along * (span + 0.2f)) + Vector3.up * 0.06f + Abs(outN * 0.5f)); // lamp hood
    }

    /// <summary>A horizontal band of trim wrapping one facade (outset from the wall).</summary>
    static void Band(Kit mb, int sub, Vector3 o, Vector3 along, Vector3 outN, float len, float y, float height, float outset)
    {
        Vector3 c = o + along * (len / 2f) + Vector3.up * (y + height / 2f) + outN * (outset / 2f);
        Vector3 size = Abs(along * (len + outset * 2f)) + Vector3.up * height + Abs(outN * outset);
        mb.Box(sub, c, size);
    }

    /// <summary>A window: glass, a frame that stands proud of the wall, a stone sill and lintel.</summary>
    static void Window(Kit mb, Vector3 bottomCentre, Vector3 along, Vector3 outN, float width, float height, bool lit, bool shop)
    {
        Vector3 up = Vector3.up;
        Vector3 c = bottomCentre + up * (height / 2f);
        Vector3 a = c - along * width / 2f - up * height / 2f + outN * 0.03f;
        mb.Quad(lit ? GlassLit : Glass, a, a + along * width, a + along * width + up * height, a + up * height, outN);
        float fw = 0.08f, fd = 0.07f;
        mb.Box(Frame, c - along * (width / 2f) + outN * fd / 2f, Abs(along * fw) + up * (height + fw) + Abs(outN * fd));
        mb.Box(Frame, c + along * (width / 2f) + outN * fd / 2f, Abs(along * fw) + up * (height + fw) + Abs(outN * fd));
        mb.Box(Frame, c + up * (height / 2f) + outN * fd / 2f, Abs(along * width) + up * fw + Abs(outN * fd));
        mb.Box(Frame, c - up * (height / 2f) + outN * fd / 2f, Abs(along * width) + up * fw + Abs(outN * fd));
        if (!shop)
        {
            mb.Box(Frame, c + outN * fd / 2f, Abs(along * 0.05f) + up * height + Abs(outN * 0.04f));     // mullion
            mb.Box(TrimLight, c - up * (height / 2f + 0.07f) + outN * 0.08f, Abs(along * (width + 0.3f)) + up * 0.1f + Abs(outN * 0.16f)); // sill
            mb.Box(TrimLight, c + up * (height / 2f + 0.12f) + outN * 0.04f, Abs(along * (width + 0.24f)) + up * 0.18f + Abs(outN * 0.08f)); // lintel
        }
        else
        {
            mb.Box(Accent, c + up * (height / 2f + 0.35f) + outN * 0.45f, Abs(along * (width + 0.2f)) + up * 0.08f + Abs(outN * 0.9f)); // awning
        }
    }

    // ------------------------------------------------------------------ rooftops

    static void RoofProps(Kit mb, List<(Vector3 c, Vector3 s)> cols, float w, float d, float h, System.Random rnd, Vector2 clear, float clearR)
    {
        var used = new List<(Vector2 p, float r)>();
        if (clearR > 0f) used.Add((clear, clearR));
        bool Place(float radius, out Vector2 p)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                p = new Vector2((float)(rnd.NextDouble() - 0.5) * (w - 2f * radius - 1.2f), (float)(rnd.NextDouble() - 0.5) * (d - 2f * radius - 1.2f));
                bool ok = true;
                foreach (var (q, r) in used) if (Vector2.Distance(p, q) < r + radius + 0.4f) { ok = false; break; }
                if (ok) { used.Add((p, radius)); return true; }
            }
            p = default; return false;
        }

        float area = w * d;
        int towers = area > 120f ? rnd.Next(1, 3) : (rnd.NextDouble() < 0.4 ? 1 : 0);
        for (int i = 0; i < towers; i++) if (Place(1.7f, out var p)) WaterTower(mb, cols, new Vector3(p.x, h, p.y), rnd);
        if (rnd.NextDouble() < 0.7 && Place(1.4f, out var hut)) StairHut(mb, cols, new Vector3(hut.x, h, hut.y), rnd);
        int acs = 1 + rnd.Next(Mathf.Clamp((int)(area / 60f), 1, 5));
        for (int i = 0; i < acs; i++) if (Place(0.8f, out var p)) ACUnit(mb, cols, new Vector3(p.x, h, p.y), rnd);
        int vents = 2 + rnd.Next(4);
        for (int i = 0; i < vents; i++) if (Place(0.25f, out var p)) Vent(mb, new Vector3(p.x, h, p.y), rnd);
        if (rnd.NextDouble() < 0.6 && Place(0.5f, out var ant)) Antenna(mb, new Vector3(ant.x, h, ant.y), rnd);
        if (area > 100f && rnd.NextDouble() < 0.5 && Place(1.1f, out var sky)) Skylight(mb, cols, new Vector3(sky.x, h, sky.y));
    }

    static void WaterTower(Kit mb, List<(Vector3, Vector3)> cols, Vector3 p, System.Random rnd)
    {
        float legH = 2.2f + (float)rnd.NextDouble() * 0.8f, R = 1.25f, tankH = 2.6f;
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            mb.Box(Metal, p + new Vector3(x * 0.95f, legH / 2f, z * 0.95f), new Vector3(0.16f, legH, 0.16f));
        mb.Box(Metal, p + new Vector3(0, legH * 0.45f, -0.95f), new Vector3(1.9f, 0.08f, 0.08f));
        mb.Box(Metal, p + new Vector3(0, legH * 0.45f, 0.95f), new Vector3(1.9f, 0.08f, 0.08f));
        mb.Box(Metal, p + new Vector3(-0.95f, legH * 0.45f, 0), new Vector3(0.08f, 0.08f, 1.9f));
        mb.Box(Metal, p + new Vector3(0.95f, legH * 0.45f, 0), new Vector3(0.08f, 0.08f, 1.9f));
        mb.Box(Wood, p + new Vector3(0, legH + 0.08f, 0), new Vector3(2.7f, 0.16f, 2.7f));   // platform
        Vector3 tb = p + Vector3.up * (legH + 0.16f);
        mb.Cylinder(Wood, tb, R, tankH, 14);
        for (int k = 0; k < 4; k++) mb.Cylinder(Metal, tb + Vector3.up * (0.3f + k * 0.7f), R + 0.03f, 0.07f, 14);
        mb.Cone(Wood, tb + Vector3.up * tankH, R + 0.12f, 0.9f, 14);
        mb.Cylinder(Metal, tb + Vector3.up * (tankH + 0.85f), 0.06f, 0.3f, 6);
        cols.Add((p + Vector3.up * (legH + tankH + 1f) / 2f, new Vector3(2.7f, legH + tankH + 1f, 2.7f)));
    }

    static void StairHut(Kit mb, List<(Vector3, Vector3)> cols, Vector3 p, System.Random rnd)
    {
        Vector3 s = new Vector3(2.4f, 2.5f, 2.2f);
        mb.Box(Brick, p + Vector3.up * s.y / 2f, s);
        mb.Box(TrimLight, p + Vector3.up * (s.y + 0.06f), new Vector3(s.x + 0.2f, 0.12f, s.z + 0.2f));
        Vector3 door = p + new Vector3(0, 1.05f, -s.z / 2f - 0.02f);
        mb.Box(Accent, door, new Vector3(0.95f, 2.0f, 0.05f));
        mb.Box(Frame, door + Vector3.up * 1.05f, new Vector3(1.15f, 0.1f, 0.08f));
        mb.Box(Metal, door + new Vector3(0.32f, 0f, -0.04f), new Vector3(0.12f, 0.04f, 0.05f));
        cols.Add((p + Vector3.up * s.y / 2f, s));
    }

    static void ACUnit(Kit mb, List<(Vector3, Vector3)> cols, Vector3 p, System.Random rnd)
    {
        Vector3 s = new Vector3(1.3f, 0.9f, 1.0f);
        mb.Box(Metal, p + Vector3.up * (s.y / 2f + 0.1f), s);
        mb.Box(TrimDark, p + Vector3.up * 0.05f, new Vector3(s.x + 0.1f, 0.1f, s.z + 0.1f));
        mb.Cylinder(Glass, p + Vector3.up * (s.y + 0.1f), 0.35f, 0.03f, 12);   // fan
        for (int k = 0; k < 5; k++) mb.Box(TrimDark, p + new Vector3(-0.5f + k * 0.25f, 0.55f, -s.z / 2f - 0.01f), new Vector3(0.05f, 0.6f, 0.02f));
        cols.Add((p + Vector3.up * (s.y / 2f + 0.1f), s));
    }

    static void Vent(Kit mb, Vector3 p, System.Random rnd)
    {
        float hgt = 0.5f + (float)rnd.NextDouble() * 0.7f;
        mb.Cylinder(Metal, p, 0.13f, hgt, 8);
        mb.Cone(Metal, p + Vector3.up * (hgt + 0.08f), 0.24f, 0.18f, 8);
        mb.Cylinder(Metal, p + Vector3.up * hgt, 0.04f, 0.1f, 6);
    }

    static void Antenna(Kit mb, Vector3 p, System.Random rnd)
    {
        float hgt = 3f + (float)rnd.NextDouble() * 1.5f;
        mb.Box(Metal, p + Vector3.up * hgt / 2f, new Vector3(0.06f, hgt, 0.06f));
        for (int k = 0; k < 3; k++)
        {
            float y = hgt - 0.2f - k * 0.45f, len = 1.6f - k * 0.35f;
            mb.Box(Metal, p + Vector3.up * y, new Vector3(len, 0.04f, 0.04f));
            for (int e = -2; e <= 2; e++) mb.Box(Metal, p + new Vector3(e * len / 5f, y, 0.12f), new Vector3(0.03f, 0.03f, 0.3f));
        }
        mb.Box(TrimDark, p + Vector3.up * 0.1f, new Vector3(0.4f, 0.2f, 0.4f));
    }

    static void Skylight(Kit mb, List<(Vector3, Vector3)> cols, Vector3 p)
    {
        mb.Box(TrimLight, p + Vector3.up * 0.2f, new Vector3(2f, 0.4f, 1.4f));
        mb.Box(Glass, p + Vector3.up * 0.45f, new Vector3(1.8f, 0.1f, 1.2f));
        for (int k = -1; k <= 1; k++) mb.Box(Frame, p + new Vector3(k * 0.6f, 0.52f, 0), new Vector3(0.05f, 0.05f, 1.25f));
        cols.Add((p + Vector3.up * 0.25f, new Vector3(2f, 0.5f, 1.4f)));
    }

    // ------------------------------------------------------------------ filling in the city

    /// <summary>More buildings on a street grid between the gameplay blocks (the concept city is dense),
    /// keeping the helipad, the hospital and every existing block clear.</summary>
    static void Fill(Transform parent, Transform env, System.Random rnd, Vector3 keepClear)
    {
        var avoid = new List<(Vector2 c, float r)> { (Vector2.zero, 24f) };
        var hosp = GameObject.Find("Hospital"); if (hosp) avoid.Add((new Vector2(hosp.transform.position.x, hosp.transform.position.z - 6f), 24f));
        foreach (Transform b in env)
        {
            if (!b.name.StartsWith("Block_")) continue;
            avoid.Add((new Vector2(b.position.x, b.position.z), Mathf.Max(b.localScale.x, b.localScale.z) * 0.75f + 7f));
        }
        const float pitch = 24f; int made = 0;
        for (int gx = -7; gx <= 7; gx++)
            for (int gz = -7; gz <= 7; gz++)
            {
                Vector2 c = new Vector2(gx * pitch + (float)(rnd.NextDouble() - 0.5) * 3f, gz * pitch + (float)(rnd.NextDouble() - 0.5) * 3f);
                if (c.magnitude > 165f || rnd.NextDouble() < 0.18) continue;
                float w = 9f + (float)rnd.NextDouble() * 5f, d = 9f + (float)rnd.NextDouble() * 5f;   // (leaves room for traffic + pavements in the 24 m grid)
                float r = Mathf.Max(w, d) * 0.72f;
                bool ok = true;
                foreach (var (ac, ar) in avoid) if (Vector2.Distance(c, ac) < ar + r) { ok = false; break; }
                if (!ok) continue;
                float dist01 = c.magnitude / 165f;
                float h = 6f + (float)(rnd.NextDouble() * rnd.NextDouble()) * (14f + 26f * dist01) + (float)rnd.NextDouble() * 4f;
                h = Mathf.Round(h / 1.5f) * 1.5f;
                string[] bricks = { "Brick_Red", "Brick_Brown", "Brick_Tan" };
                MakeBuilding(parent, $"Filler_{gx}_{gz}", new Vector3(c.x, 0f, c.y), Quaternion.Euler(0f, rnd.Next(4) * 90f + (float)(rnd.NextDouble() - 0.5) * 6f, 0f),
                    w, d, h, bricks[rnd.Next(3)], rnd, keepClear, bodyCollider: true);
                avoid.Add((c, r));
                made++;
            }
        Debug.Log($"[City] {made} filler buildings");
    }

    // ------------------------------------------------------------------ distance + streets

    static void Skyline(Transform parent, System.Random rnd)
    {
        var mb = new Kit(10);
        for (int i = 0; i < 90; i++)
        {
            float a = (float)(i / 90.0 * Mathf.PI * 2 + rnd.NextDouble() * 0.05);
            float r = 190f + (float)rnd.NextDouble() * 150f;
            float w = 12f + (float)rnd.NextDouble() * 20f, d = 12f + (float)rnd.NextDouble() * 20f;
            float h = 14f + (float)(rnd.NextDouble() * rnd.NextDouble()) * 70f;
            Vector3 c = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            var q = Quaternion.Euler(0f, (float)rnd.NextDouble() * 90f, 0f);
            mb.BoxRot(Brick, c + Vector3.up * h / 2f, new Vector3(w, h, d), q);
            mb.BoxRot(TrimLight, c + Vector3.up * (h + 0.3f), new Vector3(w + 0.4f, 0.6f, d + 0.4f), q);
            if (rnd.NextDouble() < 0.35)
            {
                Vector3 t = c + q * new Vector3((float)(rnd.NextDouble() - 0.5) * w * 0.5f, h, (float)(rnd.NextDouble() - 0.5) * d * 0.5f);
                mb.Box(Metal, t + Vector3.up * 1.5f, new Vector3(2f, 3f, 2f));
                mb.Cylinder(Wood, t + Vector3.up * 3f, 1.6f, 3.2f, 10);
                mb.Cone(Wood, t + Vector3.up * 6.2f, 1.7f, 1.1f, 10);
            }
        }
        var go = new GameObject("Skyline"); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = Save(mb.ToMesh("Skyline"), "Skyline");
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = Materials("Brick_Brown");
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Streets(Transform parent, Transform env)
    {
        var mb = new Kit(10);
        var cols = new List<(Vector3 c, Vector3 s, Quaternion q)>();
        foreach (Transform b in env)
        {
            if (!b.name.StartsWith("Block_")) continue;
            Vector3 s = b.localScale;
            var q = Quaternion.Euler(0f, b.eulerAngles.y, 0f);
            Vector3 c = new Vector3(b.position.x, 0.075f, b.position.z);
            Vector3 size = new Vector3(s.x + 4f, 0.15f, s.z + 4f);
            mb.BoxRot(TrimLight, c, size, q);
            cols.Add((c, size, q));
        }
        // ...and a kerb of pavement around every filler building too
        foreach (Transform f in parent)
        {
            if (!f.name.StartsWith("Filler_")) continue;
            BoxCollider body = null;
            foreach (var bc in f.GetComponentsInChildren<BoxCollider>()) if (bc.size.y > 4f && (body == null || bc.size.x * bc.size.z > body.size.x * body.size.z)) body = bc;
            if (body == null) continue;
            var q = Quaternion.Euler(0f, f.eulerAngles.y, 0f);
            Vector3 c = new Vector3(f.position.x, 0.075f, f.position.z);
            Vector3 size = new Vector3(body.size.x + 3f, 0.15f, body.size.z + 3f);
            mb.BoxRot(TrimLight, c, size, q);
            cols.Add((c, size, q));
        }
        // Dashed lane markings down the middle of the grid streets, and lamp posts at the corners.
        var marks = new Kit(10);
        var hosp = GameObject.Find("Hospital");
        Vector2 hospC = hosp ? new Vector2(hosp.transform.position.x, hosp.transform.position.z) : new Vector2(9999f, 9999f);
        bool Clear(Vector2 p) => p.magnitude > 17f && Vector2.Distance(p, hospC) > 12f && p.magnitude < 170f && !UnderBlock(env, p);
        const float pitch = 24f;
        for (int k = -7; k <= 6; k++)
        {
            float line = (k + 0.5f) * pitch;
            for (float t = -170f; t < 170f; t += 6f)
            {
                if (Clear(new Vector2(line, t))) marks.Box(TrimLight, new Vector3(line, 0.012f, t), new Vector3(0.18f, 0.02f, 3f));
                if (Clear(new Vector2(t, line))) marks.Box(TrimLight, new Vector3(t, 0.012f, line), new Vector3(3f, 0.02f, 0.18f));
            }
        }
        for (int gx = -7; gx <= 6; gx++)
            for (int gz = -7; gz <= 6; gz++)
            {
                Vector2 corner = new Vector2((gx + 0.5f) * pitch + 3.4f, (gz + 0.5f) * pitch + 3.4f);
                if (!Clear(corner) || ((gx + gz) & 1) == 1) continue;
                Vector3 b = new Vector3(corner.x, 0f, corner.y);
                marks.Cylinder(Metal, b, 0.09f, 4.2f, 6);
                marks.Box(Metal, b + new Vector3(-0.45f, 4.15f, 0f), new Vector3(0.9f, 0.08f, 0.08f));
                marks.Box(GlassLit, b + new Vector3(-0.85f, 4.0f, 0f), new Vector3(0.35f, 0.18f, 0.25f));
                marks.Box(TrimDark, b + Vector3.up * 0.25f, new Vector3(0.3f, 0.5f, 0.3f));
            }
        var mgo = new GameObject("StreetDetails"); mgo.transform.SetParent(parent, false);
        mgo.AddComponent<MeshFilter>().sharedMesh = Save(marks.ToMesh("StreetDetails"), "StreetDetails");
        var mm = Materials("Brick_Red"); mm[TrimLight] = current.roadPaint;
        mgo.AddComponent<MeshRenderer>().sharedMaterials = mm;

        var go = new GameObject("Pavements"); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = Save(mb.ToMesh("Pavements"), "Pavements");
        var mats2 = Materials("Brick_Red"); mats2[TrimLight] = current.pavement;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats2;
        foreach (var (c, s, q) in cols)
        {
            var cg = new GameObject("PavementCollider"); cg.transform.SetParent(go.transform, false);
            cg.transform.SetPositionAndRotation(c, q);
            cg.AddComponent<BoxCollider>().size = s;
        }
    }

    /// <summary>Is this ground point under (or right beside) one of the gameplay blocks?</summary>
    public static bool UnderBlock(Transform env, Vector2 p)
    {
        foreach (Transform b in env)
        {
            if (!b.name.StartsWith("Block_")) continue;
            Vector3 l = Quaternion.Inverse(Quaternion.Euler(0f, b.eulerAngles.y, 0f)) * (new Vector3(p.x, 0f, p.y) - new Vector3(b.position.x, 0f, b.position.z));
            if (Mathf.Abs(l.x) < b.localScale.x / 2f + 2.5f && Mathf.Abs(l.z) < b.localScale.z / 2f + 2.5f) return true;
        }
        var hb = GameObject.Find("HospitalBuilding");
        if (hb)
        {
            var bb = hb.GetComponent<Renderer>().bounds;
            if (p.x > bb.min.x - 2f && p.x < bb.max.x + 2f && p.y > bb.min.z - 2f && p.y < bb.max.z + 2f) return true;
        }
        var city = GameObject.Find("City");
        if (city)
            foreach (Transform f in city.transform)
            {
                if (!f.name.StartsWith("Filler_")) continue;
                var col = f.GetComponentInChildren<BoxCollider>();
                if (col == null) continue;
                var bb = f.GetComponent<Renderer>().bounds;
                if (p.x > bb.min.x - 2f && p.x < bb.max.x + 2f && p.y > bb.min.z - 2f && p.y < bb.max.z + 2f) return true;
            }
        return false;
    }

    // ------------------------------------------------------------------ helpers

    static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static Mesh Save(Mesh m, string name) => m; // generated at load: nothing stored

    /// <summary>Flat-shaded mesh builder with per-material submeshes and UVs in metres (2 m per texture tile on walls).</summary>
    public class Kit
    {
        readonly List<Vector3> v = new(); readonly List<Vector3> n = new(); readonly List<Vector2> uv = new();
        readonly List<int>[] subs;
        public Kit(int count) { subs = new List<int>[count]; for (int i = 0; i < count; i++) subs[i] = new List<int>(); }

        Vector2 UV(Vector3 p, Vector3 normal)
        {
            const float tile = 2f;
            if (Mathf.Abs(normal.y) > 0.7f) return new Vector2(p.x, p.z) / tile;
            Vector3 t = Vector3.Cross(Vector3.up, normal).normalized;
            return new Vector2(Vector3.Dot(p, t), p.y) / tile;
        }

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (nrm.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(nrm, facing) < 0f) { (b, c) = (c, b); nrm = -nrm; }
            nrm.Normalize();
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(nrm); n.Add(nrm); n.Add(nrm);
            uv.Add(UV(a, nrm)); uv.Add(UV(b, nrm)); uv.Add(UV(c, nrm));
            subs[sub].Add(i); subs[sub].Add(i + 1); subs[sub].Add(i + 2);
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            Tri(sub, a, b, c, facing); Tri(sub, a, c, d, facing);
        }

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
                Vector3 mid = (p0 + p1).normalized;
                Quad(sub, bottom + p0, bottom + p1, bottom + p1 + Vector3.up * h, bottom + p0 + Vector3.up * h, mid);
                Tri(sub, bottom + Vector3.up * h, bottom + p0 + Vector3.up * h, bottom + p1 + Vector3.up * h, Vector3.up);
                Tri(sub, bottom, bottom + p0, bottom + p1, Vector3.down);
            }
        }

        public void Cone(int sub, Vector3 bottom, float r, float h, int segs)
        {
            Vector3 apex = bottom + Vector3.up * h;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                Vector3 p0 = bottom + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r, p1 = bottom + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r;
                Vector3 outN = ((p0 + p1) / 2f - bottom).normalized * h + Vector3.up * r;
                Tri(sub, p0, p1, apex, outN);
                Tri(sub, bottom, p0, p1, Vector3.down);
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
