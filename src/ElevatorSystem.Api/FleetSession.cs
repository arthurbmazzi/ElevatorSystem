using ElevatorSystem.Api.Infrastructure;

namespace ElevatorSystem.Api;

/// <summary>One shared fleet; flushes events after each command or background step.</summary>
public sealed class FleetSession : IDisposable
{
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly EventBuffer _buffer = new();
    private readonly FileLogProvider _log;
    private readonly EnterpriseElevatorSystem _system;

    public FleetSession(FileLogProvider log, IConfiguration configuration)
    {
        _log = log;
        var timeout = TimeSpan.FromSeconds(configuration.GetValue<int?>("Elevators:StuckTimeoutSeconds") ?? 30);
        _system = new EnterpriseElevatorSystem(EnterpriseFleetFactory.CreateDefault(),
            stuckTimeout: timeout, eventSink: _buffer);
    }

    public async Task<T> ExecuteAsync<T>(Func<EnterpriseElevatorSystem, T> command, CancellationToken cancellationToken)
    {
        await _commands.WaitAsync(cancellationToken);
        try
        {
            // A failed write prevents new changes until the buffered events can be flushed.
            FlushEvents();
            try { return command(_system); }
            finally { FlushEvents(); }
        }
        finally { _commands.Release(); }
    }

    public static FleetOverview Status(EnterpriseElevatorSystem system) => new(system.GetAnalytics(), system.Elevators);

    private void FlushEvents()
    {
        while (_buffer.Entries.TryPeek(out var entry))
        {
            _log.Write($"tick={entry.Tick} event={entry.Name} car={entry.ElevatorId} trip={entry.RequestId} " +
                $"floor={entry.Floor} state={entry.State} mode={entry.Mode}");
            _buffer.Entries.Dequeue();
        }
    }

    public void Dispose() { _commands.Dispose(); }

    // Owned by the session's command gate. Drained per operation, independent of the UI history limit.
    private sealed class EventBuffer : IEnterpriseEventSink
    {
        public Queue<EnterpriseEvent> Entries { get; } = new();
        public void Record(EnterpriseEvent entry) => Entries.Enqueue(entry);
    }
}
