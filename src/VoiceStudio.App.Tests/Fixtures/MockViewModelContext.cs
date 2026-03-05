using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Fixtures
{
    /// <summary>
    /// Mock implementation of IViewModelContext for testing ViewModels.
    /// Provides a test-friendly logger and mock dispatcher.
    /// </summary>
    public class MockViewModelContext : IViewModelContext
    {
        private readonly ILogger _logger;
        private readonly IDispatcher _dispatcher;

        public MockViewModelContext()
            : this(NullLogger.Instance, new MockDispatcherQueue())
        {
        }

        public MockViewModelContext(ILogger logger, IDispatcher dispatcher)
        {
            _logger = logger;
            _dispatcher = dispatcher;
        }

        public ILogger Logger => _logger;

        public IDispatcher Dispatcher => _dispatcher;

        /// <summary>
        /// Creates a MockViewModelContext with a test logger that records log entries.
        /// </summary>
        /// <param name="testLogger">The test logger that will capture log entries.</param>
        /// <returns>A new MockViewModelContext with the test logger.</returns>
        public static MockViewModelContext WithTestLogger(out TestLogger testLogger)
        {
            testLogger = new TestLogger();
            return new MockViewModelContext(testLogger, new MockDispatcherQueue());
        }
    }

    /// <summary>
    /// Mock dispatcher queue that executes actions synchronously for testing.
    /// Implements IDispatcher for ViewModel testability.
    /// </summary>
    public class MockDispatcherQueue : IDispatcher
    {
        /// <summary>
        /// Synchronously executes the action (mimics dispatcher behavior in tests).
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <returns>True if the action was executed.</returns>
        public bool TryEnqueue(Action action)
        {
            action?.Invoke();
            return true;
        }

        /// <summary>
        /// Creates a mock timer for testing.
        /// </summary>
        public IDispatcherTimer CreateTimer()
        {
            return new MockDispatcherQueueTimer();
        }

        /// <summary>
        /// Synchronously executes the action with priority (ignored in tests).
        /// </summary>
        /// <param name="priority">The priority (ignored).</param>
        /// <param name="action">The action to execute.</param>
        /// <returns>True if the action was executed.</returns>
        public bool TryEnqueue(int priority, Action action)
        {
            _ = priority; // Unused in test implementation
            action?.Invoke();
            return true;
        }
    }

    /// <summary>
    /// Mock timer for testing ViewModels that use IDispatcherTimer.
    /// </summary>
    public class MockDispatcherQueueTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public event EventHandler<object>? Tick;

        public void Start() { }
        public void Stop() { }
    }

    /// <summary>
    /// Test logger that records log entries for verification in tests.
    /// </summary>
    public class TestLogger : ILogger
    {
        private readonly List<LogEntry> _entries = new();
        private readonly object _lock = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_lock)
                {
                    return _entries.ToList().AsReadOnly();
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                LogLevel = logLevel,
                EventId = eventId,
                Message = formatter(state, exception),
                Exception = exception
            };

            lock (_lock)
            {
                _entries.Add(entry);
            }
        }

        public bool HasErrors => Entries.Any(e => e.LogLevel >= LogLevel.Error);
        public bool HasWarnings => Entries.Any(e => e.LogLevel >= LogLevel.Warning);

        public IEnumerable<LogEntry> GetEntriesAtLevel(LogLevel level)
        {
            return Entries.Where(e => e.LogLevel == level);
        }
    }

    /// <summary>
    /// Represents a single log entry for test verification.
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel LogLevel { get; set; }
        public EventId EventId { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }
}
