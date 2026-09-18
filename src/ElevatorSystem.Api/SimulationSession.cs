using ElevatorSystem.Api.Infrastructure;

namespace ElevatorSystem.Api;

/// <summary>One shared demo session; flushes every command/tick before accepting the next mutation.</summary>
public sealed class SimulationSession : IDisposable
{
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly SemaphoreSlim _processors = new(1, 1);
    private readonly EventBuffer _buffer = new();
    private readonly FileLogProvider _log;
    private readonly TimeSpan _timeout;
    private EnterpriseElevatorSystem _system;

    public SimulationSession(FileLogProvider log, IConfiguration configuration)
    {
        _log = log;
        _timeout = TimeSpan.FromSeconds(configuration.GetValue<int?>("Simulation:StuckTimeoutSeconds") ?? 300);
        _system = CreateSystem();
    }

    private EnterpriseElevatorSystem CreateSystem() => new(EnterpriseFleetFactory.CreateDefault(),
        stuckTimeout: _timeout, eventSink: _buffer);

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

    public async Task<SimulationStatus> ProcessAsync(CancellationToken cancellationToken)
    {
        await _processors.WaitAsync(cancellationToken);
        try
        {
            while (await ExecuteAsync(s => s.ProcessTick(), cancellationToken))
                await Task.Yield();
            return await ExecuteAsync(Status, cancellationToken);
        }
        finally { _processors.Release(); }
    }

    public async Task<SimulationStatus> ResetAsync(CancellationToken cancellationToken)
    {
        // Do not let a running processor carry on against a newly reset fleet.
        await _processors.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteAsync(_ =>
            {
                _log.Write("SimulationReset: new fleet; trips and metrics reset.");
                _system = CreateSystem();
                return Status(_system);
            }, cancellationToken);
        }
        finally { _processors.Release(); }
    }

    public static SimulationStatus Status(EnterpriseElevatorSystem system) => new(system.GetAnalytics(), system.Elevators);

    private void FlushEvents()
    {
        while (_buffer.Entries.TryPeek(out var entry))
        {
            _log.Write($"tick={entry.Tick} event={entry.Name} car={entry.ElevatorId} trip={entry.RequestId} " +
                $"floor={entry.Floor} state={entry.State} mode={entry.Mode}");
            _buffer.Entries.Dequeue();
        }
    }

    public void Dispose() { _commands.Dispose(); _processors.Dispose(); }

    // Owned by the session's command gate. Drained per operation, independent of the UI history limit.
    private sealed class EventBuffer : IEnterpriseEventSink
    {
        public Queue<EnterpriseEvent> Entries { get; } = new();
        public void Record(EnterpriseEvent entry) => Entries.Enqueue(entry);
    }
}
