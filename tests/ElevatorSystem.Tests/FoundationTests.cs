using ElevatorSystem;

public class FoundationTests
{
    [Fact]
    public void InputValidation()
    {
Assert.True(new PassengerRequest(-1, 10).Direction == Direction.UP, "Upward direction");
Assert.True(new PassengerRequest(10, -1).Direction == Direction.DOWN, "Downward direction");
Assert.ThrowsAny<ArgumentException>(() => new PassengerRequest(2, 2));
Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new ElevatorController(0, 0, 10));
Assert.ThrowsAny<ArgumentException>(() => new ElevatorController(1, 10, 0));
Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new Elevator(0, 0, 10, 11));


    }
    [Fact]
    public void AssignmentAndSnapshots()
    {
var controller = new ElevatorController(2, -1, 10);
Assert.True(controller.Elevators.All(e => e.State == ElevatorState.IDLE && e.CurrentFloor == -1),
    "Initial state and floor");
Assert.ThrowsAny<ArgumentNullException>(() => controller.SubmitRequest(null!));
Assert.ThrowsAny<ArgumentOutOfRangeException>(() => controller.SubmitRequest(new PassengerRequest(-2, 5)));
Assert.ThrowsAny<ArgumentOutOfRangeException>(() => controller.SubmitRequest(new PassengerRequest(0, 11)));
Assert.True(controller.Elevators.Sum(e => e.PendingRequestCount) == 0, "Rejected requests leave queues unchanged");
Assert.True(controller.SubmitRequest(new PassengerRequest(-1, 10)) == 0, "First assignment and inclusive bounds");
Assert.True(controller.SubmitRequest(new PassengerRequest(10, -1)) == 1, "Shortest queue assignment");

var snapshot = controller.Elevators[0].GetPendingRequests();
Assert.ThrowsAny<NotSupportedException>(() => ((IList<PassengerRequest>)snapshot).Clear());
Assert.ThrowsAny<NotSupportedException>(() => ((IList<Elevator>)controller.Elevators).Clear());
controller.SubmitRequest(new PassengerRequest(0, 3));
Assert.True(snapshot.Count == 1, "Snapshots do not change after subsequent submissions");
Assert.True(controller.Elevators[0].GetPendingRequests()[0].PickupFloor == -1, "Arrival order preserved");


    }
    [Fact]
    public void ConcurrentAssignment()
    {
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
Assert.True(allReady, "All concurrent callers reached the start gate");
foreach (var thread in threads)
{
    Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Concurrent assignment completes without deadlock");
}
Assert.True(failures.IsEmpty, "Concurrent submissions do not throw");
var queued = concurrentController.Elevators.SelectMany(e => e.GetPendingRequests()).ToArray();
Assert.True(queued.Length == requestCount, "No requests lost");
Assert.True(queued.Distinct().Count() == requestCount, "No requests duplicated");
Assert.True(concurrentController.Elevators.All(e => e.PendingRequestCount == requestCount / 4),
    "Assignment stays balanced under concurrency");
for (int index = 0; index < requestCount; index++)
{
    Assert.True(concurrentController.Elevators[assignments[index]].GetPendingRequests().Contains(requests[index]),
        "Returned elevator owns the request");
}


    }
    [Fact]
    public void SelectionStrategies()
    {
var request = new PassengerRequest(0, 10);
IReadOnlyList<ElevatorSnapshot> candidates = Array.AsReadOnly(new[]
{
    new ElevatorSnapshot(9, 1, 0),
    new ElevatorSnapshot(3, 8, 0),
    new ElevatorSnapshot(1, 0, 5)
});
Assert.True(new ShortestQueueStrategy().SelectElevator(request, candidates) == 3,
    "Shortest queue ties use ID rather than input order");
Assert.True(new NearestPickupStrategy().SelectElevator(request, candidates) == 1,
    "Nearest pickup can choose a different elevator from shortest queue");
foreach (var strategy in new IElevatorSelectionStrategy[] { new ShortestQueueStrategy(), new NearestPickupStrategy() })
{
    Assert.ThrowsAny<ArgumentNullException>(() => strategy.SelectElevator(null!, candidates));
    Assert.ThrowsAny<ArgumentNullException>(() => strategy.SelectElevator(request, null!));
    Assert.ThrowsAny<ArgumentException>(() => strategy.SelectElevator(request, Array.Empty<ElevatorSnapshot>()));
    Assert.True(strategy.SelectElevator(request, new[] { new ElevatorSnapshot(42, 0, 1) }) == 42,
        "Strategies return candidate IDs, not indexes");
    Assert.True(strategy.SelectElevator(request, new[] { new ElevatorSnapshot(9, 1, 0), new ElevatorSnapshot(3, -1, 0) }) == 3,
        "Policy ties consistently select the lowest ID");
}
Assert.True(new NearestPickupStrategy().SelectElevator(new PassengerRequest(int.MinValue, 0),
    new[] { new ElevatorSnapshot(0, int.MaxValue, 0), new ElevatorSnapshot(1, 0, 0) }) == 1,
    "Distance arithmetic handles the full integer floor range");


    }
    [Fact]
    public void InjectedPoliciesAndFailureAtomicity()
    {
var request = new PassengerRequest(0, 10);
Assert.ThrowsAny<ArgumentNullException>(() => new ElevatorController(2, 0, 10, null!));
var customPolicy = new RecordingStrategy();
var customController = new ElevatorController(2, 0, 10, customPolicy);
Assert.True(customController.SubmitRequest(request) == 1, "Injected strategy controls assignment");
var policySnapshot = customPolicy.LastSnapshot!;
Assert.ThrowsAny<NotSupportedException>(() => ((IList<ElevatorSnapshot>)policySnapshot).Clear());
customController.SubmitRequest(new PassengerRequest(2, 8));
Assert.True(policySnapshot[1].PendingRequestCount == 0 && customPolicy.LastSnapshot![1].PendingRequestCount == 1,
    "Strategy snapshots remain isolated and new selections observe previous enqueues");
Assert.True(customController.Elevators[1].GetPendingRequests()[0] == request, "Custom policy preserves FIFO");
Assert.ThrowsAny<ArgumentOutOfRangeException>(() => customController.SubmitRequest(new PassengerRequest(-1, 5)));
Assert.True(customPolicy.Calls == 2, "Invalid requests never reach the strategy");
foreach (bool shouldThrow in new[] { false, true })
{
    var failing = new ElevatorController(2, 0, 10, new FailingOnceStrategy(shouldThrow));
    Assert.ThrowsAny<InvalidOperationException>(() => failing.SubmitRequest(request));
    Assert.True(failing.Elevators.All(e => e.PendingRequestCount == 0), "Policy failure leaves all queues unchanged");
    Assert.True(failing.SubmitRequest(request) == 0, "Assignment lock is released after policy failure");
}

    }
    [Fact]
    public void IndependentFleetsAndAlternateConcurrency()
    {
var independent = new ElevatorController(2, 0, 10);
Assert.True(independent.Elevators.All(e => e.PendingRequestCount == 0), "Factory creates independently owned fleets");
var nearestController = new ElevatorController(2, 0, 10, new NearestPickupStrategy());
Parallel.For(0, 128, _ => nearestController.SubmitRequest(new PassengerRequest(0, 10)));
Assert.True(nearestController.Elevators[0].PendingRequestCount == 128,
    "Alternate strategy works under concurrent submission");


    }
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
