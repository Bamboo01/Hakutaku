using System.Globalization;
using Hakutaku;
using Hakutaku.ClientModels;

namespace simulator
{
    // Interactive mode: make mock players and call the SDK by hand. This is how to finish
    // the flows that need an emailed code, since with no SMTP configured the server only
    // writes the code to its log.
    class Shell
    {
        const string HelpText = """
              new [display name]              register a new mock player with a random device ID, and switch to it
              device <deviceId>               log in with a device ID you already have, as a new mock player
              login <email> <password>        sign in by email as a new mock player, like a second device, and switch to it
              players                         list the mock players made in this session
              use <n>                         switch to mock player n

              name <display name>             set the current player's display name (3-25 characters, not unique)
              link [<email> <password>]       link an email and password to the current player (random if left out)
              verify <code>                   redeem the emailed code; with no SMTP it is in the server log
              resend                          mail a new verification code
              forgot <email>                  mail a password-reset code (only verified emails get one)
              reset <email> <code> <password> set a new password; revokes all of that player's sessions

              event <name> [key=value ...]    WritePlayerEvent        (admin-only on the server for now)
              chars                           GetAllUsersCharacters   (admin-only on the server for now)
              grant <name>                    GrantCharacterToUser    (admin-only on the server for now)
              raw <METHOD> <path> [json]      any request, sent with the current player's token
              probe                           try every non-player route as the current player
              forget                          drop the current player's session locally (there is no logout route)

              help, quit
            """;

        readonly string url;
        readonly List<MockPlayer> players = [];
        // For the calls that don't need a session (forgot, reset) and for raw requests
        // made before any player exists.
        readonly HakutakuClientInstanceAPI anonymous;
        MockPlayer? current;

        public Shell(string url)
        {
            this.url = url;
            anonymous = new HakutakuClientInstanceAPI(new HakutakuApiSettings { ServerUrl = url });
        }

        public async Task RunAsync()
        {
            Console.WriteLine($"Talking to {url}. Type 'help' for the commands.");
            while (true)
            {
                Console.Write(current is null ? "> " : $"{current.Name}> ");
                var line = Console.ReadLine();
                if (line is null) return;

                var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0) continue;
                var command = parts[0].ToLowerInvariant();
                if (command is "quit" or "exit") return;
                await Run(command, parts.Length > 1 ? parts[1] : "");
            }
        }

        // The commands that act as the current player.
        static readonly HashSet<string> PlayerCommands = ["name", "link", "verify", "resend", "event", "chars", "grant", "probe", "forget"];

        async Task Run(string command, string rest)
        {
            if (PlayerCommands.Contains(command) && current is null)
            {
                Console.WriteLine("  no current player; make one with 'new'");
                return;
            }
            var p = current!;
            var args = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            switch (command)
            {
                case "help":
                    Console.WriteLine(HelpText);
                    break;

                case "new":
                    await New(MockPlayer.NewDeviceId(), rest.Length > 0 ? rest : null);
                    break;

                case "device" when args.Length == 1:
                    await New(args[0], null);
                    break;

                case "login" when args.Length == 2:
                    await Login(args[0], args[1]);
                    break;

                case "players":
                    ListPlayers();
                    break;

                case "use" when args.Length == 1 && int.TryParse(args[0], out var n) && n >= 1 && n <= players.Count:
                    current = players[n - 1];
                    break;

                case "name" when args.Length >= 1:
                    var renamed = await p.Client.UpdateUserTitleDisplayNameAsync(new UpdateUserTitleDisplayNameRequest { DisplayName = rest });
                    Show(renamed, r => $"display name is now '{r.DisplayName}'");
                    if (renamed.Result is not null) p.DisplayName = renamed.Result.DisplayName;
                    break;

                case "link" when args.Length is 0 or 2:
                    await Link(p, args.Length == 2 ? args[0] : MockPlayer.NewEmail(), args.Length == 2 ? args[1] : MockPlayer.NewPassword());
                    break;

                case "verify" when args.Length == 1:
                    Show(await p.Client.VerifyEmailAsync(new VerifyEmailRequest { Code = args[0] }),
                        r => r.Verified ? "verified" : "not verified");
                    break;

                case "resend":
                    Show(await p.Client.ResendVerificationEmailAsync(new ResendVerificationEmailRequest()),
                        _ => "sent; with no SMTP the code is in the server log");
                    break;

                case "forgot" when args.Length == 1:
                    Show(await anonymous.SendAccountRecoveryEmailAsync(new SendAccountRecoveryEmailRequest { Email = args[0] }),
                        _ => "ok -- the answer is the same whether or not a code was sent");
                    break;

                case "reset" when args.Length == 3:
                    Show(await anonymous.ResetPasswordAsync(new ResetPasswordRequest { Email = args[0], Code = args[1], NewPassword = args[2] }),
                        _ => "password changed; every session that player had is now revoked");
                    break;

                case "event" when args.Length >= 1:
                    var body = new Dictionary<string, object>();
                    foreach (var pair in args.Skip(1).Select(a => a.Split('=', 2)))
                        body[pair[0]] = ParseValue(pair.Length > 1 ? pair[1] : "");
                    Show(await p.Client.WritePlayerEventAsync(new WriteClientPlayerEventRequest { EventName = args[0], Body = body }),
                        r => $"event {r.EventId}");
                    break;

                case "chars":
                    Show(await p.Client.GetAllUsersCharactersAsync(new ListUsersCharactersRequest()),
                        r => r.Characters.Count == 0 ? "no characters" : string.Join(", ", r.Characters.Select(ch => $"{ch.CharacterName} ({ch.CharacterId})")));
                    break;

                case "grant" when args.Length >= 1:
                    Show(await p.Client.GrantCharacterToUserAsync(new GrantCharacterToUserRequest { CharacterName = rest }),
                        r => $"character {r.CharacterId}");
                    break;

                case "raw" when args.Length >= 2:
                    var raw = rest.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    var client = current?.Client ?? anonymous;
                    Show(await client.SendRawRequestAsync(raw[0].ToUpperInvariant(), raw[1], raw.Length > 2 ? raw[2] : null),
                        r => r.Length == 0 ? "(empty body)" : r);
                    break;

                case "probe":
                    var report = new Report(url);
                    await Probe.RunAccessChecks(p, report);
                    report.PrintSummary();
                    break;

                case "forget":
                    p.Client.ForgetAllCredentials();
                    Console.WriteLine("  forgotten here; the token itself stays valid on the server until it expires");
                    break;

                case "device" or "use" or "login" or "name" or "link" or "verify" or "forgot" or "reset" or "event" or "grant" or "raw":
                    Console.WriteLine("  wrong arguments; type 'help'");
                    break;

                default:
                    Console.WriteLine("  unknown command; type 'help'");
                    break;
            }
        }

