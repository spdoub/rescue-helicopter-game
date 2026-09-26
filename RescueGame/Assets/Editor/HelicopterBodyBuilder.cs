using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Rescue Heli > Build Helicopter Body.
/// Puts the hand-built body (tools/blender/build_body.py -> Body.fbx) on the Helicopter in place of the
/// old AI-generated hull + shrink-wrapped shell: maps its Mat_* materials, wires the rotors, doors
/// (on rails, sliding), the camera blocker (the lining) and the hull colliders. Re-runnable.
/// </summary>
public static class HelicopterBodyBuilder
{
    const string BodyFbx = "Assets/Art/Helicopters/Medevac/Body/Body.fbx";
    const string InteriorFbx = "Assets/Art/Helicopters/Medevac/Interior/Interior.fbx";
    const string Mats = "Assets/Materials/Interior/";

    [MenuItem("Tools/Rescue Heli/Build Helicopter Body")]
    public static void Build()
    {
        var heli = GameObject.Find("Helicopter").transform;
        RemapMaterials(BodyFbx);
        RemapMaterials(InteriorFbx);

        // ---- out with the old: the Meshy hull, the shrink-wrapped shell, the tuck-in clip
        foreach (var n in new[] { "Model", "Shell", "Body" })
        {
            var t = heli.Find(n); if (t) Object.DestroyImmediate(t.gameObject);
        }
        foreach (var n in new[] { "Door_L", "Door_R", "MainRotor", "TailRotor" })
        {
            var t = heli.Find(n); if (t) Object.DestroyImmediate(t.gameObject);
        }

        // ---- in with the new (unlike the interior's FBX, this one imports the right way up)
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyFbx);
        var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        body.name = "Body";
        body.transform.SetParent(heli, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one;
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // moving parts become direct children of the helicopter, keeping their world pose, so their
        // local position is in helicopter space (doors slide along -z; rotors spin about the heli's axes)
        foreach (var n in new[] { "Door_L", "Door_R", "MainRotor", "TailRotor" })
        {
            var t = FindDeep(body.transform, n);
            if (t == null) { Debug.LogWarning("[Body] missing " + n); continue; }
            t.SetParent(heli, true);
        }

        // the interior is exported the same (flat) way now: no instance rotation either
        var interior = heli.Find("Interior");
        if (interior) { interior.localRotation = Quaternion.identity; interior.localPosition = Vector3.zero; }

        // ---- rotors
        var ctrl = heli.GetComponent<HelicopterController>();
        ctrl.mainRotor = heli.Find("MainRotor");
        ctrl.tailRotor = heli.Find("TailRotor");
        EditorUtility.SetDirty(ctrl);

        // ---- doors: sliding on rails, with a collider matching the panel
        foreach (var (n, left) in new[] { ("Door_L", true), ("Door_R", false) })
        {
            var d = heli.Find(n);
            var sd = d.GetComponent<SlidingDoor>(); if (!sd) sd = d.gameObject.AddComponent<SlidingDoor>();
            sd.left = left;
            var col = d.GetComponent<BoxCollider>(); if (!col) col = d.gameObject.AddComponent<BoxCollider>();
            // panel bounds in helicopter space, expressed in the door's own local space
            var b = new Bounds(heli.TransformPoint(new Vector3(left ? -0.99f : 0.99f, 1.62f, 0.525f)), Vector3.zero);
            b.Encapsulate(heli.TransformPoint(new Vector3(left ? -1.03f : 1.03f, 0.95f, -0.35f)));
            b.Encapsulate(heli.TransformPoint(new Vector3(left ? -0.95f : 0.95f, 2.3f, 1.40f)));
            var min = d.InverseTransformPoint(b.min); var max = d.InverseTransformPoint(b.max);
            col.center = (min + max) / 2f;
            col.size = new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), Mathf.Abs(max.z - min.z));
            foreach (var r in d.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            EditorUtility.SetDirty(sd);
        }

