/*!************************************************************************
\file   StatsService.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   16th November 2026
\brief  This file defines StatsService, which counts what the admin UI's
        Overview page shows, like PlayFab's Title Overview: unique users,
        new users and logins, for the last 24 hours and 30 days with the
        period before each to compare against, and one bar per day.

        A "day" is a rolling 24 hours counted back from now, not a calendar
        day, so the numbers mean the same thing in every timezone. A login
        is a session being issued, which register does too. New users are
        read from the player ids (UUIDv7), see PlayerIds.

        Used by GetStats.cs. Reads player_sessions and Players through Db.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Services
{
    // one figure for the last 24 hours and 30 days, and the period just before each
    public record StatWindow(long Last24h, long Previous24h, long Last30Days, long Previous30Days);

    // one rolling day, oldest first in Stats.Days. EndsAt is the end of the window
    public record StatDay(DateTimeOffset EndsAt, long UniqueUsers, long NewUsers, long Logins);

    public record Stats(
        DateTimeOffset GeneratedAt, long TotalPlayers,
        StatWindow UniqueUsers, StatWindow NewUsers, StatWindow Logins, List<StatDay> Days);

    public class StatsService(Db db, TimeProvider time)
    {
        public const int ChartDays = 30;
        // two of the 30-day periods, so the last one has one to compare against
        const int LookbackDays = ChartDays * 2;
        static readonly TimeSpan Day = TimeSpan.FromDays(1);

        // what SqlQuery reads a row into: the column aliases below match these names
        sealed class SessionBucket
        {
            public int Bucket { get; set; }
            public long Logins { get; set; }
            public long Users { get; set; }
        }

        sealed class DistinctUsers
        {
            public long Last24h { get; set; }
            public long Previous24h { get; set; }
            public long Last30Days { get; set; }
            public long Previous30Days { get; set; }
        }

        public async Task<Stats> Read()
        {
            var now = time.GetUtcNow();
            var nowUtc = now.UtcDateTime;
            var from = nowUtc - LookbackDays * Day;

            // logins and unique users per day. bucket 0 is the last 24 hours, 1 the day before, and so on.
            // issued_at comes from the database's clock and `now` from the app's, which can be a little apart
            // (a session made a moment ago can look a few ms in the future), so there's no upper bound and
            // anything ahead of now counts in the latest day
            var buckets = await db.Database.SqlQuery<SessionBucket>($"""
                select greatest(0, floor(extract(epoch from ({nowUtc} - issued_at)) / 86400))::int as "Bucket",
                       count(*) as "Logins",
                       count(distinct player_id) as "Users"
                from player_sessions
                where issued_at > {from}
                group by 1
                """).ToListAsync();

            // distinct players across a whole period can't be summed from the days (one player on two
            // days is still one), so they're counted on their own
            var d1 = nowUtc - Day;
            var d2 = nowUtc - 2 * Day;
            var d30 = nowUtc - ChartDays * Day;
            var distinct = await db.Database.SqlQuery<DistinctUsers>($"""
                select count(distinct player_id) filter (where issued_at > {d1}) as "Last24h",
                       count(distinct player_id) filter (where issued_at > {d2} and issued_at <= {d1}) as "Previous24h",
                       count(distinct player_id) filter (where issued_at > {d30}) as "Last30Days",
                       count(distinct player_id) filter (where issued_at <= {d30}) as "Previous30Days"
                from player_sessions
                where issued_at > {from}
                """).SingleAsync();

            // every player made in the lookback, by id. the ids carry their own creation time
            var lowest = PlayerIds.LowestIdAt(now - LookbackDays * Day);
            var newIds = await db.Database
                .SqlQuery<Guid>($"""select "Id" as "Value" from "Players" where "Id" >= {lowest}""")
                .ToListAsync();
            var newPerBucket = new long[LookbackDays];
            foreach (var id in newIds)
            {
                if (PlayerIds.CreatedAt(id) is not { } created) continue;
                // same as the sessions: a hair ahead of now still counts as the latest day
                var bucket = Math.Max(0, (int)Math.Floor((now - created) / Day));
                if (bucket < LookbackDays) newPerBucket[bucket]++;
            }

            var logins = new long[LookbackDays];
            var users = new long[LookbackDays];
            foreach (var b in buckets.Where(b => b.Bucket is >= 0 and < LookbackDays))
            {
                logins[b.Bucket] = b.Logins;
                users[b.Bucket] = b.Users;
            }

            var days = Enumerable.Range(0, ChartDays).Reverse()
                .Select(i => new StatDay(now - i * Day, users[i], newPerBucket[i], logins[i]))
                .ToList();

            return new Stats(
                now,
                await db.Players.LongCountAsync(),
                new StatWindow(distinct.Last24h, distinct.Previous24h, distinct.Last30Days, distinct.Previous30Days),
                Window(newPerBucket),
                Window(logins),
                days);
        }

        // sums that do add up across days: new users and logins
        static StatWindow Window(long[] perBucket) => new(
            perBucket[0],
            perBucket[1],
            perBucket.Take(ChartDays).Sum(),
            perBucket.Skip(ChartDays).Sum());
    }
}
