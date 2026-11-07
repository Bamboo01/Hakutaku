using System;
using System.IO;
using UnityEngine;

namespace Hakutaku.Unity
{
    // Where a game server gets Hakutaku's URL and its server key, and the one-line way to make the
    // server API from them:
    //
    //     var server = HakutakuServerSettings.CreateApi();
    //
    // This assembly only exists in the editor, in a Dedicated Server build (UNITY_SERVER) or in a build
    // with HAKUTAKU_SERVER in its scripting defines, and so does the server SDK DLL. A client build has
    // neither, so it can't hold or use a server key even by mistake.
    public static class HakutakuServerSettings
    {
        public const string KeyEnvironmentVariable = "HAKUTAKU_SERVER_KEY";
        public const string UrlEnvironmentVariable = "HAKUTAKU_URL";

        // The server key, or null if there is none. In order:
        //   1. HAKUTAKU_SERVER_KEY from the environment. The only source in a build, so the homelab
        //      sets it there, and it wins in the editor too.
        //   2. In the editor only, the key saved by Window > Hakutaku > Settings, in
        //      UserSettings/Hakutaku/ServerKey.txt. UserSettings is per user and is in Unity's standard
        //      .gitignore, so the key stays on this machine.
        // Never an asset: assets are committed and built into the game.
        public static string ResolveKey()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(KeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim();
#if UNITY_EDITOR
            var saved = ReadEditorKey();
            if (saved != null) return saved;
#endif
            return null;
        }

        // HAKUTAKU_URL from the environment if set, so a game server can be pointed elsewhere without a
        // rebuild, then the settings asset, then the default. not HakutakuApiSettings: that's in the
        // client SDK, which this assembly doesn't reference
        public static string ResolveUrl()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(UrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim();
            var settings = HakutakuSharedSettings.Load();
            if (settings != null && !string.IsNullOrWhiteSpace(settings.ServerUrl)) return settings.ServerUrl.Trim();
            return HakutakuSharedSettings.DefaultServerUrl;
        }

        // The server API with the URL and key above. With no key it still works, but every call fails
        // with MissingServerKey without sending anything.
        public static HakutakuServerInstanceAPI CreateApi(TimeSpan? timeout = null) =>
            new HakutakuServerInstanceAPI(ResolveUrl(), ResolveKey(), timeout);

#if UNITY_EDITOR
        // UserSettings sits next to Assets in the project folder
        public static string EditorKeyPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings", "Hakutaku", "ServerKey.txt"));

        public static string ReadEditorKey()
        {
            if (!File.Exists(EditorKeyPath)) return null;
            var key = File.ReadAllText(EditorKeyPath).Trim();
            return key.Length == 0 ? null : key;
        }

        // saves the key for this machine and this project only; an empty key deletes the file
        public static void SaveEditorKey(string key)
        {
            key = key?.Trim() ?? "";
            if (key.Length == 0)
            {
                if (File.Exists(EditorKeyPath)) File.Delete(EditorKeyPath);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(EditorKeyPath));
            File.WriteAllText(EditorKeyPath, key);
        }
#endif
    }
}
