using UnityEngine;

/// <summary>
/// A cabin door that slides back along the outside of the hull. Open/closed state lives on
/// NetworkHelicopter (host-owned, synced); this just animates toward it, on every machine.
/// Players toggle it with F when standing near it (see NetworkPlayer).
/// </summary>
public class SlidingDoor : MonoBehaviour
{
    public bool left;
    [Tooltip("How far back (m) the door slides when open.")]
    public float travel = 1.75f;
    public float speed = 1.6f;

    NetworkHelicopter heli;
    Vector3 closedLocal;
    float t;               // 0 closed .. 1 open
    bool offline = true;   // single-player fallback state

    public bool IsOpen => heli != null && heli.IsSpawned ? heli.DoorOpen(left) : offline;

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
        t = Mathf.MoveTowards(t, target, speed / travel * Time.deltaTime);
        Apply();
    }

    void Apply()
    {
        // ease in/out, with a little overshoot-free settle at the ends
        float e = t * t * (3f - 2f * t);
        transform.localPosition = closedLocal + Vector3.back * (travel * e);
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
