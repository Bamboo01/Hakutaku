/*!************************************************************************
\file   EditPlayerData.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   16th November 2026
\brief  This file defines the POST /api/players/{id}/data route, which lets
        an admin set and remove keys in one tier of a player's data from
        the admin UI. It is held to exactly the same rules and limits as a
        game server's write, and every edit is logged with the admin's
        username, since it changes a player's progress by hand.

        Mapped in PlayerRoutes.cs, so it needs an admin session. Uses
        PlayerDataService and PlayerDataRules, like the game server's
        UpdatePlayerData.cs.
**************************************************************************/
using server.Auth;
using server.Constants;
using server.Services;

namespace server.Endpoints.Players
{
    public record AdminEditPlayerDataRequest(Dictionary<string, string?>? Data, List<string>? KeysToRemove);

    public static class EditPlayerData
    {
        // post /api/players/{id}/data?tier=ReadOnly with { data: { k: v }, keysToRemove: [k] }: answers
        // { updated: n }. the same body, checks and answer as the game server's route, an admin can use any tier
        public static async Task<IResult> Handle(
            PlayerDataService data, ILoggerFactory logs, HttpContext http, Guid id, string? tier, AdminEditPlayerDataRequest request)
        {
            if (!PlayerDataRules.TryParseTier(tier, out var parsedTier))
                return Results.BadRequest(new { error = ErrorMessages.PlayerDataTierInvalid });

            if (!PlayerDataRules.TryReadChanges(request.Data, request.KeysToRemove, out var set, out var remove, out var error))
                return Results.BadRequest(new { error });

            if (!await data.PlayerExists(id))
                return Results.NotFound(new { error = ErrorMessages.NoSuchPlayer });

            var updated = await data.Write(id, parsedTier, set, remove);
            if (updated is null)
                return Results.BadRequest(new { error = ErrorMessages.PlayerDataTooManyKeys });

            // keys only, never values: a value can be 16 kb of anything. the keys were checked above, so
            // they're safe to put in a log line
            logs.CreateLogger(LogCategories.AdminActions).LogInformation(
                "Admin {Admin} edited {Tier} data of player {PlayerId}: set [{Set}], removed [{Removed}]",
                http.Items[AdminGuards.ItemKey], parsedTier, id, string.Join(", ", set.Keys), string.Join(", ", remove));

            return Results.Ok(new { updated });
        }
    }
}
