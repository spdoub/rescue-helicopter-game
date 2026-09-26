using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Tools/Rescue Heli/Build Survivor Grandma: makes Assets/Prefabs/Survivor_Grandma.prefab from the
/// crew prefab (same floppy physics body, animation and ragdoll setup) with the grandma model
/// swapped in. The ragdoll parts are copied bone-by-bone and resized to her bone lengths.
/// Both characters come from Meshy's auto-rig, so their skeletons share bone names.
/// </summary>
public static class SurvivorBuilder
{
    const string CrewPrefab = "Assets/Prefabs/CrewPlayer.prefab";

    /// <summary>Everything that differs between survivors.</summary>
    class Spec
    {
        public string id, displayName, model, texture, voiceDir, outPath;
        public SurvivorBrain.Kind kind;
        public float height, eye, walk, sprint, jump, grip, hardLanding, mass;
        public int payBonus;
    }

    static readonly Spec Grandma = new()
    {
        id = "Grandma", displayName = "Grandma", kind = SurvivorBrain.Kind.StrongGrandma,
        model = "Assets/Art/Characters/Grandma/Grandma.fbx", texture = "Assets/Art/Characters/Grandma/Grandma_BaseColor.png",
        voiceDir = "Assets/Audio/Survivors/grandma", outPath = "Assets/Prefabs/Survivor_Grandma.prefab",
        height = 1.34f, eye = 1.14f, walk = 2.2f, sprint = 3.4f, jump = 4.2f, grip = 25000f, hardLanding = 11f, mass = 68f,
    };

    static readonly Spec ReclinerGuy = new()
    {
        id = "ReclinerGuy", displayName = "Recliner Guy", kind = SurvivorBrain.Kind.ReclinerGuy,
        model = "Assets/Art/Characters/ReclinerGuy/ReclinerGuy.fbx", texture = "Assets/Art/Characters/ReclinerGuy/ReclinerGuy_BaseColor.png",
        voiceDir = "Assets/Audio/Survivors/recliner", outPath = "Assets/Prefabs/Survivor_ReclinerGuy.prefab",
        height = 1.72f, eye = 1.5f, walk = 1.8f, sprint = 2.4f, jump = 3f, grip = 8000f, hardLanding = 11f, mass = 95f, payBonus = 400,
    };

    // For sizing each ragdoll part: the bone that ends it.
    static readonly Dictionary<string, string> BoneEnd = new()
    {
        { "LeftUpLeg", "LeftLeg" }, { "LeftLeg", "LeftFoot" }, { "RightUpLeg", "RightLeg" }, { "RightLeg", "RightFoot" },
        { "LeftArm", "LeftForeArm" }, { "LeftForeArm", "LeftHand" }, { "RightArm", "RightForeArm" }, { "RightForeArm", "RightHand" },
        { "Head", "head_end" }, { "Spine01", "Spine02" }, { "Hips", "Spine01" },
    };

    [MenuItem("Tools/Rescue Heli/Build Survivor Grandma")]
    public static void Build() => Build(Grandma);

    [MenuItem("Tools/Rescue Heli/Build Survivor Recliner Guy")]
    public static void BuildRecliner()
    {
        BuildChair();
        Build(ReclinerGuy);
    }

