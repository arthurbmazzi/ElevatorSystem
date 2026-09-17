namespace ElevatorSystem;

public enum ElevatorType { Local, Express, Freight }
public enum OperationalMode { Normal, Draining, Maintenance, EmergencyStopped }
public enum TripState { Waiting, Assigned, Onboard, Completed }
public enum TransportKind { Passenger, Freight }

/// <summary>Immutable physical service configuration; floors are copied at construction.</summary>
public sealed class ElevatorConfiguration
{
    public int Id { get; }
    public ElevatorType Type { get; }
    public IReadOnlyList<int> ServedFloors { get; }
    public decimal CapacityKg { get; }
    public int InitialFloor { get; }

    public ElevatorConfiguration(int id, ElevatorType type, IEnumerable<int> servedFloors,
        decimal capacityKg = 1000, int initialFloor = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentNullException.ThrowIfNull(servedFloors);
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        var floors = servedFloors.Distinct().Order().ToArray();
        if (floors.Length == 0 || floors.Any(f => f is < 1 or > 20))
            throw new ArgumentException("Service floors must be between 1 and 20.", nameof(servedFloors));
        if (!floors.Contains(initialFloor)) throw new ArgumentOutOfRangeException(nameof(initialFloor));
        if (capacityKg <= 0) throw new ArgumentOutOfRangeException(nameof(capacityKg));
        Id = id; Type = type; ServedFloors = Array.AsReadOnly(floors);
        CapacityKg = capacityKg; InitialFloor = initialFloor;
    }
}

/// <summary>Authorization supplied by a trusted caller, independent of VIP priority.</summary>
public sealed class AccessProfile
{
    public bool IsVip { get; }
    public IReadOnlyList<int> AllowedFloors { get; }
    public AccessProfile(IEnumerable<int> allowedFloors, bool isVip = false)
    {
        ArgumentNullException.ThrowIfNull(allowedFloors);
        var floors = allowedFloors.Distinct().ToArray();
        if (floors.Any(f => f is < 1 or > 20)) throw new ArgumentOutOfRangeException(nameof(allowedFloors));
        AllowedFloors = Array.AsReadOnly(floors); IsVip = isVip;
    }
}

public sealed class EnterpriseRequest
{
    public Request Trip { get; }
    public AccessProfile Access { get; }
    public TransportKind Kind { get; }
    public decimal WeightKg { get; }
    public EnterpriseRequest(Request trip, AccessProfile access, TransportKind kind = TransportKind.Passenger,
        decimal weightKg = 75)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(access);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (weightKg <= 0) throw new ArgumentOutOfRangeException(nameof(weightKg));
        Trip = trip; Access = access; Kind = kind; WeightKg = weightKg;
    }
}

public sealed record TripSnapshot(Guid Id, TripState State, int? ElevatorId, long SubmittedTick,
    long? PickupTick, long? CompletedTick);
public sealed record EnterpriseElevatorSnapshot(int Id, ElevatorType Type, int Floor, ElevatorState State,
    OperationalMode Mode, Direction Direction, int PendingTrips, decimal ReservedKg);
public sealed record EnterpriseEvent(long Tick, string Name, int? ElevatorId = null, Guid? RequestId = null,
    int? Floor = null, ElevatorState? State = null, OperationalMode? Mode = null);
public sealed record EnterpriseAnalytics(long Tick, long Submitted, long Completed, int Pending,
    double AverageWaitTicks, double P95WaitTicks, double AverageTravelTicks, long FloorsTravelled,
    long MovingTicks, long DoorTicks, long IdleTicks, long UnavailableTicks, double ThroughputPerTick,
    long AssignmentCount, double AverageAssignmentMilliseconds, double MaxAssignmentMilliseconds,
    long OldestWaitingTicks);
