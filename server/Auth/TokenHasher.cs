/*!************************************************************************
\file   TokenHasher.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines TokenHasher, the SHA-256 helper used for session
        tokens, device tokens and emailed codes. Only the hash is ever
        stored, so a copy of the database cannot be used to log in as
        anyone.

        Used by AdminSessionService, PlayerSessionService,
        PlayerIdentityService and EmailCodeService.
**************************************************************************/
using System.Security.Cryptography;
using System.Text;

namespace server.Auth
{
    // sha-256 of a token or code. session tokens, device tokens and emailed codes are all
    // stored like this, so a copy of the db can't be used to log in as anyone
    public static class TokenHasher
    {
        public static byte[] Hash(string token) =>
            SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
