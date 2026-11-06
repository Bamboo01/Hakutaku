/*!************************************************************************
\file   PlayerDataRulesTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the unit tests for PlayerDataRules: how a tier is
        named, what a key may look like, what a value may hold, and how a
        list of keys in a URL is read. They need no database or server.

        Tests PlayerDataRules.cs in server/Services.
**************************************************************************/
using server.Constants;
using server.Models;
using server.Services;

namespace server.Tests
{
    public class PlayerDataRulesTests
    {
        // ---- the tier is given by name

        [Theory]
        [InlineData("ClientWritable", PlayerDataTier.ClientWritable)]
        [InlineData("ReadOnly", PlayerDataTier.ReadOnly)]
        [InlineData("Internal", PlayerDataTier.Internal)]
        [InlineData("readonly", PlayerDataTier.ReadOnly)]
        [InlineData("READONLY", PlayerDataTier.ReadOnly)]
        [InlineData("clientWRITABLE", PlayerDataTier.ClientWritable)]
        public void A_tier_name_works_in_any_case(string raw, PlayerDataTier expected)
        {
            Assert.True(PlayerDataRules.TryParseTier(raw, out var tier));
            Assert.Equal(expected, tier);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("0")]
        [InlineData("1")]
        [InlineData("2")]
        [InlineData("3")]
        [InlineData("Public")]
        [InlineData("Read Only")]
        [InlineData(" ReadOnly")]
        [InlineData("ReadOnly,Internal")]
        public void Anything_that_is_not_a_tier_name_is_refused_and_numbers_too(string? raw)
        {
            Assert.False(PlayerDataRules.TryParseTier(raw, out _));
        }

        // ---- keys

        [Theory]
        [InlineData("Xp")]
        [InlineData("a")]
        [InlineData("Some_Key-1.2")]
        [InlineData("UPPER")]
        [InlineData("0123456789")]
        public void A_key_of_letters_digits_and_underscore_dot_dash_is_fine(string key)
        {
            Assert.True(PlayerDataRules.IsValidKey(key));
        }

        [Fact]
        public void A_key_can_be_1_to_64_characters()
        {
            Assert.True(PlayerDataRules.IsValidKey(new string('k', 1)));
            Assert.True(PlayerDataRules.IsValidKey(new string('k', Limits.PlayerDataKeyMaxLength)));
            Assert.False(PlayerDataRules.IsValidKey(new string('k', Limits.PlayerDataKeyMaxLength + 1)));
            Assert.False(PlayerDataRules.IsValidKey(""));
            Assert.False(PlayerDataRules.IsValidKey(null));
        }

        [Theory]
        [InlineData("a key")]
        [InlineData(" Xp")]
        [InlineData("Xp ")]
        [InlineData("Xp\n")]
        [InlineData("\nXp")]
        [InlineData("Xp\t")]
        [InlineData("a/b")]
        [InlineData("a,b")]
        [InlineData("a:b")]
        [InlineData("a=b")]
        [InlineData("é")]
        [InlineData("名前")]
        [InlineData("a\0b")]
        public void A_key_with_anything_else_in_it_is_refused(string key)
        {
            Assert.False(PlayerDataRules.IsValidKey(key));
        }

        // ---- values

        [Fact]
        public void A_value_can_be_up_to_16_KB_counted_in_bytes()
        {
            Assert.True(PlayerDataRules.IsValidValue(""));
            Assert.True(PlayerDataRules.IsValidValue(new string('v', Limits.PlayerDataValueMaxBytes)));
            Assert.False(PlayerDataRules.IsValidValue(new string('v', Limits.PlayerDataValueMaxBytes + 1)));
        }

        [Fact]
        public void The_limit_counts_bytes_and_not_characters()
        {
            // an "é" is 2 bytes in UTF-8, so half the limit in characters already fills it
            var half = Limits.PlayerDataValueMaxBytes / 2;
            Assert.True(PlayerDataRules.IsValidValue(new string('é', half)));
            Assert.False(PlayerDataRules.IsValidValue(new string('é', half + 1)));
            // and the same number of plain characters is well under the limit
            Assert.True(PlayerDataRules.IsValidValue(new string('v', half + 1)));
        }

        [Theory]
        [InlineData("1200")]
        [InlineData("{\"a\":[1,2,3]}")]
        [InlineData("line one\nline two")]
        [InlineData("名前 and émoji")]
        [InlineData("   ")]
        public void Any_text_is_fine_as_a_value(string value)
        {
            Assert.True(PlayerDataRules.IsValidValue(value));
        }

        [Fact]
        public void A_null_value_and_a_null_character_are_refused()
        {
            Assert.False(PlayerDataRules.IsValidValue(null));
            Assert.False(PlayerDataRules.IsValidValue("a\0b"));
        }

        // ---- the keys list in the url

        [Fact]
        public void A_list_of_keys_is_split_on_commas()
        {
            Assert.True(PlayerDataRules.TryParseKeys("Xp,Foo,Level", out var keys));
            Assert.Equal(["Xp", "Foo", "Level"], keys);
        }

        [Fact]
        public void One_key_is_a_list_of_one()
        {
            Assert.True(PlayerDataRules.TryParseKeys("Xp", out var keys));
            Assert.Equal(["Xp"], keys);
        }

        [Fact]
        public void Spaces_around_a_key_are_ignored_and_repeats_are_dropped()
        {
            Assert.True(PlayerDataRules.TryParseKeys("Xp, Foo ,Xp,Foo", out var keys));
            Assert.Equal(["Xp", "Foo"], keys);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(",")]
        [InlineData("Xp,")]
        [InlineData(",Xp")]
        [InlineData("Xp,,Foo")]
        [InlineData("Xp, ,Foo")]
        [InlineData("Xp,bad key")]
        public void A_blank_or_bad_entry_refuses_the_whole_list(string raw)
        {
            // never quietly treated as "all of them"
            Assert.False(PlayerDataRules.TryParseKeys(raw, out _));
        }
    }
}
