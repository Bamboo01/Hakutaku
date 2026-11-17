/*!************************************************************************
\file   event.hpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file declares Event, one telemetry event on its way to
        Hakutaku, and readEvent, which checks what a game server sent and
        turns it into one.

        The checks mirror the server's EventRules.cs (a player id, an event
        type of 1-100 characters with no control characters, data that is
        JSON of at most 10,000 bytes), so a bad event is refused here, at
        once, with a reason the game server can read. Hakutaku still has
        the final word: it checks everything again, and it alone knows
        whether the player exists.

        Used by main.cpp (the /ingest/events route) and forwarder.cpp.
**************************************************************************/
#pragma once
#include <chrono>
#include <optional>
#include <string>

#include <nlohmann/json.hpp>

namespace ingest
{
    inline constexpr std::size_t kEventTypeMaxLength = 100;
    inline constexpr std::size_t kEventDataMaxBytes = 10'000;

    struct Event
    {
        std::string playerId;
        std::string eventType;
        // json, as text: what Hakutaku stores
        std::string data;
        // when this service received it, sent to Hakutaku as occurredAt
        std::string occurredAt;
    };

    // checks one event from a game server. on success fills `out`, stamped with `receivedAt`; on a problem
    // returns the reason. `data` may be any json value (an object, say) or a string holding json
    std::optional<std::string> readEvent(const nlohmann::json& in, std::chrono::system_clock::time_point receivedAt, Event& out);

    // the shape Hakutaku's batch route takes for one event
    nlohmann::json toJson(const Event& e);

    // 2026-11-17T09:30:15.123Z
    std::string iso8601(std::chrono::system_clock::time_point t);

    // 8-4-4-4-12 hex digits, any case
    bool isGuid(const std::string& s);

    // how many UTF-16 code units the text is, which is what C#'s string.Length counts; nullopt if it isn't
    // valid UTF-8
    std::optional<std::size_t> utf16Length(const std::string& utf8);

    // true if the text holds a Unicode control character (U+0000-001F or U+007F-009F), like C#'s char.IsControl
    bool hasControlCharacter(const std::string& utf8);
}
