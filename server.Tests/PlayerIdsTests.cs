/*!************************************************************************
\file   PlayerIdsTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   16th November 2026
\brief  This file defines the unit tests for PlayerIds.CreatedAt: a UUIDv7
        gives back the time it was made, and any other uuid gives null
        rather than a made-up date.

        Pure tests, no database. Tests PlayerIds.cs.
**************************************************************************/
using server.Services;

namespace server.Tests
{
    public class PlayerIdsTests
    {
        [Fact]
        public void A_uuid_v7_gives_back_the_time_it_was_made()
        {
            var when = new DateTimeOffset(2026, 11, 16, 9, 30, 15, 123, TimeSpan.Zero);

            Assert.Equal(when, PlayerIds.CreatedAt(Guid.CreateVersion7(when)));
        }

        [Fact]
        public void A_known_id_decodes_to_its_millisecond()
        {
            // 0x019a8c3f6a00 ms after the epoch
            var id = Guid.Parse("019a8c3f-6a00-7000-8000-000000000000");

            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(0x019a8c3f6a00), PlayerIds.CreatedAt(id));
        }

        [Fact]
        public void The_lowest_id_at_a_time_reads_back_as_that_time()
        {
            var when = new DateTimeOffset(2026, 11, 16, 9, 30, 15, 123, TimeSpan.Zero);

            Assert.Equal(when, PlayerIds.CreatedAt(PlayerIds.LowestIdAt(when)));
        }

        [Fact]
        public void The_lowest_id_sorts_before_every_id_made_at_or_after_that_time_and_after_earlier_ones()
        {
            var when = new DateTimeOffset(2026, 11, 16, 9, 30, 15, 123, TimeSpan.Zero);
            // byte order is how postgres compares uuids
            static string Bytes(Guid g) => Convert.ToHexString(g.ToByteArray(bigEndian: true));
            var lowest = Bytes(PlayerIds.LowestIdAt(when));

            for (var i = 0; i < 100; i++)
            {
                Assert.True(string.CompareOrdinal(lowest, Bytes(Guid.CreateVersion7(when))) <= 0);
                Assert.True(string.CompareOrdinal(lowest, Bytes(Guid.CreateVersion7(when.AddMilliseconds(1)))) < 0);
                Assert.True(string.CompareOrdinal(lowest, Bytes(Guid.CreateVersion7(when.AddMilliseconds(-1)))) > 0);
            }
        }

        [Fact]
        public void Any_other_uuid_version_gives_null()
        {
            Assert.Null(PlayerIds.CreatedAt(Guid.NewGuid()));
            Assert.Null(PlayerIds.CreatedAt(Guid.Empty));
        }
    }
}
