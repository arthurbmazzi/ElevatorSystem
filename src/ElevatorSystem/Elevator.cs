namespace ElevatorSystem;

/// <summary>
/// Initial elevator model. Movement and door transitions belong to later levels.
/// A private lock guards the pending request queue.
/// </summary>
public sealed class Elevator
{
    private readonly object _sync = new();
    private readonly Queue<PassengerRequest> _pendingRequests = new();

    public int Id { get; }
    public int MinFloor { get; }
    public int MaxFloor { get; }
    public int CurrentFloor { get; }
    public ElevatorState State { get; } = ElevatorState.IDLE;

    public Elevator(int id, int minFloor, int maxFloor, int initialFloor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        if (minFloor >= maxFloor)
        {
            throw new ArgumentException("A building must have at least two floors.", nameof(maxFloor));
        }
        if (initialFloor < minFloor || initialFloor > maxFloor)
        {
            throw new ArgumentOutOfRangeException(nameof(initialFloor), "Initial floor is outside building limits.");
        }

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
        ArgumentNullException.ThrowIfNull(request);
        if (request.PickupFloor < MinFloor || request.PickupFloor > MaxFloor
            || request.DestinationFloor < MinFloor || request.DestinationFloor > MaxFloor)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Request is outside building limits.");
        }

        lock (_sync)
        {
            _pendingRequests.Enqueue(request);
        }
    }
}
