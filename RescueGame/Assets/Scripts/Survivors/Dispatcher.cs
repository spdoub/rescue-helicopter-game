using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The voice on the radio. Announces each new call ("Rescue One, Dispatch: someone on the cliff top..."),
/// comments when a patient is delivered, and says hello at the start. Runs on every machine by watching
/// the synced survivor state, so nothing extra goes over the network. Lines never overlap (they queue),
/// and the HUD shows them as a subtitle while they play.
/// Clips come from Assets/Audio/Dispatch (tools/voice_line.py with the radio filter).
/// </summary>
public class Dispatcher : MonoBehaviour
{
    [System.Serializable] public struct Line { public string key; public AudioClip clip; public string text; }
    public List<Line> lines = new();
    [Range(0f, 1f)] public float volume = 0.85f;

    public static string Subtitle { get; private set; } = "";

    AudioSource source;
    readonly Queue<string> queue = new();
    readonly Dictionary<string, Line> byKey = new();
    readonly Dictionary<SurvivorBrain, SurvivorBrain.State> lastState = new();
    string lastReport = ""; float subtitleUntil;
    bool welcomed;

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.spatialBlend = 0f; source.playOnAwake = false;
        foreach (var l in lines) if (l.clip) byKey[l.key] = l;
    }

    void Update()
    {
        if (!welcomed && Time.time > 4f) { welcomed = true; Say("welcome"); }

        foreach (var s in FindObjectsByType<SurvivorBrain>(FindObjectsSortMode.InstanceID))
        {
            if (!s.IsSpawned) continue;
            var now = s.Current;
            if (lastState.TryGetValue(s, out var was) && was == SurvivorBrain.State.Delivered && now == SurvivorBrain.State.Waiting)
                Say(s.kind == SurvivorBrain.Kind.LedgeResident ? "call_ledge" : "call_" + PlaceKey(s.Place.Value));
            lastState[s] = now;
        }
        foreach (var z in RescueZone.All)
        {
            if (!z.IsSpawned) continue;
            string rp = z.LastReport.Value.ToString();
            if (rp.Length > 0 && rp != lastReport)
            {
                lastReport = rp;
                int stars = 0; foreach (char c in rp) if (c == '*') stars++;
                Say(stars <= 2 ? "delivered_rough" : "delivered");
            }
        }

        if (!source.isPlaying && queue.Count > 0)
        {
            string key = queue.Dequeue();
            if (byKey.TryGetValue(key, out var l))
            {
                source.PlayOneShot(l.clip, volume);
                Subtitle = l.text; subtitleUntil = Time.time + l.clip.length + 0.5f;
            }
        }
        if (Time.time > subtitleUntil) Subtitle = "";
    }

    static string PlaceKey(byte place) => place switch { 1 => "beach", 2 => "mountain", 3 => "cliff", 4 => "lake", 5 => "forest", 6 => "gondola", _ => "rooftop" };

    void Say(string key) { if (byKey.ContainsKey(key) && !queue.Contains(key)) queue.Enqueue(key); }
}
