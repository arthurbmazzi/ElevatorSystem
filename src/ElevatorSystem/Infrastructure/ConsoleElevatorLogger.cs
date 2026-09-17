namespace ElevatorSystem;

/// <summary>Console adapter for the easy-level simulation.</summary>
public sealed class ConsoleElevatorLogger : IElevatorLogger
{
    public void Log(ElevatorAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var pickup = action.RequestedDirection is { } direction ? $"; pickup {direction}" : "";
        Console.WriteLine($"Elevator {action.ElevatorId}: {action.Description} at floor {action.Floor} [{action.State}]{pickup}");
    }
}
