#nullable enable

namespace Hakutaku
{
    // Chosen from the HTTP status. The server only sends { "error": "<text>" } with no
    // machine-readable code, so this is as specific as a client can get without matching
    // on message text, which would break the first time a message is reworded.
    public enum HakutakuErrorCode
    {
        Unknown = 0,
        // Nothing was sent: the call needs a session and the client isn't logged in.
        NotLoggedIn,
        // No response at all: server down, DNS, TLS, timeout, or a malformed ServerUrl.
        ConnectionError,
        // A success status whose body wasn't the JSON the SDK expected.
        JsonParseError,
        // 400
        InvalidParams,
        // 401: no session, or the token is unknown, expired or revoked.
        NotAuthenticated,
        // 403
        NotAuthorized,
        // 404. On the public domain this is also Caddy's answer for every route that
        // isn't public, so it often means "not reachable from here".
        NotFound,
        // 409
        Conflict,
        // 429: a per-IP rate limit, or the 60-second cooldown between emailed codes.
        TooManyRequests,
        // 500
        InternalServerError,
        // 502, 503, 504
        ServiceUnavailable,
    }

    public class HakutakuError
    {
        // The route that was called, e.g. "/api/players/register".
        public string ApiEndpoint = "";
        // 0 when no request was sent or no response came back.
        public int HttpCode;
        public string HttpStatus = "";
        public HakutakuErrorCode Error;
        public string ErrorMessage = "";

        public string GenerateErrorReport() =>
            HttpCode == 0
                ? ApiEndpoint + ": " + ErrorMessage + " (" + Error + ")"
                : ApiEndpoint + ": " + ErrorMessage + " (HTTP " + HttpCode + " " + HttpStatus + ")";
    }

    // Every call returns one of these instead of throwing, like PlayFab's C# SDK.
    // Exactly one of Result and Error is set.
    public class HakutakuResult<T> where T : class
    {
        public T? Result;
        public HakutakuError? Error;
    }
}
