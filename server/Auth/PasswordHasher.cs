/*!************************************************************************
\file   PasswordHasher.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines PasswordHasher, Argon2id password hashing with the
        OWASP minimum settings, shared by admin and player passwords. It
        also keeps a dummy hash to check against when an account does not
        exist, so a miss takes as long as a wrong password.

        Used by the login, create-admin, link-email and password-reset
        handlers and by OwnerSeeder.
**************************************************************************/
using Isopoh.Cryptography.Argon2;

namespace server.Auth
{
    // argon2id password hashing, used for both admins and players
    public static class PasswordHasher
    {
        // owasp minimum settings: 19 mib of memory, 2 iterations, 1 lane
        const int TimeCost = 2;
        const int MemoryCostKib = 19456;
        const int Lanes = 1;
        const int HashLength = 32;

        // checked when the account doesn't exist, so a miss takes as long as a wrong password
        // and the response can't tell you which accounts exist
        public static readonly string DummyHash = Hash("dummy-password-for-timing");

        // one string with the salt and settings baked in, so there's no separate salt to store
        public static string Hash(string password) =>
            Argon2.Hash(password, TimeCost, MemoryCostKib, Lanes, Argon2Type.HybridAddressing, HashLength);

        public static bool Verify(string encodedHash, string password) =>
            Argon2.Verify(encodedHash, password);
    }
}
