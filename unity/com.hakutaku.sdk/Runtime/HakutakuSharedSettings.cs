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

        // the server under dotnet watch. 127.0.0.1 rather than localhost: Unity's HttpClient tries
        // localhost's IPv6 address and doesn't fall back to IPv4, so a local server can time out
        public const string DefaultServerUrl = "http://127.0.0.1:5008";

        [Tooltip("The Hakutaku server, e.g. http://127.0.0.1:5008 (dotnet watch), http://127.0.0.1:8090 (local Docker) or https://51.79.242.169.nip.io (production). Use 127.0.0.1, not localhost: Unity can time out on localhost.")]
        public string ServerUrl = DefaultServerUrl;

        // null when the project has no settings asset yet
        public static HakutakuSharedSettings Load() => Resources.Load<HakutakuSharedSettings>(ResourceName);
    }
}
