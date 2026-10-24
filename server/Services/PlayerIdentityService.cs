using Microsoft.EntityFrameworkCore;
using Npgsql;
using server.Models;

namespace server.Services
{
    // Looking up and naming a player's identities (device, email).
    internal static class PlayerIdentityService
    {
        internal static string ProviderName(PlayerProvider provider) => provider.ToString().ToLowerInvariant();

        // Postgres code 23505: the insert broke a unique index, e.g. someone took the email first.
        internal static bool IsUniqueViolation(DbUpdateException e) =>
            e.InnerException is PostgresException { SqlState: "23505" };

        // What player_identities.subject holds for a device: the SHA-256 of the token as
        // lowercase hex. The AddDeviceTokens migration put the old client-chosen device IDs
        // into the same form, so those keep working as tokens.
        internal static string HashDeviceToken(string token) =>
            Convert.ToHexStringLower(AdminAuth.HashToken(token.Trim()));

        internal static Task<Player?> FindByIdentity(Db db, PlayerProvider provider, string subject) =>
            db.PlayerIdentities
                .Where(i => i.Provider == provider && i.Subject == subject)
                .Select(i => i.Player)
                .FirstOrDefaultAsync();

        // The email identity of this player, or null if none is linked.
        internal static Task<PlayerIdentity?> FindEmailIdentity(Db db, Guid playerId) =>
            db.PlayerIdentities.FirstOrDefaultAsync(i => i.PlayerId == playerId && i.Provider == PlayerProvider.Email);
    }
}
