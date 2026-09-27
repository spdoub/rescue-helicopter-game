using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rain when the weather turns gusty: a sheet of streaks falling around the camera, drops that
/// collect on the inside of the pilot's windscreen and smear the view, and a wiper that sweeps them
/// away (it's an old wiper: it leaves an arc). Cosmetic, runs off the synced Weather state, so every
/// machine sees the same rain. The pilot can't fully trust the view; that's the design pillar.
/// </summary>
public class Rain : MonoBehaviour
{
    public Material dropMaterial;     // soft transparent (the smoke material), tinted per drop
    public Material streakMaterial;   // the wind-streak material
    public float dropsPerSecond = 6f;
    public int maxDrops = 60;
    public AudioClip rainLoop, wiperSqueak;
    AudioSource rainSource; float lastSweepSign;

    class Drop { public Transform t; public float born, life; public Vector2 pos; }
    readonly List<Drop> drops = new();
    ParticleSystem sheet;
    Transform wiper;               // the blade's pivot (built here, the body's static blade is hidden while it rains)
    Transform staticWiper, staticBlade;
    Quaternion wiperRest;
    float debt, wiperPhase;
    bool raining;

    void Awake()
    {
        sheet = MakeSheet();
        staticWiper = FindDeep(transform, "Wiper_0"); staticBlade = FindDeep(transform, "WiperBlade");   // (the body's tube segments are suffixed)
        // an animated wiper: pivot at the base of the static one, sweeping across the pilot's half of the screen
        var pivot = new GameObject("WiperPivot").transform; pivot.SetParent(transform, false);
        pivot.localPosition = new Vector3(0.35f, 1.72f, 3.03f);
        pivot.localRotation = Quaternion.Euler(-52f, 0f, 0f);
        var arm = GameObject.CreatePrimitive(PrimitiveType.Cube); arm.name = "WiperArm"; DestroyImmediate(arm.GetComponent<Collider>());
        arm.transform.SetParent(pivot, false); arm.transform.localPosition = new Vector3(0f, 0.3f, 0f); arm.transform.localScale = new Vector3(0.025f, 0.6f, 0.025f);
        var blade = GameObject.CreatePrimitive(PrimitiveType.Cube); blade.name = "WiperRubber"; DestroyImmediate(blade.GetComponent<Collider>());
        blade.transform.SetParent(pivot, false); blade.transform.localPosition = new Vector3(0f, 0.36f, -0.02f); blade.transform.localScale = new Vector3(0.02f, 0.5f, 0.015f);
        foreach (var r in pivot.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = dropMaterial ? dropMaterial : r.sharedMaterial; var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.09f, 1f)); r.SetPropertyBlock(mpb); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        wiper = pivot; wiperRest = pivot.localRotation;
        pivot.gameObject.SetActive(false);
        rainSource = gameObject.AddComponent<AudioSource>();
        rainSource.clip = rainLoop; rainSource.loop = true; rainSource.spatialBlend = 0f; rainSource.volume = 0f; rainSource.playOnAwake = false;
    }

    ParticleSystem MakeSheet()
    {
        var go = new GameObject("RainSheet");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = 0.9f;
        main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f); main.maxParticles = 1500;
        main.startColor = new Color(0.85f, 0.9f, 1f, 0.45f); main.gravityModifier = 2.2f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(30f, 2f, 30f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 3f;
        r.sharedMaterial = streakMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var em = ps.emission; em.rateOverTime = 0f;
        ps.Play();
        return ps;
    }

    void Update()
    {
        var w = Weather.Instance;
        bool gusty = w != null && w.IsSpawned && w.Current.Value == Weather.Kind.Gusty;
        if (gusty != raining)
        {
            raining = gusty;
            if (staticWiper) staticWiper.gameObject.SetActive(!raining);
            if (staticBlade) staticBlade.gameObject.SetActive(!raining);
            wiper.gameObject.SetActive(raining);
        }
        // the sheet follows the camera, blown along with the wind
        var cam = Camera.main;
        if (cam)
        {
            Vector3 wind = w != null && w.IsSpawned ? w.Wind.Value : Vector3.zero;
            sheet.transform.position = cam.transform.position + Vector3.up * 12f + wind.normalized * -4f;
            var vel = sheet.velocityOverLifetime; vel.enabled = true; vel.x = wind.x * 0.6f; vel.z = wind.z * 0.6f; vel.y = -14f;
            var em = sheet.emission; em.rateOverTime = raining ? 900f : 0f;
        }
        // rain sound: fades in with the weather, louder with the doors shut (you're in a tin box)
        if (rainSource && rainLoop)
        {
            bool doorsShut = true; foreach (var sd in GetComponentsInChildren<SlidingDoor>()) if (sd.IsOpen) doorsShut = false;
            float target = raining ? (doorsShut ? 0.55f : 0.35f) : 0f;
            rainSource.volume = Mathf.MoveTowards(rainSource.volume, target, Time.deltaTime * 0.4f);
            if (rainSource.volume > 0.01f && !rainSource.isPlaying) rainSource.Play();
            if (rainSource.volume <= 0.01f && rainSource.isPlaying) rainSource.Stop();
        }
        if (!raining) { foreach (var d in drops) Destroy(d.t.gameObject); drops.Clear(); return; }

        // drops gather on the inside of the windscreen (only the local pilot sees them close up, but they exist for everyone)
        debt += dropsPerSecond * Time.deltaTime;
        while (debt >= 1f && drops.Count < maxDrops)
        {
            debt -= 1f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Raindrop"; DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            var d = new Drop { born = Time.time, life = Random.Range(4f, 9f), pos = new Vector2(Random.Range(-0.85f, 0.85f), Random.Range(-0.28f, 0.3f)) };
            go.transform.localPosition = new Vector3(d.pos.x, 2.02f + d.pos.y, 2.84f - d.pos.y * 0.55f);
            go.transform.localRotation = Quaternion.Euler(-38f, 180f, 0f);
            float s = Random.Range(0.03f, 0.07f);
            go.transform.localScale = new Vector3(s, s * Random.Range(1.5f, 3f), 1f);
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = dropMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", new Color(0.75f, 0.85f, 1f, 0.55f)); r.SetPropertyBlock(mpb);
            d.t = go.transform;
            drops.Add(d);
        }
        // drops run down the glass, then dry off
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var d = drops[i];
            float age = Time.time - d.born;
            if (age > d.life) { Destroy(d.t.gameObject); drops.RemoveAt(i); continue; }
            d.pos.y -= Time.deltaTime * 0.04f;
            d.t.localPosition = new Vector3(d.pos.x, 2.02f + d.pos.y, 2.84f - d.pos.y * 0.55f);
        }
        // the wiper sweeps, and clears whatever it passes over on the pilot's side
        wiperPhase += Time.deltaTime * 1.6f;
        float sweep = Mathf.Sin(wiperPhase) * 55f;
        float sign = Mathf.Sign(Mathf.Cos(wiperPhase));
        if (sign != lastSweepSign && wiperSqueak) { lastSweepSign = sign; AudioSource.PlayClipAtPoint(wiperSqueak, transform.TransformPoint(new Vector3(0.35f, 1.9f, 2.9f)), 0.35f); }
        wiper.localRotation = wiperRest * Quaternion.Euler(0f, 0f, sweep);
        float bladeX = 0.35f + Mathf.Sin(sweep * Mathf.Deg2Rad) * -0.5f; // roughly where the blade tip is across the screen
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var d = drops[i];
            if (d.pos.x > 0f && Mathf.Abs(d.pos.x - bladeX) < 0.06f) { Destroy(d.t.gameObject); drops.RemoveAt(i); }
        }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
        return null;
    }
}
