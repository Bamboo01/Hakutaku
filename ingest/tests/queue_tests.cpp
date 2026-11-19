/*!************************************************************************
\file   queue_tests.cpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines the unit tests for EventQueue and Forwarder: the
        queue refuses what doesn't fit as a whole, hands out full batches
        at once and partial ones after the wait, and survives many threads;
        the forwarder retries until Hakutaku answers, drops a refused batch,
        and drains on stop. The forwarder gets a fake send.
**************************************************************************/
#include "event_queue.hpp"
#include "forwarder.hpp"

#include <doctest/doctest.h>
#include <thread>

using namespace ingest;
using namespace std::chrono_literals;

TEST_CASE("a push is all or nothing")
{
    EventQueue<int> q(5);
    CHECK(q.pushAll({1, 2, 3}));
    CHECK_FALSE(q.pushAll({4, 5, 6}));
    CHECK(q.size() == 3);
    CHECK(q.pushAll({4, 5}));
    CHECK(q.size() == 5);
}

TEST_CASE("a full batch comes at once and a partial one after the wait")
{
    EventQueue<int> q(100);
    q.pushAll({1, 2, 3, 4, 5});

    const auto started = std::chrono::steady_clock::now();
    CHECK(q.popBatch(3, 5s) == std::vector<int>{1, 2, 3});
    CHECK(std::chrono::steady_clock::now() - started < 1s);

    CHECK(q.popBatch(3, 50ms) == std::vector<int>{4, 5});
    CHECK(q.popBatch(3, 20ms).empty());
}

TEST_CASE("a closed queue refuses pushes and still hands out what it has")
{
    EventQueue<int> q(10);
    q.pushAll({1, 2});
    q.close();
    CHECK_FALSE(q.pushAll({3}));
    CHECK(q.popBatch(5, 5s) == std::vector<int>{1, 2});
    CHECK(q.popBatch(5, 5s).empty());
}

TEST_CASE("many threads pushing lose nothing")
{
    EventQueue<int> q(100'000);
    std::vector<std::thread> pushers;
    for (int t = 0; t < 8; ++t)
        pushers.emplace_back([&] { for (int i = 0; i < 1000; ++i) q.pushAll({i}); });
    std::size_t taken = 0;
    while (taken < 8000)
        taken += q.popBatch(500, 100ms).size();
    for (auto& p : pushers) p.join();
    CHECK(taken == 8000);
    CHECK(q.size() == 0);
}

namespace
{
    std::vector<Event> events(int n)
    {
        std::vector<Event> out(n);
        for (int i = 0; i < n; ++i) out[i].eventType = "e" + std::to_string(i);
        return out;
    }
}

TEST_CASE("the forwarder retries until Hakutaku answers, then counts what was stored")
{
    EventQueue<Event> q(1000);
    Stats stats;
    std::atomic<int> calls{0};
    Forwarder f(q, stats, [&](const std::string&)
    {
        SendResult r;
        if (++calls < 3) return r;  // Retry twice
        r.outcome = SendResult::Outcome::Delivered;
        r.accepted = 9;
        r.rejected = 1;
        return r;
    }, 10, 20ms, 1ms, 5ms);

    q.pushAll(events(10));
    f.start();
    for (int i = 0; i < 200 && stats.batchesSent == 0; ++i) std::this_thread::sleep_for(5ms);
    f.stop(1s);

    CHECK(calls == 3);
    CHECK(stats.retries == 2);
    CHECK(stats.stored == 9);
    CHECK(stats.refusedByHakutaku == 1);
    CHECK(stats.lost == 0);
}

TEST_CASE("a batch Hakutaku refuses as a whole is dropped, not retried forever")
{
    EventQueue<Event> q(1000);
    Stats stats;
    std::atomic<int> calls{0};
    Forwarder f(q, stats, [&](const std::string&)
    {
        ++calls;
        SendResult r;
        r.outcome = SendResult::Outcome::Refused;
        r.detail = "HTTP 400";
        return r;
    }, 10, 20ms, 1ms, 5ms);

    q.pushAll(events(4));
    f.start();
    for (int i = 0; i < 200 && stats.lost == 0; ++i) std::this_thread::sleep_for(5ms);
    f.stop(1s);

    CHECK(calls == 1);
    CHECK(stats.lost == 4);
    CHECK(f.lastProblem() == "HTTP 400");
}

TEST_CASE("stop delivers everything queued, in batches")
{
    EventQueue<Event> q(10'000);
    Stats stats;
    std::atomic<std::uint64_t> delivered{0};
    Forwarder f(q, stats, [&](const std::string& body)
    {
        SendResult r;
        r.outcome = SendResult::Outcome::Delivered;
        r.accepted = nlohmann::json::parse(body)["events"].size();
        delivered += r.accepted;
        return r;
    }, 100, 5s, 1ms, 5ms);

    q.pushAll(events(950));
    f.start();
    f.stop(2s);

    CHECK(delivered == 950);
    CHECK(stats.stored == 950);
    CHECK(stats.batchesSent == 10);
    CHECK(stats.lost == 0);
}

TEST_CASE("past the shutdown deadline, what can't be delivered is counted as lost")
{
    EventQueue<Event> q(1000);
    Stats stats;
    Forwarder f(q, stats, [](const std::string&) { return SendResult{}; }, 10, 20ms, 5ms, 20ms);

    q.pushAll(events(25));
    f.start();
    std::this_thread::sleep_for(50ms);
    f.stop(50ms);

    CHECK(stats.lost == 25);
    CHECK(stats.stored == 0);
}
