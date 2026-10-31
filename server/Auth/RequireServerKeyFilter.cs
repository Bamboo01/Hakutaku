/*!************************************************************************
\file   RequireServerKeyFilter.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines RequireServerKeyFilter, the endpoint filter behind
        RequireServerKey. It reads the X-Server-Key header, hashes it and
        looks for a key with that hash that is neither revoked nor expired.
        A missing, unknown, revoked or expired key gets a 401, so with no
        keys in the table every server route is refused.

        Added to routes by RequireServerKey in ServerKeyGuards.cs. Reads
        ServerKey rows through Db.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Hosting;
using server.Models;

namespace server.Auth
{
    public sealed class RequireServerKeyFilter(Db db, TimeProvider time) : IEndpointFilter
    {
        internal const string MissingKeyError = "missing server key";
        internal const string InvalidKeyError = "unknown, revoked or expired server key";

        // how often a key's last_used_at is written, so a busy game server doesn't write on every call
        static readonly TimeSpan LastUsedEvery = TimeSpan.FromMinutes(1);

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var presented = context.HttpContext.Request.Headers[ServerKeyGuards.HeaderName].ToString().Trim();
            if (presented.Length == 0)
                return Results.Json(new { error = MissingKeyError }, statusCode: StatusCodes.Status401Unauthorized);

            // looked up by hash, which is indexed. same reasoning as session tokens: the key is 256 random
            // bits and only its hash is compared, so there's nothing to guess and no timing to leak
            var hash = TokenHasher.Hash(presented);
            var now = time.UtcNow();
            var id = await db.ServerKeys
                .Where(k => k.KeySha256 == hash && k.RevokedAt == null && (k.ExpiresAt == null || k.ExpiresAt > now))
                .Select(k => (long?)k.Id)
                .FirstOrDefaultAsync();
            if (id is null)
                return Results.Json(new { error = InvalidKeyError }, statusCode: StatusCodes.Status401Unauthorized);

            // a conditional update, so two calls at once can't both write and nothing is loaded for it
            var cutoff = now - LastUsedEvery;
            await db.ServerKeys
                .Where(k => k.Id == id && (k.LastUsedAt == null || k.LastUsedAt < cutoff))
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now));

            // so a handler or a log line can say which key made the call
            context.HttpContext.Items[ServerKeyGuards.ItemKey] = id.Value;
            return await next(context);
        }
    }
}
