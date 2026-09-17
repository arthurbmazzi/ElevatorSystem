namespace ElevatorSystem;

/// <summary>Immutable scheduling input; never exposes a live elevator or queue.</summary>
public sealed record ElevatorSnapshot(int Id, int CurrentFloor, int PendingRequestCount);
