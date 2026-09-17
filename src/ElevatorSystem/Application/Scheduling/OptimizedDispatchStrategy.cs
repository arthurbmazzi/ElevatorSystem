namespace ElevatorSystem;

/// <summary>Estimates FIFO pickup delay, including stops and a load penalty.</summary>
public sealed class OptimizedDispatchStrategy : IElevatorSelectionStrategy
{
    public int SelectElevator(PassengerRequest request, IReadOnlyList<ElevatorSnapshot> elevators)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(elevators);
        if (elevators.Count == 0) throw new ArgumentException("At least one elevator is required.", nameof(elevators));
        return elevators.OrderBy(e => Cost(e, request))
            .ThenBy(e => e.PendingRequestCount).ThenBy(e => e.Id).First().Id;
    }

    private static long Cost(ElevatorSnapshot elevator, PassengerRequest request)
    {
        long cost = 0;
        int floor = elevator.CurrentFloor;
        foreach (int stop in elevator.TargetFloors)
        {
            cost += Math.Abs((long)stop - floor) + 2; // Open and close doors.
            floor = stop;
        }
        cost += Math.Abs((long)request.PickupFloor - floor);
        cost += 4L * elevator.PendingRequestCount;
        bool sameDirection = (elevator.State == ElevatorState.MOVING_UP && request.Direction == Direction.UP
                && request.PickupFloor >= elevator.CurrentFloor)
            || (elevator.State == ElevatorState.MOVING_DOWN && request.Direction == Direction.DOWN
                && request.PickupFloor <= elevator.CurrentFloor);
        return cost * 2 + (sameDirection ? 0 : 1);
    }
}
