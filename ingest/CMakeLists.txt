# Hakutaku telemetry ingestion service. C++20, built with CMake.
# Dependencies are fetched at configure time and pinned to exact tags:
#   cpp-httplib     - the HTTP server (and the client it uses to call Hakutaku)
#   nlohmann/json   - JSON
#   doctest         - unit tests (only when HAKUTAKU_INGEST_TESTS is on)
cmake_minimum_required(VERSION 3.24)
project(hakutaku_ingest LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Release)
endif()

option(HAKUTAKU_INGEST_TESTS "Build the unit tests" ON)

include(FetchContent)
FetchContent_Declare(httplib
  GIT_REPOSITORY https://github.com/yhirose/cpp-httplib.git
  GIT_TAG v0.18.3
  GIT_SHALLOW TRUE)
FetchContent_Declare(json
  URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz
  URL_HASH SHA256=d6c65aca6b1ed68e7a182f4757257b107ae403032760ed6ef121c9d55e81757d)
FetchContent_MakeAvailable(httplib json)

find_package(Threads REQUIRED)

# everything but main, so the tests link the same code the service runs
add_library(ingest_core
  src/config.cpp
  src/event.cpp
  src/forwarder.cpp
  src/key_check.cpp
  src/log.cpp)
target_include_directories(ingest_core PUBLIC src)
target_link_libraries(ingest_core PUBLIC httplib::httplib nlohmann_json::nlohmann_json Threads::Threads)
# cpp-httplib has a second, hidden body limit of 8 kb for form-encoded requests, and curl's -d (or any
# client that forgets Content-Type) sends that type, so a batch of events got a 413 well under the real
# limit. this makes both limits the same as kMaxBodyBytes in main.cpp
target_compile_definitions(ingest_core PUBLIC CPPHTTPLIB_FORM_URL_ENCODED_PAYLOAD_MAX_LENGTH=4194304)
if(MSVC)
  target_compile_options(ingest_core PUBLIC /W4 /permissive-)
  # cpp-httplib needs Windows 10's socket api
  target_compile_definitions(ingest_core PUBLIC _WIN32_WINNT=0x0A00)
else()
  target_compile_options(ingest_core PUBLIC -Wall -Wextra -Wpedantic)
endif()

add_executable(hakutaku-ingest src/main.cpp)
target_link_libraries(hakutaku-ingest PRIVATE ingest_core)

if(HAKUTAKU_INGEST_TESTS)
  FetchContent_Declare(doctest
    GIT_REPOSITORY https://github.com/doctest/doctest.git
    GIT_TAG v2.4.11
    GIT_SHALLOW TRUE)
  FetchContent_MakeAvailable(doctest)

  enable_testing()
  add_executable(ingest_tests
    tests/main.cpp
    tests/event_tests.cpp
    tests/queue_tests.cpp
    tests/key_check_tests.cpp)
  target_link_libraries(ingest_tests PRIVATE ingest_core doctest::doctest)
  add_test(NAME ingest_tests COMMAND ingest_tests)
endif()
