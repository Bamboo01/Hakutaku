using System;
using System.Collections.Generic;
using Hakutaku;
using Hakutaku.ClientModels;
using UnityEngine;
#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
using Hakutaku.ServerModels;
using Hakutaku.Unity;
#endif

// The Hakutaku showcase. There's no real game server yet, so this one screen plays both sides:
//   the player, through the client SDK: register a guest, or log back in with the saved device token
//   the game server, through the server SDK: validate the player's ticket, read their XP, add 100
//
// Press Play in any scene and it builds itself (see Spawn). Pick the server, and save a server key for
// the game-server half, in Window > Hakutaku > Settings.
public class HakutakuDemo : MonoBehaviour
{
    // the guest's device token is its only credential, so it's kept between runs like a game would
    const string DeviceTokenKey = "hakutaku_demo_device_token";
    const int XpPerClick = 100;
    const int MaxLogLines = 12;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (FindAnyObjectByType<HakutakuDemo>() == null)
            new GameObject("Hakutaku Demo").AddComponent<HakutakuDemo>();
    }

    string playerId;
    string displayName;
    string sessionTicket;

    bool busy;
    readonly List<string> log = new List<string>();
    GUIStyle titleStyle, sectionStyle, textStyle, bigStyle, buttonStyle, logStyle;

#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
    HakutakuServerInstanceAPI server;

    // only a successful read sets the total (docs/reference/api.md). until then XP is unknown and is
    // never written, so a failed read can't wipe out the player's real total
    bool xpKnown;
    long xp;
    float xpChangedAt = -10f;
#endif

    void Awake()
    {
        // a plain dark background, so the text reads better than it does over the default sky
        var cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.09f, 0.10f, 0.12f);
#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
        server = HakutakuServerSettings.CreateApi();
#endif
        Log("Talking to " + HakutakuSettings.staticSettings.ServerUrl);
    }

    // ---- The player, through the client SDK ------------------------------------------------

    void RegisterGuest()
    {
        busy = true;
        HakutakuClientAPI.RegisterGuest(
            new RegisterGuestRequest { HardwareId = SystemInfo.deviceUniqueIdentifier, DisplayName = "Demo player" },
            result =>
            {
                PlayerPrefs.SetString(DeviceTokenKey, result.DeviceToken);
                PlayerPrefs.Save();
                LoggedIn("Registered a new guest", result);
            },
            error => ClientFailed("RegisterGuest", error));
    }

    void LogBackIn()
    {
        busy = true;
        HakutakuClientAPI.LoginWithDeviceToken(
            new LoginWithDeviceTokenRequest { DeviceToken = PlayerPrefs.GetString(DeviceTokenKey) },
            result => LoggedIn("Logged back in with the saved device token", result),
            error =>
            {
                // an unknown token never makes a new player, so the game offers "new game" instead
                if (error.Error == HakutakuErrorCode.NotAuthenticated) PlayerPrefs.DeleteKey(DeviceTokenKey);
                ClientFailed("LoginWithDeviceToken", error);
            });
    }

    void LoggedIn(string what, LoginResult result)
    {
        busy = false;
        playerId = result.PlayerId;
        displayName = result.DisplayName;
        sessionTicket = result.SessionTicket;
#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
        xpKnown = false;
#endif
        Log(what + ": player " + playerId);
    }

    void ClientFailed(string call, HakutakuError error)
    {
        busy = false;
        Log("FAILED " + call + ": " + error.Error + ", " + error.ErrorMessage);
    }

    // ---- The game server, through the server SDK ---------------------------------------------
    // not in client builds: the server SDK and its key only exist in the editor and server builds

