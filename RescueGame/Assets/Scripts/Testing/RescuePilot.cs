using UnityEngine;

/// <summary>
/// Automated test pilot for the full rescue: take off, hover so the hoist hangs over the survivor,
/// wait until they're aboard and strapped in, fly to the hospital pad and land.
/// Works on whichever machine holds the pilot seat (reads positions, so it's fine on a client).
/// </summary>
public class RescuePilot
{
    enum Phase { TakeOff, ToPickup, Hold, ToHospital, Land, Done }

    readonly SurvivorBrain.Kind target;
    const float CruiseAltitude = 48f; // above the tallest buildings
    readonly float lingerAfterBoarding;
    /// <param name="lingerAfterBoarding">Keep hovering this long after they're aboard before heading to the hospital (tests).</param>
    public RescuePilot(SurvivorBrain.Kind target = SurvivorBrain.Kind.StrongGrandma, float lingerAfterBoarding = 0f)
    { this.target = target; this.lingerAfterBoarding = lingerAfterBoarding; }
    float seatedAt = -1f;

    Phase phase;
    float holdY; Vector3 holdXZ;
    float phaseSince;
    Vector3 lastPos; float lastTime = -1f; Vector3 vel;
    NetworkHelicopter heli;
    SurvivorBrain survivor;
    RescueZone zone;

    public string Status => phase.ToString();

    public HelicopterInput Tick(float t)
    {
        if (heli == null) heli = Object.FindAnyObjectByType<NetworkHelicopter>();
        if (survivor == null) survivor = SurvivorBrain.Find(target);
        if (zone == null) zone = Object.FindAnyObjectByType<RescueZone>();
        if (heli == null) return default;

        var h = heli.transform;
        // Velocity from positions (the helicopter is kinematic on clients, so no rigidbody speed there).
        if (lastTime >= 0f && Time.time > lastTime) vel = Vector3.Lerp(vel, (h.position - lastPos) / (Time.time - lastTime), 0.3f);
        lastPos = h.position; lastTime = Time.time;
        float since = Time.time - phaseSince;

        switch (phase)
        {
            case Phase.TakeOff:
                // Wait for the crew to climb aboard first (give up waiting after a while).
                if (!CrewAboard() && t < 30f) { phaseSince = Time.time; return default; }
                if (Time.time - phaseSince < 2f) return default;
                if (h.position.y > 12f) Next(Phase.ToPickup);
                return new HelicopterInput { collective = 1f };

            case Phase.ToPickup:
            case Phase.Hold:
            {
                if (survivor == null) return Hover(h.position + Vector3.up * 0f, 0f);
                // Put the hoist tip (helicopter-local 1.4, _, 0.85) straight above the survivor.
                // (Recliner Guy: over his chair's lifting strap)
                Vector3 over = survivor.chair != null ? survivor.chair.liftPoint.position : survivor.transform.position;
                Vector3 target = over - h.rotation * new Vector3(1.4f, 0f, 0.85f);
                // Hover height is fixed once over the pickup (following her up while she's
                // winched up would just climb forever).
                if (phase == Phase.ToPickup) holdY = survivor.transform.position.y + 9f;
                target.y = holdY;
                // Cruise above the rooftops, only coming down once nearly there.
                if (phase == Phase.ToPickup && Flat(target - h.position).magnitude > 25f) target.y = Mathf.Max(holdY, CruiseAltitude);
                if (phase == Phase.Hold) { target.x = holdXZ.x; target.z = holdXZ.z; }
                if (phase == Phase.ToPickup && Flat(target - h.position).magnitude < 1.2f && vel.magnitude < 1f) { Next(Phase.Hold); holdXZ = target; }
                bool aboard = survivor.Current == SurvivorBrain.State.Seated || survivor.Current == SurvivorBrain.State.Meddling;
                if (aboard && seatedAt < 0f) seatedAt = Time.time;
                if (aboard && since > 1f && Time.time - seatedAt >= lingerAfterBoarding) Next(Phase.ToHospital);
                return FlyTo(h, target, phase == Phase.Hold ? 2f : 7f, 0f);
            }

            case Phase.ToHospital:
            {
                if (zone == null) return Hover(h.position, 0f);
                Vector3 target = zone.transform.position + Vector3.up * 14f;
                if (Flat(target - h.position).magnitude > 25f) target.y = CruiseAltitude;
                if (Flat(target - h.position).magnitude < 1.5f && vel.magnitude < 1.2f && Mathf.Abs(Mathf.DeltaAngle(h.eulerAngles.y, LandHeading())) < 10f) Next(Phase.Land);
                return FlyTo(h, target, 8f, Flat(target - h.position).magnitude < 20f ? LandHeading() : 0f);
            }

            case Phase.Land:
            {
                Vector3 target = zone.transform.position;
                var input = FlyTo(h, target, 1.5f, LandHeading());
                float height = h.position.y - zone.transform.position.y;
                input.collective = height > 3f ? -0.6f : -0.25f;
                bool onGround = Physics.Raycast(h.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit g, 0.8f, GameLayers.Gameplay, QueryTriggerInteraction.Ignore)
                                && g.rigidbody != heli.GetComponent<Rigidbody>();
                if (onGround && Mathf.Abs(vel.y) < 0.3f && since > 1f) Next(Phase.Done);
                return input;
            }

            default:
                // Landed at the hospital. When the next call comes in, go again.
                if (survivor != null && survivor.Current == SurvivorBrain.State.Waiting && since > 2f) Next(Phase.TakeOff);
                return default; // collective at rest, engine idles
        }
    }

    /// <summary>Land nose-in toward the hospital door, so the tail points away from the building.</summary>
    float LandHeading()
    {
        if (zone == null || zone.exitPoint == null) return 0f;
        Vector3 d = Flat(zone.exitPoint.position - zone.transform.position);
        return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d).eulerAngles.y : 0f;
    }

    static bool CrewAboard()
    {
        foreach (var p in Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            if (!p.IsNpc && !p.SeatedAsPilot.Value && p.OnHelicopter.Value) return true;
        return false;
    }

    void Next(Phase p) { phase = p; phaseSince = Time.time; }

    HelicopterInput Hover(Vector3 at, float heading) => FlyTo(heli.transform, at, 2f, heading);

    /// <summary>Tilt toward the target (speed limited, braking as it arrives), climb/descend to its height, hold a heading.</summary>
    HelicopterInput FlyTo(Transform h, Vector3 target, float maxSpeed, float heading)
    {
        Vector3 err = Flat(target - h.position);
        Vector3 wantVel = err.normalized * Mathf.Min(maxSpeed, err.magnitude * 0.7f);
        Vector3 dv = wantVel - Flat(vel);
        Vector3 fwd = Flat(h.forward).normalized, right = Vector3.Cross(Vector3.up, fwd);
        return new HelicopterInput
        {
            pitch = Mathf.Clamp(Vector3.Dot(dv, fwd) * 0.25f, -0.5f, 0.5f),
            roll = Mathf.Clamp(Vector3.Dot(dv, right) * 0.25f, -0.5f, 0.5f),
            collective = Mathf.Clamp((target.y - h.position.y) * 0.35f - vel.y * 0.1f, -0.45f, 1f), // gentle descents (crew aboard)
            yaw = Mathf.Clamp(Mathf.DeltaAngle(h.eulerAngles.y, heading) / 30f, -1f, 1f),
        };
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
