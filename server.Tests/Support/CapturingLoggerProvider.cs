/*!************************************************************************
\file   CapturingLoggerProvider.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   4th November 2026
\brief  This file defines CapturingLoggerProvider, a logger for the
        integration tests that keeps every log line the server writes, so a
        test can check that something was logged and under which category.

        Put in place by HakutakuFactory, next to the normal loggers.
**************************************************************************/
using Microsoft.Extensions.Logging;

namespace server.Tests.Support
{
    public record LogLine(string Category, LogLevel Level, string Message);

    public sealed class CapturingLoggerProvider : ILoggerProvider
    {
        readonly List<LogLine> lines = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public List<LogLine> In(string category)
        {
            lock (lines) return lines.Where(l => l.Category == category).ToList();
        }

        public void Dispose() { }

        sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.lines) owner.lines.Add(new LogLine(category, logLevel, formatter(state, exception)));
            }
        }
    }
}
