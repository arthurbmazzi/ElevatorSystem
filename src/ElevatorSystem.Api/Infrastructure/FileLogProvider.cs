using System.Text;

namespace ElevatorSystem.Api.Infrastructure;

/// <summary>Small synchronous file adapter. Callers must never hold a fleet lock during writes.</summary>
public sealed class FileLogProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly long _maxBytes;
    private readonly int _retainedFiles;
    private string? _currentFile;
    private DateOnly _date;
    private string? _lastWriteError;
    private string? _retentionWarning;
    public string DirectoryPath { get; }

    public FileLogProvider(IHostEnvironment environment, IConfiguration configuration)
    {
        DirectoryPath = Path.GetFullPath(configuration["FileLog:Directory"] ?? "logs", environment.ContentRootPath);
        _maxBytes = configuration.GetValue<long?>("FileLog:MaxFileBytes") ?? 10 * 1024 * 1024;
        _retainedFiles = configuration.GetValue<int?>("FileLog:RetainedFiles") ?? 7;
        if (_maxBytes <= 0 || _retainedFiles <= 0) throw new ArgumentException("Invalid file log limits.");

    }

    public FileLogStatus GetStatus()
    {
        lock (_sync) return new(DirectoryPath, _currentFile, _lastWriteError,
            _retentionWarning, _maxBytes, _retainedFiles);
    }

    public void Write(string message)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                if (_currentFile is null || today != _date || !File.Exists(_currentFile)
                    || new FileInfo(_currentFile).Length >= _maxBytes)
                {
                    // Publish the new path only after creation succeeds, allowing a later retry.
                    var nextFile = Path.Combine(DirectoryPath,
                        $"elevators-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.txt");
                    File.WriteAllText(nextFile, "", Encoding.UTF8);
                    _currentFile = nextFile;
                    _date = today;
                    RemoveOldFiles();
                }
                File.AppendAllText(_currentFile, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}", Encoding.UTF8);
                _lastWriteError = null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _lastWriteError = error.Message;
                throw new IOException("Cannot write the TXT log. Check GET /logs/status and restore access to the log directory.", error);
            }
        }
    }

    private void RemoveOldFiles()
    {
        _retentionWarning = null;
        try
        {
            var oldFiles = new DirectoryInfo(DirectoryPath).GetFiles("elevators-*.txt")
                .Where(f => f.FullName != _currentFile)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(_retainedFiles - 1);
            foreach (var old in oldFiles)
            {
                try { old.Delete(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    _retentionWarning = error.Message;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _retentionWarning = error.Message;
        }
        // Cleanup is best effort: an old file in use must not stop writing new events.
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }

    private sealed class FileLogger(FileLogProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            try { provider.Write($"[{level}] {category}: {formatter(state, exception)} {exception}"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Do not let diagnostic logging prevent the API from returning its error response.
                Console.Error.WriteLine($"File logging failed: {error.Message}");
            }
        }
    }
}

public sealed record FileLogStatus(string Directory, string? CurrentFile, string? LastWriteError,
    string? RetentionWarning, long MaxFileBytes, int RetainedFiles);
