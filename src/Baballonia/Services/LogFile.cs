using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Baballonia.Services;

public class LogFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly StreamWriter _file;
    private static readonly Mutex Mutex = new ();

    public LogFileLogger(string categoryName, StreamWriter file)
    {
        _categoryName = categoryName;
        _file = file;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel)
    {
        var minLogLevel = GetMinimumLogLevel();
        return logLevel >= minLogLevel;
    }

    private static LogLevel GetMinimumLogLevel()
    {
        try
        {
            var settingsPath = Path.Combine(Utils.PersistentDataDirectory, "ApplicationData", "LocalSettings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<JsonDocument>(json);
                
                if (settings?.RootElement.TryGetProperty("AppSettings_LogLevel", out var logLevelElement) == true)
                {
                    var logLevelString = logLevelElement.GetString();
                    if (Enum.TryParse<LogLevel>(logLevelString, true, out var logLevel)) 
                    {
                        return logLevel;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors, use default
        }
        
        // Default to Debug level
        return LogLevel.Debug;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;
            
        Mutex.WaitOne(); // Wait for the semaphore to be released
        try
        {
            _file.Write($"[{_categoryName}] {logLevel}: {formatter(state, exception)}\n");
            _file.Flush();
        }
        catch
        {
            // Ignore cus sandboxing causes a lot of issues here
        }
        finally
        {
            Mutex.ReleaseMutex(); // Always release the semaphore
        }
    }
}
