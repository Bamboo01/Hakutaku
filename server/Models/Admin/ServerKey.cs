/*!************************************************************************
\file   ServerKey.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the ServerKey model: a key that a game server sends
        in the X-Server-Key header to use the /api/server routes. Only the
        SHA-256 of the key is stored, never the key itself, and a key can
        expire or be revoked.

        Table mapped in Db.cs. Made, listed and revoked by the owner-only
        routes in Endpoints/Admin, and checked by RequireServerKeyFilter.
**************************************************************************/
namespace server.Models
{
    public class ServerKey
    {
        public long Id { get; set; }
        // what the owner calls it, e.g. "homelab". only a label, not unique
        public string Name { get; set; } = "";
        // sha-256 of the key, the raw key is only shown once when it's made
        public byte[] KeySha256 { get; set; } = [];
        public DateTime CreatedAt { get; set; }
        // null means it never expires
        public DateTime? ExpiresAt { get; set; }
        // set when the owner revokes it, one-way like deactivating an admin
        public DateTime? RevokedAt { get; set; }
        // updated at most once a minute, see RequireServerKeyFilter
        public DateTime? LastUsedAt { get; set; }
    }
}
