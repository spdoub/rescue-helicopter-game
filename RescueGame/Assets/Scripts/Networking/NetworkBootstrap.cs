using System;
using System.Collections;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Prototype lobby. The game hosts automatically when it opens, so you always spawn as a crew
/// member. To play together, the other copy types the host's address and clicks Join.
/// Command-line switches for automated testing:
///   -host               start as host
///   -client [ip]        join (default 127.0.0.1)
///   -autopilot          the pilot flies a scripted test pattern
///   -takepilot          sit in the pilot seat as soon as connected (dev builds only)
///   -testboard          your crew member walks to the helicopter and jumps aboard (dev test)
///   -testgrab           your crew member looks down and holds on to the floor (dev test)
///   -netlog             print helicopter + crew positions once a second (for comparing machines)
///
/// Uses Unity Transport (direct IP) for now. Steam is added later as a second transport.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class NetworkBootstrap : MonoBehaviour
{
    public ushort port = 7777;

    string joinAddress = "127.0.0.1";
    string status = "";
    string localAddress;
    bool netLog;
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
                case "-takepilot": NetworkPlayer.ForceSeatForTests = true; break;
                case "-testgrab": NetworkPlayer.HoldGrabForTests = true; break;
                case "-testboard": NetworkPlayer.BoardForTests = true; break;
                case "-client" when i + 1 < args.Length && !args[i + 1].StartsWith("-"): joinAddress = args[i + 1]; break;
            }
        }
        localAddress = FindLocalAddress();

        if (args.Contains("-client")) StartClient();
        else StartHost(); // default: host straight away, even with nobody else around
    }

    static string FindLocalAddress()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork) return ua.Address.ToString();
        }
        return "127.0.0.1";
    }

    void Transport(string address)
    {
        var utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
        utp.SetConnectionData(address, port, "0.0.0.0");
    }

    public void StartHost()
    {
        Transport("127.0.0.1");
        if (nm.StartHost())
        {
            status = "";
            Debug.Log($"[Net] Hosting on port {port}");
        }
        else
        {
            // Usually: another copy on this computer is already hosting.
            status = "Couldn't host (another copy may already be hosting). Join it instead.";
            Debug.Log("[Net] Host failed to start");
        }
    }

    public void StartClient()
    {
        Transport(joinAddress);
        nm.StartClient();
        status = "";
        Debug.Log($"[Net] Joining {joinAddress}:{port}");
    }

    /// <summary>Stop whatever session is running, then run the next step once it has fully shut down.</summary>
    IEnumerator Restart(Action next)
    {
        if (nm.IsListening) nm.Shutdown();
        while (nm.ShutdownInProgress || nm.IsListening) yield return null;
        next();
    }

    void Update()
    {
        if (!netLog || heli == null || Time.time < nextLogTime) return;
        nextLogTime = Time.time + 1f;
        string role = nm.IsHost ? "HOST" : nm.IsClient ? "CLIENT" : "OFFLINE";
        Vector3 p = heli.transform.position;
        string crew = "";
        foreach (var pl in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            Vector3 local = heli.transform.InverseTransformPoint(pl.transform.position);
            crew += $" | p{pl.OwnerClientId} local=({local.x:F1},{local.y:F1},{local.z:F1}){(pl.SeatedAsPilot.Value ? " SEATED" : "")}{(pl.KnockedDown.Value ? " DOWN" : "")}{(pl.OnHelicopter.Value ? " onheli" : "")}{(pl.Grabbing.Value ? " GRAB" : "")}";
        }
        var ci = heli.GetComponent<HelicopterController>().CurrentInput;
        crew += $" | in c{ci.collective:F1} p{ci.pitch:F1} r{ci.roll:F1} y{ci.yaw:F1} w{heli.GetComponent<Rigidbody>().angularVelocity.magnitude:F2}";
        Debug.Log($"[NetLog] {role} t={Time.time:F0} pilot={(heli.IsSpawned ? heli.PilotClientId.Value.ToString() : "-")} pos=({p.x:F1},{p.y:F1},{p.z:F1}) rot=({heli.transform.eulerAngles.x:F0},{heli.transform.eulerAngles.y:F0},{heli.transform.eulerAngles.z:F0}){crew}");
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 300, 230), GUI.skin.box);
        if (nm.IsHost)
            GUILayout.Label($"Hosting - friends join at {localAddress}   Players: {nm.ConnectedClientsIds.Count}");
        else if (nm.IsConnectedClient)
            GUILayout.Label($"Connected to {joinAddress}");
        else if (nm.IsClient)
            GUILayout.Label($"Connecting to {joinAddress}...");
        else
            GUILayout.Label("Not connected");
        if (status != "") GUILayout.Label(status);

        if (!nm.IsClient || nm.IsHost)
        {
            GUILayout.BeginHorizontal();
            joinAddress = GUILayout.TextField(joinAddress, GUILayout.Width(150));
            if (GUILayout.Button("Join")) StartCoroutine(Restart(StartClient));
            GUILayout.EndHorizontal();
        }
        if (!nm.IsHost && GUILayout.Button("Host my own game")) StartCoroutine(Restart(StartHost));

        if (heli != null && heli.IsSpawned)
            GUILayout.Label(heli.IsLocalPilot ? "PILOT - E to get up" : "Walk to the cockpit, E to fly");
        GUILayout.Label("WASD walk, Space jump, hold click to grab");
        GUILayout.Label("Esc frees the mouse, click to look again");
        GUILayout.EndArea();
    }
}
