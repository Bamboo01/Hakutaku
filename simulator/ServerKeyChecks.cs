/*!************************************************************************
\file   ServerKeyChecks.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the server key checks of the probe. Signed in as
        the owner, it makes server keys and checks that on /api/server/*
        no key, a wrong key, a revoked key and an expired key all get a 401
        while a fresh key works, and that a server key never opens an admin
        route. It revokes every key it made when it is done.

        Run from Probe.cs when an owner password is given. There is no SDK
        call for any of this, so it uses plain HTTP with a cookie jar.
**************************************************************************/
using System.Net;
using System.Text.Json;

namespace simulator
{
    static class ServerKeyChecks
    {
        const string Who = "owner";
        const string Ping = "/api/server/ping";

        public static async Task RunAsync(string url, string adminPassword, Report report)
        {
            Report.Heading("Server keys, as the owner");

            using var owner = new RawClient(url);
            var login = await owner.SendAsync(HttpMethod.Post, "/api/admin/login", new { username = "admin", password = adminPassword });
            report.Assert(Who, "login as admin", login.Status == 204, $"{login.Status}");
            if (login.Status != 204)
            {
                Report.Note("Can't go on without the owner. Is the password right, and does this server let /api/admin through?");
                return;
            }

            using var anonymous = new RawClient(url);
            var madeIds = new List<long>();

            // a key with a lot of time left
            var fresh = await MakeKey(owner, report, "sim-probe-fresh", DateTimeOffset.UtcNow.AddDays(1), madeIds);
            if (fresh is null) return;

            var none = await anonymous.SendAsync(HttpMethod.Get, Ping);
            report.Assert(Who, $"GET {Ping} (no key)", none.Status == 401, $"{none.Status}");
            var wrong = await anonymous.SendAsync(HttpMethod.Get, Ping, serverKey: "not-a-real-key");
            report.Assert(Who, $"GET {Ping} (wrong key)", wrong.Status == 401, $"{wrong.Status}");
            var works = await anonymous.SendAsync(HttpMethod.Get, Ping, serverKey: fresh.Value.Key);
            report.Assert(Who, $"GET {Ping} (fresh key)", works.Status == 200, $"{works.Status}");

            // a server key opens server routes only, never an admin route
            var onAdmin = await anonymous.SendAsync(HttpMethod.Get, "/api/admin/server-keys", serverKey: fresh.Value.Key);
            report.Assert(Who, "GET /api/admin/server-keys (server key, no cookie)", onAdmin.Status == 401, $"{onAdmin.Status}");

            // revoking one: it works, then it doesn't
            var toRevoke = await MakeKey(owner, report, "sim-probe-revoked", null, madeIds);
            if (toRevoke is not null)
            {
                var before = await anonymous.SendAsync(HttpMethod.Get, Ping, serverKey: toRevoke.Value.Key);
                report.Assert(Who, "  ...works before it is revoked", before.Status == 200, $"{before.Status}");
                var revoke = await owner.SendAsync(HttpMethod.Delete, $"/api/admin/server-keys/{toRevoke.Value.Id}");
                report.Assert(Who, "DELETE /api/admin/server-keys/{id}", revoke.Status == 204, $"{revoke.Status}");
                var after = await anonymous.SendAsync(HttpMethod.Get, Ping, serverKey: toRevoke.Value.Key);
                report.Assert(Who, $"GET {Ping} (revoked key)", after.Status == 401, $"{after.Status}");
            }

            // an expired one. a key can't be made already expired, so make one that runs out in a few seconds
            var expiring = await MakeKey(owner, report, "sim-probe-expiring", DateTimeOffset.UtcNow.AddSeconds(3), madeIds);
            if (expiring is not null)
            {
                var before = await anonymous.SendAsync(HttpMethod.Get, Ping, serverKey: expiring.Value.Key);
                report.Assert(Who, "  ...works before it expires", before.Status == 200, $"{before.Status}");
                Report.Note("waiting 4 s for it to expire");
                await Task.Delay(TimeSpan.FromSeconds(4));
                var after = await anonymous.SendAsync(HttpMethod.Get, Ping, serverKey: expiring.Value.Key);
                report.Assert(Who, $"GET {Ping} (expired key)", after.Status == 401, $"{after.Status}");
            }

            // the list has them, and never a key
            var list = await owner.SendAsync(HttpMethod.Get, "/api/admin/server-keys");
            report.Assert(Who, "GET /api/admin/server-keys never shows a key",
                list.Status == 200 && !list.Body.Contains(fresh.Value.Key), $"{list.Status}");

            // leave nothing usable behind
            foreach (var id in madeIds)
                await owner.SendAsync(HttpMethod.Delete, $"/api/admin/server-keys/{id}");
            var gone = await anonymous.SendAsync(HttpMethod.Get, Ping, serverKey: fresh.Value.Key);
            report.Assert(Who, "  ...and the probe's keys are revoked again", gone.Status == 401, $"{gone.Status}");
        }

        // makes a key and checks the answer. null means the check failed and the rest can't go on
        static async Task<(long Id, string Key)?> MakeKey(RawClient owner, Report report, string name, DateTimeOffset? expiresAt, List<long> madeIds)
        {
            var reply = await owner.SendAsync(HttpMethod.Post, "/api/admin/server-keys", new { name, expiresAt });
            if (reply.Status != 200)
            {
                report.Assert(Who, $"POST /api/admin/server-keys ({name})", false, $"{reply.Status} {reply.Body}");
                return null;
            }
            using var json = JsonDocument.Parse(reply.Body);
            var id = json.RootElement.GetProperty("id").GetInt64();
            var key = json.RootElement.GetProperty("key").GetString() ?? "";
            madeIds.Add(id);
            report.Assert(Who, $"POST /api/admin/server-keys ({name})", key.Length == 43, $"{reply.Status}, key of {key.Length} characters");
            return (id, key);
        }
    }
}
