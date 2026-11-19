/*!************************************************************************
\file   config.cpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines Config::fromEnvironment. A number that doesn't
        parse or is out of range is an error rather than a silent default,
        so a typo in a compose file stops the service at startup instead of
        running it with settings nobody chose.
**************************************************************************/
#include "config.hpp"

#include <charconv>
#include <cstdlib>
#include <string_view>

namespace ingest
{
    namespace
    {
        std::optional<std::string> env(const char* name)
        {
            const char* value = std::getenv(name);
            if (value == nullptr || *value == '\0') return std::nullopt;
            return std::string(value);
        }

        // reads a whole-number setting into `out` if it's set, in [min, max]. false on a bad value
        template <typename T>
        bool readNumber(const char* name, T& out, T min, T max, std::string& error)
        {
            const auto raw = env(name);
            if (!raw) return true;
            T value{};
            const auto [end, problem] = std::from_chars(raw->data(), raw->data() + raw->size(), value);
            if (problem != std::errc{} || end != raw->data() + raw->size() || value < min || value > max)
            {
                error = std::string(name) + " must be a whole number from " + std::to_string(min) + " to " + std::to_string(max);
                return false;
            }
            out = value;
            return true;
        }
    }

    std::optional<Config> Config::fromEnvironment(std::string& error)
    {
        Config c;
        if (const auto url = env("HAKUTAKU_URL")) c.hakutakuUrl = *url;
        while (!c.hakutakuUrl.empty() && c.hakutakuUrl.back() == '/') c.hakutakuUrl.pop_back();

        const auto key = env("HAKUTAKU_SERVER_KEY");
        if (!key)
        {
            error = "HAKUTAKU_SERVER_KEY is not set: make a server key on the admin UI's Server keys page";
            return std::nullopt;
        }
        c.serverKey = *key;

        // the batch size can't exceed what Hakutaku takes in one call (Limits.EventBatchMaxEvents)
        if (!readNumber<int>("INGEST_PORT", c.port, 1, 65535, error)) return std::nullopt;
        if (!readNumber<std::size_t>("INGEST_BATCH_SIZE", c.batchSize, 1, 500, error)) return std::nullopt;
        if (!readNumber<int>("INGEST_FLUSH_MS", c.flushMs, 10, 60000, error)) return std::nullopt;
        if (!readNumber<std::size_t>("INGEST_QUEUE_MAX", c.queueMax, 1, 10'000'000, error)) return std::nullopt;
        return c;
    }
}
