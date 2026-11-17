/*!************************************************************************
\file   key_check_tests.cpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines the unit tests for KeyCheck, with a fake ping and
        a fake clock: answers are remembered for their time and then asked
        again, "can't tell" is never remembered, obvious junk never costs a
        ping, and the memory of bad keys stays bounded.
**************************************************************************/
#include "key_check.hpp"

#include <doctest/doctest.h>

using namespace ingest;
using namespace std::chrono_literals;

namespace
{
    struct Fake
    {
        std::chrono::steady_clock::time_point now{};
        int pings = 0;
        KeyVerdict answer = KeyVerdict::Valid;

        KeyCheck make()
        {
            return KeyCheck([this](const std::string&) { ++pings; return answer; }, [this] { return now; });
        }
    };
}

TEST_CASE("a good key is asked about once and trusted for a minute")
{
    Fake fake;
    auto keys = fake.make();

    CHECK(keys.check("good") == KeyVerdict::Valid);
    fake.now += 59s;
    CHECK(keys.check("good") == KeyVerdict::Valid);
    CHECK(fake.pings == 1);

    // revoked meanwhile: noticed once the minute is up
    fake.answer = KeyVerdict::Invalid;
    fake.now += 2s;
    CHECK(keys.check("good") == KeyVerdict::Invalid);
    CHECK(fake.pings == 2);
}

TEST_CASE("a bad key is remembered for ten seconds")
{
    Fake fake;
    fake.answer = KeyVerdict::Invalid;
    auto keys = fake.make();

    CHECK(keys.check("bad") == KeyVerdict::Invalid);
    fake.now += 9s;
    CHECK(keys.check("bad") == KeyVerdict::Invalid);
    CHECK(fake.pings == 1);
    fake.now += 2s;
    keys.check("bad");
    CHECK(fake.pings == 2);
}

TEST_CASE("when Hakutaku can't be reached the answer is unknown and nothing is remembered")
{
    Fake fake;
    fake.answer = KeyVerdict::Unknown;
    auto keys = fake.make();

    CHECK(keys.check("any") == KeyVerdict::Unknown);
    fake.answer = KeyVerdict::Valid;
    CHECK(keys.check("any") == KeyVerdict::Valid);
    CHECK(fake.pings == 2);
}

TEST_CASE("an empty or absurdly long key never costs a ping")
{
    Fake fake;
    auto keys = fake.make();

    CHECK(keys.check("") == KeyVerdict::Invalid);
    CHECK(keys.check(std::string(513, 'k')) == KeyVerdict::Invalid);
    CHECK(fake.pings == 0);
}

TEST_CASE("thousands of different bad keys don't grow the memory without bound")
{
    Fake fake;
    fake.answer = KeyVerdict::Invalid;
    auto keys = fake.make();

    for (int i = 0; i < 5000; ++i) keys.check("junk" + std::to_string(i));
    // the good key still works, and is still remembered after the flood
    fake.answer = KeyVerdict::Valid;
    CHECK(keys.check("good") == KeyVerdict::Valid);
    const auto before = fake.pings;
    CHECK(keys.check("good") == KeyVerdict::Valid);
    CHECK(fake.pings == before);
}
