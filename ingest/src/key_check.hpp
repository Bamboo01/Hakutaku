/*!************************************************************************
\file   key_check.hpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file declares KeyCheck, which decides whether the X-Server-Key
        a game server sent is a real, live server key. Only Hakutaku knows,
        so KeyCheck asks it (GET /api/server/ping with that key) and
        remembers the answer for a while, so a busy game server costs one
        ping a minute rather than one per request.

        A good key is trusted for 60 seconds, so a key revoked on the admin
        UI stops working here within a minute. A bad key is remembered for
        10 seconds, and at most 1,000 of them, so someone sending random
        keys can neither flood Hakutaku with pings nor fill this service's
        memory. If Hakutaku can't be reached, the answer is "unknown" and
        nothing is cached: the request is refused with a 503, never let in.

        Used by main.cpp. The tests give it a fake ping and clock.
**************************************************************************/
#pragma once
#include <chrono>
#include <functional>
#include <mutex>
#include <string>
#include <unordered_map>

namespace ingest
{
    enum class KeyVerdict
    {
        Valid,
        Invalid,
        // Hakutaku didn't answer: can't tell
        Unknown,
    };

    class KeyCheck
    {
    public:
        using Ping = std::function<KeyVerdict(const std::string& key)>;
        using Clock = std::function<std::chrono::steady_clock::time_point()>;

        static constexpr std::chrono::seconds kValidFor{60};
        static constexpr std::chrono::seconds kInvalidFor{10};
        static constexpr std::size_t kMaxRemembered = 1000;

        explicit KeyCheck(Ping ping, Clock clock = std::chrono::steady_clock::now);

        KeyVerdict check(const std::string& key);

        // the real ping: GET {hakutakuUrl}/api/server/ping with the key in X-Server-Key
        static Ping httpPing(const std::string& hakutakuUrl);

    private:
        struct Remembered
        {
            bool valid;
            std::chrono::steady_clock::time_point until;
        };

        Ping ping_;
        Clock clock_;
        std::mutex lock_;
        std::unordered_map<std::string, Remembered> remembered_;
    };
}
