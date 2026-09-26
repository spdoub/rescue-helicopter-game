using UnityEngine;

/// <summary>
/// Gives the first-person camera an exact copy of the helicopter's curved inner shell to bump into,
/// so it can never poke through the cabin walls or ceiling.
/// A detailed (non-convex) mesh collider can't be part of the flying helicopter's own physics body,
/// so this lives on a separate invisible object that follows the helicopter every frame, on the
/// CameraBlock layer (collides with nothing; only the camera looks at it).
/// </summary>
[DefaultExecutionOrder(-100)] // move before the camera places itself
public class CameraBlocker : MonoBehaviour
{
    public Mesh mesh;
    Transform follower;

    void Start()
    {
        if (mesh == null || GameLayers.CameraBlock < 0) return;
        var go = new GameObject(name + " (camera block)") { layer = GameLayers.CameraBlock };
        follower = go.transform;
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        Follow();
    }

    void OnDestroy() { if (follower) Destroy(follower.gameObject); }

    void LateUpdate() => Follow();

    void Follow()
    {
        if (follower == null) return;
        // Sit where the helicopter's own colliders are for physics (not the smoothed drawn pose),
        // matching how the camera does its wall check while riding.
        var heli = GetComponentInParent<Rigidbody>();
        Matrix4x4 m = (heli ? FirstPersonSeatCamera.RenderToPhysics(heli.transform) : Matrix4x4.identity) * transform.localToWorldMatrix;
        follower.SetPositionAndRotation(m.GetColumn(3), m.rotation);
        follower.localScale = transform.lossyScale;
        Physics.SyncTransforms();
    }
}
