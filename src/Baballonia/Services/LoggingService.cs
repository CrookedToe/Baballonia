using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Baballonia.Services;

// File-based logger that writes log entries to disk with thread safety
public sealed class LogFileLogger(string categoryName, StreamWriter writer) : ILogger
{
    private static LogLevel? _cachedMinLogLevel;
    private static readonly object LogLevelLock = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= GetMinimumLogLevel();

    // Gets minimum log level from settings with caching for performance
    public static LogLevel GetMinimumLogLevel()
    {
        if (_cachedMinLogLevel.HasValue)
            return _cachedMinLogLevel.Value;

        // Double-checked locking pattern for thread-safe caching
        lock (LogLevelLock)
        {
            if (_cachedMinLogLevel.HasValue)
                return _cachedMinLogLevel.Value;

            try
            {
                var settingsPath = Path.Combine(Utils.PersistentDataDirectory, "ApplicationData", "LocalSettings.json");
                if (File.Exists(settingsPath))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
                    if (document.RootElement.TryGetProperty("AppSettings_LogLevel", out var element) &&
                        Enum.TryParse<LogLevel>(element.GetString(), true, out var level))
                    {
                        _cachedMinLogLevel = level;
                        return level;
                    }
                }
            }
            catch
            {
                // Ignore errors, use default
            }

            _cachedMinLogLevel = LogLevel.Debug;
            return LogLevel.Debug;
        }
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var logEntry = $"[{categoryName}][{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {logLevel}: {message}\n";

        // Thread-safe file writing
        lock (writer)
        {
            try
            {
                writer.Write(logEntry);
                writer.Flush();
            }
            catch
            {
                // Ignore sandboxing issues
            }
        }
    }
}

// UI logger that adds log entries to observable collections for display in the application
public sealed class OutputPageLogger(string categoryName, Dispatcher dispatcher) : ILogger
{
    // Static collections shared across all UI loggers for display in OutputPageView
    public static readonly ObservableCollection<string> FilteredLogs = new();
    public static readonly ObservableCollection<string> AllLogs = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var logEntry = $"[{categoryName}] {logLevel}: {message}";

        // Add to UI collections on dispatcher thread for thread safety
        dispatcher.Post(() =>
        {
            AllLogs.Add(logEntry);
            if (logLevel >= LogLevel.Information)
                FilteredLogs.Add(logEntry);
        }, DispatcherPriority.Background);
    }
}

// Reusable null scope instance to avoid allocations
internal sealed class NullScope : IDisposable
{
    public static readonly NullScope Instance = new();
    private NullScope() { }
    public void Dispose() { }
}

// Provider Classes for Dependency Injection

// Logger provider that creates file-based loggers with automatic log rotation
[ProviderAlias("Debug")]
public sealed class LogFileProvider : ILoggerProvider
{
    private readonly StreamWriter? _writer;
    private readonly ConcurrentDictionary<string, LogFileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxLogs = 10;

    public LogFileProvider()
    {
        try
        {
            if (!Directory.Exists(Utils.UserAccessibleDataDirectory))
                Directory.CreateDirectory(Utils.UserAccessibleDataDirectory);

            CleanupOldLogFiles();

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var logPath = Path.Combine(Utils.UserAccessibleDataDirectory, $"baballonia_desktop.{timestamp}.log");

            var fileStream = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough);
            _writer = new StreamWriter(fileStream) { AutoFlush = true };
        }
        catch
        {
            // If file creation fails, _writer remains null and NullLogger will be used
        }
    }

    // Removes old log files to maintain only the most recent MaxLogs files
    private static void CleanupOldLogFiles()
    {
        try
        {
            var logFiles = Directory.GetFiles(Utils.UserAccessibleDataDirectory, "baballonia_desktop.*.log")
                .Select(file => new FileInfo(file))
                .OrderByDescending(fi => fi.CreationTime)
                .Skip(MaxLogs - 1);

            foreach (var file in logFiles)
            {
                try { file.Delete(); }
                catch { /* Ignore individual file deletion errors */ }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        _writer != null
            ? _loggers.GetOrAdd(categoryName, name => new LogFileLogger(name, _writer))
            : NullLogger.Instance;

    public void Dispose()
    {
        _loggers.Clear();
        _writer?.Dispose();
    }
}

// Logger provider that creates UI-based loggers for displaying logs in the application interface
public sealed class OutputLogProvider(Dispatcher dispatcher) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, OutputPageLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new OutputPageLogger(name, dispatcher));

    public void Dispose() => _loggers.Clear();
}


