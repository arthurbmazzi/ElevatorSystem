using ElevatorSystem;

Check(new PassengerRequest(-1, 10).Direction == Direction.UP, "Upward direction");
Check(new PassengerRequest(10, -1).Direction == Direction.DOWN, "Downward direction");
Expect<ArgumentException>(() => new PassengerRequest(2, 2));
Expect<ArgumentOutOfRangeException>(() => new ElevatorController(0, 0, 10));
Expect<ArgumentException>(() => new ElevatorController(1, 10, 0));
Expect<ArgumentOutOfRangeException>(() => new Elevator(0, 0, 10, 11));

var controller = new ElevatorController(2, -1, 10);
Check(controller.Elevators.All(e => e.State == ElevatorState.IDLE && e.CurrentFloor == -1),
    "Initial state and floor");
Expect<ArgumentNullException>(() => controller.SubmitRequest(null!));
Expect<ArgumentOutOfRangeException>(() => controller.SubmitRequest(new PassengerRequest(-2, 5)));
Expect<ArgumentOutOfRangeException>(() => controller.SubmitRequest(new PassengerRequest(0, 11)));
Check(controller.Elevators.Sum(e => e.PendingRequestCount) == 0, "Rejected requests leave queues unchanged");
Check(controller.SubmitRequest(new PassengerRequest(-1, 10)) == 0, "First assignment and inclusive bounds");
Check(controller.SubmitRequest(new PassengerRequest(10, -1)) == 1, "Shortest queue assignment");

var snapshot = controller.Elevators[0].GetPendingRequests();
Expect<NotSupportedException>(() => ((IList<PassengerRequest>)snapshot).Clear());
Expect<NotSupportedException>(() => ((IList<Elevator>)controller.Elevators).Clear());
controller.SubmitRequest(new PassengerRequest(0, 3));
Check(snapshot.Count == 1, "Snapshots do not change after subsequent submissions");
Check(controller.Elevators[0].GetPendingRequests()[0].PickupFloor == -1, "Arrival order preserved");

// Dedicated threads and a common start gate exercise overlapping callers.
const int requestCount = 128;
var concurrentController = new ElevatorController(4, -1, 20);
var requests = Enumerable.Range(0, requestCount).Select(_ => new PassengerRequest(0, 20)).ToArray();
var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
var assignments = new int[requestCount];
using var ready = new CountdownEvent(requestCount);
using var start = new ManualResetEventSlim(false);
var threads = Enumerable.Range(0, requestCount).Select(index => new Thread(() =>
{
    ready.Signal();
    if (!start.Wait(TimeSpan.FromSeconds(30)))
    {
        failures.Enqueue(new TimeoutException("Concurrent start gate timed out."));
        return;
    }
    try
    {
        assignments[index] = concurrentController.SubmitRequest(requests[index]);
    }
    catch (Exception exception)
    {
        failures.Enqueue(exception);
    }
}) { IsBackground = true }).ToArray();

foreach (var thread in threads) thread.Start();
bool allReady = ready.Wait(TimeSpan.FromSeconds(30));
start.Set();
Check(allReady, "All concurrent callers reached the start gate");
foreach (var thread in threads)
{
    Check(thread.Join(TimeSpan.FromSeconds(30)), "Concurrent assignment completes without deadlock");
}
Check(failures.IsEmpty, "Concurrent submissions do not throw");
var queued = concurrentController.Elevators.SelectMany(e => e.GetPendingRequests()).ToArray();
Check(queued.Length == requestCount, "No requests lost");
Check(queued.Distinct().Count() == requestCount, "No requests duplicated");
Check(concurrentController.Elevators.All(e => e.PendingRequestCount == requestCount / 4),
    "Assignment stays balanced under concurrency");
for (int index = 0; index < requestCount; index++)
{
    Check(concurrentController.Elevators[assignments[index]].GetPendingRequests().Contains(requests[index]),
        "Returned elevator owns the request");
}

var request = new PassengerRequest(0, 10);
IReadOnlyList<ElevatorSnapshot> candidates = Array.AsReadOnly(new[]
{
    new ElevatorSnapshot(9, 1, 0),
    new ElevatorSnapshot(3, 8, 0),
    new ElevatorSnapshot(1, 0, 5)
});
Check(new ShortestQueueStrategy().SelectElevator(request, candidates) == 3,
    "Shortest queue ties use ID rather than input order");
