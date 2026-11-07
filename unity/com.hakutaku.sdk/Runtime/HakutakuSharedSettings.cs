using UnityEngine;

namespace Hakutaku.Unity
{
    // Which Hakutaku server the game talks to, like PlayFabSharedSettings: one asset in a Resources
    // folder, read before the first scene loads (see HakutakuUnitySetup). Window > Hakutaku > Settings
    // makes and edits it.
    //
    // It holds the URL and nothing secret. The server key never goes in here: an asset is committed to
    // git and built into every copy of the game, so anyone with a client build could read it and write
    // any player's data. A game server gets its key from HakutakuServerSettings instead.
    public class HakutakuSharedSettings : ScriptableObject
    {
        public const string ResourceName = "HakutakuSharedSettings";
        public const string AssetPath = "Assets/Resources/" + ResourceName + ".asset";

        // the same default as the client SDK's HakutakuApiSettings: the server under dotnet watch
        public const string DefaultServerUrl = "http://localhost:5008";

        [Tooltip("The Hakutaku server, e.g. http://localhost:5008 (dotnet watch), http://localhost:8090 (local Docker) or https://51.79.242.169.nip.io (production).")]
        public string ServerUrl = DefaultServerUrl;

        // null when the project has no settings asset yet
        public static HakutakuSharedSettings Load() => Resources.Load<HakutakuSharedSettings>(ResourceName);
    }
}
