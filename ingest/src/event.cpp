/*!************************************************************************
\file   event.cpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines readEvent and its helpers, declared in
        event.hpp. The messages match the server's ErrorMessages.cs, so a
        game server sees the same reason from either.
**************************************************************************/
#include "event.hpp"

#include <cctype>
#include <cstdio>
#include <ctime>

namespace ingest
{
    namespace
    {
        const std::string kTypeInvalid = "playerId is required and eventType must be 1-100 characters";
        const std::string kDataInvalid = "data must be valid json, up to 10000 bytes";

        // trims spaces, tabs and line breaks from both ends (C# Trim also trims other Unicode spaces; the
        // server trims again, so the worst case is a rare event refused there instead of here)
        std::string trim(const std::string& s)
        {
            const auto first = s.find_first_not_of(" \t\r\n\f\v");
            if (first == std::string::npos) return "";
            const auto last = s.find_last_not_of(" \t\r\n\f\v");
            return s.substr(first, last - first + 1);
        }
    }

    bool isGuid(const std::string& s)
    {
        if (s.size() != 36) return false;
        for (std::size_t i = 0; i < s.size(); ++i)
        {
            const bool dash = i == 8 || i == 13 || i == 18 || i == 23;
            if (dash ? s[i] != '-' : !std::isxdigit(static_cast<unsigned char>(s[i]))) return false;
        }
        return true;
    }

    std::optional<std::size_t> utf16Length(const std::string& utf8)
    {
        std::size_t units = 0;
        for (std::size_t i = 0; i < utf8.size();)
        {
            const auto b = static_cast<unsigned char>(utf8[i]);
            std::size_t n;
            if (b < 0x80) n = 1;
            else if ((b & 0xE0) == 0xC0) n = 2;
            else if ((b & 0xF0) == 0xE0) n = 3;
            else if ((b & 0xF8) == 0xF0) n = 4;
            else return std::nullopt;
            if (i + n > utf8.size()) return std::nullopt;
            for (std::size_t k = 1; k < n; ++k)
                if ((static_cast<unsigned char>(utf8[i + k]) & 0xC0) != 0x80) return std::nullopt;
            // a code point past U+FFFF is two UTF-16 units (a surrogate pair)
            units += n == 4 ? 2 : 1;
            i += n;
        }
        return units;
    }

    bool hasControlCharacter(const std::string& utf8)
    {
        for (std::size_t i = 0; i < utf8.size(); ++i)
        {
            const auto b = static_cast<unsigned char>(utf8[i]);
            if (b < 0x20 || b == 0x7F) return true;
            // U+0080-009F is C2 80 - C2 9F in UTF-8
            if (b == 0xC2 && i + 1 < utf8.size())
            {
                const auto next = static_cast<unsigned char>(utf8[i + 1]);
                if (next >= 0x80 && next <= 0x9F) return true;
            }
        }
        return false;
    }

    std::string iso8601(std::chrono::system_clock::time_point t)
    {
        using namespace std::chrono;
        const auto ms = duration_cast<milliseconds>(t.time_since_epoch()).count();
        const std::time_t seconds = static_cast<std::time_t>(ms / 1000);
        std::tm utc{};
#ifdef _WIN32
        gmtime_s(&utc, &seconds);
#else
        gmtime_r(&seconds, &utc);
#endif
        // 24 characters for any real date; room to spare so the compiler can see nothing is ever cut off
        char text[64];
        std::snprintf(text, sizeof text, "%04d-%02d-%02dT%02d:%02d:%02d.%03dZ", utc.tm_year + 1900, utc.tm_mon + 1, utc.tm_mday,
                      utc.tm_hour, utc.tm_min, utc.tm_sec, static_cast<int>(ms % 1000));
        return text;
    }

    std::optional<std::string> readEvent(const nlohmann::json& in, std::chrono::system_clock::time_point receivedAt, Event& out)
    {
        if (!in.is_object()) return kTypeInvalid;

        const auto player = in.find("playerId");
        if (player == in.end() || !player->is_string() || !isGuid(player->get<std::string>())) return kTypeInvalid;
        // all zeroes isn't a player (the server refuses Guid.Empty too)
        if (player->get<std::string>() == "00000000-0000-0000-0000-000000000000") return kTypeInvalid;

        const auto type = in.find("eventType");
        if (type == in.end() || !type->is_string()) return kTypeInvalid;
        auto eventType = trim(type->get<std::string>());
        const auto typeLength = utf16Length(eventType);
        if (!typeLength || *typeLength == 0 || *typeLength > kEventTypeMaxLength || hasControlCharacter(eventType))
            return kTypeInvalid;

        // data may come as json (an object, a list, a number...) or as a string that holds json. either way
        // what's stored is json text
        const auto data = in.find("data");
        if (data == in.end() || data->is_null()) return kDataInvalid;
        std::string text;
        if (data->is_string())
        {
            text = data->get<std::string>();
            if (trim(text).empty() || !nlohmann::json::accept(text)) return kDataInvalid;
        }
        else
        {
            text = data->dump();
        }
        if (text.size() > kEventDataMaxBytes) return kDataInvalid;

        out.playerId = player->get<std::string>();
        out.eventType = std::move(eventType);
        out.data = std::move(text);
        out.occurredAt = iso8601(receivedAt);
        return std::nullopt;
    }

    nlohmann::json toJson(const Event& e)
    {
        return {{"playerId", e.playerId}, {"eventType", e.eventType}, {"data", e.data}, {"occurredAt", e.occurredAt}};
    }
}
