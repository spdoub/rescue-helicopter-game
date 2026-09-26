using Unity.Netcode;
using UnityEngine;

/// <summary>
/// All the helicopter's sounds, on every machine:
///  - rotor loop (pitch rises with the collective; muffled like headset audio when you're aboard)
///  - winch motor while the cable is moving
///  - the sliding doors opening/closing
///  - the hook clanking into things
///  - a cheerful jingle whenever a survivor is delivered anywhere
/// </summary>
[RequireComponent(typeof(NetworkHelicopter))]
public class HeliAudio : MonoBehaviour
{
    public AudioClip rotorLoop, winchMotor, doorSlide, hookClank, deliveredJingle;
    [Range(0f, 1f)] public float rotorVolume = 0.75f;
    [Tooltip("Low-pass cutoff (Hz) while you're aboard: rotor heard through a headset.")]
    public float aboardCutoff = 2600f;

    NetworkHelicopter heli;
    HelicopterController controller;
    NetworkWinch winch;
    AudioSource rotor, motor, oneShots;
    AudioLowPassFilter muffle;
    bool lastLeft, lastRight;
    int lastRescued;
    float nextClank;

    void Awake()
    {
        heli = GetComponent<NetworkHelicopter>();
        controller = GetComponent<HelicopterController>();
        winch = GetComponent<NetworkWinch>();

        rotor = Source("RotorAudio", transform, new Vector3(0f, 3f, 0.5f), rotorLoop, true, 10f, 300f);
        rotor.volume = rotorVolume;
        muffle = rotor.gameObject.AddComponent<AudioLowPassFilter>();
        muffle.cutoffFrequency = 22000f;
        rotor.Play();

        Vector3 tip = winch != null ? transform.InverseTransformPoint(winch.TipPosition) : new Vector3(1.4f, 2.4f, 0.85f);
        motor = Source("WinchAudio", transform, tip, winchMotor, true, 2f, 40f);
        oneShots = Source("CabinAudio", transform, new Vector3(0f, 1.6f, 0.6f), null, false, 2f, 50f);
    }

    void Start()
    {
        lastLeft = heli.DoorLeftOpen.Value; lastRight = heli.DoorRightOpen.Value;
        if (winch != null && winch.hook != null) winch.hook.gameObject.AddComponent<HookClank>().heliAudio = this;
    }

    static AudioSource Source(string name, Transform parent, Vector3 local, AudioClip clip, bool loop, float min, float max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var s = go.AddComponent<AudioSource>();
        s.clip = clip; s.loop = loop; s.playOnAwake = false;
        s.spatialBlend = 1f; s.minDistance = min; s.maxDistance = max;
        s.rolloffMode = AudioRolloffMode.Logarithmic; s.dopplerLevel = 0.3f;
        return s;
    }

    void Update()
    {
        // Rotor: a bit higher-pitched and louder when pulling up.
        float collective = heli.IsSpawned ? heli.ControlsSync.Value.x : (controller ? controller.CurrentInput.collective : 0f);
        rotor.pitch = Mathf.Lerp(rotor.pitch, 1f + collective * 0.08f, Time.deltaTime * 2f);
        rotor.volume = Mathf.Lerp(rotor.volume, rotorVolume * (1f + Mathf.Max(0f, collective) * 0.2f), Time.deltaTime * 2f);
        var me = NetworkPlayer.Local;
        bool aboard = me != null && (me.OnHelicopter.Value || me.IsSeated);
        muffle.cutoffFrequency = Mathf.Lerp(muffle.cutoffFrequency, aboard ? aboardCutoff : 22000f, Time.deltaTime * 4f);

        // Winch motor while the cable moves.
        bool moving = winch != null && winch.IsSpawned && winch.Motion.Value != 0;
        if (moving && !motor.isPlaying) motor.Play();
        if (!moving && motor.isPlaying) motor.Stop();
        if (moving) motor.pitch = winch.Motion.Value > 0 ? 1.05f : 0.92f;

        // Doors.
        if (heli.IsSpawned)
        {
            if (heli.DoorLeftOpen.Value != lastLeft) { lastLeft = heli.DoorLeftOpen.Value; PlayAt(doorSlide, new Vector3(-0.9f, 1.5f, 0.5f)); }
            if (heli.DoorRightOpen.Value != lastRight) { lastRight = heli.DoorRightOpen.Value; PlayAt(doorSlide, new Vector3(0.9f, 1.5f, 0.5f)); }
        }

        // A survivor delivered anywhere: jingle (heard by everyone, not positional).
        int rescued = 0;
        foreach (var z in RescueZone.All) if (z.IsSpawned) rescued += z.Rescued.Value;
        if (rescued > lastRescued && deliveredJingle)
        {
            var cam = Camera.main;
            if (cam) AudioSource.PlayClipAtPoint(deliveredJingle, cam.transform.position, 0.8f);
        }
        lastRescued = rescued;
    }

    void PlayAt(AudioClip clip, Vector3 local)
    {
        if (!clip) return;
        oneShots.transform.localPosition = local;
        oneShots.PlayOneShot(clip);
    }

    public void Clank(Vector3 where, float strength)
    {
        if (!hookClank || Time.time < nextClank) return;
        nextClank = Time.time + 0.15f;
        AudioSource.PlayClipAtPoint(hookClank, where, Mathf.Clamp01(strength));
    }

    /// <summary>On the hook: clank when it hits something (host only; clients see it move but don't collide).</summary>
    class HookClank : MonoBehaviour
    {
        public HeliAudio heliAudio;
        void OnCollisionEnter(Collision c)
        {
            float v = c.relativeVelocity.magnitude;
            if (v > 1.2f && heliAudio) heliAudio.Clank(c.GetContact(0).point, v / 6f);
        }
    }
}
