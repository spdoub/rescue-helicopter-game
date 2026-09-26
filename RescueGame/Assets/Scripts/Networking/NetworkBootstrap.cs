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
///   -shot <file> [-shotpitch deg] [-shotview front|fp] [-shotat x,y,z,yaw,pitch[,fov]]   save a screenshot after 5 s and quit (dev test)
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
                case "-shotrun": NetworkPlayer.RunForTests = true; break;
                case "-client" when i + 1 < args.Length && !args[i + 1].StartsWith("-"): joinAddress = args[i + 1]; break;
            }
        }
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-shot") StartCoroutine(TestScreenshot(args[i + 1],
                ArgFloat(args, "-shotpitch", 0f), ArgValue(args, "-shotview") ?? "fp",
                ArgFloat(args, "-shotdelay", 5f), ArgFloat(args, "-shotyaw", float.NaN)));
        }

        localAddress = FindLocalAddress();

        if (args.Contains("-client")) StartClient();
        else StartHost(); // default: host straight away, even with nobody else around
    }

    static string ArgValue(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static float ArgFloat(string[] args, string key, float fallback) =>
        float.TryParse(ArgValue(args, key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

    IEnumerator TestScreenshot(string file, float pitch, string view, float delay, float yaw)
    {
        FirstPersonSeatCamera.ForcedView = view switch
        {
            "front" => FirstPersonSeatCamera.View.Front,
            "side" => FirstPersonSeatCamera.View.Side,
            "chase" => FirstPersonSeatCamera.View.HeliChase,
            _ => FirstPersonSeatCamera.View.FirstPerson,
        };
        float end = Time.time + delay;
        while (Time.time < end)
        {
            // hold the look for the final second (bots may be steering before that)
            if (NetworkPlayer.Local != null && end - Time.time < 1f)
            {
                if (!float.IsNaN(yaw) && heli != null) NetworkPlayer.Local.BotLook(heli.transform.eulerAngles.y + yaw, pitch);
                else NetworkPlayer.Local.SetLookForTests(pitch);
            }
            yield return null;
        }
        // -shotat x,y,z,yaw,pitch : a free camera anywhere in the world (for showing places, not the player)
        string at = ArgValue(System.Environment.GetCommandLineArgs(), "-shotat");
        if (at != null && Camera.main != null)
        {
            var v = System.Array.ConvertAll(at.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
            var cam = Camera.main;
            var rig = cam.GetComponent<FirstPersonSeatCamera>(); if (rig) rig.enabled = false;
            cam.transform.SetPositionAndRotation(new Vector3(v[0], v[1], v[2]), Quaternion.Euler(v.Length > 4 ? v[4] : 0f, v.Length > 3 ? v[3] : 0f, 0f));
            if (v.Length > 5) cam.fieldOfView = v[5];
            yield return null; yield return null;
        }
        // -shotnear Name[,dist,height,side] : frame whatever that object is (e.g. a survivor wherever the host put them),
        // from in front of the way it faces
        string near = ArgValue(System.Environment.GetCommandLineArgs(), "-shotnear");
        if (near != null && Camera.main != null)
        {
            var parts = near.Split(',');
            var target = GameObject.Find(parts[0]);
            float F(int i, float d) => parts.Length > i ? float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture) : d;
            if (target != null)
            {
                var cam = Camera.main;
                var rig = cam.GetComponent<FirstPersonSeatCamera>(); if (rig) rig.enabled = false;
                Vector3 look = target.transform.position + Vector3.up * 0.5f;
                Vector3 fwd = Vector3.ProjectOnPlane(target.transform.forward, Vector3.up).normalized;
                Vector3 pos = look + fwd * F(1, 7f) + Vector3.up * F(2, 1.5f) + Vector3.Cross(Vector3.up, fwd) * F(3, 2.5f);
                // keep the camera above whatever ground is there (hillsides)
                if (Physics.Raycast(pos + Vector3.up * 60f, Vector3.down, out RaycastHit gh, 120f, GameLayers.Gameplay, QueryTriggerInteraction.Ignore) && pos.y < gh.point.y + 1.6f) pos.y = gh.point.y + 1.6f;
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                cam.fieldOfView = 55f;
                yield return null; yield return null;
            }
        }
        if (Camera.main && heli)
        {
            var cam = Camera.main.transform;
            var me = NetworkPlayer.Local;
            Debug.Log($"[Shot] camera heli-local {heli.transform.InverseTransformPoint(cam.position):F2} fwd {heli.transform.InverseTransformDirection(cam.forward):F2}" +
                      (me ? $" eye heli-local {heli.transform.InverseTransformPoint(me.GetComponent<CrewBody>().Eye):F2} pilot {me.SeatedAsPilot.Value}" : ""));
        }
        ScreenCapture.CaptureScreenshot(file);
        yield return new WaitForSeconds(1f);
        Application.Quit();
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
        HelpToggle();
        if (!netLog || heli == null || Time.time < nextLogTime) return;
        nextLogTime = Time.time + 1f;
        string role = nm.IsHost ? "HOST" : nm.IsClient ? "CLIENT" : "OFFLINE";
        Vector3 p = heli.transform.position;
        string crew = "";
        foreach (var pl in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            Vector3 local = heli.transform.InverseTransformPoint(pl.transform.position);
            crew += $" | p{pl.OwnerClientId} local=({local.x:F1},{local.y:F1},{local.z:F1}){(pl.SeatedAsPilot.Value ? " SEATED" : "")}{(pl.KnockedDown.Value ? " DOWN" : "")}{(pl.OnHelicopter.Value ? " onheli" : "")}{(pl.HoldLeft.Value || pl.HoldRight.Value ? " GRAB" : "")}{(!pl.Grounded.Value && !pl.SeatedAsPilot.Value ? " air" : "")}";
        }
        var ci = heli.GetComponent<HelicopterController>().CurrentInput;
        crew += $" | in c{ci.collective:F1} p{ci.pitch:F1} r{ci.roll:F1} y{ci.yaw:F1} w{heli.GetComponent<Rigidbody>().angularVelocity.magnitude:F2}";
        Debug.Log($"[NetLog] {role} t={Time.time:F0} pilot={(heli.IsSpawned ? heli.PilotClientId.Value.ToString() : "-")} pos=({p.x:F1},{p.y:F1},{p.z:F1}) rot=({heli.transform.eulerAngles.x:F0},{heli.transform.eulerAngles.y:F0},{heli.transform.eulerAngles.z:F0}){crew}");
    }

    bool helpOpen = true, helpAutoClosed;
    /// <summary>Where the help panel is on screen right now (empty when hidden), so other HUD bits keep clear of it.</summary>
    public static Rect HelpRect { get; private set; }

    void HelpToggle()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.hKey.wasPressedThisFrame) helpOpen = !helpOpen;
        if (!helpAutoClosed && Time.time > 20f) { helpAutoClosed = true; helpOpen = false; } // shown for the first 20 s
    }

    void OnGUI()
    {
        if (!helpOpen)
        {
            HelpRect = new Rect(0, Screen.height - 32, 320, 32);
            GUI.Label(new Rect(12, Screen.height - 28, 300, 24), "H: help / invite friends");
            return;
        }
        HelpRect = new Rect(0, Screen.height - 270, 340, 270);
        GUILayout.BeginArea(new Rect(12, Screen.height - 262, 320, 250), GUI.skin.box);
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
            GUILayout.Label(heli.IsLocalPilot ? "PILOT - E to get up" : "Pilot: stand by the pilot's seat and press E");
        GUILayout.Label("WASD walk, Shift run, Space jump, click to grab");
        GUILayout.Label("E: take the controls / sit in a seat / get up");
        GUILayout.Label("F: doors, and buying at the upgrade board");
        GUILayout.Label("Winch box: hold Q to lower the hook, R to raise it");
        GUILayout.Label("Esc frees the mouse, click to look again.  H hides this");
        GUILayout.EndArea();
    }
}
