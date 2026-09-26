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

    GUIStyle bubble, banner, money, title, objective, fuelStyle, marker;
    NetworkHelicopter heli; HeliFuel fuel; HeliDamage damage;
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
        // Concept 3's HUD: chunky yellow title, white timer, black outline.
        title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
        title.normal.textColor = new Color(1f, 0.84f, 0.2f);
        objective = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = true };
        objective.normal.textColor = Color.white;
        fuelStyle = new GUIStyle(objective) { fontSize = 24 };
        marker = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
        marker.normal.textColor = new Color(1f, 0.84f, 0.2f);
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t;
    }

    /// <summary>Text with a thick black outline (readable over the bright sky).</summary>
    static void Outlined(Rect r, string text, GUIStyle style)
    {
        var c = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        for (int dx = -2; dx <= 2; dx += 2) for (int dy = -2; dy <= 2; dy += 2)
            if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), text, style);
        style.normal.textColor = c;
        GUI.Label(r, text, style);
    }

    void MissionPanel(Camera cam)
    {
        if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint) placed.Clear();
        if (heli == null) { heli = FindAnyObjectByType<NetworkHelicopter>(); if (heli) { fuel = heli.GetComponent<HeliFuel>(); damage = heli.GetComponent<HeliDamage>(); } }
        var all = FindObjectsByType<SurvivorBrain>(FindObjectsSortMode.InstanceID);
        Outlined(new Rect(24, 18, 600, 44), "CO-OP RESCUE", title);
        if (fuel != null && fuel.IsSpawned)
        {
            float secs = fuel.SecondsLeft;
            bool low = secs < 60f;
            fuelStyle.normal.textColor = low && Mathf.Repeat(Time.time, 1f) < 0.5f ? new Color(1f, 0.35f, 0.25f) : Color.white;
            string refuel = fuel.Refuelling ? "  (refuelling)" : fuel.Fuel.Value <= 0f ? "  OUT OF FUEL - LAND NOW" : "";
            Outlined(new Rect(26, 60, 600, 32), $"FUEL REMAINING: {Mathf.FloorToInt(secs / 60f):00}:{Mathf.FloorToInt(secs % 60f):00}{refuel}", fuelStyle);
        }

        float y = 94f; bool anyoneAboard = false;
        var weather = Weather.Instance;
        if (weather != null && weather.IsSpawned)
        {
            Vector3 w = weather.Wind.Value;
            float knots = w.magnitude * 1.944f;
            string[] compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            float from = (Mathf.Atan2(-w.x, -w.z) * Mathf.Rad2Deg + 360f) % 360f;   // where it's blowing FROM
            string kind = weather.Current.Value switch { Weather.Kind.Calm => "calm", Weather.Kind.Breezy => "breezy", _ => "GUSTY" };
            objective.normal.textColor = weather.Current.Value == Weather.Kind.Gusty ? new Color(1f, 0.8f, 0.5f) : Color.white;
            Outlined(new Rect(26, y, 620, 30), $"WIND: {knots:0} kn from {compass[Mathf.RoundToInt(from / 45f) % 8]} ({kind})", objective);
            objective.normal.textColor = Color.white;
            y += 30f;
        }
        if (damage != null && damage.IsSpawned && damage.Health.Value < 0.999f)
        {
            float h = damage.Health.Value;
            fuelStyle.normal.textColor = h < 0.3f && Mathf.Repeat(Time.time, 0.6f) < 0.3f ? new Color(1f, 0.35f, 0.25f) : h < 0.6f ? new Color(1f, 0.75f, 0.4f) : Color.white;
            string state = h <= 0f ? "  ENGINE FAILING - LAND IT" : h < 0.3f ? "  (land on the home pad to repair)" : "";
            Outlined(new Rect(26, y - 2, 620, 32), $"HULL: {Mathf.RoundToInt(h * 100f)}%{state}", fuelStyle);
            y += 30f;
        }
        foreach (var s in all)
        {
            if (!s.IsSpawned) continue;
            string them = s.kind == SurvivorBrain.Kind.StrongGrandma || s.kind == SurvivorBrain.Kind.DogPerson ? "her" : "him";
            if (s.Current == SurvivorBrain.State.Meddling)
            {
                Outlined(new Rect(26, y, 620, 30), $"- {s.displayName} is grabbing the controls!  Grab him (click) and pull him back!", objective);
                y += 28f; anyoneAboard = true;
                continue;
            }
            string goal;
            switch (s.Current)
            {
                case SurvivorBrain.State.Waiting when s.kind == SurvivorBrain.Kind.DogPerson && s.dog != null && !s.dog.Aboard:
                    goal = $"Get {s.dog.dogName} the dog first - hang the hook low and still near him";
                    Marker(cam, s.dog.transform.position + Vector3.up * 1f, s.dog.dogName);
                    break;
                case SurvivorBrain.State.Waiting:
                    goal = s.kind == SurvivorBrain.Kind.ReclinerGuy ? $"Pick up {s.displayName} - hook his chair's strap" : $"Pick up {s.displayName} from the rooftop";
                    Marker(cam, s.transform.position + Vector3.up * 1.8f, s.displayName);
                    break;
                case SurvivorBrain.State.Hooked: goal = $"Winch {them} up!  (hold R at the winch box)"; break;
                case SurvivorBrain.State.Boarding: goal = $"Get {them} in!"; break;
                case SurvivorBrain.State.Seated: goal = $"Fly {s.displayName} to the hospital and land on the pad"; anyoneAboard = true; break;
                default:
                    float next = s.NextCallIn;
                    goal = next >= 0f ? $"{s.displayName} delivered!  Next call in {Mathf.CeilToInt(next)}..." : $"{s.displayName} delivered!";
                    break;
            }
            Outlined(new Rect(26, y, 620, 30), "- " + goal, objective);
            y += 28f;
        }
        if (anyoneAboard && RescueZone.All.Count > 0) Marker(cam, RescueZone.All[0].transform.position + Vector3.up * 4f, "HOSPITAL");
    }

    /// <summary>A floating marker with distance; clamped to the screen edge with an arrow when off screen.</summary>
    void Marker(Camera cam, Vector3 world, string label)
    {
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(world);
        float dist = Vector3.Distance(cam.transform.position, world);
        bool behind = sp.z < 0f;
        if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
        float m = 40f;
        bool off = behind || sp.x < m || sp.x > Screen.width - m || sp.y < m || sp.y > Screen.height - m;
        float x = Mathf.Clamp(sp.x, m, Screen.width - m), yy = Mathf.Clamp(Screen.height - sp.y, m, Screen.height - m);
        if (behind) yy = Screen.height - m;
        string icon = off ? (x <= m ? "◀" : x >= Screen.width - m ? "▶" : yy <= m ? "▲" : "▼") : "▼";
        // Several markers bunched up at the same edge: stack them instead of printing on top of each other.
        var box = new Rect(x - 100, yy - 44, 200, 46);
        for (int tries = 0; tries < 8 && placed.Exists(r => r.Overlaps(box)); tries++)
            box.y += yy > Screen.height * 0.5f ? -46f : 46f;
        placed.Add(box);
        Outlined(new Rect(box.x, box.y, 200, 24), $"{label}  {dist:0} m", marker);
        Outlined(new Rect(box.x + 80, box.y + 22, 40, 28), icon, marker);
    }
    readonly System.Collections.Generic.List<Rect> placed = new();

    void OnGUI()
    {
        Styles();
        var cam = Camera.main;
        MissionPanel(cam);

        // Name tags over the other crew members (in their suit colour).
        if (cam != null)
            foreach (var p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                if (!p.IsSpawned || p.IsNpc || p.IsLocalPlayer) continue;
                Vector3 head = p.transform.position + Vector3.up * 1.15f;
                Vector3 sp = cam.WorldToScreenPoint(head);
                float dist = Vector3.Distance(cam.transform.position, head);
                if (sp.z < 0.3f || dist > 80f) continue;
                marker.normal.textColor = CrewVisual.SuitColor(p.OwnerClientId);
                string tag = p.SeatedAsPilot.Value ? $"Crew {p.OwnerClientId + 1} (pilot)" : $"Crew {p.OwnerClientId + 1}";
                Outlined(new Rect(sp.x - 80, Screen.height - sp.y - 24, 160, 22), tag, marker);
            }
        marker.normal.textColor = new Color(1f, 0.84f, 0.2f);

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
