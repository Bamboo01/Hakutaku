using UnityEngine;

namespace Hakutaku.Unity
{
    // Points the static HakutakuClientAPI at the server in the settings asset before the first scene
    // loads, the way PlayFab applies PlayFabSharedSettings, so a game needs no setup code of its own.
    // Without an asset the SDK keeps its default, http://localhost:5008.
    static class HakutakuUnitySetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplySettings()
        {
            var settings = HakutakuSharedSettings.Load();
            if (settings != null && !string.IsNullOrWhiteSpace(settings.ServerUrl))
                HakutakuSettings.staticSettings.ServerUrl = settings.ServerUrl.Trim();
        }
    }
}
