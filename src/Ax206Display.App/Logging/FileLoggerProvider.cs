using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Ax206Display.App.Logging;

/// <summary>
/// Minimal append-only file logger so a tray app with no console still leaves
/// a diagnostic trail. Writes Information and above to a single file under
/// the per-user local app data folder, truncating it when it grows past
/// <see cref="MaxLogFileBytes"/> so it can never eat the disk.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxLogFileBytes = 5 * 1024 * 1024;

    private readonly StreamWriter _writer;
    private readonly object _sync = new();

    public FileLoggerProvider(string logFilePath)
    {
        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(logFilePath) && new FileInfo(logFilePath).Length > MaxLogFileBytes)
        {
            File.Delete(logFilePath);
        }

        _writer = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
    }

    public static string GetDefaultLogFilePath()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDirectory, "Ax206Display", "logs", "app.log");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_sync)
        {
            _writer.Dispose();
        }
    }

    private void WriteLine(string line)
    {
        lock (_sync)
        {
            _writer.WriteLine(line);
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _categoryName;

        public FileLogger(FileLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{logLevel}] {_categoryName}: {formatter(state, exception)}");

            if (exception is not null)
            {
                line = line + Environment.NewLine + exception;
            }

            _provider.WriteLine(line);
        }
    }
}
