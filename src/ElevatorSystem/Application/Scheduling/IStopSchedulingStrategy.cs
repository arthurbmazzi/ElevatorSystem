namespace ElevatorSystem;

public sealed record ScheduledStop(Guid RequestId, int Floor, Direction Direction, bool IsPickup);
public sealed record StopDecision(ScheduledStop Stop, Direction Direction);

/// <summary>Pure bounded policy. Destinations are supplied only after pickup.</summary>
public interface IStopSchedulingStrategy
{
    StopDecision SelectNext(int currentFloor, Direction direction, IReadOnlyList<ScheduledStop> stops);
}

public sealed class LookStopSchedulingStrategy : IStopSchedulingStrategy
{
    public StopDecision SelectNext(int currentFloor, Direction direction, IReadOnlyList<ScheduledStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Count == 0) throw new ArgumentException("At least one stop is required.", nameof(stops));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        // Opposite-direction pickups are reached at the end of the sweep, then served on reversal.
        var ahead = stops.Where(s => direction == Direction.UP ? s.Floor >= currentFloor : s.Floor <= currentFloor).ToArray();
        if (ahead.Length == 0)
        {
            direction = direction == Direction.UP ? Direction.DOWN : Direction.UP;
            ahead = stops.ToArray();
        }
        var compatible = ahead.Where(s => !s.IsPickup || s.Direction == direction).ToArray();
        var selected = compatible.Length > 0
            ? compatible.OrderBy(s => Math.Abs(s.Floor - currentFloor)).First()
            : ahead.OrderByDescending(s => Math.Abs(s.Floor - currentFloor)).First();
        if (selected.Floor == currentFloor && selected.IsPickup) direction = selected.Direction;
        return new(selected, direction);
    }
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
