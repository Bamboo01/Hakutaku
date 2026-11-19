/*!************************************************************************
\file   log.hpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file declares the service's logger: one line per message on
        stderr, with a UTC time and a level, safe to call from any thread.
        Docker collects stderr, so `docker compose logs ingest` shows it.

        Used everywhere in ingest/src.
**************************************************************************/
#pragma once
#include <string_view>

namespace ingest::log
{
    void info(std::string_view message);
    void warn(std::string_view message);
    void error(std::string_view message);
}
