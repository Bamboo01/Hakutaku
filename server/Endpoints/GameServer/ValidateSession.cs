/*!************************************************************************
\file   ValidateSession.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the POST /api/server/sessions/validate route. A
        game server sends the session ticket a joining player gave it (the
        token the player got when they signed in), and gets back who the
        player is. An unknown, revoked or expired ticket is a 401 with a
        code that says it is the ticket that is bad, so the game can tell
        that apart from a bad server key. It writes nothing.

        Mapped in GameServerRoutes.cs, so it needs a server key. Uses
        PlayerSessionService, the same lookup the player routes use.
**************************************************************************/
using server.Constants;
using server.Services;

namespace server.Endpoints.GameServer
{
    public record ValidateSessionRequest(string? Ticket);

    public static class ValidateSession
    {
        // in the body of a 401 from this route, so a game server can tell "this player's ticket is bad" from
        // "my server key was refused", which is also a 401 but comes from the key filter and has no code
        public const string InvalidTicketCode = "invalid_ticket";

        // post /api/server/sessions/validate with { ticket }: answers { playerId, displayName }.
        // the lookup is the one the player routes use (hash the ticket, find a session that isn't revoked
        // or expired), and nothing is written, not even a last-used time
        public static async Task<IResult> Handle(PlayerSessionService sessions, ValidateSessionRequest request)
        {
            // an empty or oversized ticket is a bad ticket too, so it's a 401 and never a 400. the game rejects a
            // player on a bad ticket, and a player must not be able to pick an answer that lets them in
            var ticket = request.Ticket?.Trim();
            if (string.IsNullOrEmpty(ticket) || ticket.Length > Limits.SessionTicketMaxLength)
                return InvalidTicket();

            var player = await sessions.FindByToken(ticket);
            if (player is null)
                return InvalidTicket();

            return Results.Ok(new { playerId = player.Id, displayName = player.DisplayName });
        }

        static IResult InvalidTicket() =>
            Results.Json(new { error = ErrorMessages.InvalidSessionTicket, code = InvalidTicketCode }, statusCode: StatusCodes.Status401Unauthorized);
    }
}
