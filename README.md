# Elevator System — Interview Exercise

C# / .NET 8 implementation of the **easy level: one elevator serving floors 1–10**.
The elevator starts at floor 1 in `IDLE`, serves floor requests in FIFO order, and
logs each movement and door transition. The next two levels await specification.

See [REQUIREMENTS.md](REQUIREMENTS.md) for requirements and engineering guidelines,
and [ARCHITECTURE.md](ARCHITECTURE.md) for the SOLID mapping and design decisions.

## Run

Requires a .NET SDK supporting .NET 8. No external packages are used.

```powershell
dotnet build ElevatorSystem.sln
dotnet run --project src/ElevatorSystem.Demo
dotnet run --project tests/ElevatorSystem.Checks
```

The demo serves floors **3 → 8 → 6 → 1**, opening and closing the doors at each
stop, then finishes at floor 1 in `IDLE`. The checks are a standalone console
runner that exits with a failure when an assertion fails.

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

Checks cover the original foundation and strategy contracts plus floor-by-floor
movement, FIFO service, both pickup directions, duplicate/current-floor stops,
door safety, invalid input, paired-request completion, console logs, failure
recovery, and 128 submissions overlapping processing with two processors.
