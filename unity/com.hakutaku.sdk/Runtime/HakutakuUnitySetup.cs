using UnityEngine;

namespace Hakutaku.Unity
{
    // Points the static HakutakuClientAPI at the server in the settings asset before the first scene
    // loads, the way PlayFab applies PlayFabSharedSettings, so a game needs no setup code of its own.
    // Without an asset it uses HakutakuSharedSettings.DefaultServerUrl, not the SDK's own localhost
    // default, which Unity can time out on.
    static class HakutakuUnitySetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplySettings()
        {
            var settings = HakutakuSharedSettings.Load();
            HakutakuSettings.staticSettings.ServerUrl = settings != null && !string.IsNullOrWhiteSpace(settings.ServerUrl)
                ? settings.ServerUrl.Trim()
                : HakutakuSharedSettings.DefaultServerUrl;
        }
    }
}
