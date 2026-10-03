using Hakutaku;
using Hakutaku.ClientModels;

namespace simulator
{
    enum Expect
    {
        Ok,
        BadRequest,
        Unauthorized,
        Forbidden,
        Conflict,
        // 401 or 403, which is what the admin gate answers locally or through the tunnel.
        // Against any other host 404 counts too, since that's Caddy's answer on the public
        // domain for a route that isn't public (see Report).
        Denied,
    }

    // The scripted run: makes mock players, takes one through the account flows, then has
    // every one of them try the routes beyond player auth. Each step is checked against
    // what the server is supposed to do, so the run doubles as a regression check.
    static class Probe
    {
        // Every route a player token shouldn't open yet, and what it should get. When the
        // server opens one to players (TODO.md item 2), change its Expect here so the probe
        // holds the server to the new rule.
        public static readonly (string Label, Expect Expect, Func<HakutakuClientInstanceAPI, Task<HakutakuError?>> Call)[] AccessChecks =
        [
            ("WritePlayerEvent       POST /api/events", Expect.Denied, async c => (await c.WritePlayerEventAsync(new WriteClientPlayerEventRequest
            {
                EventName = "sim_probe",
                Body = new Dictionary<string, object> { ["source"] = "simulator" },
            })).Error),
            ("GetAllUsersCharacters  GET /api/characters", Expect.Denied, async c => (await c.GetAllUsersCharactersAsync(new ListUsersCharactersRequest())).Error),
            ("GrantCharacterToUser   POST /api/characters", Expect.Denied, async c => (await c.GrantCharacterToUserAsync(new GrantCharacterToUserRequest { CharacterName = "sim" })).Error),
            // No SDK method for these: no game client should ever call them.
            ("raw                    GET /api/players", Expect.Denied, async c => (await c.SendRawRequestAsync("GET", "/api/players")).Error),
            ("raw                    POST /api/players", Expect.Denied, async c => (await c.SendRawRequestAsync("POST", "/api/players", """{"xp":0}""")).Error),
            ("raw                    GET /api/events", Expect.Denied, async c => (await c.SendRawRequestAsync("GET", "/api/events")).Error),
            ("raw                    GET /api/admin/me", Expect.Denied, async c => (await c.SendRawRequestAsync("GET", "/api/admin/me")).Error),
            ("raw                    GET /api/admin/admins", Expect.Denied, async c => (await c.SendRawRequestAsync("GET", "/api/admin/admins")).Error),
        ];

        public static async Task<int> RunAsync(string url, int playerCount)
        {
            var report = new Report(url);

            Report.Heading($"Registering {playerCount} mock player(s)");
            var players = new List<MockPlayer>();
            for (var i = 1; i <= playerCount; i++)
            {
                var player = new MockPlayer($"player-{i}", url);
                var wanted = $"Sim {player.Name}";
                var login = await RetryIfLimited(() => player.Client.RegisterGuestAsync(new RegisterGuestRequest { HardwareId = player.HardwareId, DisplayName = wanted }));
                if (login.Error?.Error == HakutakuErrorCode.ConnectionError)
                {
                    Report.Problem($"Can't reach {url}: {login.Error.ErrorMessage}. Is the server running?");
                    return 1;
                }
                report.Check(player.Name, "RegisterGuest (named)", Expect.Ok, login.Error, login.Result?.PlayerId);
                if (login.Result == null) continue;

                report.Assert(player.Name, "  ...has the display name it asked for", login.Result.DisplayName == wanted, login.Result.DisplayName);
                report.Assert(player.Name, "  ...and a device token to keep", !string.IsNullOrEmpty(login.Result.DeviceToken),
                    $"{login.Result.DeviceToken?.Length ?? 0} characters");
                player.DisplayName = login.Result.DisplayName;
                player.DeviceToken = login.Result.DeviceToken;
                players.Add(player);
            }

            if (players.Count > 0)
            {
                Report.Heading($"Account flows, as {players[0].Name}");
                await AccountFlows(url, players[0], players.Count > 1 ? players[1] : null, report);

                Report.Heading("Routes beyond player auth, as each player");
                foreach (var player in players)
                    await RunAccessChecks(player, report);
            }

            report.PrintSummary();
            return report.Failed == 0 ? 0 : 1;
        }

