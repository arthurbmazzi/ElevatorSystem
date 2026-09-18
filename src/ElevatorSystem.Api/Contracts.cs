using ElevatorSystem;

namespace ElevatorSystem.Api;

public sealed class CreateTripCommand
{
    public int PickupFloor { get; init; } = 3;
    public int DestinationFloor { get; init; } = 15;
    public TransportKind Kind { get; init; } = TransportKind.Passenger;
    public decimal WeightKg { get; init; } = 75;
    public bool IsVip { get; init; }
    public int[] AllowedFloors { get; init; } = Enumerable.Range(1, 20).ToArray();

    public EnterpriseRequest ToRequest() => new(new Request(PickupFloor, DestinationFloor),
        new AccessProfile(AllowedFloors, IsVip), Kind, WeightKg);
}

public sealed record FleetOverview(EnterpriseAnalytics Analytics,
    IReadOnlyList<EnterpriseElevatorSnapshot> Elevators);
