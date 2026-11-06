/*!************************************************************************
\file   LinkEmailTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   4th November 2026
\brief  This file defines the integration tests for who holds an email
        address. An unverified link holds its address for a day and then
        the next link replaces it, so a guest who links someone else's
        email can't lock the real owner out of it. A verified link is never
        replaced, however old, and only a verified owner is warned about a
        taken address.

        Runs against the real server through HakutakuFactory. Tests the
        link/email and email/verify routes.
**************************************************************************/
using System.Net;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class LinkEmailTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>
    {
        readonly ApiClient api = app.Api();

        // a fresh address per test, so tests don't see each other's rows
        static string NewEmail() => "u" + Guid.NewGuid().ToString("N")[..16] + "@example.com";

        // moves a player's email link back in time, past the day an unverified link holds its address
        Task Age(Guid playerId) =>
            app.Run($"update player_identities set linked_at = now() - interval '25 hours' where player_id = '{playerId}' and provider = 1");

        Task<string?> Holder(string email) =>
            app.Scalar<string>($"select player_id::text from player_identities where provider = 1 and subject = '{email}'");

        bool Warned(string email) => app.Mail.To(email).Any(m => m.Subject.Contains("someone tried"));

        [Fact]
        public async Task A_fresh_unverified_link_keeps_its_address_and_nobody_is_warned()
        {
            var email = NewEmail();
            var first = await api.RegisterGuest();
            var second = await api.RegisterGuest();
            await api.LinkEmail(first.Token, email);

            var reply = await api.LinkEmail(second.Token, email);

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.True(reply.Json.GetProperty("verificationSent").GetBoolean());
            Assert.Equal(first.Id.ToString(), await Holder(email));
            await Task.Delay(500);
            Assert.False(Warned(email));
        }

        [Fact]
        public async Task A_stale_unverified_link_is_replaced_and_the_new_player_can_verify()
        {
            var email = NewEmail();
            var squatter = await api.RegisterGuest();
            var owner = await api.RegisterGuest();
            await api.LinkEmail(squatter.Token, email);
            await Age(squatter.Id);

            var reply = await api.LinkEmail(owner.Token, email);

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.True(reply.Json.GetProperty("verificationSent").GetBoolean());
            Assert.Equal(owner.Id.ToString(), await Holder(email));
            Assert.Equal(0, await app.Scalar<long>($"select count(*) from player_identities where player_id = '{squatter.Id}' and provider = 1"));
            Assert.False(Warned(email));

            var code = app.Mail.LastCode(email, "Verify your email");
            var verify = await api.Post("/api/players/email/verify", new { code }, owner.Token);
            Assert.Equal(HttpStatusCode.OK, verify.Status);
            Assert.Equal(HttpStatusCode.OK, (await api.LoginWithEmail(email)).Status);
        }

        [Fact]
        public async Task The_replaced_player_no_longer_has_an_email_to_verify()
        {
            var email = NewEmail();
            var squatter = await api.RegisterGuest();
            var owner = await api.RegisterGuest();
            await api.LinkEmail(squatter.Token, email);
            await Age(squatter.Id);
            await api.LinkEmail(owner.Token, email);

            var verify = await api.Post("/api/players/email/verify", new { code = "123456" }, squatter.Token);

            Assert.Equal(HttpStatusCode.BadRequest, verify.Status);
            Assert.Null(await app.Scalar<string>($"select verified_at::text from player_identities where provider = 1 and subject = '{email}'"));
        }

        [Fact]
        public async Task A_verified_link_is_never_replaced_and_its_owner_is_warned()
        {
            var email = NewEmail();
            var owner = await api.RegisterGuest();
            var other = await api.RegisterGuest();
            await api.LinkEmail(owner.Token, email);
            await app.Run($"update player_identities set verified_at = now() where player_id = '{owner.Id}' and provider = 1");
            await Age(owner.Id);

            var reply = await api.LinkEmail(other.Token, email);

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal(owner.Id.ToString(), await Holder(email));
            Assert.True(await CapturingEmailSender.WaitFor(() => Warned(email)));
        }
    }
}
