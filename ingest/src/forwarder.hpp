/*!************************************************************************
\file   forwarder.hpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file declares Forwarder, the one background thread that takes
        batches off the queue and posts them to Hakutaku's
        POST /api/server/events/batch.

        A batch goes when it has `batchSize` events or `flushMs` after the
        first wait began, whichever is first. If Hakutaku is down or busy
        (no answer, a 5xx, a 429) or refuses this service's own key (a 401),
        the same batch is retried, waiting 0.5 s, then 1 s, 2 s... up to
        30 s between tries, while new events wait in the queue. Delivery is
        at least once: if Hakutaku stored a batch but the answer was lost,
        the retry stores it again. A 400 means the batch itself was bad,
        which retrying can't fix, so it is dropped and logged.

        On shutdown it keeps delivering what's queued until a deadline,
        then counts anything left as lost.

        Used by main.cpp. The tests give it a fake send.
**************************************************************************/
#pragma once
#include "event.hpp"
#include "event_queue.hpp"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <functional>
#include <mutex>
#include <string>
#include <thread>

namespace ingest
{
    // what happened to one attempt at sending a batch
    struct SendResult
    {
        enum class Outcome
        {
            // Hakutaku answered: `accepted` stored, `rejected` refused one by one
            Delivered,
            // the batch itself was refused; retrying won't help
            Refused,
            // try again later
            Retry,
        };
        Outcome outcome = Outcome::Retry;
        std::uint64_t accepted = 0;
        std::uint64_t rejected = 0;
        std::string detail;
    };

    // counters for /ingest/health, readable from any thread
    struct Stats
    {
        std::atomic<std::uint64_t> received{0};
        std::atomic<std::uint64_t> refusedOnArrival{0};
        std::atomic<std::uint64_t> stored{0};
        std::atomic<std::uint64_t> refusedByHakutaku{0};
        std::atomic<std::uint64_t> batchesSent{0};
        std::atomic<std::uint64_t> retries{0};
        std::atomic<std::uint64_t> lost{0};
    };

    class Forwarder
    {
    public:
        using Send = std::function<SendResult(const std::string& body)>;

        Forwarder(EventQueue<Event>& queue, Stats& stats, Send send, std::size_t batchSize, std::chrono::milliseconds flush,
                  std::chrono::milliseconds firstBackoff = std::chrono::milliseconds(500),
                  std::chrono::milliseconds maxBackoff = std::chrono::seconds(30));
        ~Forwarder();

        void start();
        // closes the queue, delivers what's left until `grace` runs out, then stops the thread
        void stop(std::chrono::milliseconds grace);

        // true while a batch is being retried, i.e. Hakutaku isn't taking events right now
        bool backingOff() const { return backingOff_; }
        std::string lastProblem() const;

        // the real send: POST {hakutakuUrl}/api/server/events/batch with this service's key
        static Send httpSend(const std::string& hakutakuUrl, const std::string& serverKey);

    private:
        void run();
        void deliver(std::vector<Event>&& batch);
        bool pastDeadline() const;
        void sleepFor(std::chrono::milliseconds d);

        EventQueue<Event>& queue_;
        Stats& stats_;
        Send send_;
        const std::size_t batchSize_;
        const std::chrono::milliseconds flush_;
        const std::chrono::milliseconds firstBackoff_;
        const std::chrono::milliseconds maxBackoff_;

        std::thread thread_;
        std::atomic<bool> stopping_{false};
        std::atomic<bool> backingOff_{false};
        std::atomic<std::int64_t> deadlineNs_{0};
        mutable std::mutex problemLock_;
        std::string lastProblem_;
        std::mutex sleepLock_;
        std::condition_variable wake_;
    };
}
