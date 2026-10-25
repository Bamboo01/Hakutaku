namespace server.Hosting
{
    public static class TimeProviderExtensions
    {
        // "now" as a plain utc DateTime, which is what the db columns and comparisons use
        public static DateTime UtcNow(this TimeProvider time) => time.GetUtcNow().UtcDateTime;
    }
}
