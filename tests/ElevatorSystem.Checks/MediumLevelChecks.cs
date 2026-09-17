using System.Collections.Concurrent;
using ElevatorSystem;
using Fleet = ElevatorSystem.ElevatorSystem;

internal static class MediumLevelChecks
{
    public static async Task RunAsync()
    {
        foreach (int count in new[] { 3, 4, 5 })
            Check(new Fleet(count).Elevators.Count == count, "Fleet sizes");
        Expect<ArgumentOutOfRangeException>(() => new Fleet(2));
        Expect<ArgumentOutOfRangeException>(() => new Fleet(6));
        Expect<ArgumentOutOfRangeException>(() => new Request(0, 20));
        Expect<ArgumentOutOfRangeException>(() => new Request(1, 21));
        Expect<ArgumentException>(() => new Request(3, 3));

        var policy = new OptimizedDispatchStrategy();
        var trip = new PassengerRequest(5, 15);
        Check(policy.SelectElevator(trip, new[] { new ElevatorSnapshot(0, 1, 0),
            new ElevatorSnapshot(1, 4, 0) }) == 1, "Closest idle car");
        Check(policy.SelectElevator(trip, new[] {
            new ElevatorSnapshot(0, 6, 0) { State = ElevatorState.MOVING_DOWN },
            new ElevatorSnapshot(1, 4, 0) { State = ElevatorState.MOVING_UP } }) == 1,
            "Same direction wins equal delay");
        Check(policy.SelectElevator(trip, new[] {
            new ElevatorSnapshot(0, 5, 1) { TargetFloors = new[] { 20, 1 } },
            new ElevatorSnapshot(1, 1, 0) }) == 1, "Queued route avoids busy nearby car");

        var logger = new CaptureLogger();
        var system = new Fleet(logger: logger);
        system.SubmitRequest(new Request(8, 1, 200));
        system.SubmitRequest(new Request(1, 20, 100));
        Check(system.RequestQueue[0].Timestamp == 100, "Oldest timestamp first");
        system.BalanceLoad();
        await system.ProcessRequestsAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Check(logger.Actions.First().Description.Contains("timestamp 100"), "Priority applied during assignment");
        Check(system.GetStatus().CompletedRequests == 2, "Trips complete");

        var balanced = new Fleet(logger: logger);
        for (int i = 0; i < 128; i++) balanced.SubmitRequest(new Request(1, 20));
        balanced.BalanceLoad();
        Check(balanced.Elevators.All(e => e.PendingRequestCount == 32), "Identical work balanced");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await balanced.ProcessRequestsAsync(cancelled.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        Check(balanced.GetStatus().PendingRequests == 128, "Cancellation retains work");
        await balanced.ProcessRequestsAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Check(balanced.GetStatus().CompletedRequests == 128, "Cancelled work resumes");

        using var gate = new GateLogger();
        var concurrent = new Fleet(logger: gate);
        concurrent.SubmitRequest(new Request(1, 20));
        var processing = concurrent.ProcessRequestsAsync();
        Check(gate.Entered.Wait(TimeSpan.FromSeconds(10)), "Worker reaches logger");
        try
        {
            await Task.Run(() => Parallel.For(0, 128, i =>
                concurrent.SubmitRequest(new Request(i % 10 + 1, 20 - i % 10))))
                .WaitAsync(TimeSpan.FromSeconds(10));
            var snapshot = concurrent.GetStatus();
            Check(snapshot.SubmittedRequests == 129, "Submissions proceed while logging blocks");
        }
        finally { gate.Release.Set(); }
        await Task.WhenAll(processing, concurrent.ProcessRequestsAsync()).WaitAsync(TimeSpan.FromSeconds(30));
        var final = concurrent.GetStatus();
        Check(final.CompletedRequests == 129 && final.PendingRequests == 0, "Concurrent drain loses no trips");
        Check(final.Elevators.All(e => e.State == ElevatorState.IDLE && e.TargetFloors.Count == 0), "Final safe state");
        Check(gate.Actions.Count(a => a.Description.StartsWith("Assigned request")) == 129, "Exactly one assignment per trip");
        Check(gate.Actions.Count(a => a.Description == "Doors opened") == 258, "Pickup and destination each served once");

        var failing = new Fleet(selectionStrategy: new BadStrategy(), logger: logger);
        failing.SubmitRequest(new Request(1, 20));
        Expect<InvalidOperationException>(() => failing.BalanceLoad());
        Check(failing.RequestQueue.Count == 1 && failing.Elevators.All(e => e.PendingRequestCount == 0),
            "Invalid strategy leaves request queued");
        Console.WriteLine("All medium-level checks passed: dispatch, priority, balancing, cancellation, and concurrent processing.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
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
}
