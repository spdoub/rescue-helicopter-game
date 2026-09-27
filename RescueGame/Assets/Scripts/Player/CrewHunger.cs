using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The crew's pizza (the PRD's other consumable). Each crew member gets hungry over a mission; starving
/// crew have weak hands (the grab joints break easily, so they lose their grip on the handles and the
/// hook when the cabin lurches). A pizza box in the cabin has slices: look at it, press F to eat one.
/// The box is refilled when the helicopter is parked on the home pad. Host-owned numbers, synced.
/// Lives on each NetworkPlayer.
/// </summary>
public class CrewHunger : NetworkBehaviour
{
    [Tooltip("Minutes from full to starving while aboard.")]
    public float minutesToStarve = 6f;
    public float weakBelow = 0.25f, weakGrip = 0.35f;

    public NetworkVariable<float> Fullness = new(1f);
    public bool Starving => Fullness.Value < weakBelow;

    NetworkPlayer me; CrewBody body; float full = 1f, baseGrip;

    void Awake() { me = GetComponent<NetworkPlayer>(); body = GetComponent<CrewBody>(); baseGrip = body ? body.grabBreakForce : 6000f; }

    void FixedUpdate()
    {
        if (!IsSpawned) return;
        if (IsServer && !me.IsNpc)
        {
            if (me.OnHelicopter.Value || me.IsSeated) full = Mathf.Max(0f, full - Time.fixedDeltaTime / (minutesToStarve * 60f));
            if (Mathf.Abs(full - Fullness.Value) > 0.005f || (full == 0f && Fullness.Value != 0f)) Fullness.Value = full;
        }
        // weak hands (applied wherever this body is simulated: the host)
        if (body != null) body.grabBreakForce = baseGrip * (Fullness.Value < weakBelow ? Mathf.Lerp(weakGrip, 1f, Fullness.Value / weakBelow) : 1f);
    }

    /// <summary>Host: ate a slice.</summary>
    public void Feed(float amount) { full = Mathf.Min(1f, full + amount); Fullness.Value = full; }
}

/// <summary>The pizza box in the cabin: built from primitives at load, slices go as they're eaten, refills on the home pad.</summary>
public class PizzaBox : NetworkBehaviour
{
    public Material paintMaterial;
    public AudioClip munch;
    public int slicesPerBox = 8;
    public NetworkVariable<byte> Slices = new(8);
    public static PizzaBox Instance { get; private set; }

    Transform box, lid; readonly Transform[] slices = new Transform[8];
    HeliFuel fuel;

    void Awake()
    {
        Instance = this;
        fuel = GetComponent<HeliFuel>();
        Build();
    }
    public override void OnDestroy() { if (Instance == this) Instance = null; base.OnDestroy(); }

    public Vector3 BoxPosition => box ? box.position : transform.TransformPoint(new Vector3(-0.55f, 1.0f, 0.15f));

    public bool Aimed(Camera cam)
    {
        if (cam == null) return false;
        Vector3 to = BoxPosition + Vector3.up * 0.05f - cam.transform.position;
        return to.magnitude < 1.6f && Vector3.Angle(cam.transform.forward, to) < 14f;
    }

    void Build()
    {
        // on the cabin floor, left side, just forward of the bench (out of the walkway to the winch box)
        box = new GameObject("PizzaBox").transform; box.SetParent(transform, false);
        box.localPosition = new Vector3(-0.55f, 0.96f, 0.15f); box.localRotation = Quaternion.Euler(0f, 12f, 0f);
        var card = new Color(0.78f, 0.62f, 0.38f);
        Part(box, PrimitiveType.Cube, "Base", new Vector3(0f, 0.02f, 0f), new Vector3(0.42f, 0.04f, 0.42f), card);
        lid = Part(box, PrimitiveType.Cube, "Lid", new Vector3(0f, 0.045f, -0.21f), new Vector3(0.42f, 0.01f, 0.42f), card).transform;
        // lid open: hinged at the back, standing up-ish
        lid.localPosition = new Vector3(0f, 0.25f, -0.21f); lid.localRotation = Quaternion.Euler(-70f, 0f, 0f);
        Part(lid, PrimitiveType.Cube, "LidLogo", new Vector3(0f, 0.006f, 0f), new Vector3(0.2f, 0.002f, 0.12f), new Color(0.8f, 0.2f, 0.15f));
        for (int i = 0; i < 8; i++)
        {
            var s = new GameObject("Slice" + i).transform; s.SetParent(box, false);
            s.localRotation = Quaternion.Euler(0f, i * 45f, 0f);
            Part(s, PrimitiveType.Cube, "Crust", new Vector3(0f, 0.052f, 0.1f), new Vector3(0.14f, 0.02f, 0.17f), new Color(0.9f, 0.72f, 0.4f));
            Part(s, PrimitiveType.Cube, "Cheese", new Vector3(0f, 0.064f, 0.09f), new Vector3(0.11f, 0.006f, 0.13f), new Color(0.98f, 0.85f, 0.45f));
            Part(s, PrimitiveType.Cylinder, "Pepperoni", new Vector3(0.02f, 0.07f, 0.1f), new Vector3(0.04f, 0.003f, 0.04f), new Color(0.6f, 0.15f, 0.12f));
            slices[i] = s;
        }
    }

    void Update()
    {
        if (!IsSpawned) return;
        for (int i = 0; i < 8; i++) if (slices[i]) slices[i].gameObject.SetActive(i < Slices.Value);
        if (IsServer && fuel != null && fuel.Refuelling && Slices.Value < slicesPerBox) { refill += Time.deltaTime; if (refill > 2f) { refill = 0f; Slices.Value++; } }
    }
    float refill;

    [Rpc(SendTo.Server)]
    public void EatRpc(RpcParams p = default)
    {
        if (Slices.Value == 0) return;
        ulong who = p.Receive.SenderClientId;
        foreach (var np in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            if (np.OwnerClientId == who && !np.IsNpc) { var h = np.GetComponent<CrewHunger>(); if (h) h.Feed(0.35f); }
        Slices.Value--;
        MunchRpc();
    }

    [Rpc(SendTo.Everyone)]
    void MunchRpc() { if (munch) AudioSource.PlayClipAtPoint(munch, BoxPosition, 0.9f); }

    GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 local, Vector3 scale, Color colour)
    {
        var p = GameObject.CreatePrimitive(type); p.name = name; DestroyImmediate(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false); p.transform.localPosition = local; p.transform.localScale = scale;
        var r = p.GetComponent<Renderer>(); if (paintMaterial) r.sharedMaterial = paintMaterial;
        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", colour); r.SetPropertyBlock(mpb);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return p;
    }
}