    static void Build(Spec spec)
    {
        string ModelPath = spec.model, OutPath = spec.outPath;
        string MatPath = System.IO.Path.GetDirectoryName(spec.model) + "/M_" + spec.id + ".mat";
        Setup(spec, MatPath);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CrewPrefab));
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = "Survivor_" + spec.id;
        var modelT = inst.transform.Find("Model");
        var oldArm = modelT.Find("Armature");
        var oldMesh = modelT.Find("char1");

        // ---- new skeleton + skin
        var gma = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        var newArm = gma.transform.Find("Armature");
        var newMesh = gma.transform.Find("char1");
        newArm.SetParent(modelT, false);
        newMesh.SetParent(modelT, false);
        Object.DestroyImmediate(gma);
        newMesh.GetComponent<SkinnedMeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatPath);

        var oldBones = oldArm.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
        var newBones = newArm.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
        float Len(Dictionary<string, Transform> b, string bone) =>
            BoneEnd.TryGetValue(bone, out var end) && b.ContainsKey(end) ? Vector3.Distance(b[bone].position, b[end].position) : 1f;

        // ---- ragdoll: copy each part, resize to her proportions, rewire the joints
        foreach (var src in oldArm.GetComponentsInChildren<Rigidbody>(true))
        {
            if (!newBones.TryGetValue(src.name, out var dst)) { Debug.LogWarning("[Survivor] no bone " + src.name); continue; }
            dst.gameObject.layer = src.gameObject.layer;
            float k = Len(newBones, src.name) / Mathf.Max(0.001f, Len(oldBones, src.name));
            foreach (var c in src.GetComponents<Component>())
            {
                if (c is Transform) continue;
                ComponentUtility.CopyComponent(c);
                ComponentUtility.PasteComponentAsNew(dst.gameObject);
            }
            foreach (var col in dst.GetComponents<Collider>())
            {
                switch (col)
                {
                    case BoxCollider b: b.center *= k; b.size *= k; break;
                    case SphereCollider s: s.center *= k; s.radius *= k; break;
                    case CapsuleCollider cc: cc.center *= k; cc.height *= k; cc.radius *= Mathf.Lerp(1f, k, 0.5f); break;
                }
            }
        }
        foreach (var j in newArm.GetComponentsInChildren<Joint>(true))
            if (j.connectedBody != null && newBones.TryGetValue(j.connectedBody.name, out var nb)) j.connectedBody = nb.GetComponent<Rigidbody>();

        Object.DestroyImmediate(oldArm.gameObject);
        Object.DestroyImmediate(oldMesh.gameObject);

        // ---- animator: her avatar, the crew's animation controller (humanoid retargeting)
        var anim = modelT.GetComponent<Animator>();
        anim.avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First();

        // ---- body
        float height = spec.height;
        var cap = inst.GetComponent<CapsuleCollider>();
        cap.height = height;
        modelT.localPosition = new Vector3(0f, -height / 2f, 0f);
        var body = inst.GetComponent<CrewBody>();
        body.eyeHeight = spec.eye;
        body.walkSpeed = spec.walk;
        body.sprintSpeed = spec.sprint;
        body.jumpSpeed = spec.jump;
        body.grabBreakForce = spec.grip;
        body.hardLandingSpeed = spec.hardLanding;
        inst.GetComponent<Rigidbody>().mass = spec.mass;

        var brain = inst.GetComponent<SurvivorBrain>() ?? inst.AddComponent<SurvivorBrain>();
        brain.kind = spec.kind; brain.displayName = spec.displayName; brain.payBonus = spec.payBonus;

        // voice: every recorded line in the survivor's folder (tools/survivor_lines.py)
        var voice = inst.GetComponent<SurvivorVoice>() ?? inst.AddComponent<SurvivorVoice>();
        voice.lines.Clear();
        if (AssetDatabase.IsValidFolder(spec.voiceDir))
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { spec.voiceDir }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                voice.lines.Add(new SurvivorVoice.Line { key = clip.name, clip = clip });
            }

        PrefabUtility.SaveAsPrefabAsset(inst, OutPath);
        Object.DestroyImmediate(inst);
        Debug.Log("[Survivor] saved " + OutPath);
    }

    /// <summary>Humanoid import (animations come from the crew controller) and a material for the skin texture.</summary>
    static void Setup(Spec spec, string matPath)
    {
        var crewImp = (ModelImporter)AssetImporter.GetAtPath("Assets/Art/Characters/Crew/Crew.fbx");
        var imp = (ModelImporter)AssetImporter.GetAtPath(spec.model);
        if (imp.animationType != ModelImporterAnimationType.Human || imp.importAnimation)
        {
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.globalScale = crewImp.globalScale;
            imp.SaveAndReimport();
        }
        if (AssetDatabase.LoadAssetAtPath<Material>(matPath) == null)
        {
            var m = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Characters/Crew/M_Crew.mat"));
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(spec.texture));
            AssetDatabase.CreateAsset(m, matPath);
        }
    }

    /// <summary>The recliner prop: networked heavy physics chair with a seat and a lifting strap.</summary>
    static void BuildChair()
    {
        const string dir = "Assets/Art/Props/Recliner/";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(dir + "M_Recliner.mat");
        if (mat == null)
        {
            mat = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Interior/Mat_White.mat"));
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "Recliner_BaseColor.png"));
            AssetDatabase.CreateAsset(mat, dir + "M_Recliner.mat");
        }
        var root = new GameObject("Recliner");
        var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(dir + "Recliner.fbx"), root.transform);
        model.name = "Model";
        foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
        void Box(Vector3 c, Vector3 size) { var b = root.AddComponent<BoxCollider>(); b.center = c; b.size = size; }
        Box(new Vector3(0f, 0.26f, 0.02f), new Vector3(1.0f, 0.5f, 0.85f));     // base + seat
        Box(new Vector3(0f, 0.78f, -0.45f), new Vector3(1.0f, 0.62f, 0.32f));    // backrest
        Box(new Vector3(0f, 0.2f, 0.52f), new Vector3(0.8f, 0.14f, 0.38f));      // footrest
        var seat = new GameObject("SeatPoint").transform; seat.SetParent(root.transform, false); seat.localPosition = new Vector3(0f, 0.1f, 0.12f);
        var lift = new GameObject("LiftPoint").transform; lift.SetParent(root.transform, false); lift.localPosition = new Vector3(0f, 1.12f, -0.5f);
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 150f; rb.linearDamping = 0.2f; rb.angularDamping = 1.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        root.AddComponent<Unity.Netcode.NetworkObject>();
        root.AddComponent<Unity.Netcode.Components.NetworkTransform>().Interpolate = true;
        root.AddComponent<Unity.Netcode.Components.NetworkRigidbody>();
        var rc = root.AddComponent<Recliner>();
        rc.seatPoint = seat; rc.liftPoint = lift;
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Recliner.prefab");
        Object.DestroyImmediate(root);
        Debug.Log("[Survivor] saved Assets/Prefabs/Recliner.prefab");
    }
}
