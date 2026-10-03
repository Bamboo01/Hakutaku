#nullable enable

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Hakutaku
{
    // The only file that touches the network or JSON. A Unity build that has to run on
    // WebGL, where HttpClient doesn't work, replaces this with a UnityWebRequest version
    // and nothing else in the SDK changes.
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

        // Sends one request and parses a success body as TWire. Never throws.
        public static async Task<HakutakuResult<TWire>> SendAsync<TWire>(
            HakutakuApiSettings settings, string method, string path, object? body, string? sessionTicket)
            where TWire : class
        {
            var raw = await SendRawAsync(settings, method, path, body == null ? null : Serialize(body), sessionTicket)
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

        // Sends one request and returns the body as text. Never throws: an error status
        // and a missing response both come back as a HakutakuError.
        public static async Task<HakutakuResult<string>> SendRawAsync(
            HakutakuApiSettings settings, string method, string path, string? jsonBody, string? sessionTicket)
        {
            int status;
            string reason, text;
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), settings.ServerUrl.TrimEnd('/') + path);
                if (!string.IsNullOrEmpty(sessionTicket))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionTicket);
                if (jsonBody != null)
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                using var response = await Client.SendAsync(request).ConfigureAwait(false);
                status = (int)response.StatusCode;
                reason = response.ReasonPhrase ?? "";
                text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return Fail<string>(path, HakutakuErrorCode.ConnectionError, e.Message);
            }

            if (status >= 200 && status < 300) return new HakutakuResult<string> { Result = text };

            return new HakutakuResult<string>
            {
                Error = new HakutakuError
                {
                    ApiEndpoint = path,
                    HttpCode = status,
                    HttpStatus = reason,
                    Error = CodeFor(status),
                    ErrorMessage = MessageFor(status, text),
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

        // Most errors are { "error": "..." }, but not all: the rate limiter's 429 and
        // Caddy's 404 have empty bodies, and an unhandled server exception is a bare 500.
        static string MessageFor(int status, string body)
        {
            try
            {
                var parsed = JsonConvert.DeserializeObject<ErrorWire>(body);
                if (!string.IsNullOrEmpty(parsed?.Error)) return parsed!.Error!;
            }
            catch (JsonException)
            {
            }

            return status switch
            {
                429 => "too many requests; the limits are per minute, so wait and try again",
                404 => "not found (on the public domain, Caddy answers 404 for every route that isn't public)",
                _ => "request failed",
            };
        }
    }
}
