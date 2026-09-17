namespace ElevatorSystem;

// Keeps the original constructor API while separating wiring from the use case.
public sealed partial class ElevatorController
{
    public ElevatorController() : this(new ConsoleElevatorLogger()) { }

    public ElevatorController(IElevatorLogger logger)
        : this(1, 1, 10, new ShortestQueueStrategy())
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

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
