/*!************************************************************************
\file   log.cpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines the logger declared in log.hpp. A mutex keeps
        lines from two threads from interleaving.
**************************************************************************/
#include "log.hpp"
#include "event.hpp"

#include <iostream>
#include <mutex>

namespace ingest::log
{
    namespace
    {
        std::mutex lock;

        void write(std::string_view level, std::string_view message)
        {
            const auto when = iso8601(std::chrono::system_clock::now());
            std::lock_guard guard(lock);
            std::cerr << when << ' ' << level << ' ' << message << '\n';
        }
    }

    void info(std::string_view message) { write("info ", message); }
    void warn(std::string_view message) { write("warn ", message); }
    void error(std::string_view message) { write("error", message); }
}
