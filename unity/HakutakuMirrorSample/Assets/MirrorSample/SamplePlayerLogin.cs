// Ported from Sundered Frames' SFPlayerLogin.cs (Gam300, feature/hakutaku-integration).
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hakutaku;
using Hakutaku.ClientModels;
using UnityEngine;

namespace HakutakuMirrorSample
{
    // Signs this client in to Hakutaku and holds the session ticket the game server checks.
    //
    //   saved device token -> LoginWithDeviceToken
    //   no token           -> RegisterGuest, then save the token it returns (it's only ever returned once)
    //   token refused      -> set it aside and register a new guest
    //
    // GetTicketAsync never throws and never waits longer than LoginTimeoutMs. If Hakutaku can't be reached it
    // answers "", and the server decides what an empty ticket means. A failed login isn't remembered, so the
    // next connect tries again.
    //
    // Tokens are kept per server and per profile:
    //   <persistentDataPath>/Hakutaku/<server>/<profile>.token
    // Per server so that testing against a local Hakutaku can't overwrite the token of your production player.
    // Per profile so that two clients on one machine are two players: run one with -profile <name>. ParrelSync
    // clones share persistentDataPath with the main editor, so each clone gets its own profile automatically.
    public static class SamplePlayerLogin
    {
        const int LoginTimeoutMs = 5000;
        const string DefaultProfile = "default";
        const int ProfileMaxLength = 32;

        // Don't hand out a ticket that's about to run out on the way to the game server
        static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(1);

        static string ticket;
        static DateTime ticketExpires;
        static Task loginInFlight;

        public static string PlayerId { get; private set; }

        // Statics survive entering play mode when domain reload is off
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ticket = null;
            ticketExpires = default;
            loginInFlight = null;
            PlayerId = null;
        }

        // Call from the main thread. The ticket, or "" if there's no session right now.
        public static async Task<string> GetTicketAsync()
        {
            if (HasLiveTicket())
                return ticket;

            loginInFlight ??= LoginAsync();
            var login = loginInFlight;

            // A login that outlasts this wait carries on, so its ticket is there for the next connect
            if (await Task.WhenAny(login, Task.Delay(LoginTimeoutMs)) != login)
            {
                Debug.LogWarning("[SamplePlayerLogin] Hakutaku didn't answer in time; connecting without a session.");
                return "";
            }

            return HasLiveTicket() ? ticket : "";
        }

        // The game server refused this ticket, so the next connect has to log in again
        public static void ForgetSession()
        {
            ticket = null;
            ticketExpires = default;
        }

        static bool HasLiveTicket() =>
            !string.IsNullOrEmpty(ticket) && DateTime.UtcNow + ExpiryMargin < ticketExpires;

        static async Task LoginAsync()
        {
            try
            {
                string path = TokenPath();
                string token = ReadToken(path);

                if (token != null)
                {
                    var login = await HakutakuClientAPI.LoginWithDeviceTokenAsync(
                        new LoginWithDeviceTokenRequest { DeviceToken = token });

                    if (login.Error == null)
                    {
                        Remember(login.Result);
                        Debug.Log($"[SamplePlayerLogin] Logged in as player {PlayerId} (profile '{Profile()}').");
                        return;
                    }

                    // Only a 401 means the token is dead. Being offline must never cost a player their account.
                    if (login.Error.Error != HakutakuErrorCode.NotAuthenticated)
                    {
                        Debug.LogWarning($"[SamplePlayerLogin] Couldn't log in: {login.Error.Error}, {login.Error.ErrorMessage}");
                        return;
                    }

                    SetAside(path);
                    Debug.LogWarning("[SamplePlayerLogin] Hakutaku refused the saved device token; registering a new guest.");
                }

                var registered = await HakutakuClientAPI.RegisterGuestAsync(
                    new RegisterGuestRequest { HardwareId = SystemInfo.deviceUniqueIdentifier });

                if (registered.Error != null)
                {
                    Debug.LogWarning($"[SamplePlayerLogin] Couldn't register: {registered.Error.Error}, {registered.Error.ErrorMessage}");
                    return;
                }

                Remember(registered.Result);
                WriteToken(path, registered.Result.DeviceToken);
                Debug.Log($"[SamplePlayerLogin] Registered new player {PlayerId} (profile '{Profile()}').");
            }
            catch (Exception e)
            {
                // A disk problem, say. The player can still join without a session.
                Debug.LogError($"[SamplePlayerLogin] Login failed: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                loginInFlight = null;
            }
        }

        static void Remember(LoginResult result)
        {
            PlayerId = result.PlayerId;
            ticket = result.SessionTicket;
            ticketExpires = result.SessionExpiration.ToUniversalTime();
        }

        // ---- The token file. Its contents are a credential: never log them. ----

        static string TokenPath()
        {
            string server = Sanitize(new Uri(HakutakuSettings.staticSettings.ServerUrl).Authority);
            return Path.Combine(Application.persistentDataPath, "Hakutaku", server, Profile() + ".token");
        }

        static string ReadToken(string path)
        {
            if (!File.Exists(path))
                return null;

            string token = File.ReadAllText(path).Trim();
            return token.Length > 0 ? token : null;
        }

        // Through a temp file, so a crash mid-write can't leave half a token where the real one was
        static void WriteToken(string path, string token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, token);

            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }

        // Kept rather than deleted: it's the only record of the old account if the refusal was a mistake
        static void SetAside(string path)
        {
            string rejected = $"{path}.rejected-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(path, rejected);
        }

        // ---- The profile ----

        static string Profile()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-profile");

            if (index >= 0)
            {
                string name = index + 1 < args.Length ? args[index + 1] : "";
                if (IsValidProfile(name))
                    return name;

                Debug.LogWarning($"[SamplePlayerLogin] Ignoring -profile: use 1-{ProfileMaxLength} letters, digits, _ or -.");
            }

#if UNITY_EDITOR
            // A ParrelSync clone's project folder ends in _clone_<n>
            var clone = Regex.Match(Application.dataPath, @"_clone_(\d+)/Assets$");
            if (clone.Success)
                return "clone_" + clone.Groups[1].Value;
#endif
            return DefaultProfile;
        }

        static bool IsValidProfile(string name) =>
            name.Length is > 0 and <= ProfileMaxLength && name.All(c => c is '_' or '-' || (c < 128 && char.IsLetterOrDigit(c)));

        static string Sanitize(string text) => Regex.Replace(text, @"[^A-Za-z0-9._-]", "_");
    }
}
