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

        Used by ListPlayers.cs and GetPlayer.cs. A plain check with no
        database, so it is easy to test on its own.
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
    }
}
