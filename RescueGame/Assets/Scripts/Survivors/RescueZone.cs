using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A drop-off point (the hospital helipad). Land here with a survivor on board: they get out,
/// walk to the exit and rate the trip. Pays the crew based on how fast and how gentle it was.
/// The zone is the box of this object's BoxCollider (a trigger; nothing bumps into it).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RescueZone : NetworkBehaviour
{
    public static readonly List<RescueZone> All = new();

    [Tooltip("Where delivered survivors walk to (the hospital door).")]
    public Transform exitPoint;
    public int basePay = 1000;

    public NetworkVariable<int> Rescued = new();
    public NetworkVariable<int> Money = new();
    /// <summary>The last survivor's verdict, shown on everyone's screen for a while.</summary>
    public NetworkVariable<FixedString128Bytes> LastReport = new();
    public NetworkVariable<float> ReportTime = new(-100f);

    BoxCollider box;

    void Awake() { box = GetComponent<BoxCollider>(); box.isTrigger = true; }
    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public bool Contains(Vector3 worldPoint)
    {
        Vector3 p = transform.InverseTransformPoint(worldPoint) - box.center;
        Vector3 h = box.size * 0.5f;
        return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
    }

    /// <summary>Host: a survivor made it. Work out the pay and the review.</summary>
    public void Deliver(SurvivorBrain s)
    {
        float minutes = s.RescueStartedAt >= 0f ? (Time.time - s.RescueStartedAt) / 60f : 5f;
        int speedBonus = Mathf.Max(0, Mathf.RoundToInt(600f - minutes * 150f));
        int roughPenalty = s.RoughMoments * 150;
        int pay = Mathf.Max(100, basePay + speedBonus - roughPenalty);
        int stars = Mathf.Clamp(5 - s.RoughMoments - (minutes > 4f ? 1 : 0), 1, 5);
        string review = stars switch
        {
            5 => "Lovely boys. Five stars. Would get winched again.",
            4 => "Very nice. Bit bumpy. Four stars.",
            3 => "I've had worse. I've had better. Three stars.",
            2 => "My hip will never forgive you. Two stars.",
            _ => "One star. I'm telling the newspaper.",
        };
        Rescued.Value++;
        Money.Value += pay;
        string text = $"{s.displayName}: \"{review}\"  {new string('*', stars)}  +${pay:N0}";
        LastReport.Value = new FixedString128Bytes(text.Length > 120 ? text[..120] : text);
        ReportTime.Value = Time.time;
        s.Say(review, 5f);
    }
}
