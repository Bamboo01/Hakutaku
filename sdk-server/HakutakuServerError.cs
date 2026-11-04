#nullable enable

namespace Hakutaku
{
    // The server SDK's own public error types. The client SDK's HakutakuError is compiled in here as
    // internal (see HakutakuError.cs), so these are what a game server sees, and their names start
    // with HakutakuServer so a project that references both SDKs never has two types of one name.

    // What went wrong, chosen so the game's authenticator can make its one decision:
    //   InvalidSessionTicket       turn the player away
    //   IsHakutakuUnavailable      Hakutaku is down or slow: let them join offline
    //   anything else              a bug or a setup problem on the game server's side
    public enum HakutakuServerErrorCode
    {
        Unknown = 0,
        // The player's session ticket is unknown, revoked or expired. The player isn't who they say.
        InvalidSessionTicket,
        // This game server's own key was refused: unknown, revoked or expired. A setup problem, not
        // the player's fault, so don't turn players away for it.
        ServerKeyRejected,
        // No server key was given to the constructor, so nothing was sent.
        MissingServerKey,
        // The request was sent but no answer came in time.
        Timeout,
        // No response at all: Hakutaku is down, DNS, TLS, or a malformed server URL.
        ConnectionError,
        // 500
        InternalServerError,
        // 502, 503, 504
        ServiceUnavailable,
        // 400, or a call that was refused here before sending (an empty player id, say)
        InvalidParams,
        // 404: no such player. On the public domain, Caddy also answers 404 for every route it doesn't
        // proxy, and today that includes /api/server/*
        NotFound,
        // 429
        TooManyRequests,
        // A success status whose body wasn't the JSON the SDK expected.
        JsonParseError,
    }

    public class HakutakuServerError
    {
        // The route that was called, e.g. "/api/server/sessions/validate".
        public string ApiEndpoint = "";
        // 0 when no request was sent or no response came back.
        public int HttpCode;
        public string HttpStatus = "";
        public HakutakuServerErrorCode Error;
        public string ErrorMessage = "";

        // True when Hakutaku couldn't answer: a timeout, no connection, or any 5xx. A game lets the
        // player in offline on these, and turns them away only on InvalidSessionTicket.
        public bool IsHakutakuUnavailable =>
            Error == HakutakuServerErrorCode.Timeout
            || Error == HakutakuServerErrorCode.ConnectionError
            || HttpCode >= 500;

        public string GenerateErrorReport() =>
            HttpCode == 0
                ? ApiEndpoint + ": " + ErrorMessage + " (" + Error + ")"
                : ApiEndpoint + ": " + ErrorMessage + " (HTTP " + HttpCode + " " + HttpStatus + ")";
    }

    // Every call returns one of these instead of throwing, like the client SDK.
    // Exactly one of Result and Error is set.
    public class HakutakuServerResult<T> where T : class
    {
        public T? Result;
        public HakutakuServerError? Error;
    }
}
