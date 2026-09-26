using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Prototype lobby: a small on-screen panel to Host or Join a game, plus command-line
/// switches for automated testing:
///   -host               start as host
///   -client [ip]        join (default 127.0.0.1)
///   -autopilot          the pilot flies a scripted test pattern
///   -takepilot          grab the pilot seat as soon as connected (tests a remote pilot)
///   -netlog             print the helicopter position once a second (for comparing machines)
///
/// Uses Unity Transport (direct IP) for now. Steam is added later as a second transport.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class NetworkBootstrap : MonoBehaviour
{
    public ushort port = 7777;

    string joinAddress = "127.0.0.1";
    bool netLog;
    bool takePilot;
    float nextLogTime;
    NetworkManager nm;
    NetworkHelicopter heli;

    void Awake()
    {
        nm = GetComponent<NetworkManager>();
    }

    void Start()
    {
        heli = FindAnyObjectByType<NetworkHelicopter>();

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-autopilot": NetworkHelicopter.Autopilot = true; break;
                case "-netlog": netLog = true; break;
                case "-takepilot": takePilot = true; break;
                case "-client" when i + 1 < args.Length && !args[i + 1].StartsWith("-"): joinAddress = args[i + 1]; break;
            }
        }
        foreach (var a in args)
        {
            if (a == "-host") StartHost();
            else if (a == "-client") StartClient();
        }
    }

    void Transport(string address)
    {
        var utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
        utp.SetConnectionData(address, port, "0.0.0.0");
    }

    public void StartHost()
    {
        Transport("127.0.0.1");
        nm.StartHost();
        Debug.Log($"[Net] Hosting on port {port}");
    }

    public void StartClient()
    {
        Transport(joinAddress);
        nm.StartClient();
        Debug.Log($"[Net] Joining {joinAddress}:{port}");
    }

    void Update()
    {
        if (takePilot && heli != null && heli.IsSpawned && nm.IsConnectedClient && !nm.IsServer)
        {
            heli.RequestPilotSeat();
            takePilot = false;
        }

        if (!netLog || heli == null || Time.time < nextLogTime) return;
        nextLogTime = Time.time + 1f;
        string role = nm.IsHost ? "HOST" : nm.IsClient ? "CLIENT" : "OFFLINE";
        Vector3 p = heli.transform.position;
        Debug.Log($"[NetLog] {role} t={Time.time:F0} pilot={(heli.IsSpawned ? heli.PilotClientId.Value.ToString() : "-")} pos=({p.x:F1},{p.y:F1},{p.z:F1})");
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 260, 200), GUI.skin.box);
        if (!nm.IsClient && !nm.IsServer)
        {
            GUILayout.Label("Rescue Helicopter - prototype");
            if (GUILayout.Button("Host game")) StartHost();
            GUILayout.BeginHorizontal();
            joinAddress = GUILayout.TextField(joinAddress, GUILayout.Width(140));
            if (GUILayout.Button("Join")) StartClient();
            GUILayout.EndHorizontal();
            GUILayout.Label("Or just fly offline.");
        }
        else
        {
            GUILayout.Label(nm.IsHost ? "Hosting" : nm.IsConnectedClient ? "Connected" : "Connecting...");
            GUILayout.Label($"Players: {(nm.IsServer ? nm.ConnectedClientsIds.Count.ToString() : "-")}   You are #{nm.LocalClientId}");
            if (heli != null && heli.IsSpawned)
                GUILayout.Label(heli.IsLocalPilot ? "You are the PILOT" : "Crew - press P to take the stick");
            if (GUILayout.Button("Leave")) nm.Shutdown();
        }
        GUILayout.EndArea();
    }
}
