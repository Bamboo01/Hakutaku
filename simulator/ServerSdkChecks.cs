/*!************************************************************************
\file   ServerSdkChecks.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   4th November 2026
\brief  This file defines the server SDK checks of the probe. It plays a
        game server's authenticator: a fresh mock player logs in through
        the client SDK, and the server SDK validates its ticket, then
        writes Xp and reads it back. It also checks the errors the
        authenticator decides on: an unknown ticket is
        InvalidSessionTicket, a wrong server key is ServerKeyRejected, and
        a server that never answers or isn't there is Timeout or
        ConnectionError, both counted as Hakutaku being unavailable.

        Run from ServerKeyChecks.cs with the key it made. Uses
        sdk-server/ through its public API only, as a game server would.
**************************************************************************/
using System.Net;
using System.Net.Sockets;
using Hakutaku;
using Hakutaku.ClientModels;
using Hakutaku.ServerModels;

namespace simulator
{
    static class ServerSdkChecks
    {
        const string Who = "server sdk";

        public static async Task RunAsync(string url, string key, Report report)
        {
            Report.Heading("Server SDK, as a game server's authenticator");

            // a player of its own, so its ticket is fresh whatever the account flows did to the others
            var player = new MockPlayer("sdk-player", url);
            var login = await player.Client.RegisterGuestAsync(new RegisterGuestRequest { HardwareId = player.HardwareId });
            report.Assert(Who, "a player registers through the client SDK", login.Error is null, login.Error?.GenerateErrorReport());
            if (login.Result is null) return;

            var server = new HakutakuServerInstanceAPI(url, key);

            // the join: validate the ticket the player would send
            var who = await server.AuthenticateSessionTicketAsync(new AuthenticateSessionTicketRequest { SessionTicket = login.Result.SessionTicket });
            report.Assert(Who, "AuthenticateSessionTicket (real ticket) gives that player",
                who.Error is null && who.Result!.PlayerId == login.Result.PlayerId, who.Error?.GenerateErrorReport() ?? who.Result!.PlayerId);
            if (who.Result is null) return;
            var playerId = who.Result.PlayerId;

            var unknown = await server.AuthenticateSessionTicketAsync(new AuthenticateSessionTicketRequest { SessionTicket = "not-a-real-ticket" });
            report.Assert(Who, "AuthenticateSessionTicket (unknown ticket) is InvalidSessionTicket",
                unknown.Error is { Error: HakutakuServerErrorCode.InvalidSessionTicket, IsHakutakuUnavailable: false }, Describe(unknown.Error));

            var wrongKey = new HakutakuServerInstanceAPI(url, "not-a-real-key");
            var refused = await wrongKey.AuthenticateSessionTicketAsync(new AuthenticateSessionTicketRequest { SessionTicket = login.Result.SessionTicket });
            report.Assert(Who, "  ...with a wrong server key it is ServerKeyRejected, not the ticket",
                refused.Error is { Error: HakutakuServerErrorCode.ServerKeyRejected }, Describe(refused.Error));

            // the match: write Xp, then read it back as the next join would
            var write = await server.UpdateUserReadOnlyDataAsync(new UpdateUserDataRequest
            {
                PlayerId = playerId,
                Data = new Dictionary<string, string> { ["Xp"] = "1200" },
            });
            report.Assert(Who, "UpdateUserReadOnlyData: Xp=1200", write.Error is null && write.Result!.Updated == 1, Describe(write.Error) ?? $"updated {write.Result!.Updated}");
            var read = await server.GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayerId = playerId, Keys = ["Xp", "NotSet"] });
            report.Assert(Who, "GetUserReadOnlyData: Xp reads back, a key never set is left out",
                read.Error is null && read.Result!.Data.TryGetValue("Xp", out var xp) && xp == "1200" && !read.Result.Data.ContainsKey("NotSet"),
                Describe(read.Error) ?? string.Join(", ", read.Result!.Data.Select(p => $"{p.Key}={p.Value}")));

            // hakutaku down: a server that takes the connection and never answers, then no server at all
            using (var silent = new TcpListener(IPAddress.Loopback, 0))
            {
                silent.Start();
                var held = new System.Collections.Concurrent.ConcurrentBag<TcpClient>();
                var accepting = Task.Run(async () =>
                {
                    try { while (true) held.Add(await silent.AcceptTcpClientAsync()); }
                    catch (Exception) { }
                });
                var slow = new HakutakuServerInstanceAPI($"http://127.0.0.1:{((IPEndPoint)silent.LocalEndpoint).Port}", key, TimeSpan.FromSeconds(1));
                var timedOut = await slow.AuthenticateSessionTicketAsync(new AuthenticateSessionTicketRequest { SessionTicket = login.Result.SessionTicket });
                report.Assert(Who, "AuthenticateSessionTicket (no answer in 1 s) is Timeout, and unavailable",
                    timedOut.Error is { Error: HakutakuServerErrorCode.Timeout, IsHakutakuUnavailable: true }, Describe(timedOut.Error));
                silent.Stop();
                foreach (var c in held) c.Dispose();
            }

            var closed = new TcpListener(IPAddress.Loopback, 0);
            closed.Start();
            var closedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
            closed.Stop();
            var down = new HakutakuServerInstanceAPI($"http://127.0.0.1:{closedPort}", key);
            var unreachable = await down.AuthenticateSessionTicketAsync(new AuthenticateSessionTicketRequest { SessionTicket = login.Result.SessionTicket });
            report.Assert(Who, "AuthenticateSessionTicket (nothing listening) is ConnectionError, and unavailable",
                unreachable.Error is { Error: HakutakuServerErrorCode.ConnectionError, IsHakutakuUnavailable: true }, Describe(unreachable.Error));

            // tidy up: the mock player is a permanent row, so don't leave XP on it
            await server.UpdateUserReadOnlyDataAsync(new UpdateUserDataRequest { PlayerId = playerId, KeysToRemove = ["Xp"] });
        }

        static string? Describe(HakutakuServerError? error) =>
            error is null ? null : $"{error.Error} ({(error.HttpCode == 0 ? "no response" : "HTTP " + error.HttpCode)})";
    }
}
