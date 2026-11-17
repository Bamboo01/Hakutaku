/*!************************************************************************
\file   forwarder.cpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines Forwarder, declared in forwarder.hpp.
**************************************************************************/
#include "forwarder.hpp"
#include "log.hpp"

#include <algorithm>
#include <httplib.h>

namespace ingest
{
    namespace
    {
        std::int64_t nowNs()
        {
            return std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
        }
    }

    Forwarder::Forwarder(EventQueue<Event>& queue, Stats& stats, Send send, std::size_t batchSize, std::chrono::milliseconds flush,
                         std::chrono::milliseconds firstBackoff, std::chrono::milliseconds maxBackoff)
        : queue_(queue), stats_(stats), send_(std::move(send)), batchSize_(batchSize), flush_(flush),
          firstBackoff_(firstBackoff), maxBackoff_(maxBackoff)
    {
    }

    Forwarder::~Forwarder()
    {
        if (thread_.joinable()) stop(std::chrono::milliseconds(0));
    }

    void Forwarder::start()
    {
        thread_ = std::thread([this] { run(); });
    }

    void Forwarder::stop(std::chrono::milliseconds grace)
    {
        deadlineNs_ = nowNs() + std::chrono::duration_cast<std::chrono::nanoseconds>(grace).count();
        stopping_ = true;
        queue_.close();
        wake_.notify_all();
        if (thread_.joinable()) thread_.join();
    }

    std::string Forwarder::lastProblem() const
    {
        std::lock_guard guard(problemLock_);
        return lastProblem_;
    }

    bool Forwarder::pastDeadline() const { return stopping_ && nowNs() >= deadlineNs_; }

    // a sleep that stop() can cut short
    void Forwarder::sleepFor(std::chrono::milliseconds d)
    {
        std::unique_lock guard(sleepLock_);
        wake_.wait_for(guard, d, [this] { return pastDeadline(); });
    }

    void Forwarder::run()
    {
        for (;;)
        {
            auto batch = queue_.popBatch(batchSize_, flush_);
            if (batch.empty())
            {
                // closed and drained: done. otherwise nothing came in this window; wait again
                if (stopping_) break;
                continue;
            }
            deliver(std::move(batch));
        }
        if (const auto left = queue_.size(); left > 0)
        {
            stats_.lost += left;
            log::error("stopped with " + std::to_string(left) + " events still queued; they are lost");
        }
    }

    void Forwarder::deliver(std::vector<Event>&& batch)
    {
        nlohmann::json events = nlohmann::json::array();
        for (const auto& e : batch) events.push_back(toJson(e));
        const auto body = nlohmann::json{{"events", std::move(events)}}.dump();

        auto wait = firstBackoff_;
        for (;;)
        {
            if (pastDeadline())
            {
                stats_.lost += batch.size();
                log::error("shutdown deadline passed; " + std::to_string(batch.size()) + " events in a pending batch are lost");
                return;
            }

            const auto result = send_(body);
            switch (result.outcome)
            {
            case SendResult::Outcome::Delivered:
                ++stats_.batchesSent;
                stats_.stored += result.accepted;
                stats_.refusedByHakutaku += result.rejected;
                if (backingOff_.exchange(false)) log::info("Hakutaku is taking events again");
                if (result.rejected > 0)
                    log::warn("Hakutaku refused " + std::to_string(result.rejected) + " of " + std::to_string(batch.size()) +
                              " events: " + result.detail);
                return;

            case SendResult::Outcome::Refused:
                stats_.lost += batch.size();
                {
                    std::lock_guard guard(problemLock_);
                    lastProblem_ = result.detail;
                }
                log::error("Hakutaku refused a whole batch of " + std::to_string(batch.size()) + " events, dropping it: " + result.detail);
                return;

            case SendResult::Outcome::Retry:
                ++stats_.retries;
                {
                    std::lock_guard guard(problemLock_);
                    lastProblem_ = result.detail;
                }
                if (!backingOff_.exchange(true)) log::warn("can't deliver to Hakutaku, retrying: " + result.detail);
                sleepFor(wait);
                wait = std::min(wait * 2, maxBackoff_);
                break;
            }
        }
    }

    Forwarder::Send Forwarder::httpSend(const std::string& hakutakuUrl, const std::string& serverKey)
    {
        // one client, reused, so the connection to Hakutaku stays open between batches. only the forwarder
        // thread uses it
        auto client = std::make_shared<httplib::Client>(hakutakuUrl);
        client->set_connection_timeout(std::chrono::seconds(3));
        client->set_read_timeout(std::chrono::seconds(15));
        client->set_keep_alive(true);

        return [client, serverKey](const std::string& body)
        {
            SendResult r;
            const auto reply = client->Post("/api/server/events/batch", {{"X-Server-Key", serverKey}}, body, "application/json");
            if (!reply)
            {
                r.outcome = SendResult::Outcome::Retry;
                r.detail = "no answer (" + httplib::to_string(reply.error()) + ")";
                return r;
            }

            const auto status = reply->status;
            if (status == 200)
            {
                r.outcome = SendResult::Outcome::Delivered;
                const auto json = nlohmann::json::parse(reply->body, nullptr, false);
                if (!json.is_discarded())
                {
                    r.accepted = json.value("accepted", 0ULL);
                    if (const auto rejected = json.find("rejected"); rejected != json.end() && rejected->is_array())
                    {
                        r.rejected = rejected->size();
                        // a few reasons are enough to see what's wrong in the log
                        for (std::size_t i = 0; i < std::min<std::size_t>(3, rejected->size()); ++i)
                            r.detail += (i ? "; " : "") + std::string("#") + std::to_string((*rejected)[i].value("index", -1)) + " " +
                                        (*rejected)[i].value("error", std::string("?"));
                    }
                }
                return r;
            }

            r.detail = "HTTP " + std::to_string(status) + " " + reply->body.substr(0, 200);
            if (status == 400)
            {
                r.outcome = SendResult::Outcome::Refused;
            }
            else
            {
                // 401: this service's own key was revoked or is wrong, which needs a person, but the events are
                // kept in case they fix it. 429 and 5xx: Hakutaku is busy or broken; wait it out
                r.outcome = SendResult::Outcome::Retry;
                if (status == 401) r.detail = "Hakutaku refused this service's HAKUTAKU_SERVER_KEY (401); make a new key and restart";
            }
            return r;
        };
    }
}
