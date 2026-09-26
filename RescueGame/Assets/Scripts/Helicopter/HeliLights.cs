using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The nose searchlight from concept 3: a warm spotlight plus a soft visible beam. The pilot
/// toggles it with L (synced). Also kicks up a ring of dust under the helicopter when it flies low
/// (rotor downwash), on every machine.
/// </summary>
[RequireComponent(typeof(NetworkHelicopter))]
public class HeliLights : NetworkBehaviour
{
    [Tooltip("Lamp position under the nose (helicopter space).")]
    public Vector3 lampPosition = new(0f, 0.75f, 3.35f);
    [Tooltip("How far down the beam points (degrees below level).")]
    public float beamTilt = 32f;
    public float beamLength = 22f;
    public Material beamMaterial;
    public Material dustMaterial;

    public NetworkVariable<bool> LightOn = new(true);

    Light spot;
    Transform beam;
    ParticleSystem dust;
    NetworkHelicopter heli;
    NetworkHelicopter heliNet => heli;
    HelicopterController controller;

    void Awake()
    {
        heli = GetComponent<NetworkHelicopter>();
        controller = GetComponent<HelicopterController>();
        var lamp = new GameObject("Searchlight").transform;
        lamp.SetParent(transform, false);
        lamp.localPosition = lampPosition;
        lamp.localRotation = Quaternion.Euler(beamTilt, 0f, 0f);
        spot = lamp.gameObject.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.86f, 0.5f);
        spot.intensity = 60f;
        spot.range = 55f;
        spot.spotAngle = 34f;
        spot.innerSpotAngle = 16f;
        spot.shadows = LightShadows.None;

        beam = new GameObject("Beam").transform;
        beam.SetParent(lamp, false);
        beam.gameObject.AddComponent<MeshFilter>().sharedMesh = Cone(0.12f, Mathf.Tan(15f * Mathf.Deg2Rad) * beamLength, beamLength, 16);
        var mr = beam.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = beamMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        dust = MakeDust();
    }

    void Update()
    {
        bool on = !IsSpawned || LightOn.Value;
        spot.enabled = on;
        beam.gameObject.SetActive(on && beamMaterial != null);

        if (heli.IsLocalPilot)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.lKey.wasPressedThisFrame) ToggleRpc();
        }

        // Downwash: dust on the ground under the rotor when it's low.
        float height = 99f;
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 30f, GameLayers.Gameplay, QueryTriggerInteraction.Ignore) && hit.rigidbody == null)
        {
            height = transform.position.y - hit.point.y;
            dust.transform.position = hit.point + Vector3.up * 0.1f;
        }
        var em = dust.emission;
        // (parked on the ground with the rotor idling: just a light stir)
        bool parked = controller != null && controller.IsLanded && heliNet.ControlsSync.Value.x <= 0.05f;
        em.rateOverTime = height < 14f ? Mathf.Lerp(80f, 0f, height / 14f) * (parked ? 0.15f : 1f) : 0f;
    }

    [Rpc(SendTo.Server)]
    void ToggleRpc() => LightOn.Value = !LightOn.Value;

    ParticleSystem MakeDust()
    {
        var go = new GameObject("Downwash");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        main.startColor = new Color(0.85f, 0.72f, 0.56f, 0.35f);
        main.maxParticles = 400;
        main.gravityModifier = -0.05f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 2.5f;
        shape.rotation = new Vector3(-90f, 0f, 0f);    // flat on the ground
        shape.radiusThickness = 0.2f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.radial = new ParticleSystem.MinMaxCurve(3f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = dustMaterial;
        ps.Play();
        return ps;
    }

    /// <summary>An open cone along +z with uv.y = distance along it (0..1), for the beam shader.</summary>
    static Mesh Cone(float r0, float r1, float length, int segs)
    {
        var v = new Vector3[(segs + 1) * 2]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length];
        var tris = new int[segs * 6];
        for (int i = 0; i <= segs; i++)
        {
            float a = i * Mathf.PI * 2f / segs;
            Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            v[i * 2] = d * r0; v[i * 2 + 1] = d * r1 + Vector3.forward * length;
            n[i * 2] = n[i * 2 + 1] = d;
            uv[i * 2] = new Vector2(i / (float)segs, 0f); uv[i * 2 + 1] = new Vector2(i / (float)segs, 1f);
            if (i < segs)
            {
                int k = i * 6, b = i * 2;
                tris[k] = b; tris[k + 1] = b + 1; tris[k + 2] = b + 2;
                tris[k + 3] = b + 2; tris[k + 4] = b + 1; tris[k + 5] = b + 3;
            }
        }
        var m = new Mesh { name = "BeamCone", vertices = v, normals = n, uv = uv, triangles = tris };
        m.RecalculateBounds();
        return m;
    }
}
