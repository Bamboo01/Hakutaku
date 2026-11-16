using System;
using Hakutaku.ServerModels;
using UnityEditor;
using UnityEngine;

namespace Hakutaku.Unity.Editor
{
    // Window > Hakutaku > Settings, Hakutaku's version of PlayFab's Editor Extensions window. Sets the
    // server URL (kept in the settings asset) and this machine's server key (kept in UserSettings, never
    // an asset), and checks both against the real server.
    public class HakutakuSettingsWindow : EditorWindow
    {
        // 127.0.0.1 and not localhost: Unity's HttpClient tries localhost's IPv6 address (::1) and doesn't
        // fall back to IPv4, while Docker publishes these ports on 127.0.0.1 only, so localhost times out
        static readonly (string Label, string Url)[] Presets =
        {
            ("Local: dotnet watch", "http://127.0.0.1:5008"),
            ("Local: Docker", "http://127.0.0.1:8090"),
            ("Production", "https://51.79.242.169.nip.io"),
        };

        string keyField = "";
        string checkResult = "";
        MessageType checkType = MessageType.None;
        bool checking;

        [MenuItem("Window/Hakutaku/Settings")]
        public static void Open() => GetWindow<HakutakuSettingsWindow>("Hakutaku");

        void OnEnable() => keyField = HakutakuServerSettings.ReadEditorKey() ?? "";

        void OnGUI()
        {
            ServerSection();
            EditorGUILayout.Space(12);
            KeySection();
            EditorGUILayout.Space(12);
            CheckSection();
        }

        void ServerSection()
        {
            EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
            var settings = HakutakuSharedSettings.Load();
            if (settings == null)
            {
                EditorGUILayout.HelpBox("This project has no settings asset yet, so the SDK uses " + HakutakuSharedSettings.DefaultServerUrl + ".", MessageType.Info);
                if (GUILayout.Button("Create settings asset")) CreateSettingsAsset();
                return;
            }

            EditorGUI.BeginChangeCheck();
            var url = EditorGUILayout.TextField("Server URL", settings.ServerUrl);
            if (EditorGUI.EndChangeCheck()) SetUrl(settings, url);

            EditorGUILayout.BeginHorizontal();
            foreach (var (label, presetUrl) in Presets)
                if (GUILayout.Button(label)) SetUrl(settings, presetUrl);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Kept in " + AssetDatabase.GetAssetPath(settings) + ". No secrets go in there.", EditorStyles.miniLabel);
        }

        void KeySection()
        {
            EditorGUILayout.LabelField("Game server key", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Only a game server needs this. It's saved in UserSettings/Hakutaku/ServerKey.txt on this machine, " +
                "which git ignores, and never in an asset. Client builds don't contain the server SDK at all. " +
                "A game server build reads HAKUTAKU_SERVER_KEY from its environment instead.", MessageType.None);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HakutakuServerSettings.KeyEnvironmentVariable)))
                EditorGUILayout.HelpBox("HAKUTAKU_SERVER_KEY is set in this environment, so it is used instead of the key below.", MessageType.Info);

            keyField = EditorGUILayout.PasswordField("Server key", keyField);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save key"))
            {
                HakutakuServerSettings.SaveEditorKey(keyField);
                ShowNotification(new GUIContent("Key saved on this machine"));
            }
            if (GUILayout.Button("Forget key"))
            {
                keyField = "";
                HakutakuServerSettings.SaveEditorKey("");
            }
            EditorGUILayout.EndHorizontal();
        }

        void CheckSection()
        {
            EditorGUILayout.LabelField("Check", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(checking))
            {
                if (GUILayout.Button(checking ? "Checking..." : "Check the server and key")) Check();
            }
            if (checkResult.Length > 0) EditorGUILayout.HelpBox(checkResult, checkType);
        }

        static void SetUrl(HakutakuSharedSettings settings, string url)
        {
            Undo.RecordObject(settings, "Change Hakutaku server URL");
            settings.ServerUrl = (url ?? "").Trim();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        static void CreateSettingsAsset()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateAsset(CreateInstance<HakutakuSharedSettings>(), HakutakuSharedSettings.AssetPath);
            AssetDatabase.SaveAssets();
        }

        // sends a ticket that can't be real. a server that has the key answers "bad ticket", which proves
        // the server is reachable and the key works, without needing a player
        async void Check()
        {
            checking = true;
            checkResult = "";
            var server = HakutakuServerSettings.CreateApi(TimeSpan.FromSeconds(10));
            var reply = await server.AuthenticateSessionTicketAsync(new AuthenticateSessionTicketRequest { SessionTicket = "hakutaku-settings-check" });
            (checkResult, checkType) = Describe(reply.Error, server.ServerUrl);
            checking = false;
            if (this != null) Repaint();
        }

        static (string, MessageType) Describe(HakutakuServerError error, string url)
        {
            if (error == null)
                return ("The check ticket was accepted, which should never happen. Is " + url + " really Hakutaku?", MessageType.Warning);
            if (error.Error == HakutakuServerErrorCode.InvalidSessionTicket)
                return ("OK: " + url + " answered and accepted the server key.", MessageType.Info);
            if (error.Error == HakutakuServerErrorCode.MissingServerKey)
                return ("No server key yet. Paste one above and save it, or set HAKUTAKU_SERVER_KEY.", MessageType.Warning);
            if (error.Error == HakutakuServerErrorCode.ServerKeyRejected)
                return (url + " answered but refused the key: it is unknown, revoked or expired. Make a new one on the admin keys page.", MessageType.Error);
            if (error.Error == HakutakuServerErrorCode.NotFound)
                return (url + " has no /api/server routes. On production, Caddy isn't proxying them yet; locally, the server is an old build.", MessageType.Error);
            if (error.IsHakutakuUnavailable)
                return ("Can't reach " + url + " (" + error.Error + "). Is the server running?", MessageType.Error);
            return (error.Error + ": " + error.ErrorMessage, MessageType.Error);
        }
    }
}
