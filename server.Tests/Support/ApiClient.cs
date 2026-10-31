/*!************************************************************************
\file   ApiClient.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines ApiClient, a small helper the integration tests
        use to call the server. Each call comes from a fresh made-up IP
        (through X-Forwarded-For, like behind Caddy), so the per-IP rate
        limits do not get in the way unless a test sets an IP on purpose.

        Used by the integration test classes. Talks to the server started
        by HakutakuFactory.
**************************************************************************/
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace server.Tests.Support
{
    public record Reply(HttpStatusCode Status, string Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body).RootElement;
        public string? Error => Json.TryGetProperty("error", out var e) ? e.GetString() : null;
    }

    public record Guest(Guid Id, string Token, string DeviceToken);

    public sealed class ApiClient(HttpClient http)
    {
        public const string Password = "test-pass-12345";

        static int counter;

        // a different address every time, so no two calls share a rate limit
        static string NextIp()
        {
            var n = Interlocked.Increment(ref counter);
            return $"10.{(n >> 16) & 255}.{(n >> 8) & 255}.{n & 255}";
        }

        // pass an ip to send a call from that address on purpose, e.g. to share a rate limit
        public async Task<Reply> Post(string path, object? body = null, string? token = null, string? ip = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("X-Forwarded-For", ip ?? NextIp());
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(body ?? new { });
            using var response = await http.SendAsync(request);
            return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async Task<Guest> RegisterGuest(string? ip = null)
        {
            var reply = await Post("/api/players/register", new { hardwareId = "test-" + Guid.NewGuid().ToString("N") }, ip: ip);
            Assert.Equal(HttpStatusCode.OK, reply.Status);
            var json = reply.Json;
            return new Guest(json.GetProperty("id").GetGuid(), json.GetProperty("token").GetString()!, json.GetProperty("deviceToken").GetString()!);
        }

        public Task<Reply> LinkEmail(string token, string email) =>
            Post("/api/players/link/email", new { email, password = Password }, token);

        public Task<Reply> LoginWithEmail(string email, string password = Password) =>
            Post("/api/players/login/email", new { email, password });

        public Task<Reply> SetDisplayName(string token, string name, string? ip = null) =>
            Post("/api/players/display-name", new { displayName = name }, token, ip);
    }
}
