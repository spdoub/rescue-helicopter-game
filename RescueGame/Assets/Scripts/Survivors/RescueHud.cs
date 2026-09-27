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
        bubble = new GUIStyle(GUI.skin.box) { fontSize = 16, wordWrap = true, richText = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(10, 10, 6, 6) };
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
        promptBox = new GUIStyle(GUI.skin.box);
        promptBox.normal.background = Solid(new Color(0.08f, 0.07f, 0.09f, 0.78f));
        keyCap = new GUIStyle(GUI.skin.box) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        keyCap.normal.background = Solid(new Color(1f, 0.84f, 0.2f)); keyCap.normal.textColor = new Color(0.1f, 0.08f, 0.05f);
        promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        promptStyle.normal.textColor = Color.white;
        radioStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Italic, richText = true, alignment = TextAnchor.MiddleCenter };
        radioStyle.normal.textColor = new Color(0.9f, 0.95f, 1f);
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t;
    }

    /// <summary>Text with a thick black outline (readable over the bright sky).</summary>
    static void Outlined(Rect r, string text, GUIStyle style, TextAnchor align)
    {
        var was = style.alignment; style.alignment = align;
        Outlined(r, text, style);
        style.alignment = was;
    }

    static void Outlined(Rect r, string text, GUIStyle style)
    {
        var c = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        for (int dx = -2; dx <= 2; dx += 2) for (int dy = -2; dy <= 2; dy += 2)
            if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), text, style);
        style.normal.textColor = c;
        GUI.Label(r, text, style);
    }

    /// <summary>Returns how far down the screen the panel reaches.</summary>
    float MissionPanel(Camera cam)
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
            Outlined(new Rect(26, y, 900, 30), $"WIND: {knots:0} kn from {compass[Mathf.RoundToInt(from / 45f) % 8]} ({kind})", objective);
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
                Outlined(new Rect(26, y, 900, 30), $"- {s.displayName} is grabbing the controls!  Grab him (click) and pull him back!", objective);
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
                    goal = s.kind == SurvivorBrain.Kind.ReclinerGuy ? $"Pick up {s.displayName} - hook his chair's strap"
                         : s.kind == SurvivorBrain.Kind.LedgeResident ? (s.Agreed.Value ? $"Pick up {s.displayName} - bring the hook right up to his ledge"
                                                                                        : $"Talk {s.displayName} round - hover close by, nice and steady")
                         : $"Pick up {s.displayName} from {SurvivorBrain.PlaceNames[Mathf.Min(s.Place.Value, 6)]}";
                    if (s.Fire.Value.start > 0.0) goal += s.Singed.Value ? "  - SINGED! Hurry!" : "  - THE ROOF IS ON FIRE!";
                    Marker(cam, s.transform.position + Vector3.up * 1.8f, s.displayName);
                    break;
                case SurvivorBrain.State.Hooked: goal = $"Winch {them} up!  (hold R at the winch box)"; break;
                case SurvivorBrain.State.Boarding: goal = $"Get {them} in!"; break;
                case SurvivorBrain.State.Seated: goal = $"Fly {s.displayName} to a landing pad (hospital, or the cliff station)"; anyoneAboard = true; break;
                default:
                    float next = s.NextCallIn;
                    goal = next >= 0f ? $"{s.displayName} delivered!  Next call in {Mathf.CeilToInt(next)}..." : $"{s.displayName} delivered!";
                    break;
            }
            Outlined(new Rect(26, y, 900, 30), "- " + goal, objective);
            y += 28f;
        }
        if (anyoneAboard && RescueZone.All.Count > 0 && cam != null)
        {
            RescueZone near = null; float best = float.MaxValue;
            foreach (var z in RescueZone.All) { float d = Vector3.Distance(cam.transform.position, z.transform.position); if (d < best) { best = d; near = z; } }
            foreach (var z in RescueZone.All) Marker(cam, z.transform.position + Vector3.up * 4f, z == near ? z.displayName : z.displayName.ToLowerInvariant());
        }
        return y + 4f;
    }

    readonly System.Collections.Generic.List<(Vector3 world, string label)> pending = new();

    /// <summary>Queue a floating marker (drawn after the panel so it can keep clear of the text).</summary>
    void Marker(Camera cam, Vector3 world, string label) => pending.Add((world, label));

    void DrawMarkers(Camera cam, float panelBottom)
    {
        // The mission text (top left) and the money (top right) are no-go areas for markers.
        placed.Add(new Rect(0f, 0f, 660f, panelBottom));
        placed.Add(new Rect(Screen.width - 340f, 0f, 340f, 80f));
        placed.Add(NetworkBootstrap.HelpRect);
        foreach (var (world, label) in pending) DrawMarker(cam, world, label);
        pending.Clear();
    }

    /// <summary>A floating marker with distance; clamped to the screen edge with an arrow when off screen.</summary>
    void DrawMarker(Camera cam, Vector3 world, string label)
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
        var box = new Rect(Mathf.Clamp(x - 100, 4f, Screen.width - 204f), Mathf.Clamp(yy - 44, 4f, Screen.height - 50f), 200, 46);
        for (int tries = 0; tries < 12 && placed.Exists(r => r.Overlaps(box)); tries++)
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
        DrawMarkers(cam, MissionPanel(cam));

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
            float w = Mathf.Min(size.x + 6f, 340f), h = bubble.CalcHeight(new GUIContent(text), w) + 6f; // (slack: bold text measures a bit narrow)
            var r = new Rect(sp.x - w / 2f, Screen.height - sp.y - h - 10f, w, h);
            r.x = Mathf.Clamp(r.x, 4f, Screen.width - w - 4f); r.y = Mathf.Clamp(r.y, 4f, Screen.height - h - 4f);
            GUI.Box(r, text, bubble);
        }

        // Money, rescues, and the latest review.
        int cash = 0, saved = 0; string report = "";
        var bank = RescueZone.Bank;
        if (bank != null && bank.IsSpawned)
        {
            cash = bank.Money.Value; saved = bank.Rescued.Value;
            string rp = bank.LastReport.Value.ToString();
            if (rp.Length > 0) report = rp;
        }
        if (RescueZone.All.Count > 0)
            GUI.Label(new Rect(Screen.width - 330, 12, 318, 60), $"${cash:N0}\nRescued: {saved}", money);
        if (report != lastReport) { lastReport = report; reportShownAt = Time.time; }
        if (report.Length > 0 && Time.time - reportShownAt < ReportSeconds)
        {
            float w = Mathf.Min(760f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - w) / 2f, 70f, w, 56f), "DELIVERED!  " + report.Replace(" [cliff]", ""), banner);
        }
        UpgradePrompt(cam, cash);
        ActionPrompt();
        // the radio: dispatcher subtitle along the bottom
        if (!string.IsNullOrEmpty(Dispatcher.Subtitle))
        {
            float w = Mathf.Min(900f, Screen.width - 40f);
            var r = new Rect((Screen.width - w) / 2f, Screen.height - 96f, w, 44f);
            GUI.Box(r, "", promptBox);
            Outlined(new Rect(r.x + 12f, r.y + 8f, r.width - 24f, 30f), "<color=#ffd633>DISPATCH:</color> " + Dispatcher.Subtitle, radioStyle, TextAnchor.MiddleCenter);
        }
    }
    GUIStyle radioStyle;

    /// <summary>The one thing you can do right now, as a key-cap prompt just under the centre of the screen:
    /// take the controls, sit down, get up, open or close a door, work the winch.</summary>
    void ActionPrompt()
    {
        var me = NetworkPlayer.Local;
        if (me == null || !me.IsSpawned) return;
        if (UpgradeBoard.Instance && UpgradeBoard.Instance.Aimed(Camera.main) >= 0) return; // the board has its own
        var lines = new System.Collections.Generic.List<(string key, string what)>();
        if (me.SeatedAsPilot.Value) lines.Add(("E", "Leave the controls"));
        else if (me.SeatedPassenger.Value) lines.Add(("E", "Stand up"));
        else
        {
            if (me.NearPilotSeat) lines.Add(("E", "Take the controls"));
            else if (me.NearestCabinSeat() != null) lines.Add(("E", "Sit down"));
            var door = me.NearestDoor();
            if (door != null) lines.Add(("F", door.IsOpen ? "Close the door" : "Open the door"));
            if (me.NearWinch) { lines.Add(("Q", "Lower the hook (hold)")); lines.Add(("R", "Raise the hook (hold)")); }
        }
        if (lines.Count == 0) return;
        float y = Screen.height * 0.56f;
        foreach (var (key, what) in lines)
        {
            float w = 46f + promptStyle.CalcSize(new GUIContent(what)).x + 24f;
            float x = (Screen.width - w) / 2f;
            GUI.Box(new Rect(x, y, w, 40f), "", promptBox);
            GUI.Label(new Rect(x + 6f, y + 5f, 30f, 30f), key, keyCap);
            GUI.Label(new Rect(x + 44f, y + 4f, w - 50f, 32f), what, promptStyle);
            y += 46f;
        }
    }
    GUIStyle promptBox, keyCap, promptStyle;

    /// <summary>Looking at a card on the upgrade board: what it is and how to buy it. Plus the till receipt.</summary>
    void UpgradePrompt(Camera cam, int cash)
    {
        var up = HeliUpgrades.Instance;
        if (up == null || !up.IsSpawned) return;
        int card = UpgradeBoard.Instance ? UpgradeBoard.Instance.Aimed(cam) : -1;
        if (card >= 0)
        {
            var info = HeliUpgrades.Catalogue[card];
            int lv = up.Level((HeliUpgrades.Kind)card), max = info.prices.Length;
            string line = lv >= max ? $"{info.name}: fully fitted" :
                cash >= info.prices[lv] ? $"{info.name} {(lv > 0 ? "II" : "")}  -  ${info.prices[lv]:N0}  -  press F to buy" :
                $"{info.name}  -  ${info.prices[lv]:N0}  (need ${info.prices[lv] - cash:N0} more: go rescue someone)";
            float w = Mathf.Min(760f, Screen.width - 40f);
            Outlined(new Rect((Screen.width - w) / 2f, Screen.height * 0.62f, w, 30f), line, objective, TextAnchor.MiddleCenter);
            Outlined(new Rect((Screen.width - w) / 2f, Screen.height * 0.62f + 28f, w, 30f), info.blurb, marker, TextAnchor.MiddleCenter);
        }
        string msg = up.LastPurchase.Value.ToString();
        if (msg.Length > 0 && Time.time - lastPurchaseSeen < 4f)
        {
            float w = Mathf.Min(620f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - w) / 2f, 130f, w, 44f), msg, banner);
        }
        if (up.LastPurchaseTime.Value != lastPurchaseStamp) { lastPurchaseStamp = up.LastPurchaseTime.Value; lastPurchaseSeen = Time.time; }
    }
    float lastPurchaseStamp = -100f, lastPurchaseSeen = -100f;
}
