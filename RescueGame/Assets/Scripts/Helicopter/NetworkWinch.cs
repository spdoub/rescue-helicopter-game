using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The rescue hoist above the right cabin door (host-authoritative, like the helicopter).
/// A cable of adjustable length joins the hoist tip to a heavy hook. The hook is a real physics
/// object: crew and survivors grab it with their hands and get hauled up with it, and whatever
/// hangs on it pulls on the helicopter.
///
/// Controls: stand at the winch box by the right door post and hold R to reel in, Q to let out.
/// (The keys travel in PlayerInput; the host checks you're really at the box.)
/// </summary>
[RequireComponent(typeof(NetworkHelicopter))]
public class NetworkWinch : NetworkBehaviour
{
    [Tooltip("The hook (a separate networked physics object in the scene).")]
    public Rigidbody hook;
    [Tooltip("Where the cable leaves the hoist (M_HoistTip); found by name if empty.")]
    public Transform tip;
    [Tooltip("The control box people stand at (WinchBox); found by name if empty.")]
    public Transform controlBox;
    [Tooltip("Where the cable attaches on the hook, in the hook's space.")]
    public Vector3 hookEye = new(0f, 0.16f, 0f);

    public float minLength = 0.3f;
    public float maxLength = 25f;
    [Tooltip("Reel speed (m/s).")]
    public float speed = 1.6f;
    public float controlReach = 1.7f;
    public Material cableMaterial;

    /// <summary>Current cable length (m).</summary>
    public NetworkVariable<float> Length = new(0.3f);
    /// <summary>-1 letting out, +1 reeling in, 0 stopped (for lights and sound).</summary>
    public NetworkVariable<sbyte> Motion = new();

    Rigidbody heliRb;
    ConfigurableJoint joint;
    LineRenderer cable;
    sbyte request; float requestTime;
    float length;

    public Vector3 TipPosition => tip ? tip.position : transform.TransformPoint(1.4f, 2.36f, 0.85f);
    public Vector3 HookEyePosition => hook ? hook.transform.TransformPoint(hookEye) : TipPosition;

    /// <summary>Is this point (someone's eyes) close enough to work the winch box?</summary>
    public bool InReach(Vector3 p) => controlBox != null && Vector3.Distance(p, ControlPoint) < controlReach;

    /// <summary>Centre of the winch box (interior props keep their pivot at the helicopter origin).</summary>
    public Vector3 ControlPoint => transform.TransformPoint(controlLocal);
    Vector3 controlLocal;

    /// <summary>Host: someone at the box is holding a direction this tick.</summary>
    public void Operate(sbyte dir) { request = dir; requestTime = Time.time; }

    void Awake()
    {
        heliRb = GetComponent<Rigidbody>();
        if (tip == null) tip = FindDeep(transform, "M_HoistTip");
        if (controlBox == null) controlBox = FindDeep(transform, "WinchBox");
        length = minLength;
        if (controlBox != null)
        {
            var r = controlBox.GetComponent<Renderer>();
            controlLocal = transform.InverseTransformPoint(r ? r.bounds.center : controlBox.position);
        }

        cable = new GameObject("WinchCable").AddComponent<LineRenderer>();
        cable.transform.SetParent(transform, false);
        cable.useWorldSpace = true;
        cable.widthMultiplier = 0.022f;
        cable.numCapVertices = 2;
        cable.positionCount = 2;
        cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        cable.generateLightingData = true; // the lit stylised shader needs normals
        if (cableMaterial) cable.sharedMaterial = cableMaterial;
    }

    public override void OnNetworkSpawn()
    {
        if (hook == null) return;
        hook.isKinematic = !IsServer;
        // The hook never bumps the helicopter itself (it sits right against the hoist when stowed).
        foreach (var hc in hook.GetComponentsInChildren<Collider>())
            foreach (var c in GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(hc, c);
        if (!IsServer) return;

        hook.position = TipPosition - hook.rotation * hookEye + Vector3.down * minLength;
        joint = hook.gameObject.AddComponent<ConfigurableJoint>();
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = hookEye;
        joint.connectedBody = heliRb;
        joint.connectedAnchor = transform.InverseTransformPoint(TipPosition);
        joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
        joint.linearLimit = new SoftJointLimit { limit = length };
        joint.enableCollision = false;
    }

    void FixedUpdate()
    {
        if (!IsSpawned || !IsServer || joint == null) return;

        sbyte dir = Time.time - requestTime < 0.2f ? request : (sbyte)0;
        float before = length;
        length = Mathf.Clamp(length - dir * speed * Time.fixedDeltaTime, minLength, maxLength);
        if (length != before) joint.linearLimit = new SoftJointLimit { limit = length };
        if ((before == length) && dir != 0) dir = 0; // at the end stop
        if (Motion.Value != dir) Motion.Value = dir;
        if (Mathf.Abs(Length.Value - length) > 0.02f || (length == minLength && Length.Value != minLength)) Length.Value = length;

        // Stowed: calm the swinging so the hook doesn't flail against the hoist in flight.
        if (length <= minLength + 0.01f)
        {
            Vector3 rel = hook.linearVelocity - heliRb.GetPointVelocity(hook.position);
            hook.AddForce(-rel * 4f, ForceMode.Acceleration);
        }
    }

    void LateUpdate()
    {
        if (hook == null) return;
        Vector3 a = TipPosition, b = HookEyePosition;
        float d = Vector3.Distance(a, b);
        float L = IsSpawned ? Length.Value : minLength;
        // Slack cable sags into a gentle curve; a taut one is straight.
        float sag = L > d + 0.02f ? Mathf.Sqrt(L * L - d * d) * 0.35f : 0f;
        int n = sag > 0.01f ? 12 : 2;
        cable.positionCount = n;
        for (int i = 0; i < n; i++)
        {
            float t = i / (n - 1f);
            cable.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t)));
        }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
        return null;
    }
}
