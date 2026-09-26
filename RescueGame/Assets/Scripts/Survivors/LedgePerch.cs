using UnityEngine;

/// <summary>
/// The Ledge Resident's home: a narrow stone ledge high up a building's wall, with his window
/// behind it, a potted geranium and a few pigeons (who scatter when the helicopter comes close,
/// and are back by the next call). Built on every machine from SurvivorBrain.LedgeSpot.
/// </summary>
[RequireComponent(typeof(SurvivorBrain))]
public class LedgePerch : MonoBehaviour
{
    public Material material;

    SurvivorBrain brain;
    Transform root;
    Vector4 built = new(float.NaN, 0, 0, 0);
    readonly Transform[] pigeons = new Transform[3];
    readonly Vector3[] pigeonHome = new Vector3[3], pigeonAway = new Vector3[3];
    float scatteredAt = -1f;

    static readonly Color Stone = new(0.78f, 0.72f, 0.62f), StoneDark = new(0.55f, 0.5f, 0.44f), Frame = new(0.9f, 0.88f, 0.82f),
                          Glass = new(0.2f, 0.28f, 0.34f), Curtain = new(0.62f, 0.2f, 0.18f), Pot = new(0.72f, 0.36f, 0.2f),
                          Leaf = new(0.28f, 0.5f, 0.22f), Flower = new(0.9f, 0.2f, 0.25f), Pigeon = new(0.52f, 0.54f, 0.6f),
                          PigeonNeck = new(0.36f, 0.5f, 0.46f), Beak = new(0.9f, 0.62f, 0.25f);

    void Awake() => brain = GetComponent<SurvivorBrain>();

