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
        EditorUtility.SetDirty(lights);
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
