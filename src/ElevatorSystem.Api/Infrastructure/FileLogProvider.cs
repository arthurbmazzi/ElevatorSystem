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
    public string DirectoryPath { get; }

    public FileLogProvider(IHostEnvironment environment, IConfiguration configuration)
    {
        DirectoryPath = Path.GetFullPath(configuration["FileLog:Directory"] ?? "logs", environment.ContentRootPath);
        _maxBytes = configuration.GetValue<long?>("FileLog:MaxFileBytes") ?? 10 * 1024 * 1024;
        _retainedFiles = configuration.GetValue<int?>("FileLog:RetainedFiles") ?? 7;
        if (_maxBytes <= 0 || _retainedFiles <= 0) throw new ArgumentException("Invalid file log limits.");
        Directory.CreateDirectory(DirectoryPath);
    }

    public void Write(string message)
    {
        lock (_sync)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (_currentFile is null || today != _date || new FileInfo(_currentFile).Length >= _maxBytes)
            {
                _date = today;
                _currentFile = Path.Combine(DirectoryPath, $"elevators-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.txt");
                File.WriteAllText(_currentFile, "", Encoding.UTF8);
                foreach (var old in new DirectoryInfo(DirectoryPath).GetFiles("elevators-*.txt")
                    .OrderByDescending(f => f.LastWriteTimeUtc).Where(f => f.FullName != _currentFile).Skip(_retainedFiles - 1))
                    old.Delete();
            }
            File.AppendAllText(_currentFile, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}", Encoding.UTF8);
        }
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
