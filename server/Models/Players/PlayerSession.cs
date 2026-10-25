/*!************************************************************************
\file   PlayerSession.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the PlayerSession model: one row per player login.
        It keeps the hash of the Bearer token (never the raw token) and its
        expiry and revocation times.

        Belongs to a Player. Created and read by PlayerSessionService. Table
        mapped in Db.cs.
**************************************************************************/
namespace server.Models
{
    public class PlayerSession
    {
        public long Id { get; set; }
        public Guid PlayerId { get; set; }
        // sha-256 of the session token, the raw token is never stored
        public byte[] TokenHash { get; set; } = [];
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
