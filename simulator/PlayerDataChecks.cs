/*!************************************************************************
\file   PlayerDataChecks.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the player data checks of the probe. With a fresh
        server key it acts as a game server: it writes Xp and reads it back,
        checks that a write sets and never adds, that a missing key is left
        out, that the tier is required, and that every limit is refused. It
        fills a tier to 100 keys to check the 101st is refused, and cleans
        up after itself.

        Run from ServerKeyChecks.cs with the key it made. A player token
        being refused on these routes is checked by AccessChecks in
        Probe.cs, which needs no owner password.
**************************************************************************/
using System.Text.Json;

namespace simulator
{
    static class PlayerDataChecks
    {
        const string Who = "game server";

        public static async Task RunAsync(RawClient server, string key, string playerId, Report report)
        {
            Report.Heading($"Player data, as a game server (player {playerId})");

            string Path(string tier) => $"/api/server/players/{playerId}/data?tier={tier}";
            Task<(int Status, string Body)> Write(string tier, object body) => server.SendAsync(HttpMethod.Post, Path(tier), body, key);
            Task<(int Status, string Body)> Read(string tier, string? keys = null) =>
                server.SendAsync(HttpMethod.Get, Path(tier) + (keys is null ? "" : $"&keys={keys}"), serverKey: key);

            // write Xp, then read it back: the same value
            var write = await Write("ReadOnly", new { data = new Dictionary<string, string> { ["Xp"] = "1200" } });
            report.Assert(Who, "POST data: Xp=1200 (ReadOnly)", write.Status == 200 && Updated(write.Body) == 1, $"{write.Status} {write.Body}");
            var read = await Read("ReadOnly", "Xp");
            report.Assert(Who, "GET data: Xp reads back the same value", read.Status == 200 && ValueOf(read.Body, "Xp") == "1200", $"{read.Status} {read.Body}");

            // a retry is harmless, and a write sets the value, it never adds to it
            var retry = await Write("ReadOnly", new { data = new Dictionary<string, string> { ["Xp"] = "1200" } });
            var afterRetry = await Read("ReadOnly", "Xp");
            report.Assert(Who, "  ...the same write again changes nothing more", retry.Status == 200 && ValueOf(afterRetry.Body, "Xp") == "1200", $"{retry.Status}, Xp is {ValueOf(afterRetry.Body, "Xp")}");
            await Write("ReadOnly", new { data = new Dictionary<string, string> { ["Xp"] = "1300" } });
            var afterSet = await Read("ReadOnly", "Xp");
            report.Assert(Who, "  ...a new value replaces the old one", ValueOf(afterSet.Body, "Xp") == "1300", $"Xp is {ValueOf(afterSet.Body, "Xp")}");

            // a key that isn't there is left out, and no keys means every key in the tier
            var partly = await Read("ReadOnly", "Xp,Nope");
            report.Assert(Who, "GET data: a missing key is left out", partly.Status == 200 && Names(partly.Body).SequenceEqual(["Xp"]), $"{partly.Status} {partly.Body}");
            var all = await Read("ReadOnly");
            report.Assert(Who, "GET data: no keys gives every key in the tier", all.Status == 200 && Names(all.Body).Contains("Xp"), $"{all.Status} {all.Body}");
            var otherTier = await Read("Internal", "Xp");
            report.Assert(Who, "  ...and another tier doesn't have it", otherTier.Status == 200 && Names(otherTier.Body).Count == 0, $"{otherTier.Status} {otherTier.Body}");

            // the tier is required, and given by name
            var noTier = await server.SendAsync(HttpMethod.Get, $"/api/server/players/{playerId}/data", serverKey: key);
            report.Assert(Who, "GET data with no tier is refused", noTier.Status == 400, $"{noTier.Status} {noTier.Body}");
            var numberTier = await Read("1");
            report.Assert(Who, "GET data with tier=1 is refused", numberTier.Status == 400, $"{numberTier.Status}");

            // every limit is refused
            await Refused(report, "a key with a space", await Write("ReadOnly", new { data = new Dictionary<string, string> { ["bad key"] = "v" } }));
            await Refused(report, "a key of 65 characters", await Write("ReadOnly", new { data = new Dictionary<string, string> { [new string('k', 65)] = "v" } }));
            await Refused(report, "a value of 16385 bytes", await Write("ReadOnly", new { data = new Dictionary<string, string> { ["Big"] = new string('v', 16 * 1024 + 1) } }));
            await Refused(report, "11 changes in one request", await Write("ReadOnly", new { data = Enumerable.Range(1, 11).ToDictionary(i => $"K{i}", _ => "v") }));
            await Refused(report, "a key both set and removed", await Write("ReadOnly", new { data = new Dictionary<string, string> { ["Xp"] = "1" }, keysToRemove = new[] { "Xp" } }));
            var untouched = await Read("ReadOnly", "Xp");
            report.Assert(Who, "  ...and none of them changed anything", ValueOf(untouched.Body, "Xp") == "1300", $"Xp is {ValueOf(untouched.Body, "Xp")}");

            // at most 100 keys in a tier: fill one, check the 101st is refused, then take them all out again
            var filled = 0;
            for (var batch = 0; batch < 10; batch++)
            {
                var keys = Enumerable.Range(1, 10).ToDictionary(i => $"ProbeKey{batch * 10 + i}", _ => "v");
                if ((await Write("Internal", new { data = keys })).Status == 200) filled += 10;
            }
            report.Assert(Who, "POST data: 100 keys in one tier, 10 requests of 10", filled == 100, $"{filled} keys written");
            var extra = await Write("Internal", new { data = new Dictionary<string, string> { ["OneTooMany"] = "v" } });
            await Refused(report, "the 101st key in a tier", extra);
            for (var batch = 0; batch < 10; batch++)
                await Write("Internal", new { keysToRemove = Enumerable.Range(1, 10).Select(i => $"ProbeKey{batch * 10 + i}") });
            var emptied = await Read("Internal");
            report.Assert(Who, "  ...and the probe's 100 keys are removed again", Names(emptied.Body).Count == 0, $"{Names(emptied.Body).Count} left");

            // a player that doesn't exist
            var stranger = Guid.NewGuid();
            var unknown = await server.SendAsync(HttpMethod.Get, $"/api/server/players/{stranger}/data?tier=ReadOnly", serverKey: key);
            report.Assert(Who, "GET data for a player that doesn't exist", unknown.Status == 404, $"{unknown.Status} {unknown.Body}");
            var unknownWrite = await server.SendAsync(HttpMethod.Post, $"/api/server/players/{stranger}/data?tier=ReadOnly", new { data = new Dictionary<string, string> { ["Xp"] = "1" } }, key);
            report.Assert(Who, "POST data for a player that doesn't exist", unknownWrite.Status == 404, $"{unknownWrite.Status}");

            // tidy up: the mock player is a permanent row, so don't leave XP on it
            await Write("ReadOnly", new { keysToRemove = new[] { "Xp" } });
        }

        // a request over a limit gets a 400 with a message
        static Task Refused(Report report, string what, (int Status, string Body) reply)
        {
            report.Assert(Who, $"POST data: {what} is refused", reply.Status == 400, $"{reply.Status} {reply.Body}");
            return Task.CompletedTask;
        }

        static int? Updated(string body)
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("updated", out var n) ? n.GetInt32() : null;
        }

        // the value of one key in an answer like {"data":{"Xp":"1200"}}, or null if it isn't there
        static string? ValueOf(string body, string name)
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty(name, out var value) ? value.GetString() : null;
        }

        static List<string> Names(string body)
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("data", out var data) ? data.EnumerateObject().Select(p => p.Name).ToList() : [];
        }
    }
}
