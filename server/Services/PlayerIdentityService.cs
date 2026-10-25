using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Models;

namespace server.Services
{
    // looking up and naming a player's identities (device, email). the lookups use the db so they
    // need an instance, the two small helpers up top are pure and stay static
    public class PlayerIdentityService(Db db)
    {
        internal static string ProviderName(PlayerProvider provider) => provider.ToString().ToLowerInvariant();

        // what player_identities.subject holds for a device: sha-256 of the token as lowercase hex.
        // the AddDeviceTokens migration turned the old client-chosen device ids into the same form,
        // so those keep working as tokens
        internal static string HashDeviceToken(string token) =>
            Convert.ToHexStringLower(TokenHasher.Hash(token.Trim()));

        public Task<Player?> FindByIdentity(PlayerProvider provider, string subject) =>
            db.PlayerIdentities
                .Where(i => i.Provider == provider && i.Subject == subject)
                .Select(i => i.Player)
                .FirstOrDefaultAsync();

        // the player's email identity, or null if none is linked
        public Task<PlayerIdentity?> FindEmailIdentity(Guid playerId) =>
            db.PlayerIdentities.FirstOrDefaultAsync(i => i.PlayerId == playerId && i.Provider == PlayerProvider.Email);
    }
}
