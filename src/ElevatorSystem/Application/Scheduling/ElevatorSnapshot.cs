namespace ElevatorSystem;

/// <summary>Immutable scheduling input; never exposes a live elevator or queue.</summary>
public sealed record ElevatorSnapshot(int Id, int CurrentFloor, int PendingRequestCount)
{
    public ElevatorState State { get; init; }
    public IReadOnlyList<int> TargetFloors { get; init; } = Array.Empty<int>();
}