        static async Task AccountFlows(string url, MockPlayer player, MockPlayer? other, Report report)
        {
            var c = player.Client;
            var playerId = player.PlayerId;
            // Someone who isn't logged in as anybody.
            var stranger = new HakutakuClientInstanceAPI(new HakutakuApiSettings { ServerUrl = url });

            // The device token signs the same player back in, with a new session.
            var again = await RetryIfLimited(() => c.LoginWithDeviceTokenAsync(new LoginWithDeviceTokenRequest { DeviceToken = player.DeviceToken }));
            report.Check(player.Name, "LoginWithDeviceToken", Expect.Ok, again.Error);
            if (again.Result != null)
            {
                report.Assert(player.Name, "  ...is the same player", again.Result.PlayerId == playerId, again.Result.PlayerId);
                report.Assert(player.Name, "  ...with its display name", again.Result.DisplayName == player.DisplayName, again.Result.DisplayName);
            }

            var unknownToken = await RetryIfLimited(() => stranger.LoginWithDeviceTokenAsync(new LoginWithDeviceTokenRequest { DeviceToken = "made-up-device-token" }));
            report.Check("stranger", "LoginWithDeviceToken (made-up token)", Expect.Unauthorized, unknownToken.Error);

            // The hardware ID is only recorded, so registering the same hardware again makes a
            // new player rather than handing out this one.
            var sameHardware = new HakutakuClientInstanceAPI(new HakutakuApiSettings { ServerUrl = url });
            var twin = await RetryIfLimited(() => sameHardware.RegisterGuestAsync(new RegisterGuestRequest { HardwareId = player.HardwareId }));
            report.Check(player.Name, "RegisterGuest (same hardware ID again)", Expect.Ok, twin.Error);
            if (twin.Result != null)
                report.Assert(player.Name, "  ...is a different player", twin.Result.PlayerId != playerId, twin.Result.PlayerId);

            // What a client from before device tokens sends: a deviceId and no hardwareId.
            var oldClient = await RetryIfLimited(() => stranger.SendRawRequestAsync("POST", "/api/players/register", """{"deviceId":"old-client-device-id"}"""));
            report.Check("stranger", "raw POST /api/players/register (old deviceId body)", Expect.BadRequest, oldClient.Error);

            // Stored trimmed. Not rate limited, so these cost nothing against the limits.
            const string newName = "Sir Simulator";
            var rename = await c.UpdateUserTitleDisplayNameAsync(new UpdateUserTitleDisplayNameRequest { DisplayName = $"  {newName}  " });
            report.Check(player.Name, "UpdateUserTitleDisplayName", Expect.Ok, rename.Error, rename.Result?.DisplayName);
            if (rename.Result != null)
            {
                report.Assert(player.Name, "  ...is stored trimmed", rename.Result.DisplayName == newName, rename.Result.DisplayName);
                player.DisplayName = rename.Result.DisplayName;
            }

            // Display names aren't unique, so someone else can have the same one.
            if (other != null)
            {
                var same = await other.Client.UpdateUserTitleDisplayNameAsync(new UpdateUserTitleDisplayNameRequest { DisplayName = newName });
                report.Check(other.Name, $"UpdateUserTitleDisplayName ({player.Name}'s name)", Expect.Ok, same.Error);
            }

            foreach (var (label, bad) in new[] { ("too short", "ab"), ("too long", new string('x', 26)), ("control character", "bad\nname") })
            {
                var refused = await c.UpdateUserTitleDisplayNameAsync(new UpdateUserTitleDisplayNameRequest { DisplayName = bad });
                report.Check(player.Name, $"UpdateUserTitleDisplayName ({label})", Expect.BadRequest, refused.Error);
            }

            player.Email = MockPlayer.NewEmail();
            player.Password = MockPlayer.NewPassword();
            var link = await c.LinkEmailAddressAsync(new LinkEmailAddressRequest { Email = player.Email, Password = player.Password });
            report.Check(player.Name, "LinkEmailAddress", Expect.Ok, link.Error, link.Result?.Email);

            var second = await c.LinkEmailAddressAsync(new LinkEmailAddressRequest { Email = MockPlayer.NewEmail(), Password = player.Password });
            report.Check(player.Name, "LinkEmailAddress (a second email)", Expect.Conflict, second.Error);

            if (other != null)
            {
                var taken = await other.Client.LinkEmailAddressAsync(new LinkEmailAddressRequest { Email = player.Email, Password = MockPlayer.NewPassword() });
                report.Check(other.Name, $"LinkEmailAddress ({player.Name}'s email)", Expect.Conflict, taken.Error);
            }

            // The device token is removed when the email is verified, not when it is linked,
            // so a mistyped address can't strand a guest. Verifying for real needs the mailed
            // code, so that half is checked by hand in the shell.
            var stillWorks = await RetryIfLimited(() => c.LoginWithDeviceTokenAsync(new LoginWithDeviceTokenRequest { DeviceToken = player.DeviceToken }));
            report.Check(player.Name, "LoginWithDeviceToken (email linked, unverified)", Expect.Ok, stillWorks.Error);

            // The real code is only mailed (or, with no SMTP, logged by the server), so the
            // probe can only check that a wrong one is refused. Use the shell for the real one.
            var verify = await RetryIfLimited(() => c.VerifyEmailAsync(new VerifyEmailRequest { Code = "000000" }));
            report.Check(player.Name, "VerifyEmail (wrong code)", Expect.BadRequest, verify.Error);

            // An email isn't a way in until it's verified, even with the right password.
            // Logging in for real needs the mailed code, so the shell covers that half.
            var login = await RetryIfLimited(() => c.LoginWithEmailAddressAsync(new LoginWithEmailAddressRequest { Email = player.Email, Password = player.Password }));
            report.Check(player.Name, "LoginWithEmailAddress (unverified email)", Expect.Forbidden, login.Error);

            var wrongPassword = await RetryIfLimited(() => stranger.LoginWithEmailAddressAsync(new LoginWithEmailAddressRequest { Email = player.Email, Password = "not-the-password" }));
            report.Check("stranger", "LoginWithEmailAddress (unverified, wrong password)", Expect.Unauthorized, wrongPassword.Error);

            var unknown = await RetryIfLimited(() => stranger.LoginWithEmailAddressAsync(new LoginWithEmailAddressRequest { Email = MockPlayer.NewEmail(), Password = "not-the-password" }));
            report.Check("stranger", "LoginWithEmailAddress (unknown email)", Expect.Unauthorized, unknown.Error);

            // Same answer for any address, so it can't be used to find accounts. This one is
            // unverified, so no code is actually made.
            var forgot = await RetryIfLimited(() => stranger.SendAccountRecoveryEmailAsync(new SendAccountRecoveryEmailRequest { Email = player.Email }));
            report.Check("stranger", "SendAccountRecoveryEmail", Expect.Ok, forgot.Error);

            var reset = await RetryIfLimited(() => stranger.ResetPasswordAsync(new ResetPasswordRequest { Email = player.Email, Code = "000000", NewPassword = MockPlayer.NewPassword() }));
            report.Check("stranger", "ResetPassword (made-up code)", Expect.BadRequest, reset.Error);

            // The SDK won't send a session call without a session, so these go around it.
            var noToken = await stranger.SendRawRequestAsync("POST", "/api/players/link/email", """{"email":"sim@example.com","password":"sim-password"}""");
            report.Check("stranger", "raw POST /api/players/link/email (no token)", Expect.Unauthorized, noToken.Error);

            var noTokenName = await stranger.SendRawRequestAsync("POST", "/api/players/display-name", """{"displayName":"Sneaky"}""");
            report.Check("stranger", "raw POST /api/players/display-name (no token)", Expect.Unauthorized, noTokenName.Error);

            var forged = new HakutakuClientInstanceAPI(new HakutakuApiSettings { ServerUrl = url });
            forged.authenticationContext.SessionTicket = "made-up-token";
            var forgedLink = await forged.LinkEmailAddressAsync(new LinkEmailAddressRequest { Email = MockPlayer.NewEmail(), Password = MockPlayer.NewPassword() });
            report.Check("stranger", "LinkEmailAddress (made-up token)", Expect.Unauthorized, forgedLink.Error);
        }

        public static async Task RunAccessChecks(MockPlayer player, Report report)
        {
            foreach (var (label, expect, call) in AccessChecks)
                report.Check(player.Name, label, expect, await call(player.Client));
        }

        // register, both logins and the four mail routes each allow 5 calls a minute per IP, and
        // the server sends no Retry-After, so a 429 waits and tries again instead of failing
        // the run. A rejected call doesn't use up the limit, so polling every 10 s is fine.
        static async Task<HakutakuResult<T>> RetryIfLimited<T>(Func<Task<HakutakuResult<T>>> call) where T : class
        {
            const int RetrySeconds = 10, GiveUpSeconds = 70;
            for (var waited = 0; ; waited += RetrySeconds)
            {
                var result = await call();
                if (result.Error?.Error != HakutakuErrorCode.TooManyRequests || waited >= GiveUpSeconds)
                    return result;
                Report.Note($"rate-limited on {result.Error.ApiEndpoint}, retrying in {RetrySeconds} s");
                await Task.Delay(TimeSpan.FromSeconds(RetrySeconds));
            }
        }
    }
}