#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
    // what a Mirror authenticator does when a player joins
    void ValidateTicket(string ticket, string what)
    {
        busy = true;
        server.AuthenticateSessionTicket(new AuthenticateSessionTicketRequest { SessionTicket = ticket },
            result =>
            {
                busy = false;
                Log(what + ": valid, player " + result.PlayerId);
            },
            error => ServerFailed(what, error));
    }

    // then runs only after a successful read, never after a failed one
    void ReadXp(bool justWritten = false, Action then = null)
    {
        busy = true;
        server.GetUserReadOnlyData(new GetUserDataRequest { PlayerId = playerId, Keys = new List<string> { "Xp" } },
            result =>
            {
                busy = false;
                // a key never set is left out, which means 0. a failed read is not 0 (see ServerFailed)
                xp = result.Data.TryGetValue("Xp", out var text) && long.TryParse(text, out var value) ? value : 0;
                xpKnown = true;
                xpChangedAt = Time.time;
                Log(justWritten ? "Read it back from Hakutaku: XP is " + xp : "Read XP: " + xp);
                then?.Invoke();
            },
            error => ServerFailed("Read XP", error));
    }

    // a write sends the new total and never "+100", so repeating it is harmless
    void AddXp()
    {
        // never write a total that wasn't read first: if the read fails, nothing is written
        if (!xpKnown)
        {
            ReadXp(then: AddXp);
            return;
        }
        busy = true;
        var total = xp + XpPerClick;
        server.UpdateUserReadOnlyData(new UpdateUserDataRequest
            {
                PlayerId = playerId,
                Data = new Dictionary<string, string> { ["Xp"] = total.ToString() },
            },
            _ =>
            {
                Log("Wrote XP = " + total);
                ReadXp(justWritten: true);
            },
            error => ServerFailed("Write XP", error));
    }

    void ServerFailed(string call, HakutakuServerError error)
    {
        busy = false;
        if (call == "Read XP") xpKnown = false;
        var meaning =
            error.Error == HakutakuServerErrorCode.InvalidSessionTicket ? " (the game turns this player away)" :
            error.IsHakutakuUnavailable ? " (Hakutaku is down: the game can let them play offline, without saving XP)" :
            error.Error == HakutakuServerErrorCode.MissingServerKey ? " (save a key in Window > Hakutaku > Settings)" :
            error.Error == HakutakuServerErrorCode.ServerKeyRejected ? " (this game server's key was refused)" : "";
        Log(call + ": " + error.Error + meaning);
    }
#endif

    // ---- Screen ---------------------------------------------------------------------------------

    // the screen is laid out for a 720-pixel-tall window and scaled up to the real one, so it's the
    // same size on screen at 1080p, 1440p or 4K
    const float DesignHeight = 720f;

    void OnGUI()
    {
        MakeStyles();
        var scale = Mathf.Max(1f, Screen.height / DesignHeight);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        var screenWidth = Screen.width / scale;
        var screenHeight = Screen.height / scale;
        var width = Mathf.Min(660f, screenWidth - 40f);
        GUILayout.BeginArea(new Rect((screenWidth - width) / 2f, 20f, width, screenHeight - 40f));

        GUILayout.Label("Hakutaku demo", titleStyle);
        GUILayout.Label("Server: " + HakutakuSettings.staticSettings.ServerUrl, textStyle);
        GUILayout.Space(14f);

        GUILayout.Label("Player  (client SDK)", sectionStyle);
        GUILayout.Label(playerId == null ? "Not logged in" : (displayName ?? "No name") + "   " + playerId, textStyle);
        GUILayout.BeginHorizontal();
        GUI.enabled = !busy;
        if (GUILayout.Button("Register a new guest", buttonStyle)) RegisterGuest();
        GUI.enabled = !busy && PlayerPrefs.HasKey(DeviceTokenKey);
        if (GUILayout.Button("Log back in", buttonStyle)) LogBackIn();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Space(18f);

        GUILayout.Label("Game server  (server SDK)", sectionStyle);
#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
        // the number goes green for a moment whenever a value comes back from Hakutaku
        var flash = Mathf.Clamp01(1f - (Time.time - xpChangedAt) / 1.5f);
        GUI.color = Color.Lerp(Color.white, new Color(0.45f, 1f, 0.55f), flash);
        GUILayout.Label("XP  " + (xpKnown ? xp.ToString() : "?"), bigStyle);
        GUI.color = Color.white;

        GUILayout.BeginHorizontal();
        GUI.enabled = !busy && sessionTicket != null;
        if (GUILayout.Button("Validate ticket", buttonStyle)) ValidateTicket(sessionTicket, "Validate ticket");
        if (GUILayout.Button("Read XP", buttonStyle)) ReadXp();
        if (GUILayout.Button("+" + XpPerClick + " XP", buttonStyle)) AddXp();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUI.enabled = !busy;
        if (GUILayout.Button("Try a fake ticket", buttonStyle)) ValidateTicket("not-a-real-ticket", "Fake ticket");
        GUI.enabled = true;
#else
        GUILayout.Label("Not in this build: the server SDK is only in the editor and in game server builds.", textStyle);
#endif
        GUILayout.Space(18f);

        GUILayout.Label("Log", sectionStyle);
        foreach (var line in log) GUILayout.Label(line, logStyle);
        GUILayout.EndArea();
    }

    void Log(string line)
    {
        Debug.Log("[Hakutaku demo] " + line);
        log.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + line);
        if (log.Count > MaxLogLines) log.RemoveAt(log.Count - 1);
    }

    // GUI.skin only exists inside OnGUI, so the styles are made on the first call
    void MakeStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold };
        sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
        bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 52, fontStyle = FontStyle.Bold };
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 17, fixedHeight = 44f };
        logStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
    }
}
