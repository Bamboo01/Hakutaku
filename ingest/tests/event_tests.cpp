/*!************************************************************************
\file   event_tests.cpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines the unit tests for readEvent and its helpers:
        the same events the server's EventRules takes and refuses are taken
        and refused here, data may come as json or as a string of json, and
        each event is stamped with when it arrived.
**************************************************************************/
#include "event.hpp"

#include <doctest/doctest.h>

using namespace ingest;
using nlohmann::json;

namespace
{
    const auto kAt = std::chrono::system_clock::time_point(std::chrono::milliseconds(1'794'900'615'123));
    const std::string kPlayer = "01a1e2fa-4935-7548-8d3f-193109a7d10f";

    std::optional<std::string> read(const json& in, Event* out = nullptr)
    {
        Event e;
        auto why = readEvent(in, kAt, e);
        if (out) *out = e;
        return why;
    }
}

TEST_CASE("a good event is read, trimmed and stamped")
{
    Event e;
    const auto why = read({{"playerId", kPlayer}, {"eventType", "  level_up "}, {"data", {{"level", 2}}}}, &e);

    CHECK_FALSE(why);
    CHECK(e.playerId == kPlayer);
    CHECK(e.eventType == "level_up");
    CHECK(e.data == R"({"level":2})");
    CHECK(e.occurredAt == "2026-11-17T07:30:15.123Z");
}

TEST_CASE("data may be a string holding json, and is kept exactly")
{
    Event e;
    CHECK_FALSE(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", R"( {"a": [1, 2]} )"}}, &e));
    CHECK(e.data == R"( {"a": [1, 2]} )");
    CHECK_FALSE(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", "12"}}));
    CHECK_FALSE(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", json::array({1, 2})}}));
}

TEST_CASE("bad data is refused with the server's message")
{
    const std::string message = "data must be valid json, up to 10000 bytes";
    CHECK(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", "not json"}}) == message);
    CHECK(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", "   "}}) == message);
    CHECK(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", nullptr}}) == message);
    CHECK(read({{"playerId", kPlayer}, {"eventType", "x"}}) == message);
    // a string is json text, so plain words aren't json
    CHECK(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", "hello"}}) == message);
    // 10,000 bytes is the limit: a json string of 9,998 x's is exactly it with its quotes, whichever way it comes
    const std::string exact = '"' + std::string(9'998, 'x') + '"';
    CHECK_FALSE(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", exact}}));
    CHECK(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", '"' + std::string(9'999, 'x') + '"'}}) == message);
    // as a json value: ["xxx..."] with 9,996 x's is 10,000 bytes once written out
    CHECK_FALSE(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", json::array({std::string(9'996, 'x')})}}));
    CHECK(read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", json::array({std::string(9'997, 'x')})}}) == message);
}

TEST_CASE("a bad player or event type is refused with the server's message")
{
    const std::string message = "playerId is required and eventType must be 1-100 characters";
    CHECK(read({{"eventType", "x"}, {"data", 1}}) == message);
    CHECK(read({{"playerId", "not-a-guid"}, {"eventType", "x"}, {"data", 1}}) == message);
    CHECK(read({{"playerId", 42}, {"eventType", "x"}, {"data", 1}}) == message);
    CHECK(read({{"playerId", "00000000-0000-0000-0000-000000000000"}, {"eventType", "x"}, {"data", 1}}) == message);
    CHECK(read({{"playerId", kPlayer}, {"eventType", "   "}, {"data", 1}}) == message);
    CHECK(read({{"playerId", kPlayer}, {"eventType", "a\tb"}, {"data", 1}}) == message);
    CHECK(read({{"playerId", kPlayer}, {"eventType", std::string(101, 'x')}, {"data", 1}}) == message);
    CHECK_FALSE(read({{"playerId", kPlayer}, {"eventType", std::string(100, 'x')}, {"data", 1}}));
    CHECK(read(json::array()) == message);
}

TEST_CASE("the event type length is counted like C# counts it")
{
    // é is one UTF-16 unit but two bytes; an emoji is two units and four bytes
    CHECK(utf16Length("é") == 1u);
    CHECK(utf16Length("😀") == 2u);
    CHECK(utf16Length("\xC3") == std::nullopt);

    std::string fifty;
    for (int i = 0; i < 50; ++i) fifty += "😀";
    CHECK_FALSE(read({{"playerId", kPlayer}, {"eventType", fifty}, {"data", 1}}));
    CHECK(read({{"playerId", kPlayer}, {"eventType", fifty + "x"}, {"data", 1}}));
}

TEST_CASE("control characters, including the C1 range, are found")
{
    CHECK(hasControlCharacter("a\x01"));
    CHECK(hasControlCharacter("a\x7F"));
    CHECK(hasControlCharacter("a\xC2\x85"));
    CHECK_FALSE(hasControlCharacter("level_up é 😀"));
}

TEST_CASE("guids")
{
    CHECK(isGuid(kPlayer));
    CHECK(isGuid("01A1E2FA-4935-7548-8D3F-193109A7D10F"));
    CHECK_FALSE(isGuid("01a1e2fa49357548-8d3f-193109a7d10f00"));
    CHECK_FALSE(isGuid("{01a1e2fa-4935-7548-8d3f-193109a7d10f}"));
}

TEST_CASE("toJson is the shape Hakutaku's batch route takes")
{
    Event e;
    read({{"playerId", kPlayer}, {"eventType", "x"}, {"data", {{"a", 1}}}}, &e);
    CHECK(toJson(e) == json{{"playerId", kPlayer}, {"eventType", "x"}, {"data", R"({"a":1})"}, {"occurredAt", "2026-11-17T07:30:15.123Z"}});
}
