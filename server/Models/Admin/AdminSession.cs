/*!************************************************************************
\file   AdminSession.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the AdminSession model: one row per admin login.
        It keeps the hash of the session token (never the raw token), the
        IP, and the idle and absolute expiry times.

        Table mapped in Db.cs. Belongs to an AdminUser. Read and written by
        AdminSessionService.
**************************************************************************/
using System.Net;

namespace server.Models
{
    public class AdminSession
    {
        public long Id { get; set; }
        public long AdminId { get; set; }
        // sha-256 of the session token, the raw token is never stored
        public byte[] TokenHash { get; set; } = [];
        public IPAddress? Ip { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        // pushed forward on each authenticated request but never past ExpiresAt. the session dies at
        // whichever limit (idle or absolute) comes first
        public DateTime IdleExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
