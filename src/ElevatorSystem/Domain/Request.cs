namespace ElevatorSystem;

/// <summary>Immutable medium-level trip; timestamp is Unix milliseconds.</summary>
public sealed class Request
{
    public Guid Id { get; } = Guid.NewGuid();
    public int PickupFloor { get; }
    public int DestinationFloor { get; }
    public Direction Direction => DestinationFloor > PickupFloor ? Direction.UP : Direction.DOWN;
    public long Timestamp { get; }

    public Request(int pickupFloor, int destinationFloor, long? timestamp = null)
    {
        var floors = new FloorRange(1, 20);
        floors.Validate(pickupFloor, nameof(pickupFloor));
        floors.Validate(destinationFloor, nameof(destinationFloor));
        if (pickupFloor == destinationFloor)
            throw new ArgumentException("Pickup and destination floors must differ.", nameof(destinationFloor));
        PickupFloor = pickupFloor;
        DestinationFloor = destinationFloor;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
