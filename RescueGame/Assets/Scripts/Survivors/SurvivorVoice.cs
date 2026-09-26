using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a survivor's recorded voice line (3D, from their head) whenever their speech-bubble line
/// changes, on every machine. Clips are matched to the line's exact text by the same file-name rule
/// tools/survivor_lines.py uses; SurvivorBuilder fills the list.
/// </summary>
[RequireComponent(typeof(SurvivorBrain))]
public class SurvivorVoice : MonoBehaviour
{
    [System.Serializable]
    public struct Line { public string key; public AudioClip clip; }

    public List<Line> lines = new();
    [Range(0f, 1f)] public float volume = 1f;

    SurvivorBrain brain;
    AudioSource source;
    Dictionary<string, AudioClip> byKey;

    void Awake()
    {
        brain = GetComponent<SurvivorBrain>();
        source = gameObject.AddComponent<AudioSource>();
        source.spatialBlend = 1f;
        source.minDistance = 4f;
        source.maxDistance = 70f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;
        source.playOnAwake = false;
        byKey = new Dictionary<string, AudioClip>();
        foreach (var l in lines) if (l.clip) byKey[l.key] = l.clip;
    }

    void Start() => brain.Line.OnValueChanged += (_, now) => Speak(now.ToString());

    void Speak(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!byKey.TryGetValue(Key(text), out var clip)) return;
        source.Stop();
        source.PlayOneShot(clip, volume);
    }

    /// <summary>Same rule as tools/survivor_lines.py key_for().</summary>
    public static string Key(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in text.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string k = sb.ToString().Trim('_');
        while (k.Contains("__")) k = k.Replace("__", "_");
        return k.Length > 48 ? k.Substring(0, 48) : k;
    }
}
