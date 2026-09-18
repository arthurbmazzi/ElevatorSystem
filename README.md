# Elevator System — Interview Exercise

C# / .NET 8 elevator simulator with an ASP.NET Core REST API and Swagger UI.
**ElevatorSystem.Api is the only application entry point.** The console demo has been removed.

## Run

From the repository root:

```powershell
dotnet build ElevatorSystem.sln -m:1
dotnet run --project src/ElevatorSystem.Api
```

Open [Swagger](http://localhost:5080/swagger) to submit trips as JSON, inspect the
fleet, advance the simulation, and trigger maintenance or emergency stops.

### Visual Studio startup project

In Solution Explorer, right-click **ElevatorSystem.Api** and choose
**Set as Startup Project**. Press F5 or Ctrl+F5. Its launch profile opens Swagger.
`ElevatorSystem` is the core library; `ElevatorSystem.Tests` contains tests.
Neither is the application startup project.

## Presentation and documentation

- [SYSTEM_GUIDE.md](SYSTEM_GUIDE.md): request flow, classes, FIFO, and concurrency.
- [API_DEMO.md](API_DEMO.md): API setup and manual presentation scenarios.
- [samples/README.md](samples/README.md): ready-to-copy JSON inputs and expected results.
- [LOGGING.md](LOGGING.md): TXT files, rotation, and error recovery.
- [HARD_LEVEL.md](HARD_LEVEL.md): hard-level rules, metrics, and limitations.
- [ARCHITECTURE.md](ARCHITECTURE.md): architecture, SOLID, and design patterns.
- [REQUIREMENTS.md](REQUIREMENTS.md): exercise requirements and current scope.

The API uses the hard coordinator with FIFO, Local/Express/Freight cars, capacity,
VIP priority, maintenance, emergency stops, and timeouts. TXT logs are stored in
`src/ElevatorSystem.Api/logs/`; `GET /logs/status` identifies the current file.
The easy and medium implementations remain in the library for their exercise levels.

## Verify

```powershell
dotnet test ElevatorSystem.sln -m:1
```

With the API running, `node tests/api-smoke.mjs` verifies HTTP behavior and 128
concurrent submissions. It resets the demonstration fleet before and after testing.
This is a correctness smoke test, not a latency or memory benchmark.

## Earlier library levels

The following sections document the preserved library APIs, not separate applications.

## Easy-level API

```csharp
using ElevatorSystem;

var controller = new ElevatorController(); // One elevator, floors 1–10, console log
controller.RequestElevator(3, Direction.UP); // Hall pickup button
controller.ProcessRequests();              // Arrives at 3, opens then closes doors
controller.RequestDestination(8);          // Passenger selects destination
controller.ProcessRequests();              // Arrives at 8 and finishes IDLE

Elevator elevator = controller.Elevator;
Console.WriteLine(elevator.CurrentFloor);   // 8
Console.WriteLine(elevator.TargetFloors.Count); // 0
```

Pickup calls and destination selections may also be queued before processing.
Both join the same FIFO queue. Pickup direction is validated and retained in the
arrival log; it does not change scheduling order. Duplicate stops are preserved,
and a request for the current floor opens/closes the doors without movement.
`TargetFloors` is a read-only snapshot that includes the active stop until its doors
close. `PendingRequestCount` counts unfinished requests, including the active one.

`Elevator` exposes `MoveUp()`, `MoveDown()`, `OpenDoor()`, `CloseDoor()`, and
`AddRequest(floor)` for direct simulation control. Movement changes the floor by
one and sets `MOVING_UP` or `MOVING_DOWN`. Each movement call completes one discrete
step; `OpenDoor` stops at that floor immediately, and `CloseDoor` sets `IDLE`.
Movement with open doors or beyond the floor bounds is rejected. Direct controls
return action records; controller processing forwards those records to the logger.
Use controller operations for coordinated request service.

Invalid floors and undefined directions raise `ArgumentOutOfRangeException`.
UP at floor 10 and DOWN at floor 1 raise `ArgumentException`. Validation failures
leave queues unchanged. Null dependencies raise `ArgumentNullException`.

## Patterns and extension points

- **Simple Factory:** `ElevatorFactory` constructs a fresh fleet with consistent IDs,
  initial floors, and states.
- **Strategy:** existing `IElevatorSelectionStrategy` implementations select a car
  for paired requests. Stop order inside a car uses `Queue<T>` for strict FIFO;
  car selection and stop order are separate responsibilities.
- **Dependency inversion:** pass an `IElevatorLogger` to
  `new ElevatorController(logger)` to capture actions without depending on console
  I/O in the domain. The default easy configuration uses `ConsoleElevatorLogger`.

The original configurable constructors and `SubmitRequest(PassengerRequest)`
remain available. A paired request atomically adds pickup then destination stops,
counts as one pending request, and remains in `GetPendingRequests()` until its
final doors close. Standalone floor requests are represented by `TargetFloors`,
not the paired-request compatibility snapshot.

```csharp
var controller = new ElevatorController(1, -1, 10, new ShortestQueueStrategy());
controller.SubmitRequest(new PassengerRequest(0, 5));
controller.ProcessRequests();
```

Configurable constructors preserve their previous quiet behavior and inclusive
floor bounds, including negative floors. Shortest queue remains the default car
selection policy; nearest pickup is optional and ignores queued travel. The earlier
multi-car assignment API is retained, but easy-level pickup, destination, and
processing operations require exactly one elevator. Multi-car processing is available through the separate medium and hard coordinators.

## Concurrency and scope

Requests and state transitions use private locks. Concurrent `ProcessRequests`
calls serialize. The controller releases assignment and domain locks between steps
and before logging, so requests may arrive during processing. A request arriving
after the empty-queue check remains queued for the next call.

A logger failure propagates after the corresponding transition; a later processing
call resumes remaining work without rolling back or replaying that transition.
Loggers must not recursively call processing or wait for another processing call.

The simulation is synchronous and has no real-time delays, background workers,
maintenance handling, or stuck-elevator timeout implementation. Reserved enum
values remain for compatibility. Queues are unbounded, and the 100 ms assignment
target has not been benchmarked.

xUnit tests cover the original foundation and strategy contracts plus floor-by-floor
movement, FIFO service, both pickup directions, duplicate/current-floor stops,
door safety, invalid input, paired-request completion, console logs, failure
recovery, and 128 submissions overlapping processing with two processors.

## Medium level: multiple elevators

The medium API is `ElevatorSystem.ElevatorSystem` (the class shares the library's
namespace). It owns 3–5 elevators, defaults to four, and serves floors 1–20.
The easy API and its defaults remain unchanged.

```csharp
using ElevatorSystem;
using Fleet = ElevatorSystem.ElevatorSystem;

var system = new Fleet(4);
system.SubmitRequest(new Request(3, 18));
system.SubmitRequest(new Request(20, 1));
await system.ProcessRequestsAsync();
Console.WriteLine(system.GetStatus());
```

`SubmitRequest` adds to a synchronized central priority queue. Older Unix-millisecond
timestamps go first; submission order breaks ties. `BalanceLoad` distributes that
queue using fresh snapshots after each assignment. `FindBestElevator` is advisory;
`AssignRequest` selects and enqueues atomically, bypassing central priority.
Requests are immutable and contain pickup, destination, derived direction, timestamp,
and an ID. Each submission is a trip; resubmitting the same object is not deduplicated.

The default `OptimizedDispatchStrategy` estimates pickup delay from all queued travel
and door steps, adds a four-step penalty per pending passenger, and favors a car
moving toward the pickup in the same direction when estimated costs tie. Remaining
ties use load and ID. This is a heuristic, not a guarantee of globally minimal wait.
Trips retain FIFO pickup/destination pairs; passengers are not pooled or reassigned
after dispatch. Balancing applies to central waiting requests, not onboard passengers.
Inject an existing or custom `IElevatorSelectionStrategy` to change assignment.

`ProcessRequestsAsync` runs one thread-pool worker per car and drains submitted work.
Concurrent processing calls serialize. Submissions may overlap processing; those
arriving after workers observe no work require another call. Optional `stepDelay`
adds cancellable movement/door pacing. Cancellation preserves unfinished work for
another processing call. This is a drain API, not a permanently running service.

`Elevators`, `RequestQueue`, and `GetStatus()` return immutable snapshots, including
floors, states, queued stops, submitted/completed totals, and pending counts. Owned
cars are not exposed for external mutation. Assignment, movement, door actions,
and trip completion are logged through `IElevatorLogger`; console logging is the
default. Logger calls serialize outside the fleet lock. Cross-car log order is not
physical event order. Logger failures propagate after committed transitions and
are not retried; call processing again to finish remaining work. Loggers must not
block waiting for processing or recursively invoke it. Queues remain unbounded;
maintenance, stuck-car recovery, and durable storage are not implemented.