        // ---- the lining is what the first-person camera bumps into
        var lining = FindDeep(body.transform, "Lining");
        if (lining)
        {
            var cb = lining.GetComponent<CameraBlocker>(); if (!cb) cb = lining.gameObject.AddComponent<CameraBlocker>();
            cb.mesh = lining.GetComponent<MeshFilter>().sharedMesh;
            EditorUtility.SetDirty(cb);
        }
        // glass casts no shadow
        foreach (var r in body.GetComponentsInChildren<Renderer>())
            if (r.name == "Glass" || r.sharedMaterials.Any(m => m && m.name == "Glass")) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // ---- hull colliders (helicopter-local boxes; hollow cabin, solid everywhere else)
        var cols = heli.Find("Colliders");
        if (cols == null) { cols = new GameObject("Colliders").transform; cols.SetParent(heli, false); }
        void BoxCol(string name, Vector3 c, Vector3 s)
        {
            var t = cols.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(cols, false); }
            t.localPosition = c; t.localRotation = Quaternion.identity;
            var bc = t.GetComponent<BoxCollider>(); if (!bc) bc = t.gameObject.AddComponent<BoxCollider>();
            bc.center = Vector3.zero; bc.size = s;
        }
        BoxCol("Belly", new Vector3(0f, 0.81f, 1.3f), new Vector3(1.9f, 0.24f, 3.6f));      // under the floor (floor top 0.95)
        BoxCol("Roof", new Vector3(0f, 2.68f, 0.45f), new Vector3(1.9f, 0.14f, 2.1f));      // cabin ceiling 2.62
        { var t = cols.Find("CockpitRoof"); if (t) Object.DestroyImmediate(t.gameObject); } // (lower than a standing crew member: the lining keeps the camera in)
        BoxCol("EngineHump", new Vector3(0f, 2.9f, 0.55f), new Vector3(1.2f, 0.34f, 1.9f));
        BoxCol("RearWall", new Vector3(0f, 1.75f, -0.58f), new Vector3(1.9f, 1.7f, 0.14f));
        BoxCol("RearCone", new Vector3(0f, 1.85f, -1.1f), new Vector3(1.3f, 1.2f, 1.0f));
        BoxCol("Nose", new Vector3(0f, 1.2f, 3.25f), new Vector3(1.6f, 0.9f, 0.5f));
        BoxCol("Windscreen", new Vector3(0f, 2.1f, 2.85f), new Vector3(1.8f, 0.7f, 0.12f));
        BoxCol("CockpitWall_R", new Vector3(0.97f, 1.72f, 2.25f), new Vector3(0.1f, 1.5f, 1.7f));
        BoxCol("CockpitWall_L", new Vector3(-0.97f, 1.72f, 2.25f), new Vector3(0.1f, 1.5f, 1.7f));
        foreach (var n in new[] { "CabinWallUpper_R", "CabinWallUpper_L" }) { var t = cols.Find(n); if (t) Object.DestroyImmediate(t.gameObject); } // (a lintel would catch heads on the way in)
        BoxCol("Bulkhead_L", new Vector3(-0.7f, 1.77f, 1.46f), new Vector3(0.5f, 1.64f, 0.12f));  // gap between them 0.9 m wide
        BoxCol("Bulkhead_R", new Vector3(0.7f, 1.77f, 1.46f), new Vector3(0.5f, 1.64f, 0.12f));
        BoxCol("TailBoom", new Vector3(0f, 1.95f, -3.0f), new Vector3(0.6f, 0.6f, 3.4f));
        BoxCol("TailFin", new Vector3(0f, 2.65f, -4.65f), new Vector3(0.12f, 1.2f, 0.7f));
        BoxCol("Stabiliser", new Vector3(0f, 2.35f, -3.85f), new Vector3(2.0f, 0.1f, 0.42f));
        BoxCol("Skid_R", new Vector3(0.98f, 0.08f, 0.75f), new Vector3(0.12f, 0.12f, 3.5f));
        BoxCol("Skid_L", new Vector3(-0.98f, 0.08f, 0.75f), new Vector3(0.12f, 0.12f, 3.5f));
        // (seat, dashboard, bulkhead and handle colliders from the interior stay as they are)

        // the old hull clip component (only meaningful for the Meshy hull)
        foreach (var c in heli.GetComponentsInChildren<HullInteriorClip>(true)) Object.DestroyImmediate(c);

        // the hoist hook hangs from the interior's M_HoistTip: re-wire it in case the marker moved
        EditorApplication.ExecuteMenuItem("Tools/Rescue Heli/Build Winch");

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(heli.gameObject.scene);
        Debug.Log("[Body] helicopter body built: rotors " + (ctrl.mainRotor && ctrl.tailRotor) + ", doors " + (heli.Find("Door_L") && heli.Find("Door_R")));
    }

    /// <summary>Point every Mat_<Name> in the FBX at Materials/Interior/Mat_<Name>.mat (creating missing ones from the palette).</summary>
    static void RemapMaterials(string fbx)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
        if (imp == null) { Debug.LogWarning("[Body] no importer for " + fbx); return; }
        bool changed = false;
        if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
        if (!imp.isReadable) { imp.isReadable = true; changed = true; }   // (the lining is a camera-block mesh collider)
        var stylized = Shader.Find("RescueGame/Stylized Lit");
        foreach (var id in imp.GetExternalObjectMap().Keys.Concat(SourceMaterials(fbx)).Distinct())
        {
            if (id.type != typeof(Material)) continue;
            string name = id.name;
            string path = Mats + (name == "Mat_Glass" ? "Glass" : name) + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(stylized);
                m.SetColor("_BaseColor", Palette(name));
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.HasProperty("_BaseColor") && m.GetColor("_BaseColor") == Color.magenta && Palette(name) != Color.magenta)
            {
                m.SetColor("_BaseColor", Palette(name)); EditorUtility.SetDirty(m);   // (made before its colour was known)
            }
            var map = imp.GetExternalObjectMap();
            if (!map.TryGetValue(id, out var cur) || cur != m) { imp.AddRemap(id, m); changed = true; }
        }
        if (changed) { imp.SaveAndReimport(); }
    }

    static System.Collections.Generic.IEnumerable<AssetImporter.SourceAssetIdentifier> SourceMaterials(string fbx)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            if (o is Material m) yield return new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
    }

    static Color Palette(string matName)
    {
        switch (matName.Replace("Mat_", ""))
        {
            case "HullRed": return new Color(0.74f, 0.17f, 0.11f);
            case "PadCream": return new Color(0.86f, 0.80f, 0.66f);
            case "PadRed": return new Color(0.70f, 0.24f, 0.18f);
            case "Grey": return new Color(0.45f, 0.45f, 0.47f);
            case "DarkGrey": return new Color(0.17f, 0.17f, 0.19f);
            case "Black": return new Color(0.06f, 0.06f, 0.07f);
            case "Metal": return new Color(0.62f, 0.63f, 0.66f);
            case "Tape": return new Color(0.68f, 0.68f, 0.64f);
            case "Yellow": return new Color(0.96f, 0.74f, 0.12f);
            case "White": return new Color(0.92f, 0.92f, 0.90f);
            case "Rubber": return new Color(0.12f, 0.12f, 0.12f);
            case "RedKnob": return new Color(0.85f, 0.10f, 0.08f);
            case "Olive": return new Color(0.36f, 0.42f, 0.30f);
            case "HullRedDark": return new Color(0.55f, 0.12f, 0.09f);
            case "Primer": return new Color(0.52f, 0.50f, 0.46f);
            case "Soot": return new Color(0.12f, 0.10f, 0.09f);
            case "Cardboard": return new Color(0.62f, 0.48f, 0.30f);
            default: return Color.magenta;
        }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
        return null;
    }
}
