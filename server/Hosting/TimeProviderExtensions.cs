/*!************************************************************************
\file   TimeProviderExtensions.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines UtcNow, a small extension that gives the current
        time from a TimeProvider as a plain UTC DateTime, which is what the
        database columns use.

        Used wherever a service or handler needs the time, so a fake clock
        can be swapped in.
**************************************************************************/
namespace server.Hosting
{
    public static class TimeProviderExtensions
    {
        // "now" as a plain utc DateTime, which is what the db columns and comparisons use
        public static DateTime UtcNow(this TimeProvider time) => time.GetUtcNow().UtcDateTime;
    }
}
