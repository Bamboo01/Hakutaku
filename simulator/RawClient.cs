/*!************************************************************************
\file   RawClient.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines RawClient, a tiny HTTP client with its own cookie
        jar for the parts of the API that the SDK does not cover: the admin
        routes, which use a session cookie, and the server routes, which
        use an X-Server-Key header. It hands back the status code and the
        body and never throws on an HTTP error.

        Used by ServerKeyChecks.cs.
**************************************************************************/
using System.Net;
using System.Net.Http.Json;

namespace simulator
{
    sealed class RawClient : IDisposable
    {
        readonly HttpClient http;

        public RawClient(string url)
        {
            // its own cookie jar, so the owner's session stays on the owner's client
            http = new HttpClient(new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() })
            {
                BaseAddress = new Uri(url),
                Timeout = TimeSpan.FromSeconds(15),
            };
        }

        public async Task<(int Status, string Body)> SendAsync(HttpMethod method, string path, object? body = null, string? serverKey = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (serverKey is not null) request.Headers.Add("X-Server-Key", serverKey);
            if (body is not null) request.Content = JsonContent.Create(body);
            try
            {
                using var response = await http.SendAsync(request);
                return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
            }
            catch (HttpRequestException e)
            {
                return (0, e.Message);
            }
        }

        public void Dispose() => http.Dispose();
    }
}
