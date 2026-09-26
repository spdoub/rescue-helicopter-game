using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/Rescue Heli/Build Rescue Level: the first playable rescue in the prototype scene.
///  - Strong Grandma waiting on the flat roof of Block_3 (like concept 3's rooftop pickup)
///  - a hospital with a helipad (RescueZone) to drop her at
/// Safe to run again: replaces what it made before.
/// </summary>
public static class RescueLevelBuilder
{
    const string Mats = "Assets/Materials/Interior/";
    const string LevelMats = "Assets/Materials/Level/";

    [MenuItem("Tools/Rescue Heli/Build Rescue Level")]
    public static void Build()
    {
        foreach (var n in new[] { "Grandma", "Hospital", "HospitalPad", "ReclinerChair", "ReclinerGuy" }) { var o = GameObject.Find(n); if (o) Object.DestroyImmediate(o); }

        // ---- the survivor, on the roof, on the side facing the helipad
        var roof = GameObject.Find("Environment/Block_3").GetComponent<Renderer>().bounds;
        var gPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Survivor_Grandma.prefab");
        var g = (GameObject)PrefabUtility.InstantiatePrefab(gPrefab);
        g.name = "Grandma";
        float halfH = g.GetComponent<CapsuleCollider>().height / 2f;
        Vector3 spot = new Vector3(roof.center.x - 2f, roof.max.y + halfH + 0.05f, roof.min.z + 4f);
        g.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-new Vector3(spot.x, 0f, spot.z)));

        // ---- Recliner Guy in his armchair on the low roof of Block_6 (the concept 3 scene)
        var b6 = GameObject.Find("Environment/Block_6").GetComponent<Collider>().bounds;
        var chair = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Recliner.prefab"));
        chair.name = "ReclinerChair";
        chair.transform.SetPositionAndRotation(new Vector3(b6.center.x, b6.max.y + 0.02f, b6.center.z),
            Quaternion.LookRotation(-new Vector3(b6.center.x, 0f, b6.center.z)));
        var guy = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Survivor_ReclinerGuy.prefab"));
        guy.name = "ReclinerGuy";
        guy.GetComponent<SurvivorBrain>().chair = chair.GetComponent<Recliner>();
        var seatT = chair.GetComponent<Recliner>().seatPoint;
        guy.transform.SetPositionAndRotation(seatT.position + Vector3.up * guy.GetComponent<CapsuleCollider>().height / 2f, seatT.rotation);

        // ---- the hospital
        var root = new GameObject("Hospital").transform;
        root.position = new Vector3(-28f, 0f, -40f);
        Material concrete = Tint("Concrete", new Color(0.62f, 0.6f, 0.56f));
        Material blue = Tint("PadBlue", new Color(0.18f, 0.36f, 0.62f));
        Material white = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Mat_White.mat");
        Material cream = Tint("HospitalWall", new Color(0.9f, 0.84f, 0.72f));
        Material green = Tint("CrossGreen", new Color(0.2f, 0.62f, 0.3f));
        Material glass = Tint("WindowBlue", new Color(0.35f, 0.5f, 0.62f));
        Material dark = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Mat_DarkGrey.mat");

        // pad: concrete slab, blue circle, white H (flat, so it never trips anyone)
        Block(root, "PadSlab", new Vector3(0f, 0.05f, 0f), new Vector3(16f, 0.1f, 16f), concrete, true);
        Disc(root, "PadCircle", new Vector3(0f, 0.105f, 0f), 6.5f, blue);
        Disc(root, "PadRing", new Vector3(0f, 0.11f, 0f), 5.6f, white);
        Disc(root, "PadInner", new Vector3(0f, 0.115f, 0f), 5.2f, blue);
        Block(root, "H_L", new Vector3(-1.6f, 0.125f, 0f), new Vector3(0.8f, 0.02f, 5f), white, false);
        Block(root, "H_R", new Vector3(1.6f, 0.125f, 0f), new Vector3(0.8f, 0.02f, 5f), white, false);
        Block(root, "H_Mid", new Vector3(0f, 0.125f, 0f), new Vector3(2.4f, 0.02f, 0.8f), white, false);
        for (int i = 0; i < 12; i++) // edge lights
        {
            float a = i * Mathf.PI * 2f / 12f;
            Block(root, "PadLight_" + i, new Vector3(Mathf.Cos(a) * 7.4f, 0.16f, Mathf.Sin(a) * 7.4f), new Vector3(0.18f, 0.12f, 0.18f), Tint("PadLight", new Color(1f, 0.85f, 0.35f)), false);
        }

        // building behind the pad (generated brick, like the rest of the city), glass entrance facing the pad
        CityBuilder.MakeBuilding(root, "HospitalBuilding", root.TransformPoint(new Vector3(0f, 0f, -19f)), root.rotation, 16f, 10f, 9f, "Brick_Tan",
            new System.Random(7), Vector3.one * 9999f, bodyCollider: true, forceShopFace: 2);
        Block(root, "Canopy", new Vector3(0f, 3.2f, -13.2f), new Vector3(5f, 0.15f, 1.8f), white, true);
        Block(root, "CanopyPostL", new Vector3(-2.3f, 1.6f, -12.5f), new Vector3(0.12f, 3.2f, 0.12f), white, false);
        Block(root, "CanopyPostR", new Vector3(2.3f, 1.6f, -12.5f), new Vector3(0.12f, 3.2f, 0.12f), white, false);
        // big green cross sign on the front
        Block(root, "CrossV", new Vector3(5.5f, 7.2f, -13.75f), new Vector3(0.6f, 1.8f, 0.12f), green, false);
        Block(root, "CrossH", new Vector3(5.5f, 7.2f, -13.75f), new Vector3(1.8f, 0.6f, 0.12f), green, false);

        var exit = new GameObject("ExitPoint").transform;
        exit.SetParent(root, false); exit.localPosition = new Vector3(0f, 0f, -13.2f);

        // drop-off zone over the pad (a trigger: nothing bumps into it)
        var zoneGo = new GameObject("HospitalPad");
        zoneGo.transform.SetParent(root, false);
        var box = zoneGo.AddComponent<BoxCollider>();
        box.isTrigger = true; box.center = new Vector3(0f, 3f, 0f); box.size = new Vector3(16f, 6f, 16f);
        zoneGo.AddComponent<NetworkObject>();
        var zone = zoneGo.AddComponent<RescueZone>();
        zone.exitPoint = exit;
        // A NetworkObject can't be nested under a plain object that also holds one; keep the zone at the root level.
        zoneGo.transform.SetParent(null, true);
        zoneGo.name = "HospitalPad";

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log($"[Level] grandma at {spot}, hospital at {root.position}");
    }

    static Material Tint(string name, Color c)
    {
        string path = LevelMats + "Mat_" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            System.IO.Directory.CreateDirectory(LevelMats);
            m = new Material(AssetDatabase.LoadAssetAtPath<Material>(Mats + "Mat_White.mat"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Block(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool solid)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = name;
        b.transform.SetParent(parent, false);
        b.transform.localPosition = pos;
        b.transform.localScale = size;
        b.GetComponent<Renderer>().sharedMaterial = mat;
        if (!solid) Object.DestroyImmediate(b.GetComponent<Collider>());
        return b;
    }

    static void Disc(Transform parent, string name, Vector3 pos, float radius, Material mat)
    {
        var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        d.name = name;
        Object.DestroyImmediate(d.GetComponent<Collider>()); // cylinders get capsule colliders; the slab is the floor
        d.transform.SetParent(parent, false);
        d.transform.localPosition = pos;
        d.transform.localScale = new Vector3(radius * 2f, 0.005f, radius * 2f);
        d.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
