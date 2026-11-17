/*!************************************************************************
\file   main.cpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines the entry point of hakutaku-ingest, the telemetry
        ingestion service. It serves:

          POST /ingest/events   a game server sends events, with its server
                                key in X-Server-Key. The body is one event,
                                or { "events": [ ... ] } with up to 1,000.
                                Good events are queued and answered 202 at
                                once; bad ones come back by index. 401 for a
                                bad key, 503 (with Retry-After) when the
                                queue is full or the key can't be checked.
          GET  /ingest/health   counters and whether Hakutaku is taking
                                events, as JSON (not published by Caddy)

        Events go to Hakutaku in batches from one background thread (see
        forwarder.hpp). On SIGTERM or Ctrl+C it stops taking requests and
        spends up to 10 seconds delivering what is queued.
**************************************************************************/
#include "config.hpp"
#include "event.hpp"
#include "event_queue.hpp"
#include "forwarder.hpp"
#include "key_check.hpp"
#include "log.hpp"

#include <csignal>
#include <httplib.h>
#include <nlohmann/json.hpp>
#include <thread>

using namespace ingest;
using nlohmann::json;

namespace
{
    constexpr std::size_t kMaxEventsPerRequest = 1000;
    // 1,000 events of 10 kb would be 10 mb; most requests are far smaller. anything bigger is refused
    constexpr std::size_t kMaxBodyBytes = 4 * 1024 * 1024;
    constexpr auto kShutdownGrace = std::chrono::seconds(10);

    volatile std::sig_atomic_t stopRequested = 0;
    // only sets a flag: almost nothing is safe to call inside a signal handler
    extern "C" void onSignal(int) { stopRequested = 1; }

    void reply(httplib::Response& res, int status, const json& body)
    {
        res.status = status;
        res.set_content(body.dump(), "application/json");
    }
}

int main()
{
    std::string problem;
    const auto config = Config::fromEnvironment(problem);
    if (!config)
    {
        log::error(problem);
        return 1;
    }

    Stats stats;
    EventQueue<Event> queue(config->queueMax);
    Forwarder forwarder(queue, stats, Forwarder::httpSend(config->hakutakuUrl, config->serverKey), config->batchSize,
                        std::chrono::milliseconds(config->flushMs));
    KeyCheck keys(KeyCheck::httpPing(config->hakutakuUrl));

    httplib::Server server;
    server.set_payload_max_length(kMaxBodyBytes);

    server.Post("/ingest/events", [&](const httplib::Request& req, httplib::Response& res)
    {
        switch (keys.check(req.get_header_value("X-Server-Key")))
        {
        case KeyVerdict::Invalid:
            return reply(res, 401, {{"error", "a valid X-Server-Key is required"}});
        case KeyVerdict::Unknown:
            res.set_header("Retry-After", "5");
            return reply(res, 503, {{"error", "can't check the server key right now, try again shortly"}});
        case KeyVerdict::Valid:
            break;
        }

        const auto body = json::parse(req.body, nullptr, false);
        if (body.is_discarded()) return reply(res, 400, {{"error", "the body must be json"}});

        // { "events": [...] }, or a single event on its own
        json list;
        if (body.is_object() && body.contains("events"))
            list = body["events"];
        else if (body.is_object())
            list = json::array({body});
        if (!list.is_array() || list.empty() || list.size() > kMaxEventsPerRequest)
            return reply(res, 400, {{"error", "send one event, or { \"events\": [...] } with 1-1000 events"}});

        const auto receivedAt = std::chrono::system_clock::now();
        std::vector<Event> good;
        good.reserve(list.size());
        json rejected = json::array();
        for (std::size_t i = 0; i < list.size(); ++i)
        {
            Event e;
            if (const auto why = readEvent(list[i], receivedAt, e))
                rejected.push_back({{"index", i}, {"error", *why}});
            else
                good.push_back(std::move(e));
        }

        const auto queued = good.size();
        if (queued > 0 && !queue.pushAll(std::move(good)))
        {
            // all or nothing, so retrying this request never stores any of it twice
            res.set_header("Retry-After", "1");
            return reply(res, 503, {{"error", "too many events waiting, try again shortly"}});
        }
        stats.received += queued;
        stats.refusedOnArrival += rejected.size();
        reply(res, 202, {{"queued", queued}, {"rejected", rejected}});
    });

    server.Get("/ingest/health", [&](const httplib::Request&, httplib::Response& res)
    {
        reply(res, 200, {
            {"status", forwarder.backingOff() ? "backing_off" : "ok"},
            {"queued", queue.size()},
            {"queueCapacity", queue.capacity()},
            {"received", stats.received.load()},
            {"refusedOnArrival", stats.refusedOnArrival.load()},
            {"stored", stats.stored.load()},
            {"refusedByHakutaku", stats.refusedByHakutaku.load()},
            {"batchesSent", stats.batchesSent.load()},
            {"retries", stats.retries.load()},
            {"lost", stats.lost.load()},
            {"lastProblem", forwarder.lastProblem()},
        });
    });

    server.set_error_handler([](const httplib::Request&, httplib::Response& res)
    {
        if (res.status == 404) res.set_content(R"({"error":"no such route"})", "application/json");
        if (res.status == 413) res.set_content(R"({"error":"the body is too big"})", "application/json");
    });

    std::signal(SIGINT, onSignal);
    std::signal(SIGTERM, onSignal);

    forwarder.start();
    std::thread listener([&]
    {
        if (!server.listen("0.0.0.0", config->port))
        {
            log::error("can't listen on port " + std::to_string(config->port));
            stopRequested = 1;
        }
    });
    log::info("listening on port " + std::to_string(config->port) + ", sending to " + config->hakutakuUrl);

    while (!stopRequested) std::this_thread::sleep_for(std::chrono::milliseconds(100));

    log::info("stopping: no new events, delivering what's queued");
    server.stop();
    listener.join();
    forwarder.stop(kShutdownGrace);
    log::info("stopped. stored " + std::to_string(stats.stored.load()) + ", lost " + std::to_string(stats.lost.load()));
    return 0;
}
