using UnityEngine;

/// <summary>
/// A cabin door on rails. Opening: it unlatches and swings OUT a few centimetres on its carriages,
/// then slides back along the rail; closing is the reverse, with a solid settle at the end.
/// Open/closed state lives on NetworkHelicopter (host-owned, synced); this only animates toward it,
/// on every machine. Players toggle it with F when standing near it (see NetworkPlayer).
/// </summary>
public class SlidingDoor : MonoBehaviour
{
    public bool left;
    [Tooltip("How far back (m) the door slides when open.")]
    public float travel = 1.2f;
    [Tooltip("How far (m) it pops out from the hull before sliding.")]
    public float standOff = 0.06f;
    public float slideSeconds = 1.1f, popSeconds = 0.25f;

    NetworkHelicopter heli;
    Vector3 closedLocal;
    float t;               // 0 closed .. 1 fully open (pop = 0..P, slide = P..1)
    bool offline = true;   // single-player fallback state

    public bool IsOpen => heli != null && heli.IsSpawned ? heli.DoorOpen(left) : offline;
    /// <summary>0 = shut, 1 = open (for sound cues).</summary>
    public float Openness => t;

    void Awake()
    {
        heli = GetComponentInParent<NetworkHelicopter>();
        closedLocal = transform.localPosition;
        t = IsOpen ? 1f : 0f;
        Apply();
    }

    public void OfflineToggle() => offline = !offline;

    void Update()
    {
        float target = IsOpen ? 1f : 0f;
        if (Mathf.Approximately(t, target)) return;
        float P = PopFraction;
        // the pop-out phase is short, the slide is long: move t at the right rate for the phase it's in
        float rate = t < P ? P / popSeconds : (1f - P) / slideSeconds;
        t = Mathf.MoveTowards(t, target, rate * Time.deltaTime);
        Apply();
    }

    float PopFraction => 0.18f;

    void Apply()
    {
        float P = PopFraction;
        float pop = Mathf.Clamp01(t / P);                       // 0..1 over the pop phase
        float slide = Mathf.Clamp01((t - P) / (1f - P));        // 0..1 over the slide phase
        float e = slide * slide * (3f - 2f * slide);           // ease in/out
        // a firm settle: the last few cm of closing are quicker
        Vector3 outDir = left ? Vector3.left : Vector3.right;
        transform.localPosition = closedLocal + outDir * (standOff * Mathf.SmoothStep(0f, 1f, pop)) + Vector3.back * (travel * e);
    }

    /// <summary>World position of the door's handle area (for "am I near it?").</summary>
    public Vector3 HandlePoint
    {
        get
        {
            var h = heli != null ? heli.transform : transform.parent;
            return h.TransformPoint(new Vector3(left ? -0.9f : 0.9f, 1.6f, 0.55f));
        }
    }
}
