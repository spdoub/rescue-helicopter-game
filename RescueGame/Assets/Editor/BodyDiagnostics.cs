using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Headless check of the imported helicopter body: where each part sits (helicopter space) and which way its faces point.</summary>
public static class BodyDiagnostics
{
    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype.unity");
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-rebuildBody") >= 0) { HelicopterBodyBuilder.Build(); UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); }
        var heli = GameObject.Find("Helicopter").transform;
        foreach (var n in new[] { "Hull", "Lining", "Glass", "Jambs", "Door_R", "Cowling", "Fin", "Boom", "MainRotor", "TailRotor", "Door_L", "Skid_1_0", "Floor", "DashBody", "HoistBoom", "Cyclic", "Collective", "PilotSeatCushion" })
        {
            var t = heli.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
            if (t == null) { Debug.Log($"[Diag] {n}: MISSING"); continue; }
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { Debug.Log($"[Diag] {n}: no mesh (children {t.childCount}) localPos {t.localPosition} localRot {t.localRotation.eulerAngles}"); continue; }
            var m = mf.sharedMesh;
            var v = m.vertices; var tris = m.triangles;
            Vector3 min = Vector3.one * 1e9f, max = -min;
            var toHeli = heli.worldToLocalMatrix * t.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++) { var p = toHeli.MultiplyPoint3x4(v[i]); min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            int outward = 0, inward = 0, noseOut = 0, noseIn = 0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = toHeli.MultiplyPoint3x4(v[tris[i]]), b = toHeli.MultiplyPoint3x4(v[tris[i + 1]]), c = toHeli.MultiplyPoint3x4(v[tris[i + 2]]);
                Vector3 nrm = Vector3.Cross(b - a, c - a); Vector3 ctr = (a + b + c) / 3f;
                Vector3 away = new Vector3(ctr.x, ctr.y - 1.72f, 0f);
                bool o = Vector3.Dot(nrm, away) > 0f;
                if (o) outward++; else inward++;
                if (ctr.z > 1.5f) { if (o) noseOut++; else noseIn++; }
            }
            Debug.Log($"[Diag] {n}: verts {v.Length} tris {tris.Length / 3} heli-space bounds {min} .. {max}  outward {outward} inward {inward}  (z>1.5: out {noseOut} in {noseIn})  mats {string.Join(",", t.GetComponent<Renderer>().sharedMaterials.Select(x => x ? x.name : "null"))}  readable {m.isReadable}");
        }
        var imp = (ModelImporter)AssetImporter.GetAtPath("Assets/Art/Helicopters/Medevac/Body/Body.fbx");
        Debug.Log($"[Diag] importer: readable {imp.isReadable} scale {imp.globalScale} useFileScale {imp.useFileScale} bakeAxis {imp.bakeAxisConversion} remaps {imp.GetExternalObjectMap().Count}");
        var ctrl = heli.GetComponent<HelicopterController>();
        Debug.Log($"[Diag] rotors: main {(ctrl.mainRotor ? heli.InverseTransformPoint(ctrl.mainRotor.position).ToString() : "null")} tail {(ctrl.tailRotor ? heli.InverseTransformPoint(ctrl.tailRotor.position).ToString() : "null")}");
        foreach (var n in new[] { "M_HoistTip", "M_GripCyclic", "M_BenchSeat_0", "M_PilotSeat", "M_Gauge_Airspeed" })
        {
            var t = heli.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
            Debug.Log($"[Diag] marker {n}: {(t ? heli.InverseTransformPoint(t.position).ToString("F2") : "MISSING")}");
        }
        foreach (var c in heli.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger) continue;
            var b = c.bounds; var min = heli.InverseTransformPoint(b.min); var max = heli.InverseTransformPoint(b.max);
            Debug.Log($"[Diag] collider {c.name} ({c.GetType().Name}) layer {c.gameObject.layer} heli-space {min:F2} .. {max:F2}");
        }
        foreach (var d in heli.GetComponentsInChildren<SlidingDoor>()) Debug.Log($"[Diag] door {d.name} left {d.left} localPos {d.transform.localPosition} col {(d.GetComponent<BoxCollider>() ? d.GetComponent<BoxCollider>().size.ToString() : "none")}");
    }
}
