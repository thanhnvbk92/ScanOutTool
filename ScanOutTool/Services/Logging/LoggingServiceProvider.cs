using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace ScanOutTool.Services.Logging
{
    /// <summary>
    /// Custom logger provider ?? bridge ILogger v?i ILoggingService
    /// </summary>
    public class LoggingServiceProvider : ILoggerProvider
    {
        private readonly ILoggingService _loggingService;
        private readonly ConcurrentDictionary<string, LoggingServiceLogger> _loggers = new();

        public LoggingServiceProvider(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new LoggingServiceLogger(name, _loggingService));
        }

        public void Dispose()
        {
            _loggers.Clear();
        }
    }

    /// <summary>
    /// Custom logger implementation chuy?n ILogger calls sang ILoggingService
    /// </summary>
    internal class LoggingServiceLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ILoggingService _loggingService;

        public LoggingServiceLogger(string categoryName, ILoggingService loggingService)
        {
            _categoryName = categoryName;
            _loggingService = loggingService;
        }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            
            // Có th? thêm category name vào message n?u c?n
            var fullMessage = $"[{_categoryName}] {message}";

            switch (logLevel)
            {
                case LogLevel.Information:
                case LogLevel.Debug:
                case LogLevel.Trace:
                    _loggingService.LogInformation(fullMessage);
                    break;
                case LogLevel.Warning:
                    _loggingService.LogWarning(fullMessage);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    _loggingService.LogError(fullMessage);
                    if (exception != null)
                    {
                        _loggingService.LogError($"Exception: {exception}");
                    }
                    break;
            }
        }

        private class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}