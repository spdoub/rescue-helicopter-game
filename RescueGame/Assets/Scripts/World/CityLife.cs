using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Makes the city feel lived-in (every machine builds the same thing from the seed; movement is a
/// function of the shared clock, so nothing needs syncing):
///  - Traffic: boxy cars drive the street grid in both lanes, turning round at the ends of each clear
///    stretch, headlights on for the golden hour.
///  - People: lumpy pedestrians walk loops around the buildings' pavements with a bouncy stride.
///  - Street furniture: crosswalks, traffic lights, hydrants, bins, benches, manholes, steam vents.
///  - Sound: a distant traffic hum that fades out as you climb.
/// Runs after CityGenerator (it needs the buildings), cosmetic only: no colliders.
/// </summary>
[DefaultExecutionOrder(-400)]
public class CityLife : MonoBehaviour
{
    public Material paintMaterial;     // Stylized Lit, white; tinted per car/person
    public Material smokeMaterial;     // for the steam
    public AudioClip ambience, horn;
    public int cars = 44, peopleMax = 220;
    public float laneOffset = 1.6f;

    class Car { public Transform t; public Transform[] wheels; public int axis; public float line, start, len, speed, phase; public bool parkedLook; float yaw; public float Yaw { get => yaw; set => yaw = value; } }
    class Person { public Transform t, lArm, rArm, lLeg, rLeg, body; public Vector3 c; public float yawB, hw, hd, speed, phase, height; public bool cw; }

    readonly List<Car> carsList = new();
    readonly List<Person> people = new();
    System.Random rnd;
    Transform env, city;
    AudioSource hum;
    Transform cam, heli;
    float nextHorn;

    static readonly Color[] CarColours =
    {
        new(0.85f, 0.82f, 0.75f), new(0.2f, 0.22f, 0.26f), new(0.7f, 0.15f, 0.12f), new(0.2f, 0.35f, 0.6f), new(0.9f, 0.75f, 0.2f),
        new(0.35f, 0.5f, 0.35f), new(0.55f, 0.55f, 0.58f), new(0.95f, 0.5f, 0.15f), new(0.25f, 0.25f, 0.28f), new(0.8f, 0.8f, 0.82f),
    };
    static readonly Color[] Shirts = { new(0.9f, 0.3f, 0.25f), new(0.25f, 0.4f, 0.7f), new(0.95f, 0.8f, 0.3f), new(0.3f, 0.6f, 0.4f), new(0.9f, 0.9f, 0.85f), new(0.6f, 0.3f, 0.6f), new(0.95f, 0.55f, 0.2f), new(0.2f, 0.2f, 0.25f) };
    static readonly Color[] Trousers = { new(0.2f, 0.25f, 0.4f), new(0.25f, 0.22f, 0.2f), new(0.5f, 0.45f, 0.4f), new(0.15f, 0.15f, 0.18f) };
    static readonly Color[] Skins = { new(0.95f, 0.8f, 0.68f), new(0.85f, 0.65f, 0.5f), new(0.6f, 0.42f, 0.3f), new(0.42f, 0.28f, 0.2f) };
    static readonly Color Dark = new(0.12f, 0.12f, 0.14f), Lamp = new(1f, 0.95f, 0.7f), Tail = new(0.9f, 0.15f, 0.1f), Glass = new(0.35f, 0.42f, 0.5f);

    void Start()
    {
        rnd = new System.Random(CityGenerator.Current ? CityGenerator.Current.seed + 77 : 77);
        env = GameObject.Find("Environment")?.transform;
        city = CityGenerator.Current ? CityGenerator.Current.transform : transform;
        BuildTraffic();
        BuildPeople();
        BuildFurniture();
        BuildBirds();
        if (ambience)
        {
            hum = gameObject.AddComponent<AudioSource>();
            hum.clip = ambience; hum.loop = true; hum.spatialBlend = 0f; hum.volume = 0.22f; hum.Play();
        }
    }

    static double Now => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

    // ------------------------------------------------------------------ streets

    /// <summary>Is this point on open street (not under a building, the pad or the hospital)?</summary>
    bool Clear(Vector2 p)
    {
        var hosp = GameObject.Find("Hospital");
        Vector2 hospC = hosp ? new Vector2(hosp.transform.position.x, hosp.transform.position.z) : new Vector2(9999f, 9999f);
        return p.magnitude > 19f && Vector2.Distance(p, hospC) > 14f && p.magnitude < 168f && !CityGenerator.UnderBlock(env, p);
    }

    /// <summary>Clear stretches along one lane line (axis 0: the street runs along x at z = line; 1: along z at x = line).</summary>
    List<(float start, float len)> Stretches(int axis, float line, float lane)
    {
        var res = new List<(float, float)>();
        float runStart = float.NaN;
        for (float t = -168f; t <= 168f; t += 2f)
        {
            Vector2 p = axis == 0 ? new Vector2(t, line + lane) : new Vector2(line + lane, t);
            bool ok = Clear(p);
            if (ok && float.IsNaN(runStart)) runStart = t;
            if ((!ok || t >= 168f) && !float.IsNaN(runStart)) { float end = ok ? t : t - 2f; if (end - runStart >= 40f) res.Add((runStart + 4f, end - runStart - 8f)); runStart = float.NaN; }
        }
        return res;
    }

    void BuildTraffic()
    {
        var root = new GameObject("Traffic").transform; root.SetParent(transform, false);
        var options = new List<(int axis, float line, float start, float len, int dir)>();
        for (int k = -7; k <= 6; k++)
        {
            float line = (k + 0.5f) * CityGenerator.StreetPitch;
            foreach (int axis in new[] { 0, 1 })
                foreach (int dir in new[] { 1, -1 })
                {
                    // right-hand traffic: the lane sits to the right of the direction of travel
                    float lane = axis == 0 ? -dir * laneOffset : dir * laneOffset;
                    foreach (var (s, l) in Stretches(axis, line, lane)) options.Add((axis, line, s, l, dir));
                }
        }
        if (options.Count == 0) return;
        for (int i = 0; i < cars; i++)
        {
            var o = options[rnd.Next(options.Count)];
            var car = MakeCar(root, CarColours[rnd.Next(CarColours.Length)], rnd.Next(4) == 0);
            car.axis = o.axis; car.line = o.line; car.start = o.start; car.len = o.len;
            car.speed = 7f + (float)rnd.NextDouble() * 5f;
            car.phase = (float)rnd.NextDouble() * 1000f;
            car.Yaw = o.dir > 0 ? (o.axis == 0 ? 90f : 0f) : (o.axis == 0 ? -90f : 180f);
            carsList.Add(car);
        }
    }

    Car MakeCar(Transform parent, Color paint, bool van)
    {
        var go = new GameObject("Car"); go.transform.SetParent(parent, false);
        float L = van ? 4.6f : 4.1f, W = 1.75f, bodyH = van ? 1.1f : 0.6f;
        Part(go.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.32f + bodyH / 2f, 0f), new Vector3(W, bodyH, L), paint);
        if (van) Part(go.transform, PrimitiveType.Cube, "Cabin", new Vector3(0f, 1.42f + 0.35f, 0.1f), new Vector3(W - 0.05f, 0.7f, L - 0.6f), paint);
        else Part(go.transform, PrimitiveType.Cube, "Cabin", new Vector3(0f, 0.92f + 0.3f, -0.25f), new Vector3(W - 0.25f, 0.6f, 2.0f), paint);
        // windows (a dark band around the cabin)
        float wy = van ? 2.0f : 1.25f, wh = van ? 0.4f : 0.42f, wz = van ? 0.1f : -0.25f, wl = van ? L - 0.7f : 1.9f;
        Part(go.transform, PrimitiveType.Cube, "Glass", new Vector3(0f, wy, wz), new Vector3(W - 0.15f + 0.04f, wh, wl - 0.5f), Glass);
        Part(go.transform, PrimitiveType.Cube, "Windscreen", new Vector3(0f, wy, wz + wl / 2f - 0.2f), new Vector3(W - 0.5f, wh, 0.45f), Glass);
        var wheels = new Transform[4];
        int k = 0;
        foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1.35f, 1.35f })
        {
            var w = Part(go.transform, PrimitiveType.Cylinder, "Wheel", new Vector3(sx * (W / 2f - 0.05f), 0.34f, sz), new Vector3(0.68f, 0.12f, 0.68f), Dark);
            w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheels[k++] = w.transform;
        }
        foreach (var sx in new[] { -0.6f, 0.6f })
        {
            Part(go.transform, PrimitiveType.Cube, "Headlight", new Vector3(sx, 0.7f, L / 2f + 0.005f), new Vector3(0.32f, 0.16f, 0.02f), Lamp);
            Part(go.transform, PrimitiveType.Cube, "Taillight", new Vector3(sx, 0.7f, -L / 2f - 0.005f), new Vector3(0.32f, 0.14f, 0.02f), Tail);
        }
        if (!van && paint == CarColours[4]) // the yellow ones are taxis
        {
            var sign = Part(go.transform, PrimitiveType.Cube, "TaxiSign", new Vector3(0f, 1.62f, -0.25f), new Vector3(0.5f, 0.16f, 0.2f), Lamp);
            Part(sign.transform, PrimitiveType.Cube, "TaxiSignStripe", new Vector3(0f, 0f, 0.51f), new Vector3(0.9f, 0.5f, 0.05f), Dark);
        }
        Part(go.transform, PrimitiveType.Cube, "Bumper", new Vector3(0f, 0.42f, L / 2f), new Vector3(W, 0.14f, 0.1f), Dark);
        Part(go.transform, PrimitiveType.Cube, "BumperB", new Vector3(0f, 0.42f, -L / 2f), new Vector3(W, 0.14f, 0.1f), Dark);
        return new Car { t = go.transform, wheels = wheels };
    }

    // ------------------------------------------------------------------ people

    void BuildPeople()
    {
        var root = new GameObject("People").transform; root.SetParent(transform, false);
        var buildings = new List<(Vector3 c, float yaw, float hw, float hd)>();
        foreach (Transform b in city)
        {
            if (!b.name.StartsWith("Filler_") && !b.name.StartsWith("Building_")) continue;
            var col = b.GetComponentInChildren<BoxCollider>();
            var r = b.GetComponent<Renderer>();
            if (r == null) continue;
            // footprint from the renderer bounds in the building's own frame
            Vector3 size = b.InverseTransformVector(r.bounds.size); // (approximate for rotated ones: fine for a pavement loop)
            var bb = r.bounds;
            float hw = 0f, hd = 0f;
            foreach (var mc in b.GetComponentsInChildren<BoxCollider>())
                if (mc.size.y > 4f) { hw = Mathf.Max(hw, mc.size.x / 2f); hd = Mathf.Max(hd, mc.size.z / 2f); }
            if (hw < 3f) { hw = Mathf.Abs(size.x) / 2f - 0.3f; hd = Mathf.Abs(size.z) / 2f - 0.3f; }
            if (new Vector2(b.position.x, b.position.z).magnitude < 24f) continue; // nobody loiters on the helipad
            buildings.Add((new Vector3(b.position.x, 0f, b.position.z), b.eulerAngles.y, hw, hd));
        }
        int made = 0;
        foreach (var (c, yaw, hw, hd) in buildings)
        {
            int n = 1 + rnd.Next(3);
            for (int i = 0; i < n && made < peopleMax; i++, made++)
            {
                var p = MakePerson(root);
                p.c = c; p.yawB = yaw; p.hw = hw + 0.9f; p.hd = hd + 0.9f;
                p.speed = 0.9f + (float)rnd.NextDouble() * 0.6f;
                p.phase = (float)rnd.NextDouble() * 500f;
                p.cw = rnd.Next(2) == 0;
                people.Add(p);
            }
        }
    }

    Person MakePerson(Transform parent)
    {
        var go = new GameObject("Person"); go.transform.SetParent(parent, false);
        float h = 1.45f + (float)rnd.NextDouble() * 0.35f;
        var skin = Skins[rnd.Next(Skins.Length)]; var shirt = Shirts[rnd.Next(Shirts.Length)]; var trous = Trousers[rnd.Next(Trousers.Length)];
        var body = new GameObject("Body").transform; body.SetParent(go.transform, false);
        float legH = h * 0.45f, torsoH = h * 0.35f, headR = h * 0.09f;
        var lLeg = Part(body, PrimitiveType.Capsule, "LegL", new Vector3(-0.09f, legH / 2f, 0f), new Vector3(0.16f, legH / 2f, 0.16f), trous).transform;
        var rLeg = Part(body, PrimitiveType.Capsule, "LegR", new Vector3(0.09f, legH / 2f, 0f), new Vector3(0.16f, legH / 2f, 0.16f), trous).transform;
        Part(body, PrimitiveType.Capsule, "Torso", new Vector3(0f, legH + torsoH / 2f, 0f), new Vector3(0.42f, torsoH / 2f + 0.06f, 0.3f), shirt);
        var lArm = Part(body, PrimitiveType.Capsule, "ArmL", new Vector3(-0.27f, legH + torsoH * 0.55f, 0f), new Vector3(0.12f, torsoH * 0.45f, 0.12f), shirt).transform;
        var rArm = Part(body, PrimitiveType.Capsule, "ArmR", new Vector3(0.27f, legH + torsoH * 0.55f, 0f), new Vector3(0.12f, torsoH * 0.45f, 0.12f), shirt).transform;
        Part(body, PrimitiveType.Sphere, "Head", new Vector3(0f, legH + torsoH + headR * 1.05f, 0f), Vector3.one * headR * 2.1f, skin);
        if (rnd.Next(3) == 0) Part(body, PrimitiveType.Cylinder, "Hat", new Vector3(0f, legH + torsoH + headR * 1.9f, 0f), new Vector3(headR * 2.4f, 0.03f, headR * 2.4f), Trousers[rnd.Next(Trousers.Length)]);
        // legs and arms pivot at the hip/shoulder: put their meshes' pivots there
        foreach (var limb in new[] { lLeg, rLeg })
        {
            var pivot = new GameObject(limb.name + "Pivot").transform; pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(limb.localPosition.x, legH, 0f);
            limb.SetParent(pivot, true);
        }
        foreach (var limb in new[] { lArm, rArm })
        {
            var pivot = new GameObject(limb.name + "Pivot").transform; pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(limb.localPosition.x, legH + torsoH * 0.85f, 0f);
            limb.SetParent(pivot, true);
        }
        return new Person { t = go.transform, body = body, lLeg = lLeg.parent, rLeg = rLeg.parent, lArm = lArm.parent, rArm = rArm.parent, height = h };
    }

    // ------------------------------------------------------------------ furniture

    void BuildFurniture()
    {
        var gen = CityGenerator.Current;
        if (gen == null) return;
        var kit = new CityGenerator.Kit(10);
        const int TrimLight = 1, TrimDark = 2, Metal = 7, Wood = 8, Accent = 9, GlassLit = 6;
        float pitch = CityGenerator.StreetPitch;
        var steamSpots = new List<Vector3>();
        for (int kx = -7; kx <= 6; kx++)
            for (int kz = -7; kz <= 6; kz++)
            {
                float x = (kx + 0.5f) * pitch, z = (kz + 0.5f) * pitch;
                var c = new Vector2(x, z);
                if (!Clear(c) || !Clear(c + Vector2.right * 6f) || !Clear(c - Vector2.right * 6f) || !Clear(c + Vector2.up * 6f) || !Clear(c - Vector2.up * 6f)) continue;
                // crosswalk stripes on each approach
                foreach (var (dx, dz) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
                {
                    for (int s = -3; s <= 3; s++)
                    {
                        Vector3 centre = new Vector3(x + dx * 4.6f + dz * s * 0.85f, 0.012f, z + dz * 4.6f + dx * s * 0.85f);
                        kit.Box(TrimLight, centre, new Vector3(dx != 0 ? 2.2f : 0.45f, 0.02f, dz != 0 ? 2.2f : 0.45f));
                    }
                }
                // traffic lights on two diagonal corners
                if (((kx + kz) & 1) == 0)
                    foreach (var sgn in new[] { 1f, -1f })
                    {
                        Vector3 b = new Vector3(x + sgn * 3.6f, 0f, z - sgn * 3.6f);
                        kit.Cylinder(Metal, b, 0.08f, 5f, 6);
                        kit.Box(Metal, b + new Vector3(-sgn * 1.6f, 4.9f, 0f), new Vector3(3.2f, 0.08f, 0.08f));
                        Vector3 box = b + new Vector3(-sgn * 3.0f, 4.45f, 0f);
                        kit.Box(TrimDark, box, new Vector3(0.3f, 0.9f, 0.3f));
                        bool green = ((kx * 3 + kz) & 1) == 0;
                        kit.Box(green ? GlassLit : Accent, box + new Vector3(0f, green ? -0.28f : 0.28f, -0.16f), new Vector3(0.18f, 0.18f, 0.02f));
                    }
                // a manhole, and now and then a steam vent
                Vector3 mh = new Vector3(x + 2.5f, 0.005f, z + 8f);
                kit.Cylinder(TrimDark, mh, 0.45f, 0.02f, 10);
                if (rnd.Next(4) == 0) steamSpots.Add(mh + Vector3.up * 0.05f);
            }
        // hydrants, bins and benches on the pavements around buildings
        foreach (Transform b in city)
        {
            if (!b.name.StartsWith("Filler_")) continue;
            float hw = 0f, hd = 0f;
            foreach (var mc in b.GetComponentsInChildren<BoxCollider>()) if (mc.size.y > 4f) { hw = Mathf.Max(hw, mc.size.x / 2f); hd = Mathf.Max(hd, mc.size.z / 2f); }
            if (hw < 3f || new Vector2(b.position.x, b.position.z).magnitude < 24f) continue;
            int face = rnd.Next(4);
            Vector3 n = face == 0 ? Vector3.back : face == 1 ? Vector3.right : face == 2 ? Vector3.forward : Vector3.left;
            Vector3 along = Vector3.Cross(Vector3.up, n);
            float half = face % 2 == 0 ? hw : hd, depth = face % 2 == 0 ? hd : hw;
            Vector3 origin = b.TransformPoint(n * (depth + 0.75f));
            Vector3 alongW = b.TransformDirection(along), nW = b.TransformDirection(n);
            switch (rnd.Next(3))
            {
                case 0: // hydrant
                {
                    Vector3 p = origin + alongW * (half - 1.2f);
                    kit.Cylinder(Accent, p, 0.14f, 0.7f, 8);
                    kit.Cylinder(Accent, p + Vector3.up * 0.7f, 0.1f, 0.15f, 8);
                    kit.Box(Accent, p + Vector3.up * 0.45f, new Vector3(0.5f, 0.14f, 0.14f));
                    break;
                }
                case 1: // bin
                {
                    Vector3 p = origin + alongW * (-half + 1.0f);
                    kit.Cylinder(TrimDark, p, 0.32f, 0.95f, 8);
                    kit.Cylinder(Metal, p + Vector3.up * 0.95f, 0.34f, 0.06f, 8);
                    break;
                }
                default: // bench
                {
                    Vector3 p = origin + nW * -0.2f;
                    kit.BoxRot(Wood, p + Vector3.up * 0.45f, new Vector3(1.8f, 0.06f, 0.42f), Quaternion.LookRotation(nW));
                    kit.BoxRot(Wood, p + Vector3.up * 0.75f - nW * 0.2f, new Vector3(1.8f, 0.4f, 0.05f), Quaternion.LookRotation(nW));
                    foreach (var e in new[] { -0.8f, 0.8f }) kit.BoxRot(Metal, p + alongW * e + Vector3.up * 0.22f, new Vector3(0.06f, 0.44f, 0.4f), Quaternion.LookRotation(nW));
                    break;
                }
            }
        }
        // parked cars along kerbs that have room for them (facing the way that lane runs)
        var parked = new GameObject("ParkedCars").transform; parked.SetParent(transform, false);
        var footprints = new List<Bounds>();
        foreach (Transform b in city) { var r = b.GetComponent<Renderer>(); if (r && (b.name.StartsWith("Filler_") || b.name.StartsWith("Building_"))) footprints.Add(r.bounds); }
        var hospT = GameObject.Find("Hospital");
        Vector2 hospP = hospT ? new Vector2(hospT.transform.position.x, hospT.transform.position.z) : new Vector2(9999f, 9999f);
        bool Spot(Vector3 p, Bounds mine)
        {
            var q = new Vector2(p.x, p.z);
            if (q.magnitude < 22f || Vector2.Distance(q, hospP) > 0f && Vector2.Distance(q, hospP) < 16f || q.magnitude > 165f) return false;
            foreach (var f in footprints) { if (f == mine) continue; if (p.x > f.min.x - 1f && p.x < f.max.x + 1f && p.z > f.min.z - 1f && p.z < f.max.z + 1f) return false; }
            return true;
        }
        foreach (Transform b in city)
        {
            if (!b.name.StartsWith("Filler_")) continue;
            var rend = b.GetComponent<Renderer>(); if (!rend) continue;
            float hw = 0f, hd = 0f;
            foreach (var mc in b.GetComponentsInChildren<BoxCollider>()) if (mc.size.y > 4f) { hw = Mathf.Max(hw, mc.size.x / 2f); hd = Mathf.Max(hd, mc.size.z / 2f); }
            if (hw < 3f) continue;
            foreach (var n in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                float half = Mathf.Abs(n.x) > 0.5f ? hw : hd, along = Mathf.Abs(n.x) > 0.5f ? hd : hw;
                Vector3 e = b.TransformPoint(n * half), nW = b.TransformDirection(n), aW = Vector3.Cross(Vector3.up, nW);
                if (Mathf.Abs(Mathf.Abs(nW.x) - Mathf.Abs(nW.z)) < 0.5f) continue;             // a face on the diagonal: skip
                bool xStreet = Mathf.Abs(nW.x) > 0.7f;
                float coord = xStreet ? e.x : e.z, sgn = Mathf.Sign(xStreet ? nW.x : nW.z);
                // the street's centre line on this side of the building
                float line = (Mathf.Round(coord / pitch - 0.5f * sgn) + 0.5f * sgn) * pitch;
                if ((line - coord) * sgn < 0f) line += sgn * pitch;
                float clearance = (line - coord) * sgn;                                             // wall -> lane centre
                if (clearance < 6.2f) continue;                                                     // kerb 1.5 + car 1.8 + gap 1.6 + lane
                float yaw = xStreet ? (sgn > 0 ? 180f : 0f) : (sgn > 0 ? 90f : -90f);
                for (float t = -along + 2.6f; t <= along - 2.6f; t += 5.6f)
                {
                    if (rnd.NextDouble() > 0.55) continue;
                    Vector3 p = e + nW * 2.6f + aW * t;
                    if (!Spot(p, rend.bounds)) continue;
                    var car = MakeCar(parked, CarColours[rnd.Next(CarColours.Length)], rnd.Next(5) == 0);
                    car.t.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw + (float)(rnd.NextDouble() - 0.5) * 4f, 0f));
                }
            }
        }
        var go = new GameObject("Furniture"); go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("Furniture");
        var mats = gen.MatsFor("Brick_Red"); mats[TrimLight] = gen.roadPaint;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        foreach (var s in steamSpots) MakeSteam(s);
    }

    void MakeSteam(Vector3 at)
    {
        var go = new GameObject("Steam"); go.transform.SetParent(transform, false); go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f); main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f); main.maxParticles = 60;
        main.startColor = new Color(0.9f, 0.88f, 0.85f, 0.35f); main.gravityModifier = -0.05f;
        var em = ps.emission; em.rateOverTime = 9f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.3f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = smokeMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
    }

    // ------------------------------------------------------------------ birds

    class Flock { public Transform[] birds; public Transform[] wings; public Vector3 centre; public float radius, height, speed, phase; }
    readonly List<Flock> flocks = new();

    void BuildBirds()
    {
        var root = new GameObject("Birds").transform; root.SetParent(transform, false);
        var grey = new Color(0.4f, 0.4f, 0.45f);
        for (int f = 0; f < 4; f++)
        {
            var fl = new Flock
            {
                centre = new Vector3((float)(rnd.NextDouble() - 0.5) * 220f, 0f, (float)(rnd.NextDouble() - 0.5) * 220f),
                radius = 25f + (float)rnd.NextDouble() * 30f, height = 30f + (float)rnd.NextDouble() * 25f,
                speed = 9f + (float)rnd.NextDouble() * 4f, phase = (float)rnd.NextDouble() * 100f,
            };
            int n = 6 + rnd.Next(6);
            fl.birds = new Transform[n]; fl.wings = new Transform[n * 2];
            for (int i = 0; i < n; i++)
            {
                var b = new GameObject("Bird").transform; b.SetParent(root, false);
                Part(b, PrimitiveType.Cube, "Body", Vector3.zero, new Vector3(0.12f, 0.1f, 0.35f), grey);
                var wl = Part(b, PrimitiveType.Cube, "WingL", new Vector3(-0.3f, 0f, 0f), new Vector3(0.55f, 0.03f, 0.2f), grey).transform;
                var wr = Part(b, PrimitiveType.Cube, "WingR", new Vector3(0.3f, 0f, 0f), new Vector3(0.55f, 0.03f, 0.2f), grey).transform;
                fl.birds[i] = b; fl.wings[i * 2] = wl; fl.wings[i * 2 + 1] = wr;
            }
            flocks.Add(fl);
        }
        foreach (var fl in flocks) WindscreenSplat.RegisterBirds(fl.birds);
    }

    void UpdateBirds(double T)
    {
        foreach (var fl in flocks)
        {
            float a = (float)(T * fl.speed / fl.radius) + fl.phase;
            for (int i = 0; i < fl.birds.Length; i++)
            {
                // a loose V behind the leader, each bird bobbing on its own
                float ai = a - i * 0.06f;
                Vector3 p = fl.centre + new Vector3(Mathf.Cos(ai) * fl.radius, fl.height + Mathf.Sin(ai * 2.3f + i) * 1.5f + (i % 2 == 0 ? i * 0.4f : -i * 0.4f), Mathf.Sin(ai) * fl.radius)
                          + new Vector3(Mathf.Sin(ai) , 0f, -Mathf.Cos(ai)) * ((i % 2 == 0 ? 1 : -1) * i * 0.9f);
                Vector3 fwd = new Vector3(-Mathf.Sin(ai), 0f, Mathf.Cos(ai));
                fl.birds[i].SetPositionAndRotation(p, Quaternion.LookRotation(fwd));
                float flap = Mathf.Sin((float)(T * 7.0) + i * 1.3f) * 35f;
                fl.wings[i * 2].localRotation = Quaternion.Euler(0f, 0f, flap);
                fl.wings[i * 2 + 1].localRotation = Quaternion.Euler(0f, 0f, -flap);
            }
        }
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        double T = Now;
        if (cam == null && Camera.main) cam = Camera.main.transform;
        Vector3 camPos = cam ? cam.position : Vector3.zero;
        if (heli == null) { var h = FindAnyObjectByType<NetworkHelicopter>(); if (h) heli = h.transform; }
        Vector3 heliPos = heli ? heli.position : new Vector3(0f, 9999f, 0f);
        bool heliLow = heliPos.y < 22f;

        foreach (var c in carsList)
        {
            // sinusoidal ping-pong along the stretch: slows, turns round, comes back
            float u = (float)((c.phase + T * c.speed / c.len * 0.5) % 1.0);            // 0..1 over a there-and-back
            float along = c.len * (1f - Mathf.Cos(u * Mathf.PI * 2f)) / 2f;
            bool forward = u < 0.5f;
            float s = c.start + along;
            Vector3 p = c.axis == 0 ? new Vector3(s, 0f, c.line) : new Vector3(c.line, 0f, s);
            int dir = forward ? 1 : -1;
            float lane = c.axis == 0 ? -dir * laneOffset : dir * laneOffset;
            p += c.axis == 0 ? new Vector3(0f, 0f, lane) : new Vector3(lane, 0f, 0f);
            float targetYaw = dir > 0 ? (c.axis == 0 ? 90f : 0f) : (c.axis == 0 ? -90f : 180f);
            c.Yaw = Mathf.MoveTowardsAngle(c.Yaw, targetYaw, 240f * Time.deltaTime);
            c.t.SetPositionAndRotation(p, Quaternion.Euler(0f, c.Yaw, 0f));
            float spin = c.speed * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2f)) * Time.deltaTime / 0.34f * Mathf.Rad2Deg * dir;
            foreach (var w in c.wheels) w.Rotate(0f, spin, 0f, Space.Self);
            // a helicopter thundering over at rooftop height gets honked at
            if (horn && heliLow && Time.time > nextHorn && (p - heliPos).sqrMagnitude < 28f * 28f && (p - camPos).sqrMagnitude < 90f * 90f)
            {
                nextHorn = Time.time + 2.5f + (float)rnd.NextDouble() * 4f;
                AudioSource.PlayClipAtPoint(horn, p + Vector3.up, 0.7f);
            }
        }

        foreach (var pn in people)
        {
            // only the ones near the camera are animated and shown (the rest sleep)
            bool near = (pn.c - camPos).sqrMagnitude < 110f * 110f;
            if (pn.t.gameObject.activeSelf != near) pn.t.gameObject.SetActive(near);
            if (!near) continue;
            float per = 4f * (pn.hw + pn.hd);
            float s = (float)((pn.phase + T * pn.speed) % per);
            if (pn.cw) s = per - s;
            // walk the rectangle: right edge (+x) up +z, top edge (+z) toward -x, left edge down, bottom edge toward +x
            Vector3 local; float yaw;
            if (s < 2f * pn.hd) { local = new Vector3(pn.hw, 0f, -pn.hd + s); yaw = 0f; }
            else if (s < 2f * pn.hd + 2f * pn.hw) { local = new Vector3(pn.hw - (s - 2f * pn.hd), 0f, pn.hd); yaw = -90f; }
            else if (s < 4f * pn.hd + 2f * pn.hw) { local = new Vector3(-pn.hw, 0f, pn.hd - (s - 2f * pn.hd - 2f * pn.hw)); yaw = 180f; }
            else { local = new Vector3(-pn.hw + (s - 4f * pn.hd - 2f * pn.hw), 0f, -pn.hd); yaw = 90f; }
            if (pn.cw) yaw += 180f;
            var rot = Quaternion.Euler(0f, pn.yawB, 0f);
            Vector3 world = pn.c + rot * local;
            float stride = (float)(T * pn.speed * 5.5 + pn.phase);
            float swing = Mathf.Sin(stride) * 28f;
            pn.t.SetPositionAndRotation(world + Vector3.up * (0.15f + Mathf.Abs(Mathf.Sin(stride)) * 0.04f), rot * Quaternion.Euler(0f, yaw, 0f));
            // the helicopter going over low: stop, look up and wave
            bool gawk = heliLow && (world - heliPos).sqrMagnitude < 45f * 45f;
            if (gawk)
            {
                Vector3 toHeli = heliPos - world; toHeli.y = 0f;
                pn.t.rotation = Quaternion.LookRotation(toHeli.sqrMagnitude > 0.01f ? toHeli : Vector3.forward);
                float wave = Mathf.Sin((float)(T * 9.0) + pn.phase) * 25f;
                pn.lLeg.localRotation = Quaternion.identity; pn.rLeg.localRotation = Quaternion.identity;
                pn.lArm.localRotation = Quaternion.Euler(0f, 0f, 8f);
                pn.rArm.localRotation = Quaternion.Euler(-160f + wave, 0f, -20f);
                continue;
            }
            pn.lLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
            pn.rLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            pn.lArm.localRotation = Quaternion.Euler(-swing * 0.8f, 0f, 8f);
            pn.rArm.localRotation = Quaternion.Euler(swing * 0.8f, 0f, -8f);
        }

        UpdateBirds(T);
        if (hum) hum.volume = 0.22f * Mathf.Clamp01(1f - (camPos.y - 8f) / 60f);
    }

    GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 local, Vector3 scale, Color colour)
    {
        var p = GameObject.CreatePrimitive(type);
        p.name = name;
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = local; p.transform.localScale = scale;
        var r = p.GetComponent<Renderer>();
        if (paintMaterial) r.sharedMaterial = paintMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", colour); r.SetPropertyBlock(mpb);
        return p;
    }
}
