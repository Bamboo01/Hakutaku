using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using kcp2k;
using Mirror;
using UnityEngine;

namespace HakutakuMirrorSample
{
    // Builds the sample in any scene and shows its test screen.
    //
    // In the editor or a normal build, use the screen: start a server, start a client, give XP.
    // A build can also run itself, which is how the "done when" checks are scripted:
    //   -sample-server                  start as a server
    //   -sample-client                  start as a client
    //   -sample-address <host>          where the client connects (localhost)
    //   -sample-port <port>             7777
    //   -sample-award-count <n>         server: give each connection this many awards
    //   -sample-award-amount <xp>       server: the size of each award (10)
    //   -sample-award-interval <s>      server: seconds between awards (0.25)
    //   -sample-quit-after <s>          quit after this long
    //   -profile <name>                 which saved player this client is
    public class SampleApp : MonoBehaviour
    {
        const long ButtonAward = 10;

        SampleNetworkManager manager;
        KcpTransport transport;
        string address = "localhost";
        string portText = "7777";

        readonly HashSet<int> autoAwarded = new HashSet<int>();
        int awardCount;
        long awardAmount = 10;
        float awardInterval = 0.25f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (FindObjectOfType<SampleApp>() != null)
                return;

            // Inactive while it's put together: NetworkManager.Awake needs the transport and authenticator there
            var root = new GameObject("Hakutaku Mirror Sample");
            root.SetActive(false);
            var kcp = root.AddComponent<KcpTransport>();
            var auth = root.AddComponent<SampleAuthenticator>();
            var net = root.AddComponent<SampleNetworkManager>();
            net.transport = kcp;
            net.authenticator = auth;
            net.autoCreatePlayer = false;
            var app = root.AddComponent<SampleApp>();
            app.manager = net;
            app.transport = kcp;
            root.SetActive(true);
        }

        void Start()
        {
            Application.runInBackground = true;
            if (Camera.main == null)
            {
                var cam = new GameObject("Camera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.09f, 0.10f, 0.12f);
                cam.tag = "MainCamera";
            }

            address = Arg("-sample-address") ?? address;
            portText = Arg("-sample-port") ?? portText;
            awardCount = Number("-sample-award-count", 0);
            awardAmount = Number("-sample-award-amount", 10);
            awardInterval = (float)Decimal("-sample-award-interval", 0.25);

            double quitAfter = Decimal("-sample-quit-after", 0);
            if (quitAfter > 0)
                StartCoroutine(QuitAfter((float)quitAfter));

            if (HasArg("-sample-server"))
                StartServer();
            else if (HasArg("-sample-client"))
                StartClient();
        }

        void Update()
        {
            if (!NetworkServer.active || awardCount <= 0)
                return;

            foreach (var conn in NetworkServer.connections.Values.ToList())
                if (conn.isAuthenticated && autoAwarded.Add(conn.connectionId))
                    StartCoroutine(AwardRepeatedly(conn));
        }

        IEnumerator AwardRepeatedly(NetworkConnectionToClient conn)
        {
            for (int i = 0; i < awardCount; i++)
            {
                yield return new WaitForSecondsRealtime(awardInterval);
                if (!NetworkServer.connections.TryGetValue(conn.connectionId, out var current) || current != conn)
                    yield break;
                manager.ServerAward(conn, awardAmount);
            }
            Debug.Log($"[Sample] server: finished awards for connection {conn.connectionId}");
        }

        IEnumerator QuitAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (NetworkClient.active) manager.StopClient();
            if (NetworkServer.active)
            {
                manager.ServerEndMatch();
                yield return new WaitForSecondsRealtime(1f);
                manager.StopServer();
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        void ApplyPort()
        {
            if (ushort.TryParse(portText, out ushort port))
                transport.port = port;
        }

        void StartServer()
        {
            ApplyPort();
            manager.StartServer();
            Debug.Log($"[Sample] server: listening on port {transport.port}");
        }

        void StartClient()
        {
            ApplyPort();
            manager.networkAddress = address;
            manager.StartClient();
            Debug.Log($"[Sample] client: connecting to {address}:{transport.port}");
        }

        // ---- The test screen ----

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, 460, Screen.height - 32));
            GUILayout.Label("Hakutaku + Mirror sample");

            if (!NetworkServer.active && !NetworkClient.active)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Address", GUILayout.Width(60));
                address = GUILayout.TextField(address);
                portText = GUILayout.TextField(portText, GUILayout.Width(60));
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Start server")) StartServer();
                if (GUILayout.Button("Start client")) StartClient();
            }
            else if (NetworkServer.active)
            {
                DrawServer();
            }
            else
            {
                DrawClient();
            }
            GUILayout.EndArea();
        }

        void DrawServer()
        {
            GUILayout.Label($"Server on port {transport.port}");
            foreach (var conn in NetworkServer.connections.Values.ToList())
            {
                if (!(conn.authenticationData is SamplePlayerAuthData data))
                    continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{Short(data.PlayerId)}  {data.Xp} XP  {(data.IsOnline ? "online" : "offline")}");
                if (GUILayout.Button($"+{ButtonAward} XP", GUILayout.Width(70)))
                    manager.ServerAward(conn, ButtonAward);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("End match (save now)")) manager.ServerEndMatch();
            if (GUILayout.Button("Stop server")) manager.StopServer();
        }

        void DrawClient()
        {
            var status = manager.ClientStatus;
            GUILayout.Label(status.HasValue
                ? $"Player {Short(status.Value.playerId)}\nXP {status.Value.xp}\n{(status.Value.online ? "Online: XP is saved" : "Offline: XP isn't saved")}"
                : "Connecting...");
            if (GUILayout.Button("Disconnect")) manager.StopClient();
        }

        static string Short(string id) => string.IsNullOrEmpty(id) ? "(no account)" : id.Substring(0, Math.Min(8, id.Length));

        // ---- Command line ----

        static bool HasArg(string name) => Environment.GetCommandLineArgs().Contains(name);

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static int Number(string name, int fallback) =>
            int.TryParse(Arg(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;

        static double Decimal(string name, double fallback) =>
            double.TryParse(Arg(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : fallback;
    }
}
