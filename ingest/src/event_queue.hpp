/*!************************************************************************
\file   event_queue.hpp
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines EventQueue, the bounded, thread-safe queue
        between the HTTP threads (which push what game servers send) and
        the forwarder thread (which takes batches for Hakutaku).

        It is bounded on purpose. If Hakutaku is down, events pile up here
        until the queue is full, and then pushes are refused, so the game
        servers are told to retry later (a 503) instead of this service
        growing until it runs out of memory. A push is all or nothing, so a
        game server retrying a refused request never sends half of it twice.

        A header-only template so the tests can use it with any type. Used
        by main.cpp and forwarder.cpp.
**************************************************************************/
#pragma once
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <deque>
#include <mutex>
#include <vector>

namespace ingest
{
    template <typename T>
    class EventQueue
    {
    public:
        explicit EventQueue(std::size_t capacity) : capacity_(capacity) {}

        // adds every item, or none if they don't all fit (or the queue is closed). false means refused
        bool pushAll(std::vector<T>&& items)
        {
            {
                std::lock_guard guard(lock_);
                if (closed_ || items.size() > capacity_ - items_.size()) return false;
                for (auto& item : items) items_.push_back(std::move(item));
            }
            ready_.notify_one();
            return true;
        }

        // waits for a batch: returns as soon as `max` items are here, or once `wait` has passed since this call
        // with whatever has arrived. empty only if nothing came in time, or the queue is closed and drained
        std::vector<T> popBatch(std::size_t max, std::chrono::milliseconds wait)
        {
            std::unique_lock guard(lock_);
            const auto deadline = std::chrono::steady_clock::now() + wait;
            ready_.wait_until(guard, deadline, [&] { return closed_ || items_.size() >= max; });

            const auto n = std::min(max, items_.size());
            std::vector<T> batch;
            batch.reserve(n);
            for (std::size_t i = 0; i < n; ++i)
            {
                batch.push_back(std::move(items_.front()));
                items_.pop_front();
            }
            return batch;
        }

        // stops new pushes and wakes the forwarder so it can drain what's left
        void close()
        {
            {
                std::lock_guard guard(lock_);
                closed_ = true;
            }
            ready_.notify_all();
        }

        std::size_t size() const
        {
            std::lock_guard guard(lock_);
            return items_.size();
        }

        std::size_t capacity() const { return capacity_; }

    private:
        const std::size_t capacity_;
        mutable std::mutex lock_;
        std::condition_variable ready_;
        std::deque<T> items_;
        bool closed_ = false;
    };
}
