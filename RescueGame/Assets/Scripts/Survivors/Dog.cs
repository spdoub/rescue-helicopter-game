using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Biscuit, Dog Person's dog (host-authoritative physics animal).
///  Roaming  - zooms around his roof, never quite where you want him.
///  Chasing  - dogs love things that hold still: if the rescue hook hangs low and steady nearby he
///             runs over and chomps onto it. Swing it about and he bolts instead.
///  Latched  - dangling from the hook by his teeth, delighted. Winch him up.
///  Aboard   - swung into the cabin, sits by the door wagging (rides along with the helicopter).
///  Following- after delivery, trots after his person.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Dog : NetworkBehaviour
{
    public enum State : byte { Roaming, Chasing, Latched, Boarding, Aboard, Following }

    public string dogName = "Biscuit";
    public float runSpeed = 5.5f;
    public float accel = 18f;
    [Tooltip("The hook must hang within this height of his roof, and move slower than this, for him to go for it.")]
    public float hookMaxHeight = 1.4f, hookMaxSpeed = 0.9f;
    [Tooltip("Where he rides in the cabin (helicopter space).")]
    public Vector3 cabinSpot = new(0.35f, 0.95f, 0.35f); // between the recliner's spot, the bench and the winch box
    public Transform model;
    public AudioClip[] barks;
    public AudioClip whine;
    AudioSource voice; float nextBark;

    public NetworkVariable<State> Stage = new();
    public State Current => Stage.Value;
    public bool Aboard => Stage.Value == State.Aboard;

    Rigidbody rb;
    NetworkHelicopter heli;
    NetworkWinch winch;
    ConfigurableJoint jaws;
    Bounds roof;           // the roof he roams (world)
    Vector3 target;
    float nextTarget, stateSince, fleeUntil;
    Transform follow;      // his person, after delivery
    Vector3 swingFrom; Quaternion swingRotFrom;
    const float SwingTime = 1.0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        voice = gameObject.AddComponent<AudioSource>();
        voice.spatialBlend = 1f; voice.minDistance = 3f; voice.maxDistance = 60f; voice.rolloffMode = AudioRolloffMode.Linear; voice.playOnAwake = false;
    }

    public override void OnNetworkSpawn()
    {
        rb.isKinematic = !IsServer;
        heli = FindAnyObjectByType<NetworkHelicopter>();
        winch = heli ? heli.GetComponent<NetworkWinch>() : null;
        if (IsServer) FindRoof();
    }

    /// <summary>The flat roof under him (so he knows where the edges are).</summary>
    public void FindRoof()
    {
        roof = new Bounds(transform.position, new Vector3(6f, 1f, 6f));
        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit hit, 5f, GameLayers.Gameplay & ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
        {
            var b = hit.collider.bounds;
            roof = new Bounds(new Vector3(b.center.x, hit.point.y, b.center.z), new Vector3(Mathf.Max(2f, b.size.x - 4f), 1f, Mathf.Max(2f, b.size.z - 4f)));
        }
        nextTarget = 0f;
    }

    void FixedUpdate()
    {
        if (!IsSpawned || !IsServer) return;
        float t = Time.time - stateSince;
        switch (Stage.Value)
        {
            case State.Roaming: Roam(); break;
            case State.Chasing: Chase(); break;
            case State.Latched: Dangle(); break;
            case State.Boarding: SwingIn(t); break;
            case State.Aboard: RideAlong(); break;
            case State.Following: Follow(); break;
        }
    }

    void Update()
    {
        // Barks: lots while chasing the hook, happy yaps on it, the odd whine in the cabin.
        if (Time.time > nextBark && barks != null && barks.Length > 0)
        {
            var st = Stage.Value;
            bool whineNow = st == State.Aboard && whine != null;
            voice.pitch = Random.Range(0.92f, 1.12f);
            voice.PlayOneShot(whineNow ? whine : barks[Random.Range(0, barks.Length)], 0.9f);
            nextBark = Time.time + st switch
            {
                State.Chasing => Random.Range(0.8f, 1.8f),
                State.Latched => Random.Range(1.5f, 3f),
                State.Aboard => Random.Range(8f, 15f),
                _ => Random.Range(3.5f, 8f),
            };
        }

        // Cartoon trot: a quick bounce and wiggle while he runs (everyone sees it).
        if (model == null) return;
        Vector3 v = rb.isKinematic ? Vector3.zero : rb.linearVelocity; v.y = 0f;
        float run = Mathf.Clamp01(v.magnitude / runSpeed);
        if (Stage.Value == State.Latched) run = 0.6f; // happy wriggle on the hook
        float ph = Time.time * 14f;
        model.localPosition = Vector3.up * (Mathf.Abs(Mathf.Sin(ph)) * 0.08f * run);
        model.localRotation = Quaternion.Euler(0f, Mathf.Sin(ph * 0.5f) * 8f * run, Mathf.Sin(ph) * 4f * run);
    }

    // ------------------------------------------------------------------ on the roof

    void Roam()
    {
        if (HookInviting(out Vector3 hookPos))
        {
            Enter(State.Chasing);
            return;
        }
        if (Time.time > nextTarget || Flat(target - transform.position).magnitude < 0.4f)
        {
            target = new Vector3(Random.Range(roof.min.x, roof.max.x), roof.center.y, Random.Range(roof.min.z, roof.max.z));
            nextTarget = Time.time + Random.Range(1.2f, 3.5f);
        }
        RunTo(target, Time.time < fleeUntil ? 1.2f : 0.8f);
    }

    void Chase()
    {
        if (winch == null || winch.hook == null) { Enter(State.Roaming); return; }
        Vector3 hookPos = winch.hook.position;
        // It moved about: scared, bolt the other way.
        if (winch.hook.linearVelocity.magnitude > hookMaxSpeed * 2.2f)
        {
            target = transform.position + Flat(transform.position - hookPos).normalized * 4f;
            fleeUntil = Time.time + 2.5f; nextTarget = Time.time + 2.5f;
            Enter(State.Roaming);
            return;
        }
        if (!HookInviting(out _) && Time.time - stateSince > 1f) { Enter(State.Roaming); return; }
        RunTo(hookPos, 1f);
        if (Vector3.Distance(transform.position + Vector3.up * 0.25f, hookPos) < 0.75f) Latch();
    }

    bool HookInviting(out Vector3 hookPos)
    {
        hookPos = default;
        if (winch == null || winch.hook == null || winch.Length.Value < 1.2f) return false;
        hookPos = winch.hook.position;
        float up = hookPos.y - roof.center.y;
        return Flat(hookPos - transform.position).magnitude < 4f && up > -0.2f && up < hookMaxHeight
               && winch.hook.linearVelocity.magnitude < hookMaxSpeed && Time.time > fleeUntil;
    }

    void RunTo(Vector3 p, float speedScale)
    {
        Vector3 to = Flat(p - transform.position);
        Vector3 want = to.sqrMagnitude > 0.01f ? to.normalized * runSpeed * speedScale : Vector3.zero;
        Vector3 v = rb.linearVelocity; Vector3 dv = want - new Vector3(v.x, 0f, v.z);
        rb.AddForce(Vector3.ClampMagnitude(dv * accel, 40f), ForceMode.Acceleration);
        if (to.sqrMagnitude > 0.04f) rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(to), 0.25f));
        // don't run off the roof
        Vector3 pos = rb.position;
        if (pos.x < roof.min.x - 0.5f || pos.x > roof.max.x + 0.5f || pos.z < roof.min.z - 0.5f || pos.z > roof.max.z + 0.5f)
            rb.AddForce(Flat(roof.center - pos).normalized * 30f, ForceMode.Acceleration);
    }

    // ------------------------------------------------------------------ on the hook and aboard

    void Latch()
    {
        jaws = gameObject.AddComponent<ConfigurableJoint>();
        jaws.autoConfigureConnectedAnchor = false;
        jaws.connectedBody = winch.hook;
        jaws.anchor = new Vector3(0f, 0.35f, 0.28f);          // his mouth
        jaws.connectedAnchor = new Vector3(0f, -0.35f, 0.05f); // bottom of the hook
        jaws.xMotion = jaws.yMotion = jaws.zMotion = ConfigurableJointMotion.Limited;
        jaws.linearLimit = new SoftJointLimit { limit = 0.05f };
        rb.freezeRotation = false;
        IgnoreHeli(true);
        Enter(State.Latched);
    }

    void Dangle()
    {
        if (jaws == null) { rb.freezeRotation = true; Enter(State.Roaming); return; }
        if (winch.Length.Value < 0.9f && Vector3.Distance(transform.position, winch.TipPosition) < 3.5f)
        {
            Destroy(jaws); jaws = null;
            swingFrom = heli.transform.InverseTransformPoint(transform.position);
            swingRotFrom = Quaternion.Inverse(heli.transform.rotation) * transform.rotation;
            rb.isKinematic = true;
            Enter(State.Boarding);
        }
    }

    void SwingIn(float t)
    {
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / SwingTime));
        Vector3 mid = new Vector3(Mathf.Max(swingFrom.x, 1.3f) * 0.8f, cabinSpot.y + 0.6f, (swingFrom.z + cabinSpot.z) * 0.5f);
        Vector3 p = Vector3.Lerp(Vector3.Lerp(swingFrom, mid, k), Vector3.Lerp(mid, cabinSpot, k), k);
        Quaternion r = Quaternion.Slerp(swingRotFrom, Quaternion.Euler(0f, -90f, 0f), k); // ends looking out of the door
        Place(p, r);
        if (t >= SwingTime) Enter(State.Aboard);
    }

    void RideAlong() => Place(cabinSpot, Quaternion.Euler(0f, -90f + Mathf.Sin(Time.time * 0.7f) * 25f, 0f));

    void Place(Vector3 local, Quaternion localRot)
    {
        rb.isKinematic = true;
        Vector3 p = heli.transform.TransformPoint(local); Quaternion r = heli.transform.rotation * localRot;
        rb.MovePosition(p); rb.MoveRotation(r);
    }

    /// <summary>Host: out of the helicopter and after his person (at the drop-off).</summary>
    public void FollowPerson(Transform person, Vector3 jumpOutAt)
    {
        follow = person;
        rb.isKinematic = false; rb.freezeRotation = true;
        rb.position = jumpOutAt; rb.linearVelocity = Vector3.zero;
        GetComponent<Unity.Netcode.Components.NetworkTransform>()?.Teleport(jumpOutAt, transform.rotation, transform.localScale);
        IgnoreHeli(false);
        Enter(State.Following);
    }

    void Follow()
    {
        if (follow == null) return;
        Vector3 to = Flat(follow.position - transform.position);
        if (to.magnitude > 1.2f) RunTo(follow.position, 0.9f);
        else { Vector3 v = rb.linearVelocity; rb.AddForce(-new Vector3(v.x, 0f, v.z) * 8f, ForceMode.Acceleration); }
    }

    /// <summary>Host: a new call - he's on a different roof now (next to his person).</summary>
    public void Relocate(Vector3 spot)
    {
        if (jaws) { Destroy(jaws); jaws = null; }
        follow = null;
        rb.isKinematic = false; rb.freezeRotation = true;
        transform.position = spot; rb.position = spot; rb.linearVelocity = Vector3.zero;
        GetComponent<Unity.Netcode.Components.NetworkTransform>()?.Teleport(spot, transform.rotation, transform.localScale);
        IgnoreHeli(false);
        FindRoof();
        Enter(State.Roaming);
    }

    void Enter(State s)
    {
        Stage.Value = s; stateSince = Time.time;
        // Riding in the cabin he's a passenger, not an obstacle (never shoves the crew about).
        bool ghost = s == State.Boarding || s == State.Aboard;
        foreach (var c in GetComponentsInChildren<Collider>()) c.isTrigger = ghost;
    }

    void IgnoreHeli(bool ignore)
    {
        if (heli == null) return;
        foreach (var a in GetComponentsInChildren<Collider>())
            foreach (var b in heli.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(a, b, ignore);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
