using UnityEngine;

/// <summary>
/// The outer helicopter model is a solid shell, and bits of it (door frame edges, the shell's
/// thickness) poke into the cabin. This hides those bits using boxes in the hull's own space that
/// sit just inside the skin, so nothing is cut out of the outside of the helicopter.
/// The door zone also opens up the doorways, since the outer model has its doors painted shut.
/// Put on the outer hull model (the object with the MeshRenderer).
/// </summary>
[ExecuteAlways]
public class HullInteriorClip : MonoBehaviour
{
    [System.Serializable] public struct Zone { public Vector3 min, max; }

    public Zone[] zones =
    {
        new() { min = new Vector3(-0.86f, 0.97f, -0.45f), max = new Vector3(0.86f, 2.05f, 1.42f) }, // cabin, lower
        new() { min = new Vector3(-0.58f, 2.05f, -0.45f), max = new Vector3(0.58f, 2.6f, 1.42f) },  // cabin, under the roof
        new() { min = new Vector3(-0.86f, 0.97f, 1.42f), max = new Vector3(0.86f, 2.05f, 2.62f) },  // cockpit, lower
        new() { min = new Vector3(-0.5f, 2.05f, 1.42f), max = new Vector3(0.5f, 2.18f, 2.62f) },    // cockpit, under the roof
        new() { min = new Vector3(-1.2f, 0.97f, -0.3f), max = new Vector3(1.2f, 2.26f, 1.37f) },    // the open side doors (both)
        new() { min = new Vector3(-0.86f, 1.74f, 2.62f), max = new Vector3(0.86f, 2.19f, 3.4f) },   // windscreen (see in/out through real glass)
    };

    const int MaxZones = 6;
    static readonly int MinId = Shader.PropertyToID("_ClipMin"), MaxId = Shader.PropertyToID("_ClipMax"), CountId = Shader.PropertyToID("_ClipBoxCount");
    MaterialPropertyBlock mpb;

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    void Apply()
    {
        var r = GetComponent<Renderer>();
        if (r == null) return;
        mpb ??= new MaterialPropertyBlock();
        var mins = new Vector4[MaxZones]; var maxs = new Vector4[MaxZones];
        int n = Mathf.Min(MaxZones, zones.Length);
        // Zones are written in helicopter space; the shader tests the hull mesh's own (rotated)
        // object space, so convert each box's corners into it.
        Transform heli = transform.parent != null ? transform.parent : transform;
        Matrix4x4 toObject = transform.worldToLocalMatrix * heli.localToWorldMatrix;
        for (int i = 0; i < n; i++)
        {
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? zones[i].min.x : zones[i].max.x,
                                         (c & 2) == 0 ? zones[i].min.y : zones[i].max.y,
                                         (c & 4) == 0 ? zones[i].min.z : zones[i].max.z);
                Vector3 o = toObject.MultiplyPoint3x4(corner);
                lo = Vector3.Min(lo, o); hi = Vector3.Max(hi, o);
            }
            mins[i] = lo; maxs[i] = hi;
        }
        r.GetPropertyBlock(mpb);
        mpb.SetVectorArray(MinId, mins);
        mpb.SetVectorArray(MaxId, maxs);
        mpb.SetFloat(CountId, n);
        r.SetPropertyBlock(mpb);
    }
}
