// Ported from Sundered Frames' SFXpSaver.cs (Gam300, feature/hakutaku-integration).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HakutakuMirrorSample
{
    // Gets each online player's XP total to Hakutaku without ever blocking the tick.
    //
    //   - It writes the TOTAL, never the amount earned, so a retry can't count anything twice.
    //   - At most one request per player is in flight. A newer total replaces a waiting one.
    //   - A changed total is written at most every 30 s. Flush skips the wait (disconnect, match end).
    //   - A failed write is tried again, with the newest total, until it lands.
    //
    // A player's entry stays until their latest total is stored, so a reconnect can pick up XP that hasn't reached
    // Hakutaku yet instead of reading an older number (TryGetUnsaved).
    //
    // Plain C# with the write and the clock passed in. Call everything from the main thread.
    public sealed class SampleXpSaver
    {
        public const double MinSecondsBetweenWrites = 30;

        sealed class Entry
        {
            public long Latest;
            public bool HasPending;
            public bool InFlight;
            public bool Urgent;
            public double NextWriteAt;
        }

        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        readonly Func<string, long, Task<bool>> write;
        readonly Func<double> now;

        // write stores the total and answers whether it worked. now is a clock in seconds.
        public SampleXpSaver(Func<string, long, Task<bool>> write, Func<double> now)
        {
            this.write = write;
            this.now = now;
        }

        public int UnsavedPlayers => entries.Count;

        // The player's new total. Sent by a later Tick or Flush.
        public void SetTotal(string playerId, long total)
        {
            if (!entries.TryGetValue(playerId, out Entry entry))
            {
                entry = new Entry();
                entries[playerId] = entry;
            }

            entry.Latest = total;
            entry.HasPending = true;
        }

        // The newest total that hasn't been confirmed stored, if there is one
        public bool TryGetUnsaved(string playerId, out long total)
        {
            if (entries.TryGetValue(playerId, out Entry entry))
            {
                total = entry.Latest;
                return true;
            }

            total = 0;
            return false;
        }

        // Call every few seconds
        public void Tick()
        {
            double time = now();
            foreach (string playerId in entries.Keys.ToList())
            {
                Entry entry = entries[playerId];
                if (entry.HasPending && !entry.InFlight && (entry.Urgent || time >= entry.NextWriteAt))
                    Send(playerId, entry);
            }
        }

        // Write now, without the 30 s wait. If a write is already running, this one follows it if it's still needed.
        public void Flush(string playerId)
        {
            if (!entries.TryGetValue(playerId, out Entry entry))
                return;

            entry.Urgent = true;
            if (entry.HasPending && !entry.InFlight)
                Send(playerId, entry);
        }

        public void FlushAll()
        {
            foreach (string playerId in entries.Keys.ToList())
                Flush(playerId);
        }

        async void Send(string playerId, Entry entry)
        {
            long total = entry.Latest;
            bool wasUrgent = entry.Urgent;

            entry.HasPending = false;
            entry.Urgent = false;
            entry.InFlight = true;
            entry.NextWriteAt = now() + MinSecondsBetweenWrites;

            bool stored;
            try
            {
                stored = await write(playerId, total);
            }
            catch (Exception)
            {
                stored = false;
            }

            entry.InFlight = false;

            if (!stored)
            {
                // Latest is already the newest total, so a newer one that arrived meanwhile isn't replaced
                entry.HasPending = true;
                entry.Urgent |= wasUrgent;
            }
            else if (!entry.HasPending)
            {
                entries.Remove(playerId);
            }
        }
    }
}
