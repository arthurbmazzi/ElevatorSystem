namespace ElevatorSystem;

/// <summary>Owns a medium-level fleet. Public status exposes immutable snapshots only.</summary>
public sealed class ElevatorSystem
{
    private readonly object _sync = new();
    private readonly object _logging = new();
    private readonly SemaphoreSlim _processing = new(1, 1);
    private readonly IReadOnlyList<Elevator> _elevators;
    private readonly PriorityQueue<Request, (long Timestamp, long Sequence)> _requestQueue = new();
    private readonly Queue<ElevatorAction> _events = new();
    private readonly IElevatorSelectionStrategy _strategy;
    private readonly IElevatorLogger _logger;
    private readonly TimeSpan _stepDelay;
    private long _sequence;
    private long _submitted;
    private long _completed;

    public ElevatorSystem(int elevatorCount = 4, IElevatorSelectionStrategy? selectionStrategy = null,
        IElevatorLogger? logger = null, TimeSpan? stepDelay = null)
    {
        if (elevatorCount is < 3 or > 5)
            throw new ArgumentOutOfRangeException(nameof(elevatorCount), "Use 3 to 5 elevators.");
        _stepDelay = stepDelay ?? TimeSpan.Zero;
        if (_stepDelay < TimeSpan.Zero || _stepDelay.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(stepDelay));
        _elevators = ElevatorFactory.CreateFleet(elevatorCount, new FloorRange(1, 20));
        _strategy = selectionStrategy ?? new OptimizedDispatchStrategy();
        _logger = logger ?? new ConsoleElevatorLogger();
    }

    public IReadOnlyList<ElevatorSnapshot> Elevators { get { lock (_sync) return Snapshots(); } }
    public IReadOnlyList<Request> RequestQueue
    {
        get { lock (_sync) return Array.AsReadOnly(_requestQueue.UnorderedItems
            .OrderBy(item => item.Priority).Select(item => item.Element).ToArray()); }
    }

    public void SubmitRequest(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            _requestQueue.Enqueue(request, (request.Timestamp, _sequence++));
            _submitted++;
        }
    }

    /// <summary>Advisory selection; AssignRequest performs selection and enqueue atomically.</summary>
    public int FindBestElevator(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync) return Select(request).Id;
    }

    /// <summary>Immediately assigns a trip, bypassing central queue priority.</summary>
    public int AssignRequest(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            int id = Assign(request);
            _submitted++;
            return id;
        }
    }

    /// <summary>Distributes waiting trips oldest first using fresh load/route snapshots.</summary>
    public void BalanceLoad()
    {
        lock (_sync)
        {
            while (_requestQueue.TryPeek(out var request, out _))
            {
                Assign(request); // Keep the head queued if the policy fails.
                _requestQueue.Dequeue();
            }
        }
    }

    public FleetStatus GetStatus()
    {
        lock (_sync) return new FleetStatus(_submitted, _completed, _requestQueue.Count, Snapshots());
    }

    /// <summary>
    /// Drains on one thread-pool worker per car. Calls serialize; cancellation preserves work.
    /// Submissions after all workers observe no work require another processing call.
    /// </summary>
    public async Task ProcessRequestsAsync(CancellationToken cancellationToken = default)
    {
        await _processing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            BalanceLoad();
            await Task.WhenAll(_elevators.Select(elevator => Task.Run(async () =>
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ElevatorAction? action;
                    lock (_sync)
                    {
                        BalanceLoad();
                        int before = elevator.PendingRequestCount;
                        action = elevator.ProcessNextStep();
                        _completed += before - elevator.PendingRequestCount;
                        if (before > elevator.PendingRequestCount)
                            _events.Enqueue(new ElevatorAction(elevator.Id, elevator.CurrentFloor,
                                elevator.State, "Passenger trip completed"));
                    }
                    FlushEvents();
                    if (action is null) return;
                    lock (_logging) _logger.Log(action);
                    if (_stepDelay > TimeSpan.Zero)
                        await Task.Delay(_stepDelay, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken))).ConfigureAwait(false);
        }
        finally { _processing.Release(); }
    }

    private Elevator Select(Request request)
    {
        int id = _strategy.SelectElevator(new PassengerRequest(request.PickupFloor, request.DestinationFloor), Snapshots());
        return _elevators.FirstOrDefault(e => e.Id == id)
            ?? throw new InvalidOperationException("The selection strategy returned an unknown elevator ID.");
    }

    private int Assign(Request request)
    {
        var elevator = Select(request);
        elevator.Enqueue(new PassengerRequest(request.PickupFloor, request.DestinationFloor));
        _events.Enqueue(new ElevatorAction(elevator.Id, elevator.CurrentFloor, elevator.State,
            $"Assigned request {request.Id}: {request.PickupFloor} -> {request.DestinationFloor}; timestamp {request.Timestamp}", request.Direction));
        return elevator.Id;
    }

    private IReadOnlyList<ElevatorSnapshot> Snapshots() => Array.AsReadOnly(_elevators.Select(e =>
        new ElevatorSnapshot(e.Id, e.CurrentFloor, e.PendingRequestCount)
        { State = e.State, TargetFloors = e.TargetFloors }).ToArray());

    private void FlushEvents()
    {
        lock (_logging)
        {
            while (true)
            {
                ElevatorAction action;
                lock (_sync)
                {
                    if (!_events.TryDequeue(out action!)) return;
                }
                _logger.Log(action);
            }
        }
    }
}

public sealed record FleetStatus(long SubmittedRequests, long CompletedRequests,
    int WaitingRequests, IReadOnlyList<ElevatorSnapshot> Elevators)
{
    public long PendingRequests => WaitingRequests + Elevators.Sum(e => (long)e.PendingRequestCount);
}
