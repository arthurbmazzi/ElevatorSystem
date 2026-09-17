namespace ElevatorSystem;

/// <summary>An immutable passenger request. The controller checks building limits.</summary>
public sealed class PassengerRequest
{
    public int PickupFloor { get; }
    public int DestinationFloor { get; }
    public Direction Direction => DestinationFloor > PickupFloor ? Direction.UP : Direction.DOWN;

    public PassengerRequest(int pickupFloor, int destinationFloor)
    {
        if (pickupFloor == destinationFloor)
        {
            throw new ArgumentException("Pickup and destination floors must differ.", nameof(destinationFloor));
        }

        PickupFloor = pickupFloor;
        DestinationFloor = destinationFloor;
    }
}
