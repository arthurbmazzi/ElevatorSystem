namespace ElevatorSystem;

/// <summary>
/// Optional policy: nearest current floor to pickup, then lowest ID.
/// Does not estimate queued travel and may concentrate requests on one elevator.
/// </summary>
public sealed class NearestPickupStrategy : IElevatorSelectionStrategy
{
    public int SelectElevator(PassengerRequest request, IReadOnlyList<ElevatorSnapshot> elevators)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(elevators);
        if (elevators.Count == 0)
            throw new ArgumentException("At least one elevator is required.", nameof(elevators));

        var selected = elevators[0];
        var shortestDistance = Distance(selected, request);
        foreach (var candidate in elevators)
        {
            var distance = Distance(candidate, request);
            if (distance < shortestDistance || (distance == shortestDistance && candidate.Id < selected.Id))
            {
                selected = candidate;
                shortestDistance = distance;
            }
        }
        return selected.Id;
    }

    private static long Distance(ElevatorSnapshot elevator, PassengerRequest request) =>
        Math.Abs((long)elevator.CurrentFloor - request.PickupFloor);
}
