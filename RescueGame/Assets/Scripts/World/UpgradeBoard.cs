using UnityEngine;

/// <summary>
/// The upgrade board by the home helipad: a sturdy sign with one card per helicopter upgrade.
/// Walk up, look at a card and press F to buy the next level with the company's rescue money.
/// Built from simple parts when the level loads (nothing stored in the scene but this object).
/// Every machine builds the same board; the cards read their state from HeliUpgrades.
/// </summary>
public class UpgradeBoard : MonoBehaviour
{
    public Material material;
    [Tooltip("How close you have to be to use a card (m, from your eyes).")]
    public float reach = 3.5f;

    public static UpgradeBoard Instance { get; private set; }

    class Card { public Collider hit; public TextMesh price; public Renderer[] pips; public Renderer stamp; }
    readonly Card[] cards = new Card[4];
    TextMesh kitty;
    Font font;

    static readonly Color Steel = new(0.22f, 0.23f, 0.25f), Cork = new(0.72f, 0.56f, 0.38f), Paper = new(0.96f, 0.93f, 0.84f),
                          Ink = new(0.13f, 0.12f, 0.12f), Lit = new(0.3f, 0.85f, 0.35f), Unlit = new(0.55f, 0.52f, 0.48f),
                          Header = new(0.78f, 0.16f, 0.12f);
    // Text colours (TextMesh draws them lighter than they read here, so they're pushed darker).
    static readonly Color TextInk = new(0.02f, 0.015f, 0.01f), TextRed = new(0.5f, 0.02f, 0.01f),
                          TextGrey = new(0.25f, 0.23f, 0.21f), TextGreen = new(0.03f, 0.32f, 0.06f);
    static readonly Color[] Stripe = { new(0.95f, 0.72f, 0.18f), new(0.36f, 0.5f, 0.26f), new(0.55f, 0.57f, 0.6f), new(0.2f, 0.45f, 0.75f) };

