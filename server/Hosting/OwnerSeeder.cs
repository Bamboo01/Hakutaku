/*!************************************************************************
\file   OwnerSeeder.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines OwnerSeeder, which creates the owner account
        (username admin) on first start when there are no admins yet. The
        password comes from HAKUTAKU_ADMIN_PASSWORD, or is generated and
        logged once.

        Run from Program.cs right after the migrations. Uses PasswordHasher.
**************************************************************************/
using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Constants;
using server.Models;

namespace server.Hosting
{
    public class OwnerSeeder(Db db, IConfiguration config, ILogger<OwnerSeeder> logger)
    {
        const string OwnerUsername = "admin";

        // makes the owner account on first start, if there are no admins yet. the password comes from
        // HAKUTAKU_ADMIN_PASSWORD, or gets generated and logged once if that isn't set
        public async Task Seed()
        {
            if (await db.AdminUsers.AnyAsync()) return;

            var password = config[ConfigKeys.AdminPassword];
            var generated = string.IsNullOrWhiteSpace(password);
            if (generated) password = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(18));

            db.AdminUsers.Add(new AdminUser
            {
                Username = OwnerUsername,
                PwHash = PasswordHasher.Hash(password!),
                Role = AdminRole.Owner,
            });
            await db.SaveChangesAsync();

            if (generated)
                logger.LogWarning("Created the owner account '{Username}' with the generated password {Password}. It is shown only once.", OwnerUsername, password);
            else
                logger.LogInformation("Created the owner account '{Username}'.", OwnerUsername);
        }
    }
}
