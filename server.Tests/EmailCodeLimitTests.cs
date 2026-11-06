/*!************************************************************************
\file   EmailCodeLimitTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the integration tests for the daily cap on mailed
        codes. A player can be sent a few codes per kind a day: resend says
        which limit was hit, codes older than a day do not count, and
        forgot password stays silent past the cap so it cannot give an
        account away.

        Runs against the real server through HakutakuFactory. Tests
        EmailCodeService.CreateCode through the resend and forgot routes.
        To get past the one-a-minute gap without waiting, a test moves the
        codes it made back in time in the database.
**************************************************************************/
using System.Net;
using server.Constants;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class EmailCodeLimitTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>
    {
        readonly ApiClient api = app.Api();

        // a player with an unverified email, so resend works, with the one code that linking made
        async Task<Guest> GuestWithLinkedEmail()
        {
            var guest = await api.RegisterGuest();
            await api.LinkEmail(guest.Token, $"u{Guid.NewGuid():N}@example.com");
            return guest;
        }

        // purpose 0 is a verification code, 1 is a password reset code
        Task AddCodes(Guid player, int purpose, int count, int minutesAgo) => app.Run(
            $@"insert into player_email_codes (player_id, purpose, code_hash, expires_at, attempts, used_at, created_at)
               select '{player}', {purpose}, decode('00', 'hex'), now() + interval '15 minutes', 0, now(), now() - interval '{minutesAgo} minutes'
               from generate_series(1, {count})");

        Task MoveCodesBack(Guid player, int purpose, int minutesAgo) => app.Run(
            $"update player_email_codes set created_at = now() - interval '{minutesAgo} minutes' where player_id = '{player}' and purpose = {purpose}");

        Task<long> CodeCount(Guid player, int purpose) => app.Scalar<long>(
            $"select count(*) from player_email_codes where player_id = '{player}' and purpose = {purpose}");

        [Fact]
        public async Task Resend_is_refused_right_after_a_code_and_says_to_wait()
        {
            var guest = await GuestWithLinkedEmail();

            var reply = await api.Post("/api/players/email/resend", token: guest.Token);

            Assert.Equal(HttpStatusCode.TooManyRequests, reply.Status);
            Assert.Contains("wait", reply.Error);
        }

        [Fact]
        public async Task Resend_allows_the_last_code_of_the_day_then_refuses_and_says_why()
        {
            var guest = await GuestWithLinkedEmail();
            // one code exists from linking. add enough to be one short of the daily cap
            await AddCodes(guest.Id, 0, RateLimits.CodesPerDay - 2, 120);
            await MoveCodesBack(guest.Id, 0, 120);
            Assert.Equal(RateLimits.CodesPerDay - 1, await CodeCount(guest.Id, 0));

            var last = await api.Post("/api/players/email/resend", token: guest.Token);
            Assert.Equal(HttpStatusCode.OK, last.Status);
            Assert.Equal(RateLimits.CodesPerDay, await CodeCount(guest.Id, 0));

            await MoveCodesBack(guest.Id, 0, 5);
            var over = await api.Post("/api/players/email/resend", token: guest.Token);

            Assert.Equal(HttpStatusCode.TooManyRequests, over.Status);
            Assert.Equal(ErrorMessages.TooManyCodesToday, over.Error);
            Assert.Equal(RateLimits.CodesPerDay, await CodeCount(guest.Id, 0));
        }

        [Fact]
        public async Task Codes_older_than_a_day_do_not_count()
        {
            var guest = await GuestWithLinkedEmail();
            await AddCodes(guest.Id, 0, RateLimits.CodesPerDay * 2, 25 * 60);
            await MoveCodesBack(guest.Id, 0, 25 * 60);

            var reply = await api.Post("/api/players/email/resend", token: guest.Token);

            Assert.Equal(HttpStatusCode.OK, reply.Status);
        }

        [Fact]
        public async Task Reset_codes_and_verification_codes_are_counted_separately()
        {
            var guest = await GuestWithLinkedEmail();
            await AddCodes(guest.Id, 1, RateLimits.CodesPerDay, 120);
            // linking made a verification code a moment ago, so move it back past the one-a-minute gap
            await MoveCodesBack(guest.Id, 0, 5);

            var reply = await api.Post("/api/players/email/resend", token: guest.Token);

            Assert.Equal(HttpStatusCode.OK, reply.Status);
        }

        [Fact]
        public async Task Forgot_password_stays_silent_past_the_cap()
        {
            var email = $"u{Guid.NewGuid():N}@example.com";
            var guest = await api.RegisterGuest();
            await api.LinkEmail(guest.Token, email);
            await app.Run($"update player_identities set verified_at = now() where player_id = '{guest.Id}' and provider = 1");

            // the first forgot makes a code and mails it
            var first = await api.Post("/api/players/password/forgot", new { email });
            Assert.Equal(HttpStatusCode.OK, first.Status);
            Assert.True(await CapturingEmailSender.WaitFor(() => app.Mail.LastCode(email, "Reset your password") is not null));

            // fill the day, with every code old enough that only the daily cap can refuse
            await AddCodes(guest.Id, 1, RateLimits.CodesPerDay - 1, 120);
            await MoveCodesBack(guest.Id, 1, 5);
            Assert.Equal(RateLimits.CodesPerDay, await CodeCount(guest.Id, 1));
            var mailsBefore = app.Mail.To(email).Count;

            var over = await api.Post("/api/players/password/forgot", new { email });
            var unknown = await api.Post("/api/players/password/forgot", new { email = $"nobody{Guid.NewGuid():N}@example.com" });
            await Task.Delay(700);

            Assert.Equal(HttpStatusCode.OK, over.Status);
            Assert.Equal(unknown.Body, over.Body);
            Assert.Equal(RateLimits.CodesPerDay, await CodeCount(guest.Id, 1));
            Assert.Equal(mailsBefore, app.Mail.To(email).Count);
        }
    }
}
