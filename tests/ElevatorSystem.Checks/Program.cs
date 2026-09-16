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

Console.WriteLine("All foundation checks passed, including 128 concurrent request submissions.");

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
