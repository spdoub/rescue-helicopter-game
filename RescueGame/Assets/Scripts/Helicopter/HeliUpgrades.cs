using Unity.Netcode;
using UnityEngine;

/// <summary>
/// What the company has bought for the helicopter (host-authoritative, synced). Paid for out of
/// the rescue money at the upgrade board by the home helipad. Every upgrade changes how it flies
/// AND bolts something visible onto it, so the taped-together heli gets nicer as the business grows.
///  - Winch motor:   reels faster (a chunky new motor on the winch arm)
///  - Aux tanks:     more flying time (tanks strapped to the skids)
///  - Armour:        crashes and rotor strikes hurt less (riveted plates on the belly and nose)
///  - Stabiliser:    wind, gusts and fire updrafts shove it about less (fins on the tail)
/// </summary>
public class HeliUpgrades : NetworkBehaviour
{
    public enum Kind { Winch, Tank, Armour, Stabiliser }

    public struct Info { public string name, blurb; public int[] prices; }
    public static readonly Info[] Catalogue =
    {
        new() { name = "WINCH MOTOR", blurb = "Reels people in 50% faster per level", prices = new[] { 1200, 2600 } },
        new() { name = "AUX FUEL TANKS", blurb = "+3 minutes of flying per level", prices = new[] { 1000, 2200 } },
        new() { name = "ARMOUR PLATING", blurb = "Crashes and rotor strikes hurt less", prices = new[] { 1500, 3000 } },
        new() { name = "STABILISER", blurb = "Wind and hot air push you around less", prices = new[] { 1400, 2800 } },
    };
    public static int MaxLevel(Kind k) => Catalogue[(int)k].prices.Length;

    public static HeliUpgrades Instance { get; private set; }

    public Material partMaterial;

    public NetworkVariable<byte> WinchLevel = new(), TankLevel = new(), ArmourLevel = new(), StabiliserLevel = new();
    /// <summary>A little message after buying (or failing to), for the HUD.</summary>
    public NetworkVariable<Unity.Collections.FixedString64Bytes> LastPurchase = new();
    public NetworkVariable<float> LastPurchaseTime = new(-100f);

    public int Level(Kind k) => k switch { Kind.Winch => WinchLevel.Value, Kind.Tank => TankLevel.Value, Kind.Armour => ArmourLevel.Value, _ => StabiliserLevel.Value };
    NetworkVariable<byte> Var(Kind k) => k switch { Kind.Winch => WinchLevel, Kind.Tank => TankLevel, Kind.Armour => ArmourLevel, _ => StabiliserLevel };

    /// <summary>How much of the wind / hot-air shove still gets through.</summary>
    public float PushScale => StabiliserLevel.Value switch { 0 => 1f, 1 => 0.55f, _ => 0.3f };
    public float DamageScale => ArmourLevel.Value switch { 0 => 1f, 1 => 0.6f, _ => 0.35f };

    NetworkWinch winch; HeliFuel fuel; HeliDamage damage;
    float baseWinchSpeed, baseFlightMinutes;
    readonly GameObject[][] parts = new GameObject[4][];
    readonly int[] shown = { -1, -1, -1, -1 };

    void Awake()
    {
        Instance = this;
        winch = GetComponent<NetworkWinch>(); fuel = GetComponent<HeliFuel>(); damage = GetComponent<HeliDamage>();
        if (winch) baseWinchSpeed = winch.speed;
        if (fuel) baseFlightMinutes = fuel.flightMinutes;
        BuildParts();
    }

