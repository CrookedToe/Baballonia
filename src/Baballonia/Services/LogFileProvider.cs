using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Baballonia.Services;

[ProviderAlias("Debug")]
public class LogFileProvider : ILoggerProvider
{
    private readonly StreamWriter? _writer;

    public LogFileProvider()
    {
        try
        {
            if (!Directory.Exists(Utils.UserAccessibleDataDirectory)) // Eat my ass windows
                Directory.CreateDirectory(Utils.UserAccessibleDataDirectory);

            // Rotate log files: latest.log -> old.log -> older.log
            var latestLogPath = Path.Combine(Utils.UserAccessibleDataDirectory, "latest.log");
            var oldLogPath = Path.Combine(Utils.UserAccessibleDataDirectory, "old.log");
            var olderLogPath = Path.Combine(Utils.UserAccessibleDataDirectory, "older.log");

            if (File.Exists(olderLogPath))
            {
                File.Delete(olderLogPath);
            }

            if (File.Exists(oldLogPath))
            {
                File.Move(oldLogPath, olderLogPath);
            }

            if (File.Exists(latestLogPath))
            {
                File.Move(latestLogPath, oldLogPath);
            }

            var file = new FileStream(latestLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096,
                FileOptions.WriteThrough);
            _writer = new StreamWriter(file);
        }
        catch
        {

        }
    }

    private readonly ConcurrentDictionary<string, LogFileLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    public ILogger CreateLogger(string categoryName)
    {
        if (_writer != null)
        {
            return _loggers.GetOrAdd(categoryName, name => new LogFileLogger(name, _writer));
        }

        return NullLogger.Instance;
    }

    public void Dispose()
    {
        _loggers.Clear();
        _writer?.Dispose();
    }
}
