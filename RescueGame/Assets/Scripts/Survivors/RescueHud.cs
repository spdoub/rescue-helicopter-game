using UnityEngine;

/// <summary>
/// On-screen rescue info (every machine): what survivors are saying (speech bubbles over their
/// heads), the crew's money and rescue count, and each survivor's review when they're delivered.
/// Creates itself when the game starts.
/// </summary>
public class RescueHud : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create() => DontDestroyOnLoad(new GameObject("RescueHud").AddComponent<RescueHud>());

    GUIStyle bubble, banner, money;
    string lastReport = "";
    float reportShownAt = -100f;
    const float ReportSeconds = 9f;

    void Styles()
    {
        if (bubble != null) return;
        bubble = new GUIStyle(GUI.skin.box) { fontSize = 16, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(10, 10, 6, 6) };
        bubble.normal.textColor = new Color(0.12f, 0.1f, 0.08f);
        bubble.normal.background = Solid(new Color(1f, 0.97f, 0.9f, 0.92f));
        banner = new GUIStyle(bubble) { fontSize = 20 };
        banner.normal.background = Solid(new Color(0.12f, 0.1f, 0.14f, 0.85f));
        banner.normal.textColor = new Color(1f, 0.9f, 0.6f);
        money = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
        money.normal.textColor = new Color(1f, 0.92f, 0.55f);
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t;
    }

    void OnGUI()
    {
        Styles();
        var cam = Camera.main;

        // Speech bubbles over survivors' heads.
        foreach (var s in FindObjectsByType<SurvivorBrain>(FindObjectsSortMode.None))
        {
            if (!s.IsSpawned) continue;
            string line = s.Line.Value.ToString();
            if (string.IsNullOrEmpty(line) || cam == null) continue;
            Vector3 head = s.transform.position + Vector3.up * 1.2f;
            Vector3 sp = cam.WorldToScreenPoint(head);
            float dist = Vector3.Distance(cam.transform.position, head);
            if (sp.z < 0.3f || dist > 60f) continue;
            string text = $"<b>{s.displayName}:</b> {line}";
            var size = bubble.CalcSize(new GUIContent(text));
            float w = Mathf.Min(size.x, 320f), h = bubble.CalcHeight(new GUIContent(text), w);
            var r = new Rect(sp.x - w / 2f, Screen.height - sp.y - h - 10f, w, h);
            r.x = Mathf.Clamp(r.x, 4f, Screen.width - w - 4f); r.y = Mathf.Clamp(r.y, 4f, Screen.height - h - 4f);
            bubble.richText = true;
            GUI.Box(r, text, bubble);
        }

        // Money, rescues, and the latest review.
        int cash = 0, saved = 0; string report = "";
        foreach (var z in RescueZone.All)
        {
            if (!z.IsSpawned) continue;
            cash += z.Money.Value; saved += z.Rescued.Value;
            string rp = z.LastReport.Value.ToString();
            if (rp.Length > 0) report = rp;
        }
        if (RescueZone.All.Count > 0)
            GUI.Label(new Rect(Screen.width - 330, 12, 318, 60), $"${cash:N0}\nRescued: {saved}", money);
        if (report != lastReport) { lastReport = report; reportShownAt = Time.time; }
        if (report.Length > 0 && Time.time - reportShownAt < ReportSeconds)
        {
            float w = Mathf.Min(760f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - w) / 2f, 70f, w, 56f), "DELIVERED!  " + report, banner);
        }
    }
}
