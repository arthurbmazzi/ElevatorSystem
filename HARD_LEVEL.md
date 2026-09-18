# Hard level: implementation and decisions

For Swagger presentations and TXT event logs, see [API_DEMO.md](API_DEMO.md).
The API uses the same library with a 300-second timeout for manual pauses;
the 30 seconds described below remain the library default.

The hard level uses `EnterpriseElevatorSystem`. The `ElevatorController` (easy)
and `ElevatorSystem` (medium) coordinators retain their APIs and FIFO behavior.
The hard coordinator reuses `Elevator` for movement, bounds, and door interlocks,
preserving the guarantees already tested in earlier levels.

## Run

```powershell
dotnet build ElevatorSystem.sln -m:1
dotnet test ElevatorSystem.sln -m:1
dotnet run --project src/ElevatorSystem.Api
```

Use Swagger at http://localhost:5080/swagger to demonstrate passengers, VIPs, cargo,
maintenance, emergency stops, redistribution, and recovery. See API_DEMO.md for
the manual steps and samples/README.md for JSON inputs.

Library example:

```csharp
var system = new EnterpriseElevatorSystem(EnterpriseFleetFactory.CreateDefault());
var access = new AccessProfile(new[] { 1, 10, 20 }, isVip: true);
var request = new EnterpriseRequest(new Request(1, 20), access);
system.SubmitRequest(request);
await system.ProcessRequestsAsync();
var trip = system.Trips.Single(t => t.Id == request.Trip.Id);
var metrics = system.GetAnalytics();
```

`BalanceLoad()` assigns trips without movement. `ProcessTick()` advances the
simulation one step at a time so callers can inspect pickups and apply controls.

## Organization and responsibilities

| File/area | Responsibility |
| --- | --- |
| `Domain/EnterpriseModels.cs` | Immutable configuration, types, permissions, requests, and snapshots |
| `Application/EnterpriseElevatorSystem.cs` | Admission, dispatch, lifecycle, operations, and atomic observations |
| `Application/Scheduling/IStopSchedulingStrategy.cs` | Routing contract and FIFO implementation |
| `Composition/EnterpriseFleetFactory.cs` | Default fleet composition |
| `tests/ElevatorSystem.Tests` | xUnit tests for all levels |

Configuration uses composition instead of three subclasses duplicating movement
and door behavior. Routing is injectable and receives a read-only list. The domain
does not depend on the console, xUnit, or infrastructure. The coordinator keeps
related fleet decisions together, without unused service abstractions.

## Types, capacity, and restrictions

The default fleet has three cars serving a building with floors 1 through 20:

| ID | Type | Served floors | Capacity |
| --- | --- | --- | --- |
| 0 | Local | 1 through 20 | 1000 kg |
| 1 | Express | 1, 10, 15, 20 | 1000 kg |
| 2 | Freight | 1 through 20 | 3000 kg |

Custom configurations support 3–5 cars with unique IDs and individual service floors
and capacities. Express passes through intermediate floors without opening its
doors there. Automatic transfers are not supported. Freight serves cargo only;
Local and Express serve passengers. Every request must have a positive weight.

Dispatch reserves capacity for all assigned trips, including passengers who have
not boarded. This conservative policy prevents overload but may reduce occupancy
compared with planning capacity separately for each route segment.

Before accepting a request, the application validates access to both pickup and
destination and checks that a physically compatible car exists. Impossible requests
are rejected without changing counters. If a compatible car exists but is unavailable
or has insufficient remaining capacity, the trip waits while other requests proceed.

`AccessProfile` represents permissions supplied by a trusted caller; it is not a
login system. `IsVip` does not grant access to additional floors. With real users,
this profile would come from server-side authorization.

## Trips, dispatch, and FIFO

Each trip retains its `Request.Id` and follows this lifecycle:

```text
Waiting -> Assigned -> Onboard -> Completed
              |
              +-> Waiting (redistribution before pickup)
```

The destination enters the route only after pickup. Completion occurs when the
doors open at the destination; processing also closes the doors before finishing.
Each transition identifies its trip rather than removing passengers by queue position.

Dispatch first filters by availability, type, floors, and capacity. It then simulates
the routing policy to estimate time to pickup, including movement, earlier pickups,
activated destinations, and door operations. Ties use trip count and car ID. This
estimate does not predict future arrivals.

FIFO serves trips in the order assigned to each car: pick up the passenger, reach
the destination, then serve the next trip. It does not reorder stops by proximity or
direction. VIP priority still applies during assignment, before entering this sequence.
The routing interface remains an extension point for studying other algorithms later.

VIP requests receive a 20-tick head start in dispatch order. The key uses arrival
tick minus this bonus, followed by timestamp and submission sequence. A regular trip
that has waited more than 20 ticks therefore precedes newly arriving VIPs. This
prevents indefinite overtaking by new VIPs when compatible capacity is available and
the simulation advances. It does not guarantee service when all compatible cars
are unavailable. Assigned trips are not interrupted to give way to VIP requests.

## Maintenance, emergency stops, and timeouts

