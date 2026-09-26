using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Recliner Guy's beloved armchair (concept 3): a heavy networked physics object he never leaves.
/// The rescue hook latches onto its lifting strap; winched up it gets swung into the cabin and
/// strapped down there (a fixed joint, so the helicopter really carries its weight); at the
/// hospital it gets set down on the pad, him still in it. Host-authoritative.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Recliner : NetworkBehaviour
{
    [Tooltip("Where he sits (his seat pose).")]
    public Transform seatPoint;
    [Tooltip("The strap loop the hook latches onto.")]
    public Transform liftPoint;
    [Tooltip("Where on the hook the strap hangs (hook space).")]
    public Vector3 hookCatch = new(0f, -0.33f, 0.05f);

    public bool Latched => hookJoint != null;
    public bool Aboard => heliJoint != null;

    Rigidbody rb;
    ConfigurableJoint hookJoint;
    FixedJoint heliJoint;
    Collider[] mine;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        mine = GetComponentsInChildren<Collider>();
    }

    public override void OnNetworkSpawn() => rb.isKinematic = !IsServer;

    /// <summary>Hook it onto the winch hook by the strap.</summary>
    public void Latch(Rigidbody hook)
    {
        if (hookJoint != null) return;
        Ignore(hook.GetComponentsInChildren<Collider>(), true);
        hookJoint = gameObject.AddComponent<ConfigurableJoint>();
        hookJoint.autoConfigureConnectedAnchor = false;
        hookJoint.connectedBody = hook;
        hookJoint.anchor = transform.InverseTransformPoint(liftPoint.position);
        hookJoint.connectedAnchor = hookCatch;
        hookJoint.xMotion = hookJoint.yMotion = hookJoint.zMotion = ConfigurableJointMotion.Limited;
        hookJoint.linearLimit = new SoftJointLimit { limit = 0.08f };
        hookJoint.breakForce = Mathf.Infinity;
        // hang roughly level from the strap
        rb.centerOfMass = transform.InverseTransformPoint(liftPoint.position) + Vector3.down * 0.7f;
    }

    public void Unlatch()
    {
        if (hookJoint == null) return;
        var hook = hookJoint.connectedBody;
        Destroy(hookJoint); hookJoint = null;
        if (hook) Ignore(hook.GetComponentsInChildren<Collider>(), false);
        rb.ResetCenterOfMass();
    }

    /// <summary>Scripted move (kinematic) - used for the swing in/out of the cabin.</summary>
    public void Carry(Vector3 position, Quaternion rotation)
    {
        rb.isKinematic = true;
        rb.position = position; rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>Strap it down in the cabin: rides along as part of the helicopter.</summary>
    public void MountIn(Rigidbody heli)
    {
        Ignore(heli.GetComponentsInChildren<Collider>(), true);
        rb.isKinematic = false;
        heliJoint = gameObject.AddComponent<FixedJoint>();
        heliJoint.connectedBody = heli;
        heliJoint.breakForce = Mathf.Infinity;
    }

    /// <summary>Put it down somewhere (the hospital pad, a new roof).</summary>
    public void SetDown(Vector3 position, Quaternion rotation, Rigidbody heli)
    {
        Unlatch();
        if (heliJoint != null) { Destroy(heliJoint); heliJoint = null; }
        Carry(position, rotation);
        if (heli) Ignore(heli.GetComponentsInChildren<Collider>(), false);
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (nt) nt.Teleport(position, rotation, transform.localScale);
    }

    /// <summary>Pass through the crew's bodies (while it's being swung in). Turning it back off is
    /// gradual: anyone still standing inside the chair stays "ghosted" until they've stepped clear,
    /// so nobody gets shoved across the cabin.</summary>
    public void IgnoreCrew(bool ignore)
    {
        ghosted.Clear();
        foreach (var b in FindObjectsByType<CrewBody>(FindObjectsSortMode.None))
        {
            if (b.GetComponent<SurvivorBrain>()) continue;
            if (ignore) { Ignore(b.GetComponents<Collider>(), true); ghosted.Add(b); }
            else ghosted.Add(b); // restored in FixedUpdate once clear
        }
        restoring = !ignore;
    }

    readonly System.Collections.Generic.List<CrewBody> ghosted = new();
    bool restoring;

    void FixedUpdate()
    {
        if (!restoring) return;
        for (int i = ghosted.Count - 1; i >= 0; i--)
        {
            var b = ghosted[i];
            if (b == null) { ghosted.RemoveAt(i); continue; }
            bool overlapping = false;
            foreach (var bc in b.GetComponents<Collider>())
                foreach (var c in mine)
                    if (c && bc && c.bounds.Intersects(bc.bounds) &&
                        Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, bc, bc.transform.position, bc.transform.rotation, out _, out _))
                        overlapping = true;
            if (!overlapping) { Ignore(b.GetComponents<Collider>(), false); ghosted.RemoveAt(i); }
        }
        if (ghosted.Count == 0) restoring = false;
    }

    public void IgnoreHeli(Rigidbody heli, bool ignore) => Ignore(heli.GetComponentsInChildren<Collider>(), ignore);

    void Ignore(Collider[] others, bool ignore)
    {
        foreach (var a in mine) foreach (var b in others) if (a && b) Physics.IgnoreCollision(a, b, ignore);
    }
}
