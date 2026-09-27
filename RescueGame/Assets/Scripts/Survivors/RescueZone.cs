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
    /// <summary>The one zone that holds the company's money and tally (the first in the list); every delivery credits it.</summary>
    public static RescueZone Bank => All.Count > 0 ? All[0] : null;
    [Tooltip("Shown on the HUD marker and said on the radio.")]
    public string displayName = "HOSPITAL";

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
        int roughPenalty = s.RoughMoments * 150 + (s.Singed.Value ? 300 : 0);
        int pay = Mathf.Max(100, basePay + speedBonus - roughPenalty);
        int stars = Mathf.Clamp(5 - s.RoughMoments - (minutes > 4f ? 1 : 0) - (s.Singed.Value ? 1 : 0), 1, 5);
        string review = s.Review(stars);
        pay += s.payBonus;
        var bank = Bank ?? this;
        bank.Rescued.Value++;
        bank.Money.Value += pay;
        string text = $"{s.displayName}{(s.Singed.Value ? " (singed)" : "")}: \"{review}\"  {new string('*', stars)}  +${pay:N0}{(this != bank ? " [cliff]" : "")}";
        bank.LastReport.Value = new FixedString128Bytes(text.Length > 120 ? text[..120] : text);
        bank.ReportTime.Value = Time.time;
        s.Say(review, 5f);
    }
}
