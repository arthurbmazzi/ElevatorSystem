namespace ElevatorSystem;

/// <summary>Simple factory that creates a fresh, consistently initialized fleet.</summary>
internal static class ElevatorFactory
{
    internal static IReadOnlyList<Elevator> CreateFleet(int elevatorCount, FloorRange floors)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elevatorCount);
        var elevators = new Elevator[elevatorCount];
        for (var id = 0; id < elevatorCount; id++)
            elevators[id] = new Elevator(id, floors.MinFloor, floors.MaxFloor, floors.MinFloor);
        return Array.AsReadOnly(elevators);
    }
}
