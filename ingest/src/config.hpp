/*!************************************************************************
\file   config.hpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file declares Config, the service's settings, all read from
        environment variables so the compose files can set them:

          HAKUTAKU_URL         where Hakutaku is (http://app:8080 in compose)
          HAKUTAKU_SERVER_KEY  this service's own server key, for its batches
          INGEST_PORT          the port to listen on (9000)
          INGEST_BATCH_SIZE    events per batch to Hakutaku (500, the max)
          INGEST_FLUSH_MS      longest an event waits before a batch goes (1000)
          INGEST_QUEUE_MAX     events held in memory before refusing more (100000)

        Used by main.cpp.
**************************************************************************/
#pragma once
#include <cstddef>
#include <optional>
#include <string>

namespace ingest
{
    struct Config
    {
        std::string hakutakuUrl = "http://localhost:5008";
        std::string serverKey;
        int port = 9000;
        std::size_t batchSize = 500;
        int flushMs = 1000;
        std::size_t queueMax = 100000;

        // nullopt (with the reason in error) when a setting is missing or out of range
        static std::optional<Config> fromEnvironment(std::string& error);
    };
}
