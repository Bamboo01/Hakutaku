/*!************************************************************************
\file   key_check.cpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines KeyCheck, declared in key_check.hpp. The ping
        runs outside the lock, so one slow call to Hakutaku never holds up
        the requests whose keys are already remembered.
**************************************************************************/
#include "key_check.hpp"

#include <httplib.h>

namespace ingest
{
    KeyCheck::KeyCheck(Ping ping, Clock clock) : ping_(std::move(ping)), clock_(std::move(clock)) {}

    KeyVerdict KeyCheck::check(const std::string& key)
    {
        // an empty or absurd key can't be a server key (they're 43 characters); no need to ask
        if (key.empty() || key.size() > 512) return KeyVerdict::Invalid;

        const auto now = clock_();
        {
            std::lock_guard guard(lock_);
            const auto found = remembered_.find(key);
            if (found != remembered_.end() && found->second.until > now)
                return found->second.valid ? KeyVerdict::Valid : KeyVerdict::Invalid;
        }

        const auto verdict = ping_(key);
        if (verdict == KeyVerdict::Unknown) return verdict;

        std::lock_guard guard(lock_);
        if (remembered_.size() >= kMaxRemembered)
        {
            // drop whatever has run out; if that's not enough, start over. simple, and bounded
            std::erase_if(remembered_, [&](const auto& entry) { return entry.second.until <= now; });
            if (remembered_.size() >= kMaxRemembered) remembered_.clear();
        }
        const bool valid = verdict == KeyVerdict::Valid;
        remembered_[key] = {valid, now + (valid ? kValidFor : kInvalidFor)};
        return verdict;
    }

    KeyCheck::Ping KeyCheck::httpPing(const std::string& hakutakuUrl)
    {
        return [hakutakuUrl](const std::string& key)
        {
            httplib::Client client(hakutakuUrl);
            client.set_connection_timeout(std::chrono::seconds(3));
            client.set_read_timeout(std::chrono::seconds(5));
            const auto reply = client.Get("/api/server/ping", {{"X-Server-Key", key}});
            if (!reply) return KeyVerdict::Unknown;
            if (reply->status == 200) return KeyVerdict::Valid;
            if (reply->status == 401) return KeyVerdict::Invalid;
            // anything else (a 5xx, a 429) says nothing about the key
            return KeyVerdict::Unknown;
        };
    }
}
