namespace ElevatorSystem;

public sealed record ScheduledStop(Guid RequestId, int Floor, Direction Direction, bool IsPickup);
public sealed record StopDecision(ScheduledStop Stop, Direction Direction);

/// <summary>Pure bounded policy. Destinations are supplied only after pickup.</summary>
public interface IStopSchedulingStrategy
{
    StopDecision SelectNext(int currentFloor, Direction direction, IReadOnlyList<ScheduledStop> stops);
}

public sealed class FifoStopSchedulingStrategy : IStopSchedulingStrategy
{
    public StopDecision SelectNext(int currentFloor, Direction direction, IReadOnlyList<ScheduledStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Count == 0) throw new ArgumentException("At least one stop is required.", nameof(stops));
        var stop = stops[0];
        return new(stop, stop.Floor == currentFloor ? stop.Direction
            : stop.Floor > currentFloor ? Direction.UP : Direction.DOWN);
    }
}
