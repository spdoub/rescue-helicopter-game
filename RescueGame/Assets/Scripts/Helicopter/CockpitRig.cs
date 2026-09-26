using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Brings the helicopter interior to life. Lives on the Interior model (built by
/// tools/blender/build_interior.py) and uses its M_* marker empties to place:
/// working gauges (airspeed, attitude, altimeter, heading, climb, rotor RPM, fuel, temp, clock),
/// warning lights, radio displays, sticky notes, the cupholder energy drink, the bobblehead and the
/// cockpit glass. Moves the cyclic, collective and pedals with the pilot's controls, and exposes
/// grip points so the pilot's hands sit on them. Cosmetic only: runs the same on every machine.
/// </summary>
public class CockpitRig : MonoBehaviour
{
    [Header("Assets")]
    public Texture2D[] gaugeFaces;        // named Gauge_<Name>
    public Texture2D attitudeBall;
    public Texture2D[] notes;
    public Texture2D[] radios;
    public Material gaugeMaterial;        // Stylized Lit, unlit-ish (used as a template)
    public Material glassMaterial;
    public GameObject canPrefab;
    public GameObject bobbleheadPrefab;

    [Header("Control travel (degrees)")]
    public float cyclicTravel = 14f;
    public float collectiveTravel = 16f;
    public float pedalTravel = 0.05f;

    public Transform GripCyclic { get; private set; }
    public Transform GripCollective { get; private set; }

    static readonly Vector3 PanelNormal = new Vector3(0f, Mathf.Sin(18f * Mathf.Deg2Rad), -Mathf.Cos(18f * Mathf.Deg2Rad));

    Transform heli;
    Rigidbody heliBody;
    NetworkHelicopter netHeli;
    HelicopterController controller;
    Transform cyclic, collective, pedalL, pedalR;
    // rest poses stored relative to the helicopter, so the model's own axes don't matter
    Quaternion cyclicRest, collectiveRest;
    Vector3 pedalLRest, pedalRRest;

    class Needle { public Transform t; public System.Func<float> angle; public float smooth; public float current; }
    readonly List<Needle> needles = new();
    Transform headingCard;
    Mesh attitudeMesh; Vector3[] attitudeVerts; Vector2[] attitudeUV;
    readonly List<(Renderer r, Color on, System.Func<bool> lit)> lights = new();
    MaterialPropertyBlock mpb;
    Transform bobbleHead; Vector3 bobbleVel, bobbleOffset;

    Vector3 lastPos; float airspeed, climb;
    float fuel = 0.78f, temp = 0.2f, rpm;
    Vector4 smoothedControls;

    void Awake()
    {
        heli = GetComponentInParent<NetworkHelicopter>() ? GetComponentInParent<NetworkHelicopter>().transform : transform.parent;
        heliBody = heli.GetComponent<Rigidbody>();
        netHeli = heli.GetComponent<NetworkHelicopter>();
        controller = heli.GetComponent<HelicopterController>();
        mpb = new MaterialPropertyBlock();
        lastPos = heli.position;

        cyclic = Find("Cyclic"); collective = Find("Collective"); pedalL = Find("PedalL"); pedalR = Find("PedalR");
        if (cyclic) cyclicRest = Quaternion.Inverse(heli.rotation) * cyclic.rotation;
        if (collective) collectiveRest = Quaternion.Inverse(heli.rotation) * collective.rotation;
        if (pedalL) pedalLRest = heli.InverseTransformPoint(pedalL.position);
        if (pedalR) pedalRRest = heli.InverseTransformPoint(pedalR.position);
        GripCyclic = Find("M_GripCyclic");
        GripCollective = Find("M_GripCollective");

        BuildGauges();
        BuildLights();
        BuildNotesAndDisplays();
        BuildProps();
    }

    Transform Find(string n)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    // ------------------------------------------------------------------ building

    Material FlatMat(Texture tex, Color tint, float emission = 0.35f)
    {
        var m = new Material(gaugeMaterial);
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", tint);
        m.SetFloat("_FlatShading", 0f);
        m.SetFloat("_ShadowStrength", 0.2f);
        // A little self-light so instruments stay readable in the shade of the glareshield.
        m.SetColor("_EmissionColor", tint * emission * (tex != null ? 0.6f : 1f));
        return m;
    }

    static Mesh Disc(float radius, int segs = 40)
    {
        var v = new Vector3[segs + 1]; var uv = new Vector2[segs + 1]; var tri = new int[segs * 3];
        v[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < segs; i++)
        {
            float a = i * Mathf.PI * 2f / segs;
            v[i + 1] = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * radius;
            uv[i + 1] = new Vector2(0.5f + Mathf.Sin(a) * 0.5f, 0.5f + Mathf.Cos(a) * 0.5f);
            tri[i * 3] = 0; tri[i * 3 + 1] = i + 1; tri[i * 3 + 2] = (i + 1) % segs + 1;
        }
        var m = new Mesh { vertices = v, uv = uv, triangles = tri };
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Mesh NeedleMesh(float length, float width)
    {
        var v = new[] { new Vector3(-width, -length * 0.18f, 0), new Vector3(width, -length * 0.18f, 0), new Vector3(width * 0.35f, length, 0), new Vector3(-width * 0.35f, length, 0) };
        var m = new Mesh { vertices = v, uv = new Vector2[4], triangles = new[] { 0, 2, 1, 0, 3, 2 } };
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    GameObject Piece(string name, Transform parent, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    Transform GaugeFrame(string markerName)
    {
        var m = Find(markerName);
        if (m == null) return null;
        // Face the pilot: the marker is on the instrument panel, which leans back 18 degrees.
        m.rotation = heli.rotation * Quaternion.LookRotation(-PanelNormal, Vector3.up);
        return m;
    }

    Texture2D Face(string n)
    {
        foreach (var t in gaugeFaces) if (t && t.name == "Gauge_" + n) return t;
        return null;
    }

    void Dial(string name, float radius, System.Func<float> angle, float smooth = 6f)
    {
        var f = GaugeFrame("M_Gauge_" + name);
        if (f == null) return;
        Piece("Face", f, Disc(radius), FlatMat(Face(name), Color.white));
        var needle = Piece("Needle", f, NeedleMesh(radius * 0.85f, radius * 0.07f), FlatMat(null, new Color(1f, 0.55f, 0.15f), 0.6f));
        needle.transform.localPosition = new Vector3(0, 0, -0.004f);
        var cap = Piece("Cap", f, Disc(radius * 0.1f, 12), FlatMat(null, new Color(0.1f, 0.1f, 0.1f), 0.1f));
        cap.transform.localPosition = new Vector3(0, 0, -0.006f);
        needles.Add(new Needle { t = needle.transform, angle = angle, smooth = smooth });
    }

    void BuildGauges()
    {
        float R = 0.08f, r = 0.06f;
        Dial("Airspeed", R, () => Mathf.Clamp(airspeed * 1.944f, 0f, 160f) / 160f * 300f);           // m/s -> knots
        Dial("Altimeter", R, () => Mathf.Repeat(heli.position.y * 3.28f / 100f, 10f) / 10f * 360f);   // hundreds of feet
        Dial("VSI", R, () => -90f + Mathf.Clamp(climb * 196.85f / 100f, -20f, 20f) / 20f * 60f, 3f);  // fpm x100
        Dial("RPM", R, () => rpm / 120f * 275f, 2f);
        Dial("Fuel", r, () => -60f + fuel * 120f, 1f);
        Dial("Temp", r, () => -60f + temp * 120f, 1f);
        Dial("Clock", r, () => (float)System.DateTime.Now.TimeOfDay.TotalHours % 12f / 12f * 360f, 20f);

        // Heading: the whole card turns, a fixed orange marker points at your heading.
        var hf = GaugeFrame("M_Gauge_Heading");
        if (hf)
        {
            headingCard = Piece("Card", hf, Disc(R), FlatMat(Face("Heading"), Color.white)).transform;
            var mark = Piece("Lubber", hf, NeedleMesh(R * 0.3f, R * 0.08f), FlatMat(null, new Color(1f, 0.55f, 0.15f), 0.6f));
            mark.transform.localPosition = new Vector3(0, R * 0.62f, -0.004f);
        }

        // Attitude: a disc whose texture slides/rotates like a real artificial horizon.
        var af = GaugeFrame("M_Gauge_Attitude");
        if (af)
        {
            attitudeMesh = Disc(R, 48); attitudeMesh.MarkDynamic();
            attitudeVerts = attitudeMesh.vertices; attitudeUV = attitudeMesh.uv;
            Piece("Horizon", af, attitudeMesh, FlatMat(attitudeBall, Color.white));
            var plane = Piece("Aircraft", af, WingsMesh(R), FlatMat(null, new Color(1f, 0.55f, 0.15f), 0.6f));
            plane.transform.localPosition = new Vector3(0, 0, -0.005f);
        }

        // The co-pilot's broken gauge (under the tape): cracked face, needle stuck.
        var bf = GaugeFrame("M_Gauge_Broken");
        if (bf)
        {
            Piece("Face", bf, Disc(0.06f), FlatMat(Face("Temp"), new Color(0.6f, 0.6f, 0.6f), 0.1f));
            var n = Piece("Needle", bf, NeedleMesh(0.05f, 0.004f), FlatMat(null, new Color(1f, 0.55f, 0.15f), 0.3f));
            n.transform.localPosition = new Vector3(0, 0, -0.004f);
            n.transform.localRotation = Quaternion.Euler(0, 0, 150f);
        }
    }

    static Mesh WingsMesh(float R)
    {
        float w = R * 0.55f, h = R * 0.05f;
        var v = new[] { new Vector3(-w, -h, 0), new Vector3(-w * 0.25f, -h, 0), new Vector3(-w * 0.25f, h, 0), new Vector3(-w, h, 0),
                        new Vector3(w * 0.25f, -h, 0), new Vector3(w, -h, 0), new Vector3(w, h, 0), new Vector3(w * 0.25f, h, 0),
                        new Vector3(-h, -h * 2, 0), new Vector3(h, -h * 2, 0), new Vector3(0, h * 1.5f, 0) };
        var m = new Mesh { vertices = v, uv = new Vector2[v.Length], triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6, 8, 10, 9 } };
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    void BuildLights()
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        Color red = new(1f, 0.15f, 0.1f), amber = new(1f, 0.65f, 0.1f), green = new(0.3f, 1f, 0.35f);
        var colours = new[] { red, amber, amber, green, red, amber };
        System.Func<bool>[] rules =
        {
            () => fuel < 0.25f,                                            // low fuel
            () => Mathf.Repeat(Time.time, 7f) < 0.15f,                     // mystery light that blinks for no reason
            () => temp > 0.8f,                                             // hot engine
            () => controller != null && controller.engineOn,              // engine OK
            () => heli.up.y < 0.8f,                                        // bank angle warning
            () => heliBody != null && heliBody.position.y < 4f && airspeed > 8f, // low and fast
        };
        for (int k = 0; k < 6; k++)
        {
            var m = Find("M_Warn_" + k);
            if (m == null) continue;
            m.rotation = heli.rotation * Quaternion.LookRotation(-PanelNormal, Vector3.up);
            var go = Piece("Light", m, cube, FlatMat(null, colours[k] * 0.35f, 0f));
            go.transform.localScale = new Vector3(0.07f, 0.03f, 0.012f);
            lights.Add((go.GetComponent<Renderer>(), colours[k], rules[k]));
        }
        foreach (var (n, c) in new[] { ("M_WinchLightGreen", green), ("M_WinchLightRed", red), ("M_OverheadLight_0", amber), ("M_OverheadLight_1", green) })
        {
            var m = Find(n);
            if (m == null) continue;
            var go = Piece("Light", m, cube, FlatMat(null, c * 0.35f, 0f));
            go.transform.localScale = Vector3.one * 0.025f;
            bool isRed = n.Contains("Red");
            lights.Add((go.GetComponent<Renderer>(), c, isRed ? () => false : () => true));
        }
    }

    void BuildNotesAndDisplays()
    {
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        float[] tilts = { 8f, -6f, 4f, -3f };
        for (int k = 0; k < 4 && k < notes.Length; k++)
        {
            var m = Find("M_Note_" + k);
            if (m == null) continue;
            m.rotation = heli.rotation * Quaternion.LookRotation(-PanelNormal, Vector3.up) * Quaternion.Euler(0, 0, tilts[k]);
            var go = Piece("Note", m, quad, FlatMat(notes[k], Color.white, 0.15f));
            go.transform.localScale = new Vector3(0.095f, 0.095f, 1f); // quad's visible side already faces the pilot
        }
        for (int k = 0; k < 3 && k < radios.Length; k++)
        {
            var m = Find("M_RadioDisplay_" + k);
            if (m == null) continue;
            m.rotation = heli.rotation * Quaternion.Euler(84f, 0f, 0f); // lying on the pedestal, facing up
            var go = Piece("Display", m, quad, FlatMat(radios[k], Color.white, 0.8f));
            go.transform.localScale = new Vector3(0.11f, 0.04f, 1f);
        }
    }

    void BuildProps()
    {
        var canMarker = Find("M_Can");
        if (canMarker && canPrefab)
        {
            var can = Instantiate(canPrefab, canMarker);
            can.transform.localPosition = Vector3.zero;
            can.transform.localRotation = Quaternion.Euler(0, 200f, 0);
            can.transform.localScale = Vector3.one * 0.6f;
            foreach (var c in can.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var rb in can.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        }
        var bobMarker = Find("M_Bobblehead");
        if (bobMarker && bobbleheadPrefab)
        {
            var bob = Instantiate(bobbleheadPrefab, bobMarker);
            bob.transform.localPosition = Vector3.zero;
            bob.transform.localRotation = Quaternion.Euler(0, 180f, 0); // facing the pilot
            bob.transform.localScale = Vector3.one * 0.075f;
            foreach (var c in bob.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var rb in bob.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var r in bob.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var an = bob.GetComponent<Animator>();
            if (an) { bobbleHead = an.GetBoneTransform(HumanBodyBones.Head); an.enabled = false; }
            if (bobbleHead) bobbleHead.localScale = Vector3.one * 1.7f; // it's a BOBBLEhead
            PoseBobble(an, bob.transform);
            // a little base
            var stand = Piece("Stand", bobMarker, Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"), FlatMat(null, new Color(0.15f, 0.15f, 0.16f), 0f));
            stand.transform.localScale = new Vector3(0.05f, 0.004f, 0.05f);
        }
    }

    static void PoseBobble(Animator an, Transform root)
    {
        if (an == null) return;
        // Arms down from the T-pose so the figure looks like a proud little crewman.
        foreach (var (b, z) in new[] { (HumanBodyBones.LeftUpperArm, -70f), (HumanBodyBones.RightUpperArm, 70f) })
        {
            var t = an.GetBoneTransform(b);
            if (t) t.rotation = Quaternion.AngleAxis(z, root.forward) * t.rotation;
        }
    }

    void BuildGlass()
    {
        if (glassMaterial == null) return;
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        // windscreen panes follow the frame (bottom at the glareshield, top at the roof bar)
        foreach (var (name, x) in new[] { ("GlassL", -0.43f), ("GlassR", 0.43f) })
        {
            var go = Piece(name, transform, quad, glassMaterial);
            go.transform.position = heli.TransformPoint(new Vector3(x, 2.01f, 2.675f));
            go.transform.rotation = heli.rotation * Quaternion.Euler(-34f, 0, 0);
            go.transform.localScale = new Vector3(0.78f, 0.64f, 1f);
        }
        foreach (var (name, x) in new[] { ("SideGlassL", -0.9f), ("SideGlassR", 0.9f) })
        {
            var go = Piece(name, transform, quad, glassMaterial);
            go.transform.position = heli.TransformPoint(new Vector3(x, 1.84f, 2.12f));
            go.transform.rotation = heli.rotation * Quaternion.Euler(0, 90f, 0);
            go.transform.localScale = new Vector3(1.2f, 0.8f, 1f);
        }
    }

    // ------------------------------------------------------------------ running

    void LateUpdate()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 v = (heli.position - lastPos) / dt; lastPos = heli.position;
        Vector3 flat = v; flat.y = 0f;
        airspeed = Mathf.Lerp(airspeed, flat.magnitude, 1f - Mathf.Exp(-3f * dt));
        climb = Mathf.Lerp(climb, v.y, 1f - Mathf.Exp(-2f * dt));

        bool engine = controller == null || controller.engineOn;
        bool landedIdle = controller != null && controller.IsLanded;
        rpm = Mathf.MoveTowards(rpm, engine ? (landedIdle ? 72f : 100f) + Mathf.Sin(Time.time * 7f) * 0.8f : 0f, 25f * dt);
        temp = Mathf.MoveTowards(temp, engine ? (landedIdle ? 0.45f : 0.6f) : 0.1f, 0.02f * dt);
        if (engine && !landedIdle) fuel = Mathf.Max(0.05f, fuel - 0.0015f * dt); // placeholder until fuel is a real system

        foreach (var n in needles)
        {
            n.current = Mathf.LerpAngle(n.current, n.angle(), 1f - Mathf.Exp(-n.smooth * dt));
            n.t.localRotation = Quaternion.Euler(0, 0, -n.current);
        }
        if (headingCard) headingCard.localRotation = Quaternion.Euler(0, 0, heli.eulerAngles.y);
        UpdateAttitude();

        foreach (var (r, on, lit) in lights)
        {
            r.GetPropertyBlock(mpb);
            bool isOn = lit();
            mpb.SetColor("_BaseColor", isOn ? on : on * 0.25f);
            mpb.SetColor("_EmissionColor", isOn ? on * 1.6f : Color.black);
            r.SetPropertyBlock(mpb);
        }

        UpdateControls(dt);
        UpdateBobble(dt);
    }

    void UpdateAttitude()
    {
        if (attitudeMesh == null) return;
        float pitch = Mathf.DeltaAngle(0f, heli.eulerAngles.x);   // + = nose down
        float roll = Mathf.DeltaAngle(0f, heli.eulerAngles.z);    // + = left wing down
        float shift = -pitch / 180f;                               // texture: 1 unit = 180 degrees
        Quaternion rot = Quaternion.Euler(0, 0, -roll);
        for (int i = 0; i < attitudeVerts.Length; i++)
        {
            Vector3 p = rot * (attitudeVerts[i] / (0.08f * 2f));
            attitudeUV[i] = new Vector2(0.5f + p.x * 0.5f, 0.5f + p.y * 0.25f + shift);
        }
        attitudeMesh.uv = attitudeUV;
    }

    void UpdateControls(float dt)
    {
        Vector4 target = netHeli != null && netHeli.IsSpawned ? netHeli.ControlsSync.Value
            : controller != null ? new Vector4(controller.CurrentInput.collective, controller.CurrentInput.pitch, controller.CurrentInput.roll, controller.CurrentInput.yaw)
            : Vector4.zero;
        smoothedControls = Vector4.Lerp(smoothedControls, target, 1f - Mathf.Exp(-12f * dt));
        var c = smoothedControls;
        // stick forward (pitch +) tips the top forward, stick right (roll +) tips it right
        if (cyclic) cyclic.rotation = heli.rotation * Quaternion.Euler(c.y * cyclicTravel, 0f, -c.z * cyclicTravel) * cyclicRest;
        // collective up raises the lever's front end
        if (collective) collective.rotation = heli.rotation * Quaternion.Euler(-c.x * collectiveTravel, 0f, 0f) * collectiveRest;
        if (pedalL) pedalL.position = heli.TransformPoint(pedalLRest + Vector3.forward * (c.w * pedalTravel));
        if (pedalR) pedalR.position = heli.TransformPoint(pedalRRest - Vector3.forward * (c.w * pedalTravel));
    }

    void UpdateBobble(float dt)
    {
        if (bobbleHead == null) return;
        // The head is a spring that the helicopter's motion keeps knocking about.
        Vector3 accel = heliBody != null ? heli.InverseTransformDirection(heliBody.linearVelocity - lastVel) / dt : Vector3.zero;
        lastVel = heliBody != null ? heliBody.linearVelocity : Vector3.zero;
        accel = Vector3.ClampMagnitude(accel, 30f);
        bobbleVel += (-accel * 0.6f - bobbleOffset * 60f - bobbleVel * 3f) * dt;
        bobbleVel += new Vector3(Mathf.Sin(Time.time * 17f), 0, Mathf.Cos(Time.time * 13f)) * (rpm / 100f) * 0.6f * dt; // engine buzz
        bobbleOffset += bobbleVel * dt;
        bobbleOffset = Vector3.ClampMagnitude(bobbleOffset, 0.6f);
        bobbleHead.localRotation = bobbleHeadRest * Quaternion.Euler(bobbleOffset.z * 40f, 0f, -bobbleOffset.x * 40f);
    }

    Vector3 lastVel;
    Quaternion bobbleHeadRest = Quaternion.identity;

    void Start()
    {
        if (bobbleHead) bobbleHeadRest = bobbleHead.localRotation;
    }
}
