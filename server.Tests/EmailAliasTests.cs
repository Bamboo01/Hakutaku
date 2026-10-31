/*!************************************************************************
\file   EmailAliasTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the integration tests for Gmail aliases. It
        checks that sher.wyn+game@gmail.com and sherwyn@googlemail.com are
        one account: linking stores the key and keeps the typed address,
        a taken alias gets the same answer as any taken email, the owner is
        mailed once however many aliases are tried, and login, forgot and
        reset all find the account through any spelling.

        Runs against the real server through HakutakuFactory. Tests
        PlayerInputRules.EmailKey and the link, login, forgot and reset
        routes.
**************************************************************************/
using System.Net;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class EmailAliasTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>
    {
        readonly ApiClient api = app.Api();

        // a fresh gmail name per test, so tests don't see each other's rows
        static string NewName() => "u" + Guid.NewGuid().ToString("N")[..16];

        // the same name spelled with a dot in the middle, e.g. u1234.5678
        static string Dotted(string name) => $"{name[..6]}.{name[6..]}";

        [Fact]
        public async Task Linking_stores_the_key_and_keeps_the_address_as_typed()
        {
            var name = NewName();
            var guest = await api.RegisterGuest();

            var reply = await api.LinkEmail(guest.Token, $"{Dotted(name)}+Game@Gmail.com");

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal($"{name}@gmail.com",
                await app.Scalar<string>($"select subject from player_identities where player_id = '{guest.Id}' and provider = 1"));
            Assert.Equal($"{Dotted(name)}+game@gmail.com",
                await app.Scalar<string>($"select email from player_identities where player_id = '{guest.Id}' and provider = 1"));
        }

        [Fact]
        public async Task An_alias_of_a_taken_address_gets_the_same_answer_and_makes_no_account()
        {
            var name = NewName();
            var owner = await api.RegisterGuest();
            var other = await api.RegisterGuest();
            var free = await api.RegisterGuest();
            await api.LinkEmail(owner.Token, $"{name}@gmail.com");

            var taken = await api.LinkEmail(other.Token, $"{name}+x@googlemail.com");
            var fresh = await api.LinkEmail(free.Token, $"{NewName()}@gmail.com");

            Assert.Equal(HttpStatusCode.OK, taken.Status);
            Assert.Equal(fresh.Status, taken.Status);
            Assert.False(taken.Json.GetProperty("verified").GetBoolean());
            Assert.True(taken.Json.GetProperty("verificationSent").GetBoolean());
            Assert.Equal(fresh.Json.EnumerateObject().Select(p => p.Name), taken.Json.EnumerateObject().Select(p => p.Name));

            Assert.Equal(1, await app.Scalar<long>($"select count(*) from player_identities where provider = 1 and subject = '{name}@gmail.com'"));
            Assert.Equal(0, await app.Scalar<long>($"select count(*) from player_identities where player_id = '{other.Id}' and provider = 1"));
        }

        [Fact]
        public async Task The_owner_is_mailed_once_however_many_aliases_are_tried()
        {
            var name = NewName();
            var owner = await api.RegisterGuest();
            await api.LinkEmail(owner.Token, $"{name}@gmail.com");

            foreach (var alias in new[] { Dotted(name) + "@gmail.com", name + "+a@gmail.com", name + "@googlemail.com" })
                await api.LinkEmail((await api.RegisterGuest()).Token, alias);

            bool Warned() => app.Mail.To($"{name}@gmail.com").Any(m => m.Subject.Contains("someone tried"));
            Assert.True(await CapturingEmailSender.WaitFor(Warned));
            await Task.Delay(500);
            Assert.Equal(1, app.Mail.To($"{name}@gmail.com").Count(m => m.Subject.Contains("someone tried")));
        }

        [Fact]
        public async Task Other_providers_keep_dots_as_part_of_the_name()
        {
            var name = NewName();
            var a = await api.RegisterGuest();
            var b = await api.RegisterGuest();

            var first = await api.LinkEmail(a.Token, $"{Dotted(name)}@example.com");
            var second = await api.LinkEmail(b.Token, $"{name}@example.com");

            Assert.Equal(HttpStatusCode.OK, first.Status);
            Assert.Equal(HttpStatusCode.OK, second.Status);
            Assert.Equal(2, await app.Scalar<long>(
                $"select count(*) from player_identities where provider = 1 and subject in ('{Dotted(name)}@example.com', '{name}@example.com')"));
        }

        [Fact]
        public async Task Login_works_through_any_spelling_of_the_address()
        {
            var name = NewName();
            var guest = await api.RegisterGuest();
            await api.LinkEmail(guest.Token, $"{Dotted(name)}+game@gmail.com");
            await app.Run($"update player_identities set verified_at = now() where player_id = '{guest.Id}' and provider = 1");

            foreach (var spelling in new[] { $"{name}@gmail.com", $"{Dotted(name).ToUpper()}+GAME@gmail.com", $"{name}+zzz@googlemail.com" })
            {
                var login = await api.LoginWithEmail(spelling);
                Assert.Equal(HttpStatusCode.OK, login.Status);
                Assert.Equal(guest.Id, login.Json.GetProperty("id").GetGuid());
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await api.LoginWithEmail($"{name}@gmail.com", "wrong-password-1")).Status);
        }

        [Fact]
        public async Task Forgot_and_reset_work_through_other_spellings_and_mail_goes_to_the_stored_address()
        {
            var name = NewName();
            var stored = $"{Dotted(name)}+game@gmail.com";
            var guest = await api.RegisterGuest();
            await api.LinkEmail(guest.Token, stored);
            await app.Run($"update player_identities set verified_at = now() where player_id = '{guest.Id}' and provider = 1");

            var forgot = await api.Post("/api/players/password/forgot", new { email = $"{name}+other@gmail.com" });
            Assert.Equal(HttpStatusCode.OK, forgot.Status);

            string? Code() => app.Mail.LastCode(stored, "Reset your password");
            Assert.True(await CapturingEmailSender.WaitFor(() => Code() is not null));

            var reset = await api.Post("/api/players/password/reset",
                new { email = $"{name}@googlemail.com", code = Code(), newPassword = "a-brand-new-password" });
            Assert.Equal(HttpStatusCode.OK, reset.Status);

            Assert.Equal(HttpStatusCode.OK, (await api.LoginWithEmail($"{name}+q@gmail.com", "a-brand-new-password")).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.LoginWithEmail($"{name}@gmail.com")).Status);
        }
    }
}
