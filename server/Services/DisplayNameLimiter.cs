/*!************************************************************************
\file   DisplayNameLimiter.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines DisplayNameLimiter, which caps how often one player
        can change their display name. The count is per player and not per
        IP, so players behind one shared IP do not use up each other's
        changes. It is a singleton because the counts have to outlive a
        request.

        Used by SetDisplayName.cs. The numbers are in RateLimits.cs.
**************************************************************************/
using Microsoft.Extensions.Caching.Memory;
using server.Constants;

namespace server.Services
{
    // counts display name attempts per player in a fixed window. the route needs a token anyway, so
    // the count is only made once the player is known, which is why this isn't one of the per-ip
    // policies. kept in memory, so a restart forgets the counts, which is fine for abuse control
    public class DisplayNameLimiter(IMemoryCache cache)
    {
        sealed class Counter
        {
            public int Count;
        }

        // true if this player may try now. every call counts, refused names included, so a script
        // can't keep guessing. once the window runs out the count starts again
        public bool TryTake(Guid playerId)
        {
            var counter = cache.GetOrCreate($"display-name:{playerId}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = RateLimits.DisplayNameWindow;
                return new Counter();
            })!;
            return Interlocked.Increment(ref counter.Count) <= RateLimits.DisplayNameChanges;
        }
    }
}