    public override void OnNetworkSpawn()
    {
        // Dev builds: "-upgrades 2,1,0,2" starts with those levels, "-money 5000" with that cash (screenshots, tests).
        if (!IsServer || !Debug.isDebugBuild) return;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-upgrades")
            {
                var lv = args[i + 1].Split(',');
                for (int k = 0; k < 4 && k < lv.Length; k++) if (byte.TryParse(lv[k], out byte b)) Var((Kind)k).Value = (byte)Mathf.Min(b, MaxLevel((Kind)k));
            }
            if (args[i] == "-money" && int.TryParse(args[i + 1], out int cash) && RescueZone.All.Count > 0) RescueZone.All[0].Money.Value = cash;
        }
    }

    public override void OnDestroy() { if (Instance == this) Instance = null; base.OnDestroy(); }

    /// <summary>Host: buy the next level of an upgrade with the company's money.</summary>
    public void TryBuy(Kind k, ulong buyer)
    {
        if (!IsServer) return;
        var bank = RescueZone.All.Count > 0 ? RescueZone.All[0] : null;
        int level = Level(k);
        var info = Catalogue[(int)k];
        string msg;
        if (level >= info.prices.Length) msg = $"{info.name} is maxed out";
        else if (bank == null || bank.Money.Value < info.prices[level]) msg = $"Can't afford {info.name} (${info.prices[level]:N0})";
        else
        {
            bank.Money.Value -= info.prices[level];
            Var(k).Value = (byte)(level + 1);
            msg = $"Crew {buyer + 1} bought {info.name} {(level + 1 > 1 ? "II" : "")}".TrimEnd();
        }
        LastPurchase.Value = new Unity.Collections.FixedString64Bytes(msg.Length > 60 ? msg[..60] : msg);
        LastPurchaseTime.Value = Time.time;
    }

    void Update()
    {
        if (!IsSpawned) return;
        // Stats (the host is the one flying and winching; harmless to set everywhere)
        if (winch) winch.speed = baseWinchSpeed * (1f + 0.5f * WinchLevel.Value);
        if (fuel) fuel.flightMinutes = baseFlightMinutes + 3f * TankLevel.Value;
        if (damage) damage.damageScale = DamageScale;
        for (int i = 0; i < 4; i++)
        {
            int lv = Level((Kind)i);
            if (lv == shown[i]) continue;
            shown[i] = lv;
            for (int p = 0; p < parts[i].Length; p++) parts[i][p].SetActive(PartLevel(i, p) <= lv);
        }
    }

    // ------------------------------------------------------------------ the bolt-on parts

    // Which level each part appears at (index into parts[kind]).
    int PartLevel(int kind, int part) => kind switch
    {
        0 => part < 3 ? 1 : 2,            // motor housing + drum + cap; level 2 adds a stripe
        1 => part < 4 ? 1 : 2,            // one tank + straps per side; level 2 adds a second pair
        2 => part < 3 ? 1 : 2,            // belly + nose plates; level 2 adds side skirts
        _ => part < 4 ? 1 : 2,            // wider tailplane + end plates; level 2 adds a ventral fin
    };

    void BuildParts()
    {
        var steel = new Color(0.55f, 0.57f, 0.6f);
        var olive = new Color(0.36f, 0.4f, 0.26f);
        var chrome = new Color(0.8f, 0.82f, 0.85f);
        var yellow = new Color(0.95f, 0.72f, 0.18f);
        var dark = new Color(0.16f, 0.16f, 0.17f);

        // Winch: a fat motor housing on the arm, near the tip.
        Vector3 tipLocal = winch ? transform.InverseTransformPoint(winch.TipPosition) : new Vector3(1.4f, 2.36f, 0.85f);
        parts[0] = new[]
        {
            Part(PrimitiveType.Cylinder, "WinchMotor", tipLocal + new Vector3(-0.35f, 0.22f, 0f), new Vector3(0.26f, 0.2f, 0.26f), new Vector3(0, 0, 90), dark),
            Part(PrimitiveType.Cylinder, "WinchMotorCap", tipLocal + new Vector3(-0.14f, 0.22f, 0f), new Vector3(0.2f, 0.02f, 0.2f), new Vector3(0, 0, 90), chrome),
            Part(PrimitiveType.Cube, "WinchMotorFins", tipLocal + new Vector3(-0.35f, 0.36f, 0f), new Vector3(0.34f, 0.04f, 0.2f), Vector3.zero, steel),
            Part(PrimitiveType.Cylinder, "WinchMotorStripe", tipLocal + new Vector3(-0.35f, 0.22f, 0f), new Vector3(0.27f, 0.035f, 0.27f), new Vector3(0, 0, 90), yellow),
        };

        // Aux tanks: long cylinders strapped outboard of the skid struts, under the doors' sills.
        GameObject Tank(string n, float x, float z) => Part(PrimitiveType.Capsule, n, new Vector3(x, 0.5f, z), new Vector3(0.36f, 0.75f, 0.36f), new Vector3(90, 0, 0), olive);
        GameObject Strap(string n, float x, float z) => Part(PrimitiveType.Cylinder, n, new Vector3(x, 0.5f, z), new Vector3(0.38f, 0.03f, 0.38f), new Vector3(90, 0, 0), dark);
        parts[1] = new[]
        {
            Tank("AuxTank_L", -1.32f, 0.9f), Tank("AuxTank_R", 1.32f, 0.9f),
            Strap("AuxStrap_L", -1.32f, 0.9f), Strap("AuxStrap_R", 1.32f, 0.9f),
            Tank("AuxTank_L2", -1.32f, -0.8f), Tank("AuxTank_R2", 1.32f, -0.8f),
            Strap("AuxStrap_L2", -1.32f, -0.8f), Strap("AuxStrap_R2", 1.32f, -0.8f),
        };

        // Armour: a plate under the belly (hull bottom is at 0.70) and one under the nose, then side skirts below the sills.
        parts[2] = new[]
        {
            Part(PrimitiveType.Cube, "ArmourBelly", new Vector3(0f, 0.665f, 0.9f), new Vector3(1.5f, 0.05f, 3.6f), Vector3.zero, steel),
            Part(PrimitiveType.Cube, "ArmourBellyFront", new Vector3(0f, 0.75f, 3.05f), new Vector3(1.1f, 0.05f, 0.7f), new Vector3(-28, 0, 0), steel),
            Part(PrimitiveType.Cube, "ArmourStripe", new Vector3(0f, 0.64f, 0.9f), new Vector3(1.52f, 0.02f, 0.25f), Vector3.zero, yellow),
            Part(PrimitiveType.Cube, "ArmourSkirt_L", new Vector3(-1.0f, 0.84f, 0.5f), new Vector3(0.05f, 0.22f, 2.2f), Vector3.zero, steel),
            Part(PrimitiveType.Cube, "ArmourSkirt_R", new Vector3(1.0f, 0.84f, 0.5f), new Vector3(0.05f, 0.22f, 2.2f), Vector3.zero, steel),
        };

        // Stabiliser: the body already has a tailplane at (0, 2.35, -3.85); the upgrade extends its span with
        // yellow tips and tall end plates, then adds a ventral fin.
        Vector3 back = new Vector3(0f, 2.35f, -3.85f);
        parts[3] = new[]
        {
            Part(PrimitiveType.Cube, "StabiliserExt_L", back + new Vector3(-1.25f, 0f, 0f), new Vector3(0.6f, 0.07f, 0.42f), Vector3.zero, yellow),
            Part(PrimitiveType.Cube, "StabiliserExt_R", back + new Vector3(1.25f, 0f, 0f), new Vector3(0.6f, 0.07f, 0.42f), Vector3.zero, yellow),
            Part(PrimitiveType.Cube, "StabiliserFin_L", back + new Vector3(-1.52f, 0.2f, -0.03f), new Vector3(0.05f, 0.6f, 0.55f), Vector3.zero, steel),
            Part(PrimitiveType.Cube, "StabiliserFin_R", back + new Vector3(1.52f, 0.2f, -0.03f), new Vector3(0.05f, 0.6f, 0.55f), Vector3.zero, steel),
            Part(PrimitiveType.Cube, "StabiliserVentral", back + new Vector3(0f, -0.55f, -0.2f), new Vector3(0.05f, 0.6f, 0.7f), new Vector3(-20, 0, 0), yellow),
        };
        foreach (var set in parts) foreach (var p in set) p.SetActive(false);
    }

    GameObject Part(PrimitiveType type, string name, Vector3 local, Vector3 scale, Vector3 euler, Color color)
    {
        var p = GameObject.CreatePrimitive(type);
        p.name = "Upgrade_" + name;
        Destroy(p.GetComponent<Collider>()); // looks only: the hull colliders do the work
        p.transform.SetParent(transform, false);
        p.transform.localPosition = local;
        p.transform.localRotation = Quaternion.Euler(euler);
        p.transform.localScale = scale;
        var r = p.GetComponent<Renderer>();
        if (partMaterial) r.sharedMaterial = partMaterial;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", color);
        r.SetPropertyBlock(mpb);
        return p;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
        return null;
    }
}
