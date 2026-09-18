using ElevatorSystem;

public class HardLevelTests
{
    private static readonly AccessProfile AllFloors = new(Enumerable.Range(1, 20));
    private static EnterpriseRequest Request(int from = 1, int to = 20, bool vip = false,
        TransportKind kind = TransportKind.Passenger, decimal weight = 75) =>
        new(new Request(from, to), new AccessProfile(AllFloors.AllowedFloors, vip), kind, weight);
    private static EnterpriseElevatorSystem Create(TimeProvider? clock = null, int history = 1000,
        IStopSchedulingStrategy? routing = null) => new(EnterpriseFleetFactory.CreateDefault(),
            clock: clock, historyLimit: history, routing: routing);

    [Fact]
    public void TypesRestrictStopsCargoAndCapacity()
    {
        var system = Create();
        var local = Request(3, 7);
        var freight = Request(kind: TransportKind.Freight, weight: 2000);
        system.SubmitRequest(local); system.SubmitRequest(freight); system.BalanceLoad();
        Assert.Equal(0, system.Trips.Single(t => t.Id == local.Trip.Id).ElevatorId);
        Assert.Equal(2, system.Trips.Single(t => t.Id == freight.Trip.Id).ElevatorId);
        Assert.Throws<ArgumentException>(() => system.SubmitRequest(Request(kind: TransportKind.Freight, weight: 3001)));
        Assert.All(system.Elevators, e => Assert.True(e.ReservedKg <= (e.Type == ElevatorType.Freight ? 3000 : 1000)));
    }

