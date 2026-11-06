/*!************************************************************************
\file   PlayerDataService.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines PlayerDataService, which reads and writes a
        player's data. A write sets values and removes keys in one
        transaction, and it never adds to a value, so doing the same write
        twice changes nothing more. It also keeps a player to the key limit
        for a tier, even when two writes arrive at once.

        Used by GetPlayerData.cs and UpdatePlayerData.cs. Works on
        PlayerDataEntry rows through Db.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Hosting;
using server.Models;

namespace server.Services
{
    public class PlayerDataService(Db db, TimeProvider time)
    {
        public Task<bool> PlayerExists(Guid playerId) => db.Players.AnyAsync(p => p.Id == playerId);

        // the keys asked for that exist, or every key in the tier if none were asked for. a missing key is
        // simply left out
        public async Task<Dictionary<string, string>> Read(Guid playerId, PlayerDataTier tier, List<string>? keys)
        {
            var entries = db.PlayerData.AsNoTracking().Where(d => d.PlayerId == playerId && d.Tier == tier);
            if (keys is not null) entries = entries.Where(d => keys.Contains(d.Key));
            return await entries.OrderBy(d => d.Key).ToDictionaryAsync(d => d.Key, d => d.Value);
        }

        // sets every key in set and removes every key in remove, all or nothing. returns how many keys it set
        // plus how many it actually removed, or null (changing nothing) if the player would end up with more
        // than the limit in this tier
        public async Task<int?> Write(Guid playerId, PlayerDataTier tier, Dictionary<string, string> set, List<string> remove)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            // makes writes to one player's tier take turns. otherwise two at once could each count 99 keys,
            // each add one, and leave 101
            var lockKey = $"player-data:{playerId}:{(short)tier}";
            await db.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");

            var keysAfter = (await db.PlayerData
                .Where(d => d.PlayerId == playerId && d.Tier == tier)
                .Select(d => d.Key)
                .ToListAsync()).ToHashSet();
            keysAfter.ExceptWith(remove);
            keysAfter.UnionWith(set.Keys);
            if (keysAfter.Count > Limits.PlayerDataMaxKeysPerTier)
                return null;

            var removed = await db.PlayerData
                .Where(d => d.PlayerId == playerId && d.Tier == tier && remove.Contains(d.Key))
                .ExecuteDeleteAsync();

            // one upsert per key: a new key is inserted, an existing one has its value replaced
            var now = time.UtcNow();
            var tierValue = (short)tier;
            foreach (var (key, value) in set)
            {
                await db.Database.ExecuteSqlAsync($"""
                    insert into player_data (player_id, tier, key, value, updated_at)
                    values ({playerId}, {tierValue}, {key}, {value}, {now})
                    on conflict (player_id, tier, key) do update
                    set value = excluded.value, updated_at = excluded.updated_at
                    """);
            }

            await transaction.CommitAsync();
            return set.Count + removed;
        }
    }
}
