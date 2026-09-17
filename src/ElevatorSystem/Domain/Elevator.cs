namespace ElevatorSystem;

/// <summary>Owns floor, state, and FIFO stops under a single private lock.</summary>
public sealed class Elevator
{
    private readonly object _sync = new();
    private readonly Queue<PassengerRequest> _pendingRequests = new();
    private readonly Queue<FloorStop> _targetFloors = new();
    private readonly FloorRange _floors;
    private int _currentFloor;
    private ElevatorState _state = ElevatorState.IDLE;
    private int _pendingRequestCount;
    private bool _servicingStop;

    public int Id { get; }
    public int MinFloor => _floors.MinFloor;
    public int MaxFloor => _floors.MaxFloor;
    public int CurrentFloor { get { lock (_sync) return _currentFloor; } }
    public ElevatorState State { get { lock (_sync) return _state; } }
    public int PendingRequestCount { get { lock (_sync) return _pendingRequestCount; } }

    /// <summary>Includes the active stop until its doors close; preserves duplicates.</summary>
    public IReadOnlyList<int> TargetFloors
    {
        get { lock (_sync) return Array.AsReadOnly(_targetFloors.Select(stop => stop.Floor).ToArray()); }
    }

    public Elevator(int id, int minFloor, int maxFloor, int initialFloor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        _floors = new FloorRange(minFloor, maxFloor);
        _floors.Validate(initialFloor, nameof(initialFloor));
        Id = id;
        _currentFloor = initialFloor;
    }

    /// <summary>Compatibility snapshot of paired passenger requests still in progress.</summary>
    public IReadOnlyList<PassengerRequest> GetPendingRequests()
    {
        lock (_sync) return Array.AsReadOnly(_pendingRequests.ToArray());
    }

    public void AddRequest(int floor)
    {
        _floors.Validate(floor, nameof(floor));
        lock (_sync)
        {
            _targetFloors.Enqueue(new FloorStop(floor));
            _pendingRequestCount++;
        }
    }

    internal void AddPickupRequest(int floor, Direction direction)
    {
        _floors.Validate(floor, nameof(floor));
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if ((floor == MinFloor && direction == Direction.DOWN)
            || (floor == MaxFloor && direction == Direction.UP))
            throw new ArgumentException("Pickup direction leads outside the building.", nameof(direction));
        lock (_sync)
        {
            _targetFloors.Enqueue(new FloorStop(floor, direction));
            _pendingRequestCount++;
        }
    }

    internal void Enqueue(PassengerRequest request)
    {
        _floors.Validate(request);
        lock (_sync)
        {
            _pendingRequests.Enqueue(request);
            _targetFloors.Enqueue(new FloorStop(request.PickupFloor, request.Direction, CompletesRequest: false));
            _targetFloors.Enqueue(new FloorStop(request.DestinationFloor, Passenger: request));
            _pendingRequestCount++;
        }
    }

    public ElevatorAction MoveUp()
    {
        lock (_sync) return Move(1, ElevatorState.MOVING_UP);
    }

    public ElevatorAction MoveDown()
    {
        lock (_sync) return Move(-1, ElevatorState.MOVING_DOWN);
    }

    private ElevatorAction Move(int delta, ElevatorState state)
    {
        if (_state == ElevatorState.DOOR_OPEN)
            throw new InvalidOperationException("Close the doors before moving.");
        if ((delta > 0 && _currentFloor == MaxFloor) || (delta < 0 && _currentFloor == MinFloor))
            throw new InvalidOperationException("Cannot move beyond the building limits.");
        _currentFloor += delta;
        _state = state;
        return Action(delta > 0 ? "Moved up" : "Moved down");
    }

    /// <summary>Stops at the current floor and opens the doors immediately.</summary>
    public ElevatorAction OpenDoor()
    {
        lock (_sync)
        {
            _state = ElevatorState.DOOR_OPEN;
            return Action("Doors opened");
        }
    }

    public ElevatorAction CloseDoor()
    {
        lock (_sync)
        {
            if (_state != ElevatorState.DOOR_OPEN)
                throw new InvalidOperationException("The doors are not open.");
            if (_servicingStop)
            {
                var completed = _targetFloors.Dequeue();
                if (completed.CompletesRequest) _pendingRequestCount--;
                if (completed.Passenger is not null) _pendingRequests.Dequeue();
                _servicingStop = false;
            }
            _state = ElevatorState.IDLE;
            return Action("Doors closed");
        }
    }

    /// <summary>One atomic simulation step. No external callbacks run under this lock.</summary>
    internal ElevatorAction? ProcessNextStep()
    {
        lock (_sync)
        {
            if (_state == ElevatorState.DOOR_OPEN) return CloseDoor();
            if (!_targetFloors.TryPeek(out var stop)) return null;
            if (_currentFloor < stop.Floor) return Move(1, ElevatorState.MOVING_UP);
            if (_currentFloor > stop.Floor) return Move(-1, ElevatorState.MOVING_DOWN);
            _servicingStop = true;
            _state = ElevatorState.DOOR_OPEN;
            return Action("Doors opened", stop.Direction);
        }
    }

    private ElevatorAction Action(string description, Direction? direction = null) =>
        new(Id, _currentFloor, _state, description, direction);

    private sealed record FloorStop(int Floor, Direction? Direction = null,
        PassengerRequest? Passenger = null, bool CompletesRequest = true);
}
