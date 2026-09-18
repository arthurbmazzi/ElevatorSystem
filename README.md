# Elevator System — Interview Exercise

Ready-to-copy JSON files and a presentation walkthrough: [samples/README.md](samples/README.md).

## REST API and manual presentation

```powershell
dotnet run --project src/ElevatorSystem.Api
```

Open **http://localhost:5080/swagger** to submit trips as JSON, inspect the fleet,
advance the simulation, and trigger maintenance or emergency stops. In Visual Studio,
select `ElevatorSystem.Api` as the startup project. Rotating TXT logs are stored in
`src/ElevatorSystem.Api/logs/`. See [API_DEMO.md](API_DEMO.md) for the presentation
walkthrough, examples, HTTP status codes, and timeout configuration.

The hard level uses FIFO per car and retains elevator types, capacity, VIP priority,
and operational modes. The console described below remains available as an alternative.

C# / .NET 8 implementation of the **easy, medium and hard elevator-system levels**.
The elevator starts at floor 1 in `IDLE`, serves floor requests in FIFO order, and
logs each movement and door transition. The medium level adds 3–5 elevators serving floors 1–20.

See [REQUIREMENTS.md](REQUIREMENTS.md) for requirements and engineering guidelines,
and [ARCHITECTURE.md](ARCHITECTURE.md) for the SOLID mapping and design decisions.

The hard implementation, APIs, policies, metrics, limitations and xUnit migration
are explained in English in [HARD_LEVEL.md](HARD_LEVEL.md).

```powershell
dotnet run --project src/ElevatorSystem.Demo --no-launch-profile -- --hard
dotnet run --project src/ElevatorSystem.Demo --no-launch-profile -- --benchmark
```

## Run

Requires a .NET SDK supporting .NET 8. The library has no external packages; the test project uses xUnit and Microsoft.NET.Test.Sdk.

```powershell
dotnet build ElevatorSystem.sln
dotnet run --project src/ElevatorSystem.Demo --no-launch-profile
dotnet test ElevatorSystem.sln -m:1
```

The demo serves floors **3 → 8 → 6 → 1**, opening and closing the doors at each
stop, then finishes at floor 1 in `IDLE`. Tests are discovered by xUnit and can also run in Visual Studio Test Explorer.

### Interactive console

```powershell
dotnet run --project src/ElevatorSystem.Demo -- --interactive
```

Commands follow the API naming and are case-insensitive. Use UP or DOWN for direction. Type one command per line, for example:

```text
RequestElevator 3 UP
RequestDestination 8
Status
ProcessRequests
RequestElevator 6 DOWN
RequestDestination 1
ProcessRequests
Exit
```

`RequestElevator` queues a pickup, and `RequestDestination` queues a destination (floors 1–10).
`ProcessRequests` serves the FIFO queue and prints movement and door logs to the console.
`Status` displays the current floor, state, and queue; `Help` lists commands.
Invalid input prints an error and lets you try again. Processing is synchronous,
without real-time delays; enter the next command after processing finishes.
The default launch profile starts interactive mode, including when running from
Visual Studio with F5 or Ctrl+F5. Set `ElevatorSystem.Demo` as the startup project.
To run the fixed demo, use `dotnet run --project src/ElevatorSystem.Demo --no-launch-profile`.

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
processing operations require exactly one elevator. Multi-car execution awaits a
later level.

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

Run a concurrent 24-passenger example:

```powershell
dotnet run --project src/ElevatorSystem.Demo --no-launch-profile -- --medium
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
