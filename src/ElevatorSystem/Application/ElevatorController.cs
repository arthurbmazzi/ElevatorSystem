namespace ElevatorSystem;

/// <summary>Owns the fleet and makes selection plus enqueue one atomic assignment.</summary>
public sealed partial class ElevatorController
{
    private readonly object _assignmentLock = new();
    private readonly FloorRange _floors;
    private readonly IElevatorSelectionStrategy _selectionStrategy;
    private readonly IElevatorLogger _logger = new NullElevatorLogger();
    private readonly object _processingLock = new();
    private bool _isProcessing;

    public int MinFloor => _floors.MinFloor;
    public int MaxFloor => _floors.MaxFloor;
    public IReadOnlyList<Elevator> Elevators { get; }

    /// <summary>The single elevator used by easy-level operations.</summary>
    public Elevator Elevator => Elevators.Count == 1 ? Elevators[0]
        : throw new InvalidOperationException("Easy-level operations require exactly one elevator.");

    public void RequestElevator(int floor, Direction direction)
    {
        lock (_assignmentLock) Elevator.AddPickupRequest(floor, direction);
    }

    public void RequestDestination(int floor)
    {
        lock (_assignmentLock) Elevator.AddRequest(floor);
    }

    /// <summary>
    /// Synchronously serves the single elevator's FIFO stops, with no simulated delay.
    /// Concurrent processors are serialized; requests may be submitted during logging.
    /// </summary>
    public void ProcessRequests()
    {
        var elevator = Elevator;
        lock (_processingLock)
        {
            if (_isProcessing)
                throw new InvalidOperationException("Request processing cannot be called recursively.");
            _isProcessing = true;
            try
            {
                while (true)
                {
                    ElevatorAction? action;
                    lock (_assignmentLock) action = elevator.ProcessNextStep();
                    if (action is null) return;
                    _logger.Log(action);
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }
    }

    /// <summary>Validates and atomically queues a request using the configured policy.</summary>
    /// <returns>The assigned elevator's ID.</returns>
    public int SubmitRequest(PassengerRequest request)
    {
        _floors.Validate(request);
        lock (_assignmentLock)
        {
            var snapshots = Array.AsReadOnly(Elevators.Select(elevator => new ElevatorSnapshot(
                elevator.Id, elevator.CurrentFloor, elevator.PendingRequestCount)).ToArray());
            int selectedId = _selectionStrategy.SelectElevator(request, snapshots);
            var selected = Elevators.FirstOrDefault(elevator => elevator.Id == selectedId)
                ?? throw new InvalidOperationException("The selection strategy returned an unknown elevator ID.");
            selected.Enqueue(request);
            return selected.Id;
        }
    }
}
