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
    float holdY; Vector3 holdXZ, windTrim;
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
        if (phase == Phase.ToPickup || phase == Phase.Hold)
        {
            // land at whichever drop-off is nearest when they're aboard (the cliff station, if that's closer than the hospital)
            RescueZone best = null; float bd = float.MaxValue;
            foreach (var z in RescueZone.All) { float d = Vector3.Distance(heli ? heli.transform.position : Vector3.zero, z.transform.position); if (d < bd) { bd = d; best = z; } }
            if (best != null) zone = best;
        }
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
                // Ledge Resident: side-on to his wall, winch side in, rotor over the roof; hook just off the ledge's edge.
                float heading = 0f;
                Quaternion rot = h.rotation;
                bool ledge = survivor.kind == SurvivorBrain.Kind.LedgeResident;
                if (ledge)
                {
                    var ls = survivor.LedgeSpot.Value;
                    over = new Vector3(ls.x, ls.y + 1f, ls.z) + Quaternion.Euler(0f, ls.w, 0f) * Vector3.forward * 1.0f;
                    heading = ls.w + 90f;
                    rot = Quaternion.Euler(0f, heading, 0f);
                }
                Vector3 target = over - rot * new Vector3(1.4f, 0f, 0.85f);
                // Hover height is fixed once over the pickup (following her up while she's
                // winched up would just climb forever).
                if (phase == Phase.ToPickup) holdY = survivor.transform.position.y + 9f;
                target.y = holdY;
                // Cruise above the rooftops, only coming down once nearly there.
                if (phase == Phase.ToPickup && Flat(target - h.position).magnitude > 25f) target.y = Mathf.Max(holdY, Mathf.Max(CruiseAltitude, survivor.transform.position.y + 40f));
                // The chair can't walk to the hook: in wind the hook hangs off to one side, so
                // creep the hover over until the hook itself is above the strap (as a pilot would).
                if (phase == Phase.Hold && (survivor.chair != null || ledge) && survivor.Current == SurvivorBrain.State.Waiting)
                {
                    var w = heli.GetComponent<NetworkWinch>();
                    if (w && w.hook && w.Length.Value > 2f)
                    {
                        Vector3 miss = Flat(over - w.hook.position);
                        bool hangingFree = Flat(w.hook.position - w.TipPosition).magnitude < 4f && w.hook.position.y > over.y - 1.5f;
                        if (hangingFree && miss.magnitude > 0.4f) holdXZ += Vector3.ClampMagnitude(miss, 1f) * 0.5f * Time.deltaTime;
                    }
                }
                if (phase == Phase.Hold && ledge)
                {
                    // keep the hoist well out from the wall so the cable clears the roof's cornice
                    var ls = survivor.LedgeSpot.Value;
                    Vector3 n = Quaternion.Euler(0f, ls.w, 0f) * Vector3.forward, wallPt = new Vector3(ls.x, 0f, ls.z) - n * 0.45f;
                    Vector3 tipXZ = Flat(holdXZ + rot * new Vector3(1.4f, 0f, 0.85f));
                    float outFromWall = Vector3.Dot(tipXZ - Flat(wallPt), n);
                    if (outFromWall < 1.4f) holdXZ += n * (1.4f - outFromWall);   // (past any cornice or parapet above the ledge)
                }
                if (phase == Phase.Hold) { target.x = holdXZ.x; target.z = holdXZ.z; }
                if (phase == Phase.ToPickup && Flat(target - h.position).magnitude < (ledge ? 2.5f : 1.2f) && vel.magnitude < (ledge ? 1.5f : 1f)
                    && (!ledge || Mathf.Abs(Mathf.DeltaAngle(h.eulerAngles.y, heading)) < 15f)) { Next(Phase.Hold); holdXZ = target; }
                // Ledge: hold the spot where the helicopter should be even in wind (lean into it: an integral term)
                if (phase == Phase.Hold && ledge)
                {
                    Vector3 lag = Flat(target - h.position);
                    windTrim = Vector3.ClampMagnitude(windTrim + lag * 0.4f * Time.deltaTime, 4f);
                    target += windTrim;
                }
                bool aboard = survivor.Current == SurvivorBrain.State.Seated || survivor.Current == SurvivorBrain.State.Meddling;
                if (aboard && seatedAt < 0f) seatedAt = Time.time;
                if (aboard && since > 1f && Time.time - seatedAt >= lingerAfterBoarding) Next(Phase.ToHospital);
                // (turn to the ledge heading only once close: arrive nose-first, then swing side-on)
                // (ledge: only once nearly stopped close by, and slowly: crew are standing at an open door)
                bool turnIn = ledge && (phase == Phase.Hold || (Flat(target - h.position).magnitude < 10f && Flat(vel).magnitude < 3f));
                float far = Flat(target - h.position).magnitude;
                float cruise = far > 120f ? 18f : 7f;   // long legs out to the island: get a move on
                return FlyTo(h, target, phase == Phase.Hold ? 2f : cruise, turnIn ? heading : (ledge ? h.eulerAngles.y : 0f), ledge ? 0.3f : 1f);
            }

            case Phase.ToHospital:
            {
                if (zone == null) return Hover(h.position, 0f);
                Vector3 target = zone.transform.position + Vector3.up * 14f;
                if (Flat(target - h.position).magnitude > 25f) target.y = Mathf.Max(CruiseAltitude, h.position.y - 2f * Time.deltaTime * 6f); // (descend gently from the mountains)
                if (Flat(target - h.position).magnitude < 1.5f && vel.magnitude < 1.2f && Mathf.Abs(Mathf.DeltaAngle(h.eulerAngles.y, LandHeading())) < 10f) Next(Phase.Land);
                return FlyTo(h, target, Flat(target - h.position).magnitude > 120f ? 18f : 8f, Flat(target - h.position).magnitude < 20f ? LandHeading() : 0f);
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
    HelicopterInput FlyTo(Transform h, Vector3 target, float maxSpeed, float heading, float maxYaw = 1f)
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
            yaw = Mathf.Clamp(Mathf.DeltaAngle(h.eulerAngles.y, heading) / 30f, -maxYaw, maxYaw),
        };
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