    void Awake()
    {
        Instance = this;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Build()
    {
        // Two legs, a cork board, a red header plank, a little roof to keep the rain off.
        Box("LegL", new Vector3(-1.35f, 1.1f, 0f), new Vector3(0.1f, 2.2f, 0.1f), Steel, true);
        Box("LegR", new Vector3(1.35f, 1.1f, 0f), new Vector3(0.1f, 2.2f, 0.1f), Steel, true);
        Box("FootL", new Vector3(-1.35f, 0.03f, 0f), new Vector3(0.3f, 0.06f, 0.5f), Steel, false);
        Box("FootR", new Vector3(1.35f, 0.03f, 0f), new Vector3(0.3f, 0.06f, 0.5f), Steel, false);
        Box("Board", new Vector3(0f, 1.45f, 0f), new Vector3(2.6f, 1.3f, 0.06f), Cork, true);
        Box("Frame", new Vector3(0f, 1.45f, 0.01f), new Vector3(2.7f, 1.4f, 0.04f), Steel, false);
        Box("Header", new Vector3(0f, 2.28f, -0.02f), new Vector3(2.8f, 0.34f, 0.08f), Header, false);
        Box("Roof", new Vector3(0f, 2.5f, -0.12f), new Vector3(3f, 0.05f, 0.45f), Steel, false).transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
        Text("Title", "UPGRADES", new Vector3(0f, 2.28f, -0.07f), 0.1f, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        kitty = Text("Kitty", "", new Vector3(0f, 0.72f, -0.05f), 0.065f, Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
        Box("KittyPlate", new Vector3(0f, 0.72f, -0.01f), new Vector3(1.6f, 0.16f, 0.04f), Ink, false);

        for (int i = 0; i < 4; i++)
        {
            var info = HeliUpgrades.Catalogue[i];
            Vector3 c = new Vector3(i % 2 == 0 ? -0.64f : 0.64f, i < 2 ? 1.77f : 1.14f, -0.045f);
            var card = new Card();
            var paper = Box("Card_" + i, c, new Vector3(1.14f, 0.54f, 0.02f), Paper, true);
            card.hit = paper.GetComponent<Collider>();
            Box("CardStripe_" + i, c + new Vector3(0f, 0.23f, -0.012f), new Vector3(1.14f, 0.08f, 0.01f), Stripe[i], false);
            Box("CardPin_" + i, c + new Vector3(0f, 0.23f, -0.025f), new Vector3(0.05f, 0.05f, 0.03f), Header, false);
            Text("CardName_" + i, info.name, c + new Vector3(-0.53f, 0.12f, -0.015f), 0.06f, TextInk, TextAnchor.MiddleLeft, FontStyle.Bold);
            Text("CardBlurb_" + i, Wrap(info.blurb, 24), c + new Vector3(-0.53f, -0.02f, -0.015f), 0.045f, TextInk, TextAnchor.MiddleLeft, FontStyle.Normal);
            card.price = Text("CardPrice_" + i, "", c + new Vector3(-0.53f, -0.17f, -0.015f), 0.065f, Header, TextAnchor.MiddleLeft, FontStyle.Bold);
            card.pips = new Renderer[info.prices.Length];
            for (int p = 0; p < card.pips.Length; p++)
                card.pips[p] = Box($"CardPip_{i}_{p}", c + new Vector3(0.36f + p * 0.1f, -0.17f, -0.015f), new Vector3(0.07f, 0.07f, 0.01f), Unlit, false).GetComponent<Renderer>();
            cards[i] = card;
        }
    }

    void Update()
    {
        var up = HeliUpgrades.Instance;
        var bank = RescueZone.All.Count > 0 ? RescueZone.All[0] : null;
        int money = bank != null && bank.IsSpawned ? bank.Money.Value : 0;
        kitty.text = $"IN THE KITTY: ${money:N0}";
        if (up == null || !up.IsSpawned) return;
        for (int i = 0; i < 4; i++)
        {
            var k = (HeliUpgrades.Kind)i;
            int lv = up.Level(k), max = HeliUpgrades.MaxLevel(k);
            cards[i].price.text = lv >= max ? "FITTED" : $"${HeliUpgrades.Catalogue[i].prices[lv]:N0}";
            cards[i].price.color = lv >= max ? TextGreen : money >= HeliUpgrades.Catalogue[i].prices[lv] ? TextRed : TextGrey;
            for (int p = 0; p < cards[i].pips.Length; p++) Tint(cards[i].pips[p], p < lv ? Lit : Unlit);
        }
    }

    /// <summary>The card the local player is looking at (within reach), or -1.</summary>
    public int Aimed(Camera cam)
    {
        if (cam == null) return -1;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        float best = reach; int pick = -1;
        for (int i = 0; i < 4; i++)
            if (cards[i] != null && cards[i].hit.Raycast(ray, out RaycastHit h, best)) { best = h.distance; pick = i; }
        return pick;
    }

    // ------------------------------------------------------------------ parts

    GameObject Box(string name, Vector3 local, Vector3 size, Color color, bool solid)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
        p.name = name;
        if (!solid) Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(transform, false);
        p.transform.localPosition = local;
        p.transform.localScale = size;
        var r = p.GetComponent<Renderer>();
        if (material) r.sharedMaterial = material;
        Tint(r, color);
        return p;
    }

    static void Tint(Renderer r, Color c)
    {
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        r.SetPropertyBlock(mpb);
    }

    TextMesh Text(string name, string text, Vector3 local, float size, Color color, TextAnchor anchor, FontStyle style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        // (a TextMesh reads correctly from -Z, which is the front of the board)
        var t = go.AddComponent<TextMesh>();
        t.font = font; t.fontSize = 96; t.characterSize = size * 0.16f; t.fontStyle = style;
        t.anchor = anchor; t.alignment = anchor == TextAnchor.MiddleCenter ? TextAlignment.Center : TextAlignment.Left;
        t.color = color; t.text = text;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return t;
    }

    static string Wrap(string s, int width)
    {
        var sb = new System.Text.StringBuilder(); int line = 0;
        foreach (var w in s.Split(' '))
        {
            if (line > 0 && line + w.Length + 1 > width) { sb.Append('\n'); line = 0; }
            else if (line > 0) { sb.Append(' '); line++; }
            sb.Append(w); line += w.Length;
        }
        return sb.ToString();
    }
}
