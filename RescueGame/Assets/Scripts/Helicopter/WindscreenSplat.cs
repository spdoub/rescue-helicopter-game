using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Bird strikes. Fly the helicopter through a flock (the city pigeons, the beach gulls) and one of them
/// ends up on the windscreen: a wet thump, a squawk, and a splat on the inside of the pilot's view that
/// takes a good while to dry off. The host decides the hits (it owns the helicopter); everyone sees
/// the splat and hears it. The pilot's view can't be fully trusted: that's the point.
/// </summary>
public class WindscreenSplat : NetworkBehaviour
{
    public Material splatMaterial;   // a soft transparent puff material (the smoke one), tinted per splat
    public AudioClip splatSound;
    public int maxSplats = 4;
    public float fadeSeconds = 45f;

    class Splat { public Transform t; public float born; public Color tint; public float size; }
    readonly List<Splat> splats = new();
    readonly List<Transform> flocks = new();
    float nextScan, nextHit;
    Rigidbody rb;

    void Awake() => rb = GetComponent<Rigidbody>();

    /// <summary>Anything that flies in a group registers its birds here (their transforms).</summary>
    public static void RegisterBirds(IEnumerable<Transform> birds)
    {
        var me = FindAnyObjectByType<WindscreenSplat>();
        if (me != null) me.flocks.AddRange(birds);
    }

    void FixedUpdate()
    {
        if (!IsSpawned || !IsServer || Time.time < nextHit || rb == null) return;
        if (rb.linearVelocity.magnitude < 6f) return;                  // birds get out of the way of a hover
        Vector3 nose = transform.TransformPoint(new Vector3(0f, 2.0f, 2.9f));
        foreach (var b in flocks)
        {
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            if ((b.position - nose).sqrMagnitude < 2.2f * 2.2f)
            {
                nextHit = Time.time + 4f;
                Vector2 spot = new Vector2(Random.Range(-0.55f, 0.55f), Random.Range(-0.2f, 0.25f));
                SplatRpc(spot, Random.Range(0.14f, 0.24f), (byte)Random.Range(0, 3));
                break;
            }
        }
    }

    [Rpc(SendTo.Everyone)]
    void SplatRpc(Vector2 spot, float size, byte kind)
    {
        if (splatSound) AudioSource.PlayClipAtPoint(splatSound, transform.TransformPoint(new Vector3(0f, 2f, 2.9f)), 1f);
        var s = new Splat { born = Time.time, size = size };
        s.tint = kind switch { 0 => new Color(0.92f, 0.9f, 0.8f, 0.92f), 1 => new Color(0.55f, 0.25f, 0.2f, 0.9f), _ => new Color(0.9f, 0.88f, 0.7f, 0.85f) };
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Splat"; Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        // on the inside face of the windscreen (raked back), at the chosen spot
        go.transform.localPosition = new Vector3(spot.x, 2.02f + spot.y, 2.84f - spot.y * 0.55f);
        go.transform.localRotation = Quaternion.Euler(-38f, 180f, Random.Range(0f, 360f));
        go.transform.localScale = new Vector3(size, size * Random.Range(0.7f, 1.3f), 1f);
        var r = go.GetComponent<Renderer>(); r.sharedMaterial = splatMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        s.t = go.transform;
        splats.Add(s);
        // a few drips
        for (int i = 0; i < 3; i++)
        {
            var d = GameObject.CreatePrimitive(PrimitiveType.Quad); d.name = "Drip"; Destroy(d.GetComponent<Collider>());
            d.transform.SetParent(go.transform, false);
            d.transform.localPosition = new Vector3(Random.Range(-0.3f, 0.3f), -0.5f - i * 0.35f - Random.value * 0.3f, -0.001f);
            d.transform.localScale = new Vector3(0.18f, 0.5f + i * 0.2f, 1f);
            d.GetComponent<Renderer>().sharedMaterial = splatMaterial; d.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        while (splats.Count > maxSplats) { Destroy(splats[0].t.gameObject); splats.RemoveAt(0); }
    }

    void Update()
    {
        if (Time.time > nextScan)
        {
            nextScan = Time.time + 5f;
            flocks.RemoveAll(t => t == null);
        }
        for (int i = splats.Count - 1; i >= 0; i--)
        {
            var s = splats[i];
            float age = (Time.time - s.born) / fadeSeconds;
            if (age >= 1f) { Destroy(s.t.gameObject); splats.RemoveAt(i); continue; }
            var c = s.tint; c.a *= 1f - age * age;
            var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", c);
            foreach (var r in s.t.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
        }
    }
}
