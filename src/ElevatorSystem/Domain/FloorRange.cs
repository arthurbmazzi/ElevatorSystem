namespace ElevatorSystem;

/// <summary>Inclusive building limits shared by the domain and application.</summary>
public sealed class FloorRange
{
    public int MinFloor { get; }
    public int MaxFloor { get; }

    public FloorRange(int minFloor, int maxFloor)
    {
        if (minFloor >= maxFloor)
            throw new ArgumentException("A building must have at least two floors.", nameof(maxFloor));
        MinFloor = minFloor;
        MaxFloor = maxFloor;
    }

    public void Validate(int floor, string parameterName)
    {
        if (floor < MinFloor || floor > MaxFloor)
            throw new ArgumentOutOfRangeException(parameterName, floor,
                $"Floor must be between {MinFloor} and {MaxFloor}, inclusive.");
    }

    public void Validate(PassengerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.PickupFloor, nameof(request.PickupFloor));
        Validate(request.DestinationFloor, nameof(request.DestinationFloor));
    }
}
