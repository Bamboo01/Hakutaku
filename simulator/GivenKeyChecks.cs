/*!************************************************************************
\file   GivenKeyChecks.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   4th November 2026
\brief  This file defines the probe's checks with server keys given on the
        command line (--server-key, --revoked-key, --expired-key). They are
        for production, where the probe can't sign in as the owner to make
        keys, since /api/admin is tunnel-only. On /api/server/* no key, the
        revoked key and the expired key are all refused, the live key is
        refused on /api/admin/*, and with the live key the probe validates
        a real ticket and writes and reads Xp through the server SDK.

        Run from Probe.cs when any of those keys is given. A player ticket
        being refused on /api/server/* is in Probe.cs's AccessChecks, which
        always run. The SDK part is ServerSdkChecks.cs.
**************************************************************************/
namespace simulator
{
    // keys handed to the probe, not made by it. any of them may be missing
    record GivenKeys(string? ServerKey, string? RevokedKey, string? ExpiredKey)
    {
        public bool Any => ServerKey is not null || RevokedKey is not null || ExpiredKey is not null;
    }

    static class GivenKeyChecks
    {
        const string Who = "given key";
        const string Ping = "/api/server/ping";
        const string Validate = "/api/server/sessions/validate";

        public static async Task RunAsync(string url, GivenKeys keys, Report report)
        {
            Report.Heading("Server keys given on the command line");
            using var client = new RawClient(url);

            // on the public domain caddy answers 404 for a route it doesn't proxy, which is refused too
            var behindCaddy = !new Uri(url).IsLoopback;
            bool Denied(int status) => status is 401 or 403 || (behindCaddy && status == 404);

            // no key at all, on a route that reads one and one that doesn't need the body
            var none = await client.SendAsync(HttpMethod.Get, Ping);
            report.Assert(Who, $"GET {Ping} (no key)", none.Status == 401, $"{none.Status}");
            var noneValidate = await client.SendAsync(HttpMethod.Post, Validate, new { ticket = "anything" });
            report.Assert(Who, $"POST {Validate} (no key)", noneValidate.Status == 401, $"{noneValidate.Status}");

            if (keys.RevokedKey is null)
                Report.Note("No --revoked-key given, so a revoked key isn't checked.");
            else
            {
                var revoked = await client.SendAsync(HttpMethod.Get, Ping, serverKey: keys.RevokedKey);
                report.Assert(Who, $"GET {Ping} (revoked key)", revoked.Status == 401, $"{revoked.Status}");
            }

            if (keys.ExpiredKey is null)
                Report.Note("No --expired-key given, so an expired key isn't checked.");
            else
            {
                var expired = await client.SendAsync(HttpMethod.Get, Ping, serverKey: keys.ExpiredKey);
                report.Assert(Who, $"GET {Ping} (expired key)", expired.Status == 401, $"{expired.Status}");
            }

            if (keys.ServerKey is null)
            {
                Report.Note("No --server-key given, so the live key and XP end to end aren't checked.");
                return;
            }

            var works = await client.SendAsync(HttpMethod.Get, Ping, serverKey: keys.ServerKey);
            report.Assert(Who, $"GET {Ping} (the live key)", works.Status == 200, $"{works.Status}");
            if (works.Status != 200)
            {
                Report.Note("The live key was refused, so the rest is skipped. Is it the right key, and not revoked or expired?");
                return;
            }

            // a server key never opens an admin route
            foreach (var path in new[] { "/api/admin/me", "/api/admin/server-keys" })
            {
                var onAdmin = await client.SendAsync(HttpMethod.Get, path, serverKey: keys.ServerKey);
                report.Assert(Who, $"GET {path} (server key, no cookie)", Denied(onAdmin.Status), $"{onAdmin.Status}");
            }

            // validate a real ticket, then XP end to end, through the server SDK as a game server would
            await ServerSdkChecks.RunAsync(url, keys.ServerKey, report);
        }
    }
}
