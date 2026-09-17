using System.Collections.Concurrent;
using ElevatorSystem;
using Fleet = ElevatorSystem.ElevatorSystem;

public class MediumLevelTests
{
    [Fact]
    public void FleetAndRequestValidation()
    {
        foreach (int count in new[] { 3, 4, 5 })
            Assert.True(new Fleet(count).Elevators.Count == count, "Fleet sizes");
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new Fleet(2));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new Fleet(6));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new Request(0, 20));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new Request(1, 21));
        Assert.ThrowsAny<ArgumentException>(() => new Request(3, 3));


    }
    [Fact]
    public void OptimizedSelection()
    {
        var policy = new OptimizedDispatchStrategy();
        var trip = new PassengerRequest(5, 15);
        Assert.True(policy.SelectElevator(trip, new[] { new ElevatorSnapshot(0, 1, 0),
            new ElevatorSnapshot(1, 4, 0) }) == 1, "Closest idle car");
        Assert.True(policy.SelectElevator(trip, new[] {
            new ElevatorSnapshot(0, 6, 0) { State = ElevatorState.MOVING_DOWN },
            new ElevatorSnapshot(1, 4, 0) { State = ElevatorState.MOVING_UP } }) == 1,
            "Same direction wins equal delay");
        Assert.True(policy.SelectElevator(trip, new[] {
            new ElevatorSnapshot(0, 5, 1) { TargetFloors = new[] { 20, 1 } },
            new ElevatorSnapshot(1, 1, 0) }) == 1, "Queued route avoids busy nearby car");


    }
    [Fact]
    public async Task TimestampPriority()
    {
        var logger = new CaptureLogger();
        var system = new Fleet(logger: logger);
        system.SubmitRequest(new Request(8, 1, 200));
        system.SubmitRequest(new Request(1, 20, 100));
        Assert.True(system.RequestQueue[0].Timestamp == 100, "Oldest timestamp first");
        system.BalanceLoad();
        await system.ProcessRequestsAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(logger.Actions.First().Description.Contains("timestamp 100"), "Priority applied during assignment");
        Assert.True(system.GetStatus().CompletedRequests == 2, "Trips complete");


    }
    [Fact]
    public async Task BalancingAndCancellation()
    {
var logger = new CaptureLogger();
        var balanced = new Fleet(logger: logger);
        for (int i = 0; i < 128; i++) balanced.SubmitRequest(new Request(1, 20));
        balanced.BalanceLoad();
        Assert.True(balanced.Elevators.All(e => e.PendingRequestCount == 32), "Identical work balanced");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await balanced.ProcessRequestsAsync(cancelled.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        Assert.True(balanced.GetStatus().PendingRequests == 128, "Cancellation retains work");
        await balanced.ProcessRequestsAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(balanced.GetStatus().CompletedRequests == 128, "Cancelled work resumes");


    }
    [Fact]
    public async Task ConcurrentSubmissionAndDrain()
    {
        using var gate = new GateLogger();
        var concurrent = new Fleet(logger: gate);
        concurrent.SubmitRequest(new Request(1, 20));
        var processing = concurrent.ProcessRequestsAsync();
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)), "Worker reaches logger");
        try
        {
            await Task.Run(() => Parallel.For(0, 128, i =>
                concurrent.SubmitRequest(new Request(i % 10 + 1, 20 - i % 10))))
                .WaitAsync(TimeSpan.FromSeconds(10));
            var snapshot = concurrent.GetStatus();
            Assert.True(snapshot.SubmittedRequests == 129, "Submissions proceed while logging blocks");
        }
        finally { gate.Release.Set(); }
        await Task.WhenAll(processing, concurrent.ProcessRequestsAsync()).WaitAsync(TimeSpan.FromSeconds(30));
        var final = concurrent.GetStatus();
        Assert.True(final.CompletedRequests == 129 && final.PendingRequests == 0, "Concurrent drain loses no trips");
        Assert.True(final.Elevators.All(e => e.State == ElevatorState.IDLE && e.TargetFloors.Count == 0), "Final safe state");
        Assert.True(gate.Actions.Count(a => a.Description.StartsWith("Assigned request")) == 129, "Exactly one assignment per trip");
        Assert.True(gate.Actions.Count(a => a.Description == "Doors opened") == 258, "Pickup and destination each served once");


    }
    [Fact]
    public async Task MidTripCancellation()
    {
        using var cancelGate = new GateLogger();
        using var midFlightCancellation = new CancellationTokenSource();
        var interrupted = new Fleet(logger: cancelGate);
        interrupted.SubmitRequest(new Request(1, 20));
        var interruptedRun = interrupted.ProcessRequestsAsync(midFlightCancellation.Token);
        Assert.True(cancelGate.Entered.Wait(TimeSpan.FromSeconds(10)), "Cancellation gate reached");
        midFlightCancellation.Cancel();
        cancelGate.Release.Set();
        try { await interruptedRun.WaitAsync(TimeSpan.FromSeconds(20)); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        Assert.True(interrupted.GetStatus().PendingRequests == 1, "Mid-trip cancellation preserves passenger");
        await interrupted.ProcessRequestsAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(interrupted.GetStatus().CompletedRequests == 1, "Mid-trip resume completes once");


    }
    [Fact]
    public async Task LoggerFailureRecovery()
    {
        var recovery = new Fleet(logger: new ThrowOnceLogger());
        recovery.SubmitRequest(new Request(1, 20));
        try { await recovery.ProcessRequestsAsync(); throw new Exception("Expected logger failure"); }
        catch (InvalidOperationException) { }
        await recovery.ProcessRequestsAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(recovery.GetStatus().CompletedRequests == 1, "Logger failure releases processing and preserves work");


    }
    [Fact]
    public void InvalidPolicyAtomicity()
    {
var logger = new CaptureLogger();
        var failing = new Fleet(selectionStrategy: new BadStrategy(), logger: logger);
        failing.SubmitRequest(new Request(1, 20));
        Assert.ThrowsAny<InvalidOperationException>(() => failing.BalanceLoad());
        Assert.True(failing.RequestQueue.Count == 1 && failing.Elevators.All(e => e.PendingRequestCount == 0),
            "Invalid strategy leaves request queued");

    }
    private class CaptureLogger : IElevatorLogger
    {
        public ConcurrentQueue<ElevatorAction> Actions { get; } = new();
        public virtual void Log(ElevatorAction action) => Actions.Enqueue(action);
    }

    private sealed class GateLogger : CaptureLogger, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public override void Log(ElevatorAction action)
        {
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Logger gate");
            base.Log(action);
        }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }

    private sealed class BadStrategy : IElevatorSelectionStrategy
    {
        public int SelectElevator(PassengerRequest request, IReadOnlyList<ElevatorSnapshot> elevators) => -1;
    }

    private sealed class ThrowOnceLogger : IElevatorLogger
    {
        private bool _thrown;
        public void Log(ElevatorAction action)
        {
            if (_thrown) return;
            _thrown = true;
            throw new InvalidOperationException("Injected logger failure");
        }
    }
}