    [Fact]
    public async Task ExpressOnlyOpensAtConfiguredFloors()
    {
        var system = Create(); system.RequestMaintenance(0);
        system.SubmitRequest(Request(1, 15));
        await system.ProcessRequestsAsync();
        Assert.Equal(1, system.Trips.Single().ElevatorId);
        Assert.Equal(15, system.Elevators.Single(e => e.Id == 1).Floor);
        Assert.Equal(2, system.Events.Count(e => e.Name == "DoorsOpened"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VipNeverBypassesFloorPermissions(bool vip)
    {
        var system = Create();
        Assert.Throws<UnauthorizedAccessException>(() => system.SubmitRequest(
            new(new Request(1, 20), new AccessProfile(new[] { 1, 2 }, vip))));
        Assert.Empty(system.Trips); Assert.Equal(0, system.GetAnalytics().Submitted);
    }

    [Fact]
    public void UnavailableRequestDoesNotBlockCargo()
    {
        var system = Create(); system.RequestMaintenance(0); system.RequestMaintenance(1);
        system.SubmitRequest(Request()); system.SubmitRequest(Request(kind: TransportKind.Freight));
        system.BalanceLoad();
        Assert.Contains(system.Trips, t => t.State == TripState.Waiting);
        Assert.Contains(system.Trips, t => t.State == TripState.Assigned && t.ElevatorId == 2);
    }

    [Fact]
    public async Task MaintenanceRequeuesWaitingAndDrainsOnboard()
    {
        var system = Create(); system.RequestMaintenance(1);
        var onboard = Request(1, 3); system.SubmitRequest(onboard); system.ProcessTick();
        var waiting = Request(4, 8); system.SubmitRequest(waiting); system.BalanceLoad();
        system.RequestMaintenance(0);
        Assert.Equal(TripState.Onboard, system.Trips.Single(t => t.Id == onboard.Trip.Id).State);
        Assert.Equal(TripState.Waiting, system.Trips.Single(t => t.Id == waiting.Trip.Id).State);
        await system.ProcessRequestsAsync();
        Assert.Equal(OperationalMode.Maintenance, system.Elevators[0].Mode);
        Assert.Equal(ElevatorState.IDLE, system.Elevators[0].State);
        system.ResumeService(0); await system.ProcessRequestsAsync();
        Assert.Equal(2, system.GetAnalytics().Completed);
    }

    [Fact]
    public async Task EmergencyFreezesCarAndPreservesOnboardPassenger()
    {
        var system = Create(); system.SubmitRequest(Request()); system.ProcessTick();
        int id = system.Trips.Single().ElevatorId!.Value;
        var before = system.Elevators.Single(e => e.Id == id);
        system.EmergencyStop(id);
        for (int i = 0; i < 10; i++) system.ProcessTick();
        var stopped = system.Elevators.Single(e => e.Id == id);
        Assert.Equal(before.Floor, stopped.Floor); Assert.Equal(before.State, stopped.State);
        Assert.Equal(TripState.Onboard, system.Trips.Single().State);
        system.ResumeService(id); await system.ProcessRequestsAsync();
        Assert.Equal(1, system.GetAnalytics().Completed);
    }

    [Fact]
    public void StuckTimeoutUsesInjectedMonotonicClock()
    {
        var clock = new ManualClock(); var system = Create(clock);
        system.SubmitRequest(Request(3, 7)); system.BalanceLoad();
        clock.Advance(TimeSpan.FromSeconds(31)); system.CheckTimeouts();
        Assert.Equal(OperationalMode.EmergencyStopped, system.Elevators[0].Mode);
        Assert.Equal(TripState.Waiting, system.Trips.Single().State);
        Assert.Contains(system.Events, e => e.Name == "StuckTimeout");
    }

    [Fact]
    public void FifoKeepsInsertionOrderRegardlessOfDistance()
    {
        var fifo = new FifoStopSchedulingStrategy();
        var stops = new List<ScheduledStop> { new(Guid.NewGuid(), 12, Direction.UP, true),
            new(Guid.NewGuid(), 3, Direction.DOWN, true), new(Guid.NewGuid(), 8, Direction.UP, true) };
        var first = fifo.SelectNext(5, Direction.UP, stops);
        Assert.Equal(12, first.Stop.Floor);
        stops.Remove(first.Stop);
        var second = fifo.SelectNext(12, first.Direction, stops);
        Assert.Equal(3, second.Stop.Floor);
        Assert.Equal(Direction.DOWN, second.Direction);
    }

    [Fact]
    public async Task EveryDestinationFollowsItsOwnPickupUnderFifo()
    {
        var system = Create(history: 10000);
        foreach (var request in new[] { Request(8, 2), Request(3, 15), Request(9, 4), Request(1, 20) })
            system.SubmitRequest(request);
        await system.ProcessRequestsAsync();
        Assert.Equal(4, system.GetAnalytics().Completed);
        foreach (var trip in system.Trips)
        {
            Assert.True(trip.PickupTick < trip.CompletedTick);
            Assert.Single(system.Events, e => e.RequestId == trip.Id && e.Name == "PassengerDroppedOff");
        }
    }

    [Fact]
    public void VipHeadStartIsBoundedByWaitingAge()
    {
        var system = Create(); system.RequestMaintenance(0); system.RequestMaintenance(1);
        var old = Request(weight: 1000); system.SubmitRequest(old);
        for (int i = 0; i < 21; i++) system.ProcessTick();
        system.SubmitRequest(Request(vip: true, weight: 1000));
        system.ResumeService(0); system.BalanceLoad();
        Assert.Equal(TripState.Assigned, system.Trips.Single(t => t.Id == old.Trip.Id).State);
    }

    [Fact]
    public void VipWinsAmongEquallyOldRequests()
    {
        var system = Create(); system.RequestMaintenance(1);
        system.SubmitRequest(Request(weight: 1000)); var vip = Request(vip: true, weight: 1000);
        system.SubmitRequest(vip); system.BalanceLoad();
        Assert.Equal(TripState.Assigned, system.Trips.Single(t => t.Id == vip.Trip.Id).State);
    }

    [Fact]
    public async Task MetricsMatchKnownTripAndHistoryIsBounded()
    {
        var system = Create(history: 2); system.SubmitRequest(Request(1, 2));
        await system.ProcessRequestsAsync(); var metrics = system.GetAnalytics();
        Assert.Equal(1, metrics.Completed); Assert.Equal(1, metrics.FloorsTravelled);
        Assert.Equal(1, metrics.AverageWaitTicks); Assert.Equal(3, metrics.AverageTravelTicks);
        Assert.True(metrics.ThroughputPerTick > 0); Assert.True(system.Events.Count <= 2);
        for (int i = 0; i < 5; i++) { system.SubmitRequest(Request(1, 2)); await system.ProcessRequestsAsync(); }
        Assert.Equal(6, system.GetAnalytics().Completed); Assert.Equal(2, system.Trips.Count);
    }

    [Fact]
    public async Task ConcurrentSubmissionsAndProcessorsCompleteExactlyOnce()
    {
        var system = Create(history: 10000);
        await Task.WhenAll(Enumerable.Range(0, 128).Select(i => Task.Run(() => system.SubmitRequest(Request(i % 10 + 1, 20)))));
        await Task.WhenAll(system.ProcessRequestsAsync(), system.ProcessRequestsAsync()).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(128, system.GetAnalytics().Completed); Assert.Equal(0, system.GetAnalytics().Pending);
        Assert.Equal(128, system.Events.Count(e => e.Name == "PassengerDroppedOff"));
    }

    [Fact]
    public async Task CancellationRetainsRequestsForResumption()
    {
        var system = Create(); system.SubmitRequest(Request());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => system.ProcessRequestsAsync(cancellation.Token));
        Assert.Equal(1, system.GetAnalytics().Pending);
        await system.ProcessRequestsAsync(); Assert.Equal(1, system.GetAnalytics().Completed);
    }

    [Fact]
    public void InvalidRoutingPolicyDoesNotAssignRequest()
    {
        var system = Create(routing: new InvalidRouting()); system.SubmitRequest(Request());
        Assert.Throws<InvalidOperationException>(system.BalanceLoad);
        Assert.Equal(TripState.Waiting, system.Trips.Single().State);
        Assert.All(system.Elevators, e => Assert.Equal(0, e.PendingTrips));
    }

    [Fact]
    public void CapacityAndDuplicateRejectionsPreserveCounters()
    {
        var system = new EnterpriseElevatorSystem(EnterpriseFleetFactory.CreateDefault(), maxPending: 1);
        var request = Request(); system.SubmitRequest(request);
        Assert.Throws<ArgumentException>(() => system.SubmitRequest(request));
        Assert.Throws<InvalidOperationException>(() => system.SubmitRequest(Request()));
        Assert.Equal(1, system.GetAnalytics().Submitted);
    }

    [Fact]
    public async Task SubmissionsOverlapProcessingAndEmergencyWithoutLosingTrips()
    {
        var system = Create(history: 10000);
        system.SubmitRequest(Request());
        var processing = system.ProcessRequestsAsync();
        await Task.WhenAll(Enumerable.Range(0, 128).Select(i => Task.Run(() =>
        {
            system.SubmitRequest(Request(i % 10 + 1, 20));
            if (i == 64) system.EmergencyStop(0);
        })));
        await processing.WaitAsync(TimeSpan.FromSeconds(30));
        system.ResumeService(0);
        await system.ProcessRequestsAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(129, system.GetAnalytics().Completed);
        Assert.Equal(0, system.GetAnalytics().Pending);
        Assert.All(system.Trips, t => Assert.Equal(TripState.Completed, t.State));
    }

    [Fact]
    public async Task DefaultRoutingCompletesEachTripBeforeNextPickup()
    {
        var system = Create();
        system.RequestMaintenance(1);
        var requests = new[] { Request(1, 12), Request(1, 8), Request(1, 3) };
        foreach (var request in requests) system.SubmitRequest(request);
        await system.ProcessRequestsAsync();
        var serviceEvents = system.Events.Where(e =>
            e.Name is "PassengerPickedUp" or "PassengerDroppedOff").ToArray();
        Assert.Equal(6, serviceEvents.Length);
        for (int i = 0; i < requests.Length; i++)
        {
            Assert.Equal("PassengerPickedUp", serviceEvents[i * 2].Name);
            Assert.Equal(requests[i].Trip.Id, serviceEvents[i * 2].RequestId);
            Assert.Equal("PassengerDroppedOff", serviceEvents[i * 2 + 1].Name);
            Assert.Equal(requests[i].Trip.Id, serviceEvents[i * 2 + 1].RequestId);
        }
        Assert.Equal(3, system.GetAnalytics().Completed);
        Assert.Equal(38, system.GetAnalytics().FloorsTravelled);
    }

    [Fact]
    public async Task SnapshotIsolationAndTickAccounting()
    {
        var system = Create(); var before = system.Elevators;
        system.SubmitRequest(Request(3, 7)); await system.ProcessRequestsAsync();
        Assert.All(before, e => Assert.Equal(1, e.Floor));
        Assert.Throws<NotSupportedException>(() => ((IList<EnterpriseElevatorSnapshot>)before).Clear());
        var metrics = system.GetAnalytics();
        Assert.Equal(metrics.Tick * 3, metrics.MovingTicks + metrics.DoorTicks + metrics.IdleTicks + metrics.UnavailableTicks);
    }

    [Fact]
    public async Task EventSinkReceivesFullTripEvenWhenHistoryExpires()
    {
        var sink = new RecordingEventSink();
        var system = new EnterpriseElevatorSystem(EnterpriseFleetFactory.CreateDefault(),
            historyLimit: 2, eventSink: sink);
        system.SubmitRequest(Request(1, 20));
        await system.ProcessRequestsAsync();
        Assert.Equal(2, system.Events.Count);
        Assert.Contains(sink.Events, e => e.Name == "RequestSubmitted");
        Assert.Single(sink.Events, e => e.Name == "PassengerPickedUp");
        Assert.Single(sink.Events, e => e.Name == "PassengerDroppedOff");
        Assert.Equal("DoorsClosed", sink.Events.Last().Name);
        Assert.Equal(19, sink.Events.Count(e => e.Name == "Moved"));
    }

    private sealed class RecordingEventSink : IEnterpriseEventSink
    {
        public List<EnterpriseEvent> Events { get; } = new();
        public void Record(EnterpriseEvent entry) => Events.Add(entry);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
    private sealed class InvalidRouting : IStopSchedulingStrategy
    {
        public StopDecision SelectNext(int currentFloor, Direction direction, IReadOnlyList<ScheduledStop> stops) =>
            new(new(Guid.NewGuid(), 1, direction, true), direction);
    }
}