Check(new NearestPickupStrategy().SelectElevator(request, candidates) == 1,
    "Nearest pickup can choose a different elevator from shortest queue");
foreach (var strategy in new IElevatorSelectionStrategy[] { new ShortestQueueStrategy(), new NearestPickupStrategy() })
{
    Expect<ArgumentNullException>(() => strategy.SelectElevator(null!, candidates));
    Expect<ArgumentNullException>(() => strategy.SelectElevator(request, null!));
    Expect<ArgumentException>(() => strategy.SelectElevator(request, Array.Empty<ElevatorSnapshot>()));
    Check(strategy.SelectElevator(request, new[] { new ElevatorSnapshot(42, 0, 1) }) == 42,
        "Strategies return candidate IDs, not indexes");
    Check(strategy.SelectElevator(request, new[] { new ElevatorSnapshot(9, 1, 0), new ElevatorSnapshot(3, -1, 0) }) == 3,
        "Policy ties consistently select the lowest ID");
}
Check(new NearestPickupStrategy().SelectElevator(new PassengerRequest(int.MinValue, 0),
    new[] { new ElevatorSnapshot(0, int.MaxValue, 0), new ElevatorSnapshot(1, 0, 0) }) == 1,
    "Distance arithmetic handles the full integer floor range");

Expect<ArgumentNullException>(() => new ElevatorController(2, 0, 10, null!));
var customPolicy = new RecordingStrategy();
var customController = new ElevatorController(2, 0, 10, customPolicy);
Check(customController.SubmitRequest(request) == 1, "Injected strategy controls assignment");
var policySnapshot = customPolicy.LastSnapshot!;
Expect<NotSupportedException>(() => ((IList<ElevatorSnapshot>)policySnapshot).Clear());
customController.SubmitRequest(new PassengerRequest(2, 8));
Check(policySnapshot[1].PendingRequestCount == 0 && customPolicy.LastSnapshot![1].PendingRequestCount == 1,
    "Strategy snapshots remain isolated and new selections observe previous enqueues");
Check(customController.Elevators[1].GetPendingRequests()[0] == request, "Custom policy preserves FIFO");
Expect<ArgumentOutOfRangeException>(() => customController.SubmitRequest(new PassengerRequest(-1, 5)));
Check(customPolicy.Calls == 2, "Invalid requests never reach the strategy");
foreach (bool shouldThrow in new[] { false, true })
{
    var failing = new ElevatorController(2, 0, 10, new FailingOnceStrategy(shouldThrow));
    Expect<InvalidOperationException>(() => failing.SubmitRequest(request));
    Check(failing.Elevators.All(e => e.PendingRequestCount == 0), "Policy failure leaves all queues unchanged");
    Check(failing.SubmitRequest(request) == 0, "Assignment lock is released after policy failure");
}
var independent = new ElevatorController(2, 0, 10);
Check(independent.Elevators.All(e => e.PendingRequestCount == 0), "Factory creates independently owned fleets");
var nearestController = new ElevatorController(2, 0, 10, new NearestPickupStrategy());
Parallel.For(0, 128, _ => nearestController.SubmitRequest(new PassengerRequest(0, 10)));
Check(nearestController.Elevators[0].PendingRequestCount == 128,
    "Alternate strategy works under concurrent submission");

Console.WriteLine("All foundation and architecture checks passed, including 128 simultaneous callers and alternate-policy concurrency.");
EasyLevelChecks.Run();
await MediumLevelChecks.RunAsync();

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException($"Check failed: {description}");
}

static void Expect<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class RecordingStrategy : IElevatorSelectionStrategy
{
    public IReadOnlyList<ElevatorSnapshot>? LastSnapshot { get; private set; }
    public int Calls { get; private set; }
    public int SelectElevator(PassengerRequest request, IReadOnlyList<ElevatorSnapshot> elevators)
    {
        Calls++;
        LastSnapshot = elevators;
        return elevators[^1].Id;
    }
}

// Intentionally violates the policy contract to exercise the application boundary.
sealed class FailingOnceStrategy(bool shouldThrow) : IElevatorSelectionStrategy
{
    private bool _hasFailed;
    public int SelectElevator(PassengerRequest request, IReadOnlyList<ElevatorSnapshot> elevators)
    {
        if (_hasFailed) return elevators[0].Id;
        _hasFailed = true;
        if (shouldThrow) throw new InvalidOperationException("Policy failure");
        return -1;
    }
}
