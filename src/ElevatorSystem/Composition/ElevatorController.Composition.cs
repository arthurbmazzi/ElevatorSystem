namespace ElevatorSystem;

// Keeps the original constructor API while separating wiring from the use case.
public sealed partial class ElevatorController
{
    public ElevatorController(int elevatorCount, int minFloor, int maxFloor)
        : this(elevatorCount, minFloor, maxFloor, new ShortestQueueStrategy()) { }

    public ElevatorController(int elevatorCount, int minFloor, int maxFloor,
        IElevatorSelectionStrategy selectionStrategy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elevatorCount);
        ArgumentNullException.ThrowIfNull(selectionStrategy);
        _floors = new FloorRange(minFloor, maxFloor);
        _selectionStrategy = selectionStrategy;
        Elevators = ElevatorFactory.CreateFleet(elevatorCount, _floors);
    }
}
