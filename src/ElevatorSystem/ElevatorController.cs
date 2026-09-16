namespace ElevatorSystem;

/// <summary>Owns the fleet and makes selection plus enqueue one atomic assignment.</summary>
public sealed class ElevatorController
{
    private readonly object _assignmentLock = new();

    public int MinFloor { get; }
    public int MaxFloor { get; }
    public IReadOnlyList<Elevator> Elevators { get; }

    public ElevatorController(int elevatorCount, int minFloor, int maxFloor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elevatorCount);
        if (minFloor >= maxFloor)
        {
            throw new ArgumentException("A building must have at least two floors.", nameof(maxFloor));
        }

        MinFloor = minFloor;
        MaxFloor = maxFloor;
        var elevators = new Elevator[elevatorCount];
        for (var id = 0; id < elevatorCount; id++)
        {
            elevators[id] = new Elevator(id, minFloor, maxFloor, minFloor);
        }
        Elevators = Array.AsReadOnly(elevators);
    }

    /// <summary>
    /// Queues a request on the elevator with the fewest pending requests.
    /// Ties go to the lowest ID. This initial version does not execute trips yet.
    /// </summary>
    /// <returns>The assigned elevator's ID.</returns>
    public int SubmitRequest(PassengerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFloor(request.PickupFloor, nameof(request.PickupFloor));
        ValidateFloor(request.DestinationFloor, nameof(request.DestinationFloor));

        lock (_assignmentLock)
        {
            var selected = Elevators[0];
            foreach (var candidate in Elevators)
            {
                if (candidate.PendingRequestCount < selected.PendingRequestCount)
                {
                    selected = candidate;
                }
            }
            selected.Enqueue(request);
            return selected.Id;
        }
    }

    private void ValidateFloor(int floor, string parameterName)
    {
        if (floor < MinFloor || floor > MaxFloor)
        {
            throw new ArgumentOutOfRangeException(parameterName, floor,
                $"Floor must be between {MinFloor} and {MaxFloor}, inclusive.");
        }
    }
}
