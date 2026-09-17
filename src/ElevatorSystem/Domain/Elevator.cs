namespace ElevatorSystem;

/// <summary>
/// Initial elevator model. Movement and door transitions belong to later levels.
/// A private lock guards the pending request queue.
/// </summary>
public sealed class Elevator
{
    private readonly object _sync = new();
    private readonly Queue<PassengerRequest> _pendingRequests = new();
    private readonly FloorRange _floors;

    public int Id { get; }
    public int MinFloor { get; }
    public int MaxFloor { get; }
    public int CurrentFloor { get; }
    public ElevatorState State { get; } = ElevatorState.IDLE;

    public Elevator(int id, int minFloor, int maxFloor, int initialFloor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        _floors = new FloorRange(minFloor, maxFloor);
        _floors.Validate(initialFloor, nameof(initialFloor));

        Id = id;
        MinFloor = minFloor;
        MaxFloor = maxFloor;
        CurrentFloor = initialFloor;
    }

    public int PendingRequestCount
    {
        get
        {
            lock (_sync)
            {
                return _pendingRequests.Count;
            }
        }
    }

    /// <summary>Returns a read-only snapshot, never the live queue.</summary>
    public IReadOnlyList<PassengerRequest> GetPendingRequests()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_pendingRequests.ToArray());
        }
    }

    // Assignment is controlled by ElevatorController, not exposed to callers.
    internal void Enqueue(PassengerRequest request)
    {
        _floors.Validate(request);

        lock (_sync)
        {
            _pendingRequests.Enqueue(request);
        }
    }
}
