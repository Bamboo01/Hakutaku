/*!************************************************************************
\file   PlayerIds.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   16th November 2026
\brief  This file defines PlayerIds.CreatedAt, which reads when a player
        was made from their id. Player ids are UUIDv7, which Npgsql's EF
        Core generator makes for Guid keys, and a UUIDv7 starts with the
        Unix time in milliseconds. So the admin UI can show "Created"
        without a column of its own.

        LowestIdAt goes the other way, so a query can find every player made
        after a moment by comparing ids.

        Used by ListPlayers.cs, GetPlayer.cs and StatsService.cs. Plain
        code with no database, so it is easy to test on its own.
**************************************************************************/
namespace server.Services
{
    internal static class PlayerIds
    {
        // when the id was made, or null if it isn't a version 7 uuid (an id made some other way, by hand
        // say), so the page shows "unknown" and never a made-up date
        internal static DateTimeOffset? CreatedAt(Guid id)
        {
            // big-endian, the order the uuid is written in
            var bytes = id.ToByteArray(bigEndian: true);
            if (bytes[6] >> 4 != 7) return null;
            long ms = 0;
            for (var i = 0; i < 6; i++) ms = (ms << 8) | bytes[i];
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }

        // the smallest UUIDv7 that could be made at `at`: its time, the version, and every random bit zero.
        // postgres compares uuids byte by byte, which for v7 is time order, so `"Id" >= LowestIdAt(t)` finds
        // every player made at or after t without a created_at column (StatsService uses it)
        internal static Guid LowestIdAt(DateTimeOffset at)
        {
            var ms = at.ToUnixTimeMilliseconds();
            var bytes = new byte[16];
            for (var i = 5; i >= 0; i--, ms >>= 8) bytes[i] = (byte)ms;
            bytes[6] = 0x70;
            // the rfc 9562 variant bits, so it's still a well-formed uuid
            bytes[8] = 0x80;
            return new Guid(bytes, bigEndian: true);
        }
    }
}
