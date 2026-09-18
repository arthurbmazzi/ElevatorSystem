using System.Diagnostics;

namespace ElevatorSystem;

/// <summary>
/// Hard-level coordinator. One lock owns assignment and transitions, including pure routing decisions.
/// Each simulation tick advances every car once. Physical movement is delegated to Elevator.
/// </summary>
public sealed class EnterpriseElevatorSystem
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _processing = new(1, 1);
    private readonly List<Car> _cars;
    private readonly Dictionary<Guid, Trip> _trips = new();
    private readonly Queue<Guid> _completedIds = new();
    private readonly Queue<EnterpriseEvent> _events = new();
    private readonly Queue<long> _waitSamples = new();
    private readonly IStopSchedulingStrategy _routing;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _stuckTimeout;
    private readonly int _maxPending;
    private readonly int _historyLimit;
    private long _tick, _submitted, _completed, _distance, _moving, _doors, _idle, _unavailable;
    private long _waitTotal, _travelTotal, _pickups, _assignmentCount, _sequence;
    private double _assignmentTotal, _assignmentMax;
    private const long VipBonusTicks = 20;

    public EnterpriseElevatorSystem(IEnumerable<ElevatorConfiguration> configurations,
        IStopSchedulingStrategy? routing = null, TimeProvider? clock = null,
        TimeSpan? stuckTimeout = null, int maxPending = 10000, int historyLimit = 1000)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        var configs = configurations.ToArray();
        if (configs.Length is < 3 or > 5 || configs.Any(c => c is null)
            || configs.Select(c => c.Id).Distinct().Count() != configs.Length)
            throw new ArgumentException("Provide 3 to 5 cars with unique IDs.", nameof(configurations));
        if (maxPending <= 0) throw new ArgumentOutOfRangeException(nameof(maxPending));
        if (historyLimit <= 0) throw new ArgumentOutOfRangeException(nameof(historyLimit));
        _stuckTimeout = stuckTimeout ?? TimeSpan.FromSeconds(30);
        if (_stuckTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(stuckTimeout));
        _clock = clock ?? TimeProvider.System; _routing = routing ?? new FifoStopSchedulingStrategy();
        _maxPending = maxPending; _historyLimit = historyLimit;
        _cars = configs.Select(c => new Car(c)).ToList();
    }

    public IReadOnlyList<EnterpriseElevatorSnapshot> Elevators
    {
        get { lock (_sync) return Array.AsReadOnly(_cars.Select(c => new EnterpriseElevatorSnapshot(
            c.Config.Id, c.Config.Type, c.Elevator.CurrentFloor, c.Elevator.State, c.Mode,
            c.Direction, c.Trips.Count, c.Trips.Sum(t => t.Request.WeightKg))).ToArray()); }
    }

    public IReadOnlyList<TripSnapshot> Trips
    {
        get { lock (_sync) return Array.AsReadOnly(_trips.Values.Select(t => new TripSnapshot(
            t.Request.Trip.Id, t.State, t.CarId, t.SubmittedTick, t.PickupTick, t.CompletedTick)).ToArray()); }
    }

    public IReadOnlyList<EnterpriseEvent> Events
    {
        get { lock (_sync) return Array.AsReadOnly(_events.ToArray()); }
    }

    public void SubmitRequest(EnterpriseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            if (!request.Access.AllowedFloors.Contains(request.Trip.PickupFloor)
                || !request.Access.AllowedFloors.Contains(request.Trip.DestinationFloor))
                throw new UnauthorizedAccessException("Access to pickup or destination denied.");
            if (!_cars.Any(c => Supports(c, request)))
                throw new ArgumentException("No car supports this direct trip, kind and weight.", nameof(request));
            if (_trips.ContainsKey(request.Trip.Id)) throw new ArgumentException("Duplicate request ID.", nameof(request));
            if (_submitted - _completed >= _maxPending) throw new InvalidOperationException("Pending request limit reached.");
            _trips.Add(request.Trip.Id, new Trip(request, _tick, _sequence++));
            _submitted++;
            Emit("RequestSubmitted", requestId: request.Trip.Id);
        }
    }

    public void BalanceLoad() { lock (_sync) AssignWaiting(); }

    private static bool Supports(Car car, EnterpriseRequest request) =>
        (car.Config.Type == ElevatorType.Freight) == (request.Kind == TransportKind.Freight)
        && car.Config.ServedFloors.Contains(request.Trip.PickupFloor)
        && car.Config.ServedFloors.Contains(request.Trip.DestinationFloor)
        && request.WeightKg <= car.Config.CapacityKg;

    private void AssignWaiting()
    {
        // A fixed VIP head start plus arrival age ensures later VIPs cannot overtake forever.
        foreach (var trip in _trips.Values.Where(t => t.State == TripState.Waiting)
            .OrderBy(t => t.SubmittedTick - (t.Request.Access.IsVip ? VipBonusTicks : 0))
            .ThenBy(t => t.Request.Trip.Timestamp).ThenBy(t => t.Sequence).ToArray())
        {
            long started = Stopwatch.GetTimestamp();
            var car = _cars.Where(c => c.Mode == OperationalMode.Normal && Supports(c, trip.Request)
                && c.Trips.Sum(t => t.Request.WeightKg) + trip.Request.WeightKg <= c.Config.CapacityKg)
                .Select(c => (Car: c, Cost: EstimatePickup(c, trip)))
                .OrderBy(c => c.Cost).ThenBy(c => c.Car.Trips.Count).ThenBy(c => c.Car.Config.Id)
                .Select(c => c.Car).FirstOrDefault();
            if (car is null) continue; // An unavailable request must not block unrelated work.
            if (car.Trips.Count == 0) car.LastProgress = _clock.GetTimestamp();
            trip.CarId = car.Config.Id; trip.State = TripState.Assigned; car.Trips.Add(trip);
            Emit("RequestAssigned", car.Config.Id, trip.Request.Trip.Id);
            double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _assignmentCount++; _assignmentTotal += elapsed; _assignmentMax = Math.Max(_assignmentMax, elapsed);
        }
    }

    private long EstimatePickup(Car car, Trip added)
    {
        var stops = Stops(car).Append(new ScheduledStop(added.Request.Trip.Id,
            added.Request.Trip.PickupFloor, added.Request.Trip.Direction, true)).ToList();
        var trips = car.Trips.Append(added).ToDictionary(t => t.Request.Trip.Id);
        int floor = car.Elevator.CurrentFloor;
        var direction = car.Direction;
        long cost = car.Elevator.State == ElevatorState.DOOR_OPEN ? 1 : 0;
        while (stops.Count > 0)
        {
            var decision = Decide(floor, direction, stops);
            cost += Math.Abs(decision.Stop.Floor - floor);
            if (decision.Stop.RequestId == added.Request.Trip.Id) return cost;
            cost += 2; floor = decision.Stop.Floor; direction = decision.Direction;
            int stopIndex = stops.IndexOf(decision.Stop);
            stops.RemoveAt(stopIndex);
            if (decision.Stop.IsPickup)
            {
                var request = trips[decision.Stop.RequestId].Request.Trip;
                // Keep the trip's place: FIFO completes its destination before the next pickup.
                stops.Insert(stopIndex, new(request.Id, request.DestinationFloor, request.Direction, false));
            }
        }
        return cost;
    }

    private StopDecision Decide(int floor, Direction direction, List<ScheduledStop> stops)
    {
        var decision = _routing.SelectNext(floor, direction, stops.AsReadOnly());
        if (decision is null || !stops.Contains(decision.Stop) || !Enum.IsDefined(decision.Direction))
            throw new InvalidOperationException("Routing policy returned an invalid stop or direction.");
        return decision;
    }

    private static List<ScheduledStop> Stops(Car car) => car.Trips.Select(t => new ScheduledStop(
        t.Request.Trip.Id, t.State == TripState.Onboard ? t.Request.Trip.DestinationFloor : t.Request.Trip.PickupFloor,
        t.Request.Trip.Direction, t.State != TripState.Onboard)).ToList();

    /// <summary>One atomic fleet tick. Returns whether any car made progress.</summary>
    public bool ProcessTick()
    {
        lock (_sync)
        {
            CheckTimeoutsCore();
            AssignWaiting();
            _tick++;
            bool progressed = false;
            foreach (var car in _cars)
            {
                if (car.Mode is OperationalMode.Maintenance or OperationalMode.EmergencyStopped)
                { _unavailable++; continue; }
                if (car.Elevator.State == ElevatorState.DOOR_OPEN)
                {
                    car.Elevator.CloseDoor(); _doors++; Emit("DoorsClosed", car.Config.Id);
                    Progress(car); progressed = true; FinishMaintenance(car); continue;
                }
                if (car.Trips.Count == 0) { _idle++; FinishMaintenance(car); continue; }
                var decision = Decide(car.Elevator.CurrentFloor, car.Direction, Stops(car));
                car.Direction = decision.Direction;
                if (decision.Stop.Floor != car.Elevator.CurrentFloor)
                {
                    if (decision.Stop.Floor > car.Elevator.CurrentFloor) car.Elevator.MoveUp();
                    else car.Elevator.MoveDown();
                    _distance++; _moving++; Emit("Moved", car.Config.Id);
                }
                else
                {
                    var trip = car.Trips.Single(t => t.Request.Trip.Id == decision.Stop.RequestId);
                    car.Elevator.OpenDoor(); _doors++; Emit("DoorsOpened", car.Config.Id);
                    if (trip.State == TripState.Assigned)
                    {
                        trip.State = TripState.Onboard; trip.PickupTick = _tick; _pickups++;
                        long wait = _tick - trip.SubmittedTick;
                        _waitTotal += wait; _waitSamples.Enqueue(wait);
                        if (_waitSamples.Count > _historyLimit) _waitSamples.Dequeue();
                        Emit("PassengerPickedUp", car.Config.Id, trip.Request.Trip.Id);
                    }
                    else
                    {
                        trip.State = TripState.Completed; trip.CompletedTick = _tick; _completed++;
                        _travelTotal += _tick - trip.PickupTick!.Value; car.Trips.Remove(trip);
                        Emit("PassengerDroppedOff", car.Config.Id, trip.Request.Trip.Id);
                        _completedIds.Enqueue(trip.Request.Trip.Id);
                        if (_completedIds.Count > _historyLimit) _trips.Remove(_completedIds.Dequeue());
                    }
                }
                Progress(car); progressed = true;
            }
            return progressed;
        }
    }

    public async Task ProcessRequestsAsync(CancellationToken cancellationToken = default)
    {
        await _processing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ProcessTick()) return;
                await Task.Yield();
            }
        }
        finally { _processing.Release(); }
    }

    public void RequestMaintenance(int elevatorId)
    {
        lock (_sync)
        {
            var car = Find(elevatorId);
            if (car.Mode != OperationalMode.Normal) throw new InvalidOperationException("Car is not in normal service.");
            car.Mode = OperationalMode.Draining; ReturnWaiting(car);
            Emit("MaintenanceRequested", elevatorId); FinishMaintenance(car);
        }
    }

    public void EmergencyStop(int elevatorId)
    {
        lock (_sync) Stop(Find(elevatorId), "EmergencyStopped");
    }

    private void Stop(Car car, string reason)
    {
        car.Mode = OperationalMode.EmergencyStopped; ReturnWaiting(car);
        Emit(reason, car.Config.Id);
    }

    public void ResumeService(int elevatorId)
    {
        lock (_sync)
        {
            var car = Find(elevatorId);
            if (car.Mode is not (OperationalMode.Maintenance or OperationalMode.EmergencyStopped))
                throw new InvalidOperationException("Only a stopped car can resume service.");
            car.Mode = OperationalMode.Normal; Progress(car); Emit("ServiceResumed", elevatorId);
        }
    }

    private void ReturnWaiting(Car car)
    {
        foreach (var trip in car.Trips.Where(t => t.State == TripState.Assigned).ToArray())
        {
            car.Trips.Remove(trip); trip.State = TripState.Waiting; trip.CarId = null;
            Emit("RequestRequeued", car.Config.Id, trip.Request.Trip.Id);
        }
    }

    private void FinishMaintenance(Car car)
    {
        if (car.Mode == OperationalMode.Draining && car.Trips.Count == 0 && car.Elevator.State != ElevatorState.DOOR_OPEN)
        { car.Mode = OperationalMode.Maintenance; Emit("MaintenanceStarted", car.Config.Id); }
    }

    public void CheckTimeouts() { lock (_sync) CheckTimeoutsCore(); }
    private void CheckTimeoutsCore()
    {
        foreach (var car in _cars.Where(c => c.Mode is OperationalMode.Normal or OperationalMode.Draining))
            if (car.Trips.Count > 0 && _clock.GetElapsedTime(car.LastProgress) >= _stuckTimeout)
                Stop(car, "StuckTimeout");
    }

    public EnterpriseAnalytics GetAnalytics()
    {
        lock (_sync)
        {
            var waits = _waitSamples.Order().ToArray();
            return new(_tick, _submitted, _completed, (int)(_submitted - _completed),
                _pickups == 0 ? 0 : (double)_waitTotal / _pickups,
                waits.Length == 0 ? 0 : waits[(int)Math.Ceiling(waits.Length * .95) - 1],
                _completed == 0 ? 0 : (double)_travelTotal / _completed, _distance,
                _moving, _doors, _idle, _unavailable, _tick == 0 ? 0 : (double)_completed / _tick,
                _assignmentCount, _assignmentCount == 0 ? 0 : _assignmentTotal / _assignmentCount, _assignmentMax,
                _trips.Values.Where(t => t.State == TripState.Waiting).Select(t => _tick - t.SubmittedTick).DefaultIfEmpty().Max());
        }
    }

    private Car Find(int id) => _cars.FirstOrDefault(c => c.Config.Id == id)
        ?? throw new ArgumentOutOfRangeException(nameof(id));
    private void Progress(Car car) => car.LastProgress = _clock.GetTimestamp();
    private void Emit(string name, int? elevatorId = null, Guid? requestId = null)
    {
        var car = elevatorId is null ? null : Find(elevatorId.Value);
        _events.Enqueue(new(_tick, name, elevatorId, requestId,
            car?.Elevator.CurrentFloor, car?.Elevator.State, car?.Mode));
        if (_events.Count > _historyLimit) _events.Dequeue();
    }

    private sealed class Car(ElevatorConfiguration config)
    {
        public ElevatorConfiguration Config { get; } = config;
        public Elevator Elevator { get; } = new(config.Id, 1, 20, config.InitialFloor);
        public OperationalMode Mode { get; set; }
        public Direction Direction { get; set; } = Direction.UP;
        public List<Trip> Trips { get; } = new();
        public long LastProgress { get; set; }
    }

    private sealed class Trip(EnterpriseRequest request, long submittedTick, long sequence)
    {
        public EnterpriseRequest Request { get; } = request;
        public long SubmittedTick { get; } = submittedTick;
        public long Sequence { get; } = sequence;
        public TripState State { get; set; }
        public int? CarId { get; set; }
        public long? PickupTick { get; set; }
        public long? CompletedTick { get; set; }
    }
}
