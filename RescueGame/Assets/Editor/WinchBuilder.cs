using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/Rescue Heli/Build Winch: creates the rescue hook (a networked physics object at the scene
/// root, so it can swing freely under the helicopter) and adds NetworkWinch to the helicopter.
/// The hoist arm itself comes from the Blender interior (Hoist group, M_HoistTip marker).
/// Safe to run again: replaces the old hook.
/// </summary>
public static class WinchBuilder
{
    const string Mats = "Assets/Materials/Interior/";

    [MenuItem("Tools/Rescue Heli/Build Winch")]
    public static void Build()
    {
        var heli = GameObject.Find("Helicopter");
        var old = GameObject.Find("WinchHook");
        if (old) Object.DestroyImmediate(old);

        Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>(Mats + "Mat_" + n + ".mat");
        var hook = new GameObject("WinchHook");
        var winch = heli.GetComponent<NetworkWinch>() ?? heli.AddComponent<NetworkWinch>();
        winch.cableMaterial = M("Black");
        // Hang it under the hoist tip.
        var tipT = FindDeep(heli.transform, "M_HoistTip");
        Vector3 tip = tipT ? tipT.position : heli.transform.TransformPoint(1.4f, 2.36f, 0.85f);
        hook.transform.SetPositionAndRotation(tip + Vector3.down * (0.3f + winch.hookEye.y), heli.transform.rotation);

        // ---- looks: a yellow weighted ball on a swivel, with a big black hook and a red latch
        Part(hook, PrimitiveType.Sphere, "Ball", new Vector3(0f, 0f, 0f), Vector3.one * 0.26f, Vector3.zero, M("Yellow"));
        Part(hook, PrimitiveType.Cylinder, "Band", new Vector3(0f, 0f, 0f), new Vector3(0.265f, 0.02f, 0.265f), Vector3.zero, M("Black"));
        Part(hook, PrimitiveType.Cylinder, "Swivel", new Vector3(0f, 0.14f, 0f), new Vector3(0.05f, 0.03f, 0.05f), Vector3.zero, M("Metal"));
        Part(hook, PrimitiveType.Cylinder, "Eye", new Vector3(0f, 0.17f, 0f), new Vector3(0.06f, 0.008f, 0.06f), new Vector3(90f, 0f, 0f), M("Black"));
        Part(hook, PrimitiveType.Cylinder, "Shank", new Vector3(0f, -0.17f, 0f), new Vector3(0.035f, 0.05f, 0.035f), Vector3.zero, M("DarkGrey"));
        // the hook: a J made of three chunky bars
        Part(hook, PrimitiveType.Cube, "HookBack", new Vector3(0f, -0.28f, 0f), new Vector3(0.035f, 0.14f, 0.035f), Vector3.zero, M("Black"));
        Part(hook, PrimitiveType.Cube, "HookBottom", new Vector3(0f, -0.35f, 0.05f), new Vector3(0.035f, 0.035f, 0.12f), Vector3.zero, M("Black"));
        Part(hook, PrimitiveType.Cube, "HookTip", new Vector3(0f, -0.3f, 0.1f), new Vector3(0.035f, 0.1f, 0.035f), new Vector3(-15f, 0f, 0f), M("Black"));
        Part(hook, PrimitiveType.Cube, "Latch", new Vector3(0f, -0.25f, 0.06f), new Vector3(0.02f, 0.012f, 0.1f), new Vector3(-35f, 0f, 0f), M("RedKnob"));
        Part(hook, PrimitiveType.Cube, "Tape", new Vector3(0f, 0.02f, 0.131f), new Vector3(0.1f, 0.05f, 0.005f), new Vector3(0f, 0f, 12f), M("Tape"));

        // ---- physics: easy to grab (a bit bigger than it looks), heavy enough to hang straight
        var ball = hook.AddComponent<SphereCollider>(); ball.radius = 0.16f;
        var hk = hook.AddComponent<BoxCollider>(); hk.center = new Vector3(0f, -0.29f, 0.05f); hk.size = new Vector3(0.1f, 0.18f, 0.18f);
        var rb = hook.AddComponent<Rigidbody>();
        rb.mass = 10f; rb.linearDamping = 0.15f; rb.angularDamping = 1.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // ---- network: the host simulates it, everyone else sees it move
        hook.AddComponent<NetworkObject>();
        var nt = hook.AddComponent<NetworkTransform>();
        nt.Interpolate = true;
        hook.AddComponent<NetworkRigidbody>();

        winch.hook = rb;
        EditorUtility.SetDirty(winch);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(heli.scene);
        Debug.Log("[Winch] hook built at " + hook.transform.position + (tipT ? "" : " (no M_HoistTip found!)"));
    }

    [MenuItem("Tools/Rescue Heli/Wire Helicopter Audio")]
    public static void WireAudio()
    {
        var heli = GameObject.Find("Helicopter");
        var a = heli.GetComponent<HeliAudio>() ?? heli.AddComponent<HeliAudio>();
        AudioClip C(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/" + n + ".wav");
        a.rotorLoop = C("rotor_loop"); a.winchMotor = C("winch_motor"); a.doorSlide = C("door_slide");
        a.hookClank = C("hook_clank"); a.deliveredJingle = C("delivered_jingle");
        EditorUtility.SetDirty(a);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(heli.scene);
        // searchlight beam + downwash dust (materials are assets so their shaders are kept in builds)
        var lights = heli.GetComponent<HeliLights>() ?? heli.AddComponent<HeliLights>();
        const string dir = "Assets/Materials/Effects/";
        System.IO.Directory.CreateDirectory(dir);
        var beam = AssetDatabase.LoadAssetAtPath<Material>(dir + "M_SearchlightBeam.mat");
        if (beam == null) { beam = new Material(Shader.Find("RescueGame/Light Beam")); AssetDatabase.CreateAsset(beam, dir + "M_SearchlightBeam.mat"); }
        var dust = AssetDatabase.LoadAssetAtPath<Material>(dir + "M_Dust.mat");
        if (dust == null)
        {
            dust = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            dust.SetFloat("_Surface", 1f);
            dust.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dust.SetOverrideTag("RenderType", "Transparent");
            dust.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            dust.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            dust.SetInt("_ZWrite", 0);
            dust.renderQueue = 3000;
            dust.SetColor("_BaseColor", new Color(0.85f, 0.72f, 0.56f, 0.35f));
            AssetDatabase.CreateAsset(dust, dir + "M_Dust.mat");
        }
        dust.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Effects/SoftPuff.png")); // soft round puffs, not squares
        EditorUtility.SetDirty(dust);
        lights.beamMaterial = beam; lights.dustMaterial = dust;

        // damage: sounds + a smoke material (soft puffs, tinted per particle)
        var dmg = heli.GetComponent<HeliDamage>() ?? heli.AddComponent<HeliDamage>();
        var smoke = AssetDatabase.LoadAssetAtPath<Material>(dir + "M_Smoke.mat");
        if (smoke == null) { smoke = new Material(dust); smoke.SetColor("_BaseColor", Color.white); AssetDatabase.CreateAsset(smoke, dir + "M_Smoke.mat"); }
        dmg.smokeMaterial = smoke;
        dmg.crash = C("crash_thud"); dmg.rotorStrike = C("rotor_strike"); dmg.alarm = C("damage_alarm");
        EditorUtility.SetDirty(dmg);

        // weather: wind + streaks
        var weather = heli.GetComponent<Weather>() ?? heli.AddComponent<Weather>();
        weather.streakMaterial = smoke;
        EditorUtility.SetDirty(weather);
        EditorUtility.SetDirty(lights);

        // a plain white Stylized Lit material, tinted per part (upgrades, the upgrade board, ledges)
        var painted = AssetDatabase.LoadAssetAtPath<Material>(dir + "M_Painted.mat");
        if (painted == null)
        {
            painted = new Material(Shader.Find("RescueGame/Stylized Lit"));
            painted.SetColor("_BaseColor", Color.white); // tinted per part
            AssetDatabase.CreateAsset(painted, dir + "M_Painted.mat");
        }
        // roof fires: flame tongues (additive) + the same smoke, on every survivor
        var flame = AssetDatabase.LoadAssetAtPath<Material>(dir + "M_Fire.mat");
        if (flame == null) { flame = new Material(dust); AssetDatabase.CreateAsset(flame, dir + "M_Fire.mat"); }
        flame.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Effects/SoftPuff.png")); // soft blobs, stretched upward by speed
        flame.SetColor("_BaseColor", Color.white);
        flame.SetFloat("_Blend", 2f); // additive
        flame.SetOverrideTag("RenderType", "Transparent");
        flame.SetInt("_ZWrite", 0);
        flame.renderQueue = 3001;
        flame.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        flame.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        EditorUtility.SetDirty(flame);
        string[] folders = { "grandma", "recliner", "backseat", "dogperson", "ledge" }; // by SurvivorBrain.Kind
        foreach (var s in Object.FindObjectsByType<SurvivorBrain>(FindObjectsSortMode.None))
        {
            if (s.kind == SurvivorBrain.Kind.LedgeResident)
            {
                var lp = s.GetComponent<LedgePerch>() ?? s.gameObject.AddComponent<LedgePerch>();
                lp.material = painted;
                EditorUtility.SetDirty(lp);
            }
            var rf = s.GetComponent<RoofFire>() ?? s.gameObject.AddComponent<RoofFire>();
            rf.flameMaterial = flame; rf.smokeMaterial = smoke;
            EditorUtility.SetDirty(rf);
            // any newly voiced lines (tools/survivor_lines.py) that the voice list doesn't have yet
            var voice = s.GetComponent<SurvivorVoice>();
            if (voice == null) continue;
            string folder = "Assets/Audio/Survivors/" + folders[(int)s.kind];
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (!voice.lines.Exists(l => l.key == clip.name)) voice.lines.Add(new SurvivorVoice.Line { key = clip.name, clip = clip });
            }
            EditorUtility.SetDirty(voice);
        }
        // upgrades: bolt-on parts on the helicopter + the board by the home pad (both built at load)
        var ups = heli.GetComponent<HeliUpgrades>() ?? heli.AddComponent<HeliUpgrades>();
        ups.partMaterial = painted;
        EditorUtility.SetDirty(ups);
        var boardGo = GameObject.Find("UpgradeBoard") ?? new GameObject("UpgradeBoard");
        Vector3 boardPos = new Vector3(-9f, 0.1f, 4f);
        boardGo.transform.SetPositionAndRotation(boardPos, Quaternion.LookRotation(new Vector3(boardPos.x, 0f, boardPos.z))); // front (-Z) faces the pad
        var board = boardGo.GetComponent<UpgradeBoard>() ?? boardGo.AddComponent<UpgradeBoard>();
        board.material = painted;
        EditorUtility.SetDirty(board);

        // the island around the city (built at load by IslandGenerator)
        var islandGo = GameObject.Find("Island") ?? new GameObject("Island");
        var island = islandGo.GetComponent<IslandGenerator>(); if (!island) island = islandGo.AddComponent<IslandGenerator>();
        Material Flat(string n, Color c, float ambient = 0.1f)
        {
            string p = "Assets/Materials/Level/M_" + n + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("RescueGame/Stylized Lit")); m.SetColor("_BaseColor", c); m.SetFloat("_Ambient", ambient); AssetDatabase.CreateAsset(m, p); }
            return m;
        }
        var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Level/M_Water.mat");
        if (water == null) { water = new Material(Shader.Find("RescueGame/Stylized Lit")); AssetDatabase.CreateAsset(water, "Assets/Materials/Level/M_Water.mat"); }
        water.shader = Shader.Find("RescueGame/Stylized Lit");
        water.SetColor("_BaseColor", new Color(0.20f, 0.46f, 0.58f, 1f));   // flat painterly sea (opaque: hides the sea floor)
        water.SetFloat("_Ambient", 0.25f); water.SetFloat("_FlatShading", 0f);
        EditorUtility.SetDirty(water);
        island.mats = new[]
        {
            Flat("GrassA", new Color(0.47f, 0.62f, 0.30f)), Flat("GrassB", new Color(0.40f, 0.56f, 0.27f)),
            Flat("Sand", new Color(0.88f, 0.80f, 0.58f)), Flat("RockA", new Color(0.52f, 0.47f, 0.42f)), Flat("RockB", new Color(0.44f, 0.40f, 0.37f)),
            Flat("Snow", new Color(0.95f, 0.95f, 0.97f), 0.25f), water, Flat("Trunk", new Color(0.36f, 0.26f, 0.18f)),
            Flat("LeafA", new Color(0.30f, 0.52f, 0.24f)), Flat("LeafB", new Color(0.55f, 0.58f, 0.22f)), Flat("LeafC", new Color(0.18f, 0.40f, 0.26f)),
            Flat("CabinWood", new Color(0.50f, 0.34f, 0.22f)), Flat("Stone", new Color(0.62f, 0.60f, 0.56f)), Flat("CabinRoof", new Color(0.62f, 0.18f, 0.14f)),
            Flat("RoadAsphalt", new Color(0.27f, 0.26f, 0.27f)), Flat("RoadPaint", new Color(0.92f, 0.86f, 0.6f)), Flat("Cable", new Color(0.15f, 0.15f, 0.16f)),
        };
        EditorUtility.SetDirty(island);
        var ilife = islandGo.GetComponent<IslandLife>(); if (!ilife) ilife = islandGo.AddComponent<IslandLife>();
        ilife.paintMaterial = painted; ilife.smokeMaterial = smoke; ilife.beamMaterial = beam;
        EditorUtility.SetDirty(ilife);
        var cityGen = GameObject.Find("City")?.GetComponent<CityGenerator>();
        if (cityGen) { cityGen.skyline = false; EditorUtility.SetDirty(cityGen); }
        // see the far side of the island: haze further out, cameras further
        RenderSettings.fogStartDistance = 80f; RenderSettings.fogEndDistance = 1300f;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) { cam.farClipPlane = 3000f; EditorUtility.SetDirty(cam); }

        // bird strikes on the windscreen
        var ws = heli.GetComponent<WindscreenSplat>(); if (!ws) ws = heli.AddComponent<WindscreenSplat>();
        ws.splatMaterial = smoke; ws.splatSound = C("bird_splat");
        EditorUtility.SetDirty(ws);

        // the paper map of the island (dash + cabin wall)
        var map = heli.GetComponent<IslandMap>(); if (!map) map = heli.AddComponent<IslandMap>();
        map.paperMaterial = painted; map.pinMaterial = painted;
        EditorUtility.SetDirty(map);

        // the dispatcher on the radio
        var dispGo = GameObject.Find("Dispatch") ?? new GameObject("Dispatch");
        var disp = dispGo.GetComponent<Dispatcher>(); if (!disp) disp = dispGo.AddComponent<Dispatcher>();
        disp.lines.Clear();
        var texts = new System.Collections.Generic.Dictionary<string, string>
        {
            ["call_rooftop"] = "New call: someone stranded on a rooftop in town. Get airborne.",
            ["call_beach"] = "Caller stuck on the beach down by the pier, tide's coming in.",
            ["call_mountain"] = "Hiker in trouble up on the mountain. Watch the updrafts up there.",
            ["call_cliff"] = "Person on the cliff top out west, near the lighthouse.",
            ["call_lake"] = "Someone on the lake shore, east side.",
            ["call_forest"] = "Someone's lost in the forest. Look for the waving.",
            ["call_gondola"] = "The cable car's stuck mid-span with someone on top of it. Mind the cables.",
            ["call_ledge"] = "Neighbours report a man on a ledge downtown. He says he's fine. He is not fine.",
            ["delivered"] = "Nice work, Rescue One. Patient's in good hands. Stand by for the next one.",
            ["delivered_rough"] = "Patient delivered. Uh, they've asked to speak to a manager.",
            ["welcome"] = "Morning, crew. Coffee's on the dash. Calls are coming in, get her started.",
        };
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/Dispatch" }))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            disp.lines.Add(new Dispatcher.Line { key = clip.name, clip = clip, text = texts.TryGetValue(clip.name, out var t) ? t : clip.name });
        }
        EditorUtility.SetDirty(disp);

        // city life: traffic, people, street furniture, ambience (built at load by CityLife)
        var cityGo = GameObject.Find("City");
        if (cityGo)
        {
            var life = cityGo.GetComponent<CityLife>(); if (!life) life = cityGo.AddComponent<CityLife>();
            life.paintMaterial = painted; life.smokeMaterial = smoke; life.ambience = C("city_ambience"); life.horn = C("car_horn");
            EditorUtility.SetDirty(life);
        }

        Debug.Log("[Audio] helicopter sounds wired: " + (a.rotorLoop && a.winchMotor && a.doorSlide && a.hookClank && a.deliveredJingle));
    }

    static void Part(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        var p = GameObject.CreatePrimitive(type);
        p.name = name;
        Object.DestroyImmediate(p.GetComponent<Collider>());
        p.transform.SetParent(parent.transform, false);
        p.transform.localPosition = pos;
        p.transform.localScale = scale;
        p.transform.localRotation = Quaternion.Euler(euler);
        p.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
        return null;
    }
}