        // A display name only applies if this creates the player; a known device keeps its own.
        async Task New(string deviceId, string? displayName)
        {
            var player = new MockPlayer($"player-{players.Count + 1}", url) { DeviceId = deviceId };
            var result = await player.Client.LoginWithDeviceIDAsync(new LoginWithDeviceIDRequest { DeviceId = deviceId, DisplayName = displayName });
            Show(result, r => $"player {r.PlayerId}, {Named(r.DisplayName)}, xp {r.Xp}, device {deviceId}");
            Add(player, result.Result);
        }

        async Task Login(string email, string password)
        {
            var player = new MockPlayer($"player-{players.Count + 1}", url) { Email = email, Password = password };
            var result = await player.Client.LoginWithEmailAddressAsync(new LoginWithEmailAddressRequest { Email = email, Password = password });
            Show(result, r => $"player {r.PlayerId}, {Named(r.DisplayName)}, xp {r.Xp}");
            Add(player, result.Result);
        }

        static string Named(string? displayName) => displayName is null ? "no display name" : $"'{displayName}'";

        async Task Link(MockPlayer player, string email, string password)
        {
            var result = await player.Client.LinkEmailAddressAsync(new LinkEmailAddressRequest { Email = email, Password = password });
            Show(result, r => $"linked {r.Email} with password {password}; " +
                (r.VerificationSent ? "a code was sent (with no SMTP it is in the server log)" : "no code was sent, try 'resend'"));
            if (result.Error is null)
            {
                player.Email = email;
                player.Password = password;
            }
        }

        void Add(MockPlayer player, LoginResult? login)
        {
            if (login is null) return;
            player.DisplayName = login.DisplayName;
            players.Add(player);
            current = player;
        }

        void ListPlayers()
        {
            if (players.Count == 0) Console.WriteLine("  none yet; 'new' makes one");
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                var marker = p == current ? "*" : " ";
                // Only what this client holds: a token revoked server-side (by a password reset,
                // say) still shows here until a call gets a 401.
                var session = p.Client.IsClientLoggedIn() ? "has token" : "no token";
                Console.WriteLine($" {marker}{i + 1}  {p.Name,-10} {p.PlayerId,-36}  {session,-10}  {Named(p.DisplayName),-27}  {p.Email ?? "no email"}  {(p.DeviceId is null ? "" : "device " + p.DeviceId)}");
            }
        }

        static void Show<T>(HakutakuResult<T> result, Func<T, string> describe) where T : class
        {
            if (result.Error is null) Report.Line(ConsoleColor.Green, "  " + describe(result.Result!));
            else Report.Line(ConsoleColor.Red, "  " + result.Error.GenerateErrorReport());
        }

        // key=value from the command line: numbers and booleans as themselves, anything else a string.
        static object ParseValue(string text) =>
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l
            : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d
            : bool.TryParse(text, out var b) ? b
            : text;
    }
}
