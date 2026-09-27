using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The review wall by the home pad (the PRD's trophy wall, first slice): every delivered survivor's
/// verdict and stars are kept on the host between sessions (a small text file in the player's data
/// folder) and the last few are painted on a notice board next to the upgrade board. Clients see the
/// board too: the host streams the lines through the bank zone's LastReport as they happen, and the
/// board reads its own local file, so each machine shows the reviews it has witnessed.
/// </summary>
public class ReviewWall : MonoBehaviour
{
    public Material material;
    public int shown = 5;

    readonly List<string> reviews = new();
    TextMesh[] lines;
    string lastSeen = "";
    Font font;

    string Path => System.IO.Path.Combine(Application.persistentDataPath, "reviews.txt");

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        try { if (File.Exists(Path)) reviews.AddRange(File.ReadAllLines(Path)); } catch { }
        Build();
        Refresh();
    }

    void Build()
    {
        Color dark = new Color(0.2f, 0.2f, 0.22f), cork = new Color(0.66f, 0.5f, 0.34f), paper = new Color(0.96f, 0.94f, 0.86f);
        Box("LegL", new Vector3(-0.9f, 0.95f, 0f), new Vector3(0.08f, 1.9f, 0.08f), dark);
        Box("LegR", new Vector3(0.9f, 0.95f, 0f), new Vector3(0.08f, 1.9f, 0.08f), dark);
        Box("Board", new Vector3(0f, 1.35f, 0f), new Vector3(1.9f, 1.1f, 0.05f), cork);
        Box("Frame", new Vector3(0f, 1.35f, 0.01f), new Vector3(2.0f, 1.2f, 0.03f), dark);
        Box("Header", new Vector3(0f, 2.02f, -0.02f), new Vector3(2.0f, 0.26f, 0.07f), new Color(0.78f, 0.16f, 0.12f));
        Text("Title", "CUSTOMER REVIEWS", new Vector3(0f, 2.02f, -0.06f), 0.075f, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        lines = new TextMesh[shown];
        for (int i = 0; i < shown; i++)
        {
            float y = 1.72f - i * 0.19f;
            Box("Card" + i, new Vector3(0f, y, -0.03f), new Vector3(1.7f, 0.16f, 0.015f), paper);
            Box("Pin" + i, new Vector3(-0.78f, y + 0.06f, -0.045f), new Vector3(0.03f, 0.03f, 0.02f), new Color(0.85f, 0.2f, 0.15f));
            lines[i] = Text("Line" + i, "", new Vector3(-0.8f, y, -0.042f), 0.045f, new Color(0.02f, 0.015f, 0.01f), TextAnchor.MiddleLeft, FontStyle.Normal);
        }
    }

    void Update()
    {
        var bank = RescueZone.Bank;
        if (bank == null || !bank.IsSpawned) return;
        string rp = bank.LastReport.Value.ToString();
        if (rp.Length > 0 && rp != lastSeen)
        {
            lastSeen = rp;
            reviews.Add(rp.Replace(" [cliff]", ""));
            while (reviews.Count > 40) reviews.RemoveAt(0);
            try { File.WriteAllLines(Path, reviews); } catch { }
            Refresh();
        }
    }

    void Refresh()
    {
        for (int i = 0; i < shown; i++)
        {
            int idx = reviews.Count - 1 - i;
            string s = idx >= 0 ? reviews[idx] : (i == 0 ? "(no reviews yet - go rescue someone)" : "");
            // "Grandma: "Lovely boys..."  ***  +$1,450" -> keep it short for the card
            if (s.Length > 46) s = s.Substring(0, 44) + "..";
            lines[i].text = s;
        }
    }

    GameObject Box(string n, Vector3 local, Vector3 size, Color c)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Cube); p.name = n; DestroyImmediate(p.GetComponent<Collider>());
        p.transform.SetParent(transform, false); p.transform.localPosition = local; p.transform.localScale = size;
        var r = p.GetComponent<Renderer>(); if (material) r.sharedMaterial = material;
        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", c); r.SetPropertyBlock(mpb);
        return p;
    }

    TextMesh Text(string n, string text, Vector3 local, float size, Color c, TextAnchor anchor, FontStyle style)
    {
        var go = new GameObject(n); go.transform.SetParent(transform, false); go.transform.localPosition = local;
        var t = go.AddComponent<TextMesh>();
        t.font = font; t.fontSize = 96; t.characterSize = size * 0.16f; t.fontStyle = style; t.anchor = anchor;
        t.alignment = anchor == TextAnchor.MiddleCenter ? TextAlignment.Center : TextAlignment.Left; t.color = c; t.text = text;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return t;
    }
}
