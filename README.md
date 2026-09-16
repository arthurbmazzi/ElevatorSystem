# Elevator System — Interview Exercise

C# / .NET 8 foundation for an exercise that will grow through three difficulty
levels. The original specifications are saved in [REQUIREMENTS.md](REQUIREMENTS.md).
The user will define each level before its implementation.

## Initial classes

- `ElevatorState` and `Direction` contain the exact specified enum values.
- `PassengerRequest` stores immutable pickup and destination floors and derives
  the passenger's direction. Equal pickup and destination floors are rejected.
- `Elevator` holds its ID, floor limits, current floor, state, and pending queue.
- `ElevatorController` creates the fleet, validates building floor limits, and
  atomically assigns requests to the elevator with the fewest queued requests.
  Ties go to the lowest ID. Each elevator preserves request arrival order.

Negative floors are supported. Floor limits are inclusive. All elevators start
at the minimum floor, in `IDLE`. Invalid arguments raise `ArgumentException`
or `ArgumentOutOfRangeException`; null requests raise `ArgumentNullException`.

Assignment and queue access use private locks. Locks are acquired in
controller-to-elevator order, and elevators never call back into the controller.
Requests are immutable; queue snapshots and the fleet list are read-only.

## Scope of this first step

Requests can be assigned and inspected. Elevators do not move or consume their
queues yet, so their floor and state remain at the initial values. Movement,
pickup-before-drop-off execution, door transitions, maintenance handling,
stuck-elevator timeouts, and worker exception handling await the later levels.
The pending queues are currently unbounded; capacity limits and sustained-load
behavior also remain to be implemented. The 100 ms assignment target has not
been benchmarked. The concurrency check verifies assignment correctness only.

## Example

```csharp
using ElevatorSystem;

var controller = new ElevatorController(elevatorCount: 2, minFloor: -1, maxFloor: 10);
int elevatorId = controller.SubmitRequest(new PassengerRequest(0, 5));
Elevator assigned = controller.Elevators[elevatorId];
Console.WriteLine(assigned.PendingRequestCount); // 1
```

## Build and check

Requires a .NET SDK supporting .NET 8. No external packages are used.

```powershell
dotnet build ElevatorSystem.sln
dotnet run --project tests/ElevatorSystem.Checks
```

The checks are a standalone console runner that exits with a failure if an
assertion fails; they do not require a test framework or NuGet test packages.
