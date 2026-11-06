/*!************************************************************************
\file   PlayerInputRulesTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the unit tests for PlayerInputRules: how a Gmail
        address is folded into the key used to tell if two addresses reach
        the same inbox, which emails are accepted at all, and which display
        names are refused. They need no database or server.

        Tests PlayerInputRules.cs in server/Services.
**************************************************************************/
using server.Constants;
using server.Services;

namespace server.Tests
{
    public class PlayerInputRulesTests
    {
        // ---- EmailKey: the key two addresses are compared by

        [Theory]
        [InlineData("sherwyn@gmail.com", "sherwyn@gmail.com")]
        [InlineData("sher.wyn@gmail.com", "sherwyn@gmail.com")]
        [InlineData("s.h.e.r.w.y.n@gmail.com", "sherwyn@gmail.com")]
        [InlineData("sherwyn+game@gmail.com", "sherwyn@gmail.com")]
        [InlineData("sher.wyn+game+more@gmail.com", "sherwyn@gmail.com")]
        [InlineData("sher.wyn@googlemail.com", "sherwyn@gmail.com")]
        [InlineData("a.b+c@googlemail.com", "ab@gmail.com")]
        public void Gmail_addresses_fold_to_one_key(string typed, string key)
        {
            Assert.Equal(key, PlayerInputRules.EmailKey(typed));
        }

        [Theory]
        [InlineData("a.b@example.com")]
        [InlineData("a+b@example.com")]
        [InlineData("a.b@outlook.com")]
        [InlineData("a.b@yahoo.com")]
        public void Other_providers_keep_dots_and_plus(string address)
        {
            Assert.Equal(address, PlayerInputRules.EmailKey(address));
        }

        [Theory]
        [InlineData("a.b@mail.gmail.com")]
        [InlineData("a.b@gmail.com.evil.com")]
        [InlineData("a.b@notgmail.com")]
        [InlineData("a.b@gmail.co")]
        public void Only_the_exact_gmail_domains_are_folded(string address)
        {
            Assert.Equal(address, PlayerInputRules.EmailKey(address));
        }

        [Theory]
        [InlineData("+x@gmail.com")]
        [InlineData("...@gmail.com")]
        [InlineData(".+x@googlemail.com")]
        public void A_name_with_nothing_left_is_not_turned_into_a_blank_key(string address)
        {
            // folding would leave "@gmail.com", which every such address would share
            Assert.Equal(address, PlayerInputRules.EmailKey(address));
        }

        [Fact]
        public void Different_gmail_names_get_different_keys()
        {
            Assert.NotEqual(PlayerInputRules.EmailKey("sherwyn@gmail.com"), PlayerInputRules.EmailKey("sherwin@gmail.com"));
        }

        [Fact]
        public void Typed_in_any_case_the_same_inbox_gets_the_same_key()
        {
            var a = PlayerInputRules.NormalizeEmail("  Sher.Wyn+Game@GMAIL.com ")!;
            var b = PlayerInputRules.NormalizeEmail("SHERWYN@googlemail.com")!;
            Assert.Equal(PlayerInputRules.EmailKey(a), PlayerInputRules.EmailKey(b));
        }

        // ---- NormalizeEmail: what counts as an email at all

        [Theory]
        [InlineData("bob@example.com", "bob@example.com")]
        [InlineData("  Bob@Example.COM  ", "bob@example.com")]
        public void Email_is_trimmed_and_lowercased(string raw, string expected)
        {
            Assert.Equal(expected, PlayerInputRules.NormalizeEmail(raw));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("nope")]
        [InlineData("@example.com")]
        [InlineData("bob@")]
        [InlineData("bob@@example.com")]
        [InlineData("a@b@example.com")]
        [InlineData("bo b@example.com")]
        public void Things_that_are_not_emails_are_refused(string? raw)
        {
            Assert.Null(PlayerInputRules.NormalizeEmail(raw));
        }

        [Fact]
        public void The_length_limit_is_the_longest_an_email_can_be()
        {
            var atLimit = new string('a', Limits.MaxIdentifierLength - "@x.com".Length) + "@x.com";
            Assert.Equal(Limits.MaxIdentifierLength, atLimit.Length);
            Assert.NotNull(PlayerInputRules.NormalizeEmail(atLimit));
            Assert.Null(PlayerInputRules.NormalizeEmail("a" + atLimit));
        }

        // ---- TryNormalizeDisplayName

        [Fact]
        public void No_display_name_is_fine()
        {
            Assert.True(PlayerInputRules.TryNormalizeDisplayName(null, out var name));
            Assert.Null(name);
        }

        [Theory]
        [InlineData("Bob", "Bob")]
        [InlineData("  Sir Bob  ", "Sir Bob")]
        [InlineData("Ünï", "Ünï")]
        public void A_good_name_is_trimmed_and_kept(string raw, string expected)
        {
            Assert.True(PlayerInputRules.TryNormalizeDisplayName(raw, out var name));
            Assert.Equal(expected, name);
        }

        [Fact]
        public void Name_length_is_checked_after_trimming()
        {
            Assert.True(PlayerInputRules.TryNormalizeDisplayName(new string('x', Limits.DisplayNameMinLength), out _));
            Assert.True(PlayerInputRules.TryNormalizeDisplayName(new string('x', Limits.DisplayNameMaxLength), out _));
            Assert.True(PlayerInputRules.TryNormalizeDisplayName("  " + new string('x', Limits.DisplayNameMaxLength) + "  ", out _));

            Assert.False(PlayerInputRules.TryNormalizeDisplayName(new string('x', Limits.DisplayNameMinLength - 1), out _));
            Assert.False(PlayerInputRules.TryNormalizeDisplayName(new string('x', Limits.DisplayNameMaxLength + 1), out _));
            Assert.False(PlayerInputRules.TryNormalizeDisplayName("      ", out _));
        }

        [Theory]
        [InlineData("bad\nname")]
        [InlineData("bad\tname")]
        [InlineData("bad\u0000name")]
        public void Control_characters_are_refused(string raw)
        {
            Assert.False(PlayerInputRules.TryNormalizeDisplayName(raw, out _));
        }
    }
}
