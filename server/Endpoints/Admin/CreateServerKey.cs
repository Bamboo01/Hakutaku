/*!************************************************************************
\file   CreateServerKey.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the POST /api/admin/server-keys route. Owner only.
        It makes a server key for a game server and returns it once. Only
        the SHA-256 of the key is stored, so a lost key cannot be looked up
        and has to be replaced. The request is a small record with a name
        and an optional expiry.

        Mapped in AdminRoutes.cs. Uses AdminSessionService.RequireOwner and
        TokenHasher. The key is checked by RequireServerKeyFilter.
**************************************************************************/
using System.Buffers.Text;
using System.Security.Cryptography;
using server.Auth;
using server.Constants;
using server.Hosting;
using server.Models;

namespace server.Endpoints.Admin
{
    public record CreateServerKeyRequest(string? Name, DateTimeOffset? ExpiresAt);

    public static class CreateServerKey
    {
        // post /api/admin/server-keys: owner only. the key comes back in this response and nowhere else,
        // the table only keeps its hash
        public static async Task<IResult> Handle(Db db, AdminSessionService sessions, TimeProvider time, HttpContext http, CreateServerKeyRequest request)
        {
            var (_, error) = await sessions.RequireOwner(http);
            if (error is not null) return error;

            var name = request.Name?.Trim();
            var now = time.UtcNow();
            var expiresAt = request.ExpiresAt?.UtcDateTime;
            if (string.IsNullOrEmpty(name) || name.Length > Limits.ServerKeyNameMaxLength || name.Any(char.IsControl)
                || (expiresAt is not null && expiresAt <= now))
                return Results.BadRequest(new { error = ErrorMessages.CreateServerKeyInvalid });

            // 32 random bytes, so 256 bits, as url-safe base64 so it can sit in a header or a config file
            var key = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var row = new ServerKey { Name = name, KeySha256 = TokenHasher.Hash(key), CreatedAt = now, ExpiresAt = expiresAt };
            db.ServerKeys.Add(row);
            await db.SaveChangesAsync();

            return Results.Ok(new { id = row.Id, name = row.Name, key });
        }
    }
}
