using ElevatorSystem.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

public sealed class FileLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElevatorLogTests-" + Guid.NewGuid());

    private FileLogProvider Create(long maxBytes = 10000, int retained = 7) => new(
        new TestEnvironment { ContentRootPath = _directory },
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileLog:Directory"] = "logs",
            ["FileLog:MaxFileBytes"] = maxBytes.ToString(),
            ["FileLog:RetainedFiles"] = retained.ToString()
        }).Build());

    [Fact]
    public void DeletedActiveFileIsRecreatedOnNextWrite()
    {
        using var log = Create();
        log.Write("first");
        File.Delete(log.GetStatus().CurrentFile!);
        log.Write("second");
        Assert.Contains("second", File.ReadAllText(log.GetStatus().CurrentFile!));
        Assert.Null(log.GetStatus().LastWriteError);
    }

    [Fact]
    public void WriteFailureIsReportedAndCanRecover()
    {
        Directory.CreateDirectory(_directory);
        var blockedPath = Path.Combine(_directory, "logs");
        File.WriteAllText(blockedPath, "A file blocks directory creation.");
        using var log = Create();
        Assert.Throws<IOException>(() => log.Write("first"));
        Assert.NotNull(log.GetStatus().LastWriteError);
        Assert.Null(log.GetStatus().CurrentFile);
        File.Delete(blockedPath);
        log.Write("recovered");
        Assert.Null(log.GetStatus().LastWriteError);
        Assert.Contains("recovered", File.ReadAllText(log.GetStatus().CurrentFile!));
    }

    [Fact]
    public void RotationKeepsConfiguredNumberOfFiles()
    {
        using var log = Create(maxBytes: 1, retained: 2);
        for (int i = 0; i < 5; i++) log.Write($"event {i}");
        Assert.Equal(2, Directory.GetFiles(log.DirectoryPath, "*.txt").Length);
        Assert.Contains("event 4", File.ReadAllText(log.GetStatus().CurrentFile!));
    }

    [Fact]
    public void LockedOldFileDoesNotStopNewWrites()
    {
        // Windows prevents deletion when an open handle does not allow FileShare.Delete.
        if (!OperatingSystem.IsWindows()) return;
        using var log = Create(maxBytes: 1, retained: 1);
        log.Write("first");
        using var heldFile = new FileStream(log.GetStatus().CurrentFile!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        log.Write("second");
        Assert.NotNull(log.GetStatus().RetentionWarning);
        Assert.Null(log.GetStatus().LastWriteError);
        Assert.Contains("second", File.ReadAllText(log.GetStatus().CurrentFile!));
    }

    public void Dispose()
    {
        // Only remove the unique temporary directory created by this test instance.
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "FileLogTests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