`OperationalMode` is independent of `ElevatorState`: operational mode must coexist
with physical door state. The hard level represents maintenance in `Mode`, keeping
the physical state in `State`; the legacy enum remains compatible.

- `RequestMaintenance(id)`: Normal -> Draining; requeues trips that have not boarded,
  completes onboard trips, and enters Maintenance with doors closed.
- `EmergencyStop(id)`: blocks subsequent movement and door steps; redistributes only
  trips that have not boarded. Onboard passengers remain in the same car. An emergency
  can interrupt draining for maintenance.
- `ResumeService(id)`: explicitly returns Maintenance/EmergencyStopped to Normal.
  Physical door state is preserved; open doors are closed before movement.
- `CheckTimeouts()`: uses `TimeProvider.GetTimestamp()` to detect cars with work but
  no progress for 30 seconds (configurable), then triggers an emergency stop.

The watchdog is checked before each tick and can also be called by the host.
There is no hidden timer: if neither simulation nor watchdog is called, detection
cannot run autonomously. A prolonged real-time pause with assigned work counts as
lack of progress; inject `TimeProvider` for controlled simulations and tests.
The watchdog cannot interrupt a routing callback that blocks indefinitely. Routing
policies must execute quickly and perform no I/O.

The simulation uses integer floor positions. Emergency stops occur between atomic
steps; braking between floors and physical rescue procedures are not modeled.

## Concurrency, failures, and limits

A fleet lock protects selection plus assignment, queues, states, and metrics. Lock
order is fleet -> elevator. Hard-level cars are not exposed for external mutation;
callers receive immutable snapshots. `ProcessTick()` advances each car once.
`ProcessRequestsAsync()` serializes processing calls with a semaphore and yields
between ticks. Submissions and operational controls can run between ticks.

The hard level uses deterministic coordinated ticks. Medium-level processing with
one worker per car remains available. The hard level does not create a worker per
car, because simulated time would then depend on operating-system scheduling.

Cancellation preserves committed work and releases the semaphore. A policy that
returns an invalid decision or throws does not assign the current request; earlier
operations remain committed. Custom policies must be pure, fast, and must not call
back into the coordinator.

Processing returns when no car can advance, even if blocked trips remain. Check
`Pending` and resume after restoring service. Requests arriving after the last tick
require another processing call, as in earlier levels.

Default limits are 10,000 pending trips and 1,000 entries in each event, completed-trip,
and wait-sample history. A full queue rejects new trips. Duplicate IDs are rejected
while retained in history; deduplication is not durable after history expiration,
and fleet state does not survive a restart.

## Monitoring and analytics

`Events` returns a window of structured events containing tick, event type, car ID,
trip ID when applicable, and floor/state/mode captured at the transition. The API captures every event through `IEnterpriseEventSink`
and writes TXT files outside the fleet lock, independently of this bounded window.
Files rotate and have retention limits; they do not restore fleet state.

`GetAnalytics()` provides:

- Submitted, completed, pending, and the age of the oldest unassigned trip.
- Average wait for all boarded trips and P95 for the recent pickup window.
- Average time between pickup and drop-off, and throughput in trips per tick.
- Floors traveled and accumulated car-ticks moving, operating doors, idle, or unavailable.
- Assignment count and average/maximum selection-plus-assignment duration in real milliseconds.

One tick represents one floor movement or one door operation per car. Simulated
averages use ticks, not real seconds. Assignment latency uses `Stopwatch` and measures
each successful assignment inside the lock; it excludes waiting to acquire the lock
and time in the queue. The console benchmark has been removed with the demo project.
The API smoke test checks correctness; it does not measure an end-to-end latency SLA.

Dividing accumulated car-ticks by `Tick * carCount` yields utilization percentages.
Configuration and history limits bound retained data; heap profiling and prolonged
production load testing have not been performed.

## xUnit tests

`ElevatorSystem.Tests` replaced `ElevatorSystem.Checks`. There is no longer a `Main`
method manually invoking checks. `[Fact]` and `[Theory]` methods are discovered by
`dotnet test` and Test Explorer.

- `FoundationTests`: validation, snapshots, strategies, atomicity, and 128 callers.
- `EasyLevelTests`: movement, FIFO, doors, paired trips, logging, and concurrency.
- `MediumLevelTests`: priority, distribution, cancellation, failures, and concurrent workers.
- `HardLevelTests`: types/capacity, VIP access, FIFO, lifecycle, maintenance, emergency,
  timeout, aging, limits, metrics, snapshots, concurrency, and complete event capture.

Tests use xUnit assertions. Test-level parallelism is disabled because a legacy test
captures the process-wide `Console.Out`; concurrency tests still create concurrent
operations internally. xUnit1031 is suppressed for inherited tests using gates and
threads with explicit timeouts. The operational timeout uses a fake clock rather
than real sleeps.

Tests avoid unstable timing assertions. They verify FIFO ordering and completion
of each trip before the next pickup in the same car. GET /analytics exposes current
assignment timing; results depend on JIT, hardware, and load and do not establish
an end-to-end SLA.
