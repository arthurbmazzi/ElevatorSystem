namespace ElevatorSystem;

/// <summary>An immutable record of a completed movement or door transition.</summary>
public sealed record ElevatorAction(
    int ElevatorId, int Floor, ElevatorState State, string Description,
    Direction? RequestedDirection = null);