    void Update()
    {
        if (!brain.IsSpawned) return;
        if (brain.LedgeSpot.Value != built) Rebuild();
        if (root == null) return;

        // Pigeons: off they go when the helicopter gets close (and they don't come back until the next call).
        var heli = FindHeli();
        if (scatteredAt < 0f && heli != null && Vector3.Distance(heli.position, root.position) < 26f) scatteredAt = Time.time;
        for (int i = 0; i < 3; i++)
        {
            if (pigeons[i] == null) continue;
            if (scatteredAt < 0f)
            {
                // idle pecking
                float bob = Mathf.Max(0f, Mathf.Sin(Time.time * 3f + i * 2.1f)) * 0.03f;
                pigeons[i].position = pigeonHome[i] + Vector3.down * bob;
                continue;
            }
            float k = (Time.time - scatteredAt - i * 0.25f) / 3f;
            if (k < 0f) continue;
            if (k > 1f) { pigeons[i].gameObject.SetActive(false); continue; }
            Vector3 p = Vector3.Lerp(pigeonHome[i], pigeonAway[i], k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 3f;
            pigeons[i].forward = Vector3.Lerp(pigeons[i].forward, (pigeonAway[i] - pigeonHome[i]).normalized, Time.deltaTime * 5f);
            pigeons[i].position = p;
            // flapping: squash the wings in and out
            var wings = pigeons[i].Find("Wings");
            if (wings) wings.localScale = new Vector3(1f + Mathf.Abs(Mathf.Sin(Time.time * 30f + i)) * 2.2f, 0.25f, 0.6f);
        }
    }

    Transform heliT;
    Transform FindHeli()
    {
        if (heliT == null) { var h = FindAnyObjectByType<NetworkHelicopter>(); if (h) heliT = h.transform; }
        return heliT;
    }

    /// <summary>(Re)build the ledge where the brain says it is.</summary>
    public void Rebuild()
    {
        var s = brain.LedgeSpot.Value;
        built = s;
        if (root) Destroy(root.gameObject);
        root = null;
        if (s == Vector4.zero) return;
        root = new GameObject("Ledge_" + brain.displayName).transform;
        root.SetPositionAndRotation(new Vector3(s.x, s.y, s.z), Quaternion.Euler(0f, s.w, 0f));
        // (local: +z = out from the wall, x = along it, origin = where he stands on top of the slab; the wall is at z = -0.45)
        Box("Slab", new Vector3(0f, -0.14f, 0.25f), new Vector3(3.6f, 0.28f, 1.4f), Stone, true);
        Box("SlabLip", new Vector3(0f, -0.3f, 0.9f), new Vector3(3.7f, 0.08f, 0.12f), StoneDark, false);
        for (int i = -1; i <= 1; i += 2)
        {
            var br = Box("Bracket", new Vector3(i * 1.3f, -0.6f, -0.05f), new Vector3(0.22f, 0.65f, 0.6f), StoneDark, false);
            br.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
        }
        // his window: frame, dark glass, a curtain pulled half across
        Box("WindowFrame", new Vector3(0.2f, 1.1f, -0.44f), new Vector3(1.3f, 1.7f, 0.06f), Frame, false);
        Box("WindowGlass", new Vector3(0.2f, 1.1f, -0.405f), new Vector3(1.1f, 1.5f, 0.02f), Glass, false);
        Box("Curtain", new Vector3(-0.15f, 1.15f, -0.39f), new Vector3(0.45f, 1.4f, 0.02f), Curtain, false);
        Box("WindowSill", new Vector3(0.2f, 0.22f, -0.38f), new Vector3(1.45f, 0.08f, 0.2f), Frame, false);
        // geranium
        var pot = Shape(PrimitiveType.Cylinder, "Pot", new Vector3(1.35f, 0.14f, -0.1f), new Vector3(0.3f, 0.14f, 0.3f), Pot);
        Shape(PrimitiveType.Sphere, "Plant", new Vector3(1.35f, 0.42f, -0.1f), new Vector3(0.42f, 0.34f, 0.42f), Leaf);
        for (int f = 0; f < 4; f++)
            Shape(PrimitiveType.Sphere, "Flower", new Vector3(1.35f + Mathf.Cos(f * 1.6f) * 0.14f, 0.58f, -0.1f + Mathf.Sin(f * 1.6f) * 0.14f), Vector3.one * 0.1f, Flower);
        _ = pot;
        // pigeons along the ledge
        scatteredAt = -1f;
        float[] xs = { -1.4f, -0.95f, 0.8f };
        for (int i = 0; i < 3; i++)
        {
            var b = MakePigeon(i);
            b.localPosition = new Vector3(xs[i], 0.02f, 0.25f);
            b.localRotation = Quaternion.Euler(0f, 60f + i * 110f, 0f);
            pigeons[i] = b;
            pigeonHome[i] = b.position;
            pigeonAway[i] = root.TransformPoint(new Vector3(xs[i] * 4f + (i - 1) * 3f, 5f + i * 2f, 14f + i * 3f));
        }
    }

    Transform MakePigeon(int i)
    {
        var p = new GameObject("Pigeon").transform;
        p.SetParent(root, false);
        void Part(PrimitiveType t, string n, Vector3 pos, Vector3 scale, Color c, Transform parent = null)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = n;
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent ? parent : p, false);
            g.transform.localPosition = pos; g.transform.localScale = scale;
            Paint(g.GetComponent<Renderer>(), c);
        }
        Part(PrimitiveType.Sphere, "Body", new Vector3(0f, 0.12f, 0f), new Vector3(0.16f, 0.15f, 0.26f), Pigeon);
        Part(PrimitiveType.Sphere, "Neck", new Vector3(0f, 0.2f, 0.08f), new Vector3(0.1f, 0.1f, 0.1f), PigeonNeck);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 0.26f, 0.11f), new Vector3(0.09f, 0.09f, 0.1f), Pigeon);
        Part(PrimitiveType.Cube, "Beak", new Vector3(0f, 0.25f, 0.17f), new Vector3(0.025f, 0.02f, 0.05f), Beak);
        Part(PrimitiveType.Cube, "Tail", new Vector3(0f, 0.1f, -0.15f), new Vector3(0.1f, 0.02f, 0.12f), PigeonNeck);
        var wings = new GameObject("Wings").transform; wings.SetParent(p, false); wings.localPosition = new Vector3(0f, 0.15f, 0f);
        wings.localScale = new Vector3(1f, 0.25f, 0.6f);
        Part(PrimitiveType.Cube, "Wing", Vector3.zero, new Vector3(0.2f, 0.12f, 0.3f), Pigeon, wings);
        return p;
    }

    GameObject Box(string n, Vector3 local, Vector3 size, Color c, bool solid)
    {
        var g = Shape(PrimitiveType.Cube, n, local, size, c);
        if (solid) g.AddComponent<BoxCollider>();
        return g;
    }

    GameObject Shape(PrimitiveType t, string n, Vector3 local, Vector3 size, Color c)
    {
        var g = GameObject.CreatePrimitive(t);
        g.name = n;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(root, false);
        g.transform.localPosition = local;
        g.transform.localScale = size;
        Paint(g.GetComponent<Renderer>(), c);
        return g;
    }

    void Paint(Renderer r, Color c)
    {
        if (material) r.sharedMaterial = material;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", c);
        r.SetPropertyBlock(mpb);
    }

    void OnDestroy() { if (root) Destroy(root.gameObject); }
}
