/*!************************************************************************
\file   HakutakuHttp.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines HakutakuHttp, the only part of the SDK that
        touches the network or JSON. It sends every request through one
        shared HttpClient, puts the session token on each request, and turns
        failures into HakutakuError values instead of exceptions.

        Called by HakutakuClientInstanceAPI.cs, using the routes and shapes
        in ServerContract.cs.
**************************************************************************/
#nullable enable

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Hakutaku
{
    // The only file that touches the network or JSON. A Unity build that has to run on
    // WebGL, where HttpClient doesn't work, replaces this with a UnityWebRequest version
    // and nothing else in the SDK changes.
    //
    // The server SDK (sdk-server/) compiles this same file in, linked and not copied, so it
    // stays internal and takes only a URL, not this assembly's public settings types. A game's
    // Unity project references both DLLs, and a public type in both would be CS0433.
    static class HakutakuHttp
    {
        // One client for every player in the process, since HttpClient is meant to be
        // shared. That is why the token goes on each request instead of in
        // DefaultRequestHeaders, which concurrent players would overwrite, and why
        // cookies are off: one player's response must never set a cookie for another.
        static readonly HttpClient Client = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        public static string Serialize(object value) => JsonConvert.SerializeObject(value);

        // Sends one request and parses a success body as TWire. Never throws. A player's calls send
        // their session ticket as a Bearer token, a game server's calls send their server key in
        // X-Server-Key, and a timeout (if given) is for this request only.
        public static async Task<HakutakuResult<TWire>> SendAsync<TWire>(
            string serverUrl, string method, string path, object? body, string? sessionTicket,
            string? serverKey = null, TimeSpan? timeout = null)
            where TWire : class
        {
            var raw = await SendRawAsync(serverUrl, method, path, body == null ? null : Serialize(body), sessionTicket, serverKey, timeout)
                .ConfigureAwait(false);
            if (raw.Error != null) return new HakutakuResult<TWire> { Error = raw.Error };

            TWire? parsed = null;
            try
            {
                parsed = JsonConvert.DeserializeObject<TWire>(raw.Result!);
            }
            catch (JsonException)
            {
            }
            return parsed != null
                ? new HakutakuResult<TWire> { Result = parsed }
                : Fail<TWire>(path, HakutakuErrorCode.JsonParseError, "the response wasn't the expected JSON");
        }

        // Sends one request and returns the body as text. Never throws: an error status,
        // a timeout and a missing response all come back as a HakutakuError.
        public static async Task<HakutakuResult<string>> SendRawAsync(
            string serverUrl, string method, string path, string? jsonBody, string? sessionTicket,
            string? serverKey = null, TimeSpan? timeout = null)
        {
            int status;
            string reason, text;
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), serverUrl.TrimEnd('/') + path);
                if (!string.IsNullOrEmpty(sessionTicket))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionTicket);
                if (!string.IsNullOrEmpty(serverKey))
                    request.Headers.Add("X-Server-Key", serverKey);
                if (jsonBody != null)
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                // a timeout for this request alone: the shared client's own 30 seconds is the longest it can be
                using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
                using var response = await Client.SendAsync(request, cts == null ? CancellationToken.None : cts.Token).ConfigureAwait(false);
                status = (int)response.StatusCode;
                reason = response.ReasonPhrase ?? "";
                text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // the only things that cancel a request here are our timeout and the client's own, so this
                // is "no answer in time", which is not the same as "couldn't connect" or "was refused"
                return Fail<string>(path, HakutakuErrorCode.Timeout, "the server didn't answer in time");
            }
            catch (Exception e)
            {
                return Fail<string>(path, HakutakuErrorCode.ConnectionError, e.Message);
            }

            if (status >= 200 && status < 300) return new HakutakuResult<string> { Result = text };

            var wire = ParseError(text);
            return new HakutakuResult<string>
            {
                Error = new HakutakuError
                {
                    ApiEndpoint = path,
                    HttpCode = status,
                    HttpStatus = reason,
                    Error = CodeFor(status),
                    ErrorMessage = MessageFor(status, wire),
                    ServerCode = wire?.Code ?? "",
                },
            };
        }

        public static HakutakuResult<T> Fail<T>(string path, HakutakuErrorCode code, string message) where T : class =>
            new HakutakuResult<T>
            {
                Error = new HakutakuError { ApiEndpoint = path, Error = code, ErrorMessage = message },
            };

        static HakutakuErrorCode CodeFor(int status) => status switch
        {
            400 => HakutakuErrorCode.InvalidParams,
            401 => HakutakuErrorCode.NotAuthenticated,
            403 => HakutakuErrorCode.NotAuthorized,
            404 => HakutakuErrorCode.NotFound,
            409 => HakutakuErrorCode.Conflict,
            429 => HakutakuErrorCode.TooManyRequests,
            500 => HakutakuErrorCode.InternalServerError,
            502 or 503 or 504 => HakutakuErrorCode.ServiceUnavailable,
            _ => HakutakuErrorCode.Unknown,
        };

        // What an error body looks like: { "error": "...", "code": "..." }. Most have only the first, and a
        // few carry a code (the validate route's invalid_ticket). Nothing else in the SDK needs it.
        class ErrorWire
        {
            public string? Error { get; set; }
            public string? Code { get; set; }
        }

        static ErrorWire? ParseError(string body)
        {
            try
            {
                return JsonConvert.DeserializeObject<ErrorWire>(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // Most errors are { "error": "..." }, but not all: the rate limiter's 429 and
        // Caddy's 404 have empty bodies. An unexpected server error is a JSON 500 with a generic message.
        static string MessageFor(int status, ErrorWire? wire)
        {
            if (!string.IsNullOrEmpty(wire?.Error)) return wire!.Error!;

            return status switch
            {
                429 => "too many requests; the limits are per minute, so wait and try again",
                404 => "not found (on the public domain, Caddy answers 404 for every route that isn't public)",
                _ => "request failed",
            };
        }
    }
}
