namespace ElevatorSystem;

/// <summary>Fewest pending requests, with ties resolved by lowest elevator ID.</summary>
public sealed class ShortestQueueStrategy : IElevatorSelectionStrategy
{
    public int SelectElevator(PassengerRequest request, IReadOnlyList<ElevatorSnapshot> elevators)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(elevators);
        if (elevators.Count == 0)
            throw new ArgumentException("At least one elevator is required.", nameof(elevators));

        var selected = elevators[0];
        foreach (var candidate in elevators)
        {
            if (candidate.PendingRequestCount < selected.PendingRequestCount
                || (candidate.PendingRequestCount == selected.PendingRequestCount && candidate.Id < selected.Id))
                selected = candidate;
        }
        return selected.Id;
    }
}
