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
    const string ModelPath = "Assets/Art/Characters/Grandma/Grandma.fbx";
    const string MatPath = "Assets/Art/Characters/Grandma/M_Grandma.mat";
    const string OutPath = "Assets/Prefabs/Survivor_Grandma.prefab";

    // For sizing each ragdoll part: the bone that ends it.
    static readonly Dictionary<string, string> BoneEnd = new()
    {
        { "LeftUpLeg", "LeftLeg" }, { "LeftLeg", "LeftFoot" }, { "RightUpLeg", "RightLeg" }, { "RightLeg", "RightFoot" },
        { "LeftArm", "LeftForeArm" }, { "LeftForeArm", "LeftHand" }, { "RightArm", "RightForeArm" }, { "RightForeArm", "RightHand" },
        { "Head", "head_end" }, { "Spine01", "Spine02" }, { "Hips", "Spine01" },
    };

    [MenuItem("Tools/Rescue Heli/Build Survivor Grandma")]
    public static void Build()
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CrewPrefab));
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = "Survivor_Grandma";
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

        // ---- body: shorter, slower, and a grip of iron
        float height = 1.34f;
        var cap = inst.GetComponent<CapsuleCollider>();
        cap.height = height;
        modelT.localPosition = new Vector3(0f, -height / 2f, 0f);
        var body = inst.GetComponent<CrewBody>();
        body.eyeHeight = 1.14f;
        body.walkSpeed = 2.2f;
        body.sprintSpeed = 3.4f;
        body.jumpSpeed = 4.2f;
        body.grabBreakForce = 25000f; // strong grandma
        body.hardLandingSpeed = 11f;
        inst.GetComponent<Rigidbody>().mass = 68f;

        if (!inst.GetComponent<SurvivorBrain>()) inst.AddComponent<SurvivorBrain>();

        PrefabUtility.SaveAsPrefabAsset(inst, OutPath);
        Object.DestroyImmediate(inst);
        Debug.Log("[Survivor] saved " + OutPath);
    }
}
