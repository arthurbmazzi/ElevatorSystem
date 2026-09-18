# Start here: how the elevator system works

## The system in one paragraph

This is an in-memory elevator simulator. You send a trip through Swagger, the API
validates it, and a shared fleet keeps it waiting. Dispatch chooses a compatible
car. Each car completes its assigned trips in FIFO order. You advance the simulation
manually, one tick or a full processing run at a time. Events describe every change
and are written to TXT files. There is no batch endpoint, database, or automatic movement.

## Read the code in this order

| File | What to understand |
| --- | --- |
| [Contracts.cs](src/ElevatorSystem.Api/Contracts.cs) | JSON becomes a validated domain request |
| [Program.cs](src/ElevatorSystem.Api/Program.cs) | HTTP endpoints and error responses |
| [SimulationSession.cs](src/ElevatorSystem.Api/SimulationSession.cs) | One shared fleet, command coordination, and log flushing |
| [EnterpriseFleetFactory.cs](src/ElevatorSystem/Composition/EnterpriseFleetFactory.cs) | The three default car configurations |
| [EnterpriseElevatorSystem.cs](src/ElevatorSystem/Application/EnterpriseElevatorSystem.cs) | Admission, assignment, ticks, maintenance, and metrics |
| [IStopSchedulingStrategy.cs](src/ElevatorSystem/Application/Scheduling/IStopSchedulingStrategy.cs) | FIFO selects the first trip's current stop |
| [Elevator.cs](src/ElevatorSystem/Domain/Elevator.cs) | Atomic movement and door operations |
| [FileLogProvider.cs](src/ElevatorSystem.Api/Infrastructure/FileLogProvider.cs) | TXT writing, rotation, and diagnostic status |

## Follow one request

1. **Receive:** POST /requests binds the JSON to CreateTripCommand.
2. **Validate:** ToRequest constructs Request, AccessProfile, and EnterpriseRequest.
   They reject invalid floors, identical endpoints, invalid kinds, and nonpositive weight.
3. **Accept:** SubmitRequest checks floor permissions, compatible cars, duplicate IDs,
   and the pending limit. It stores a Waiting trip and emits RequestSubmitted.
4. **Reply:** the API returns 201 with a trip ID. Acceptance does not mean assignment.
5. **Assign:** /simulation/assign or the next tick calls AssignWaiting. A selected
   car reserves weight and the trip becomes Assigned.
6. **Pick up:** the car reaches the pickup floor and opens its doors. The trip becomes Onboard.
7. **Drop off:** the car reaches the destination and opens its doors. The trip becomes Completed.
8. **Close:** the next tick closes the doors before another movement or the end of processing.

Trip lifecycle: Waiting -> Assigned -> Onboard -> Completed.
Maintenance/emergency can return Assigned trips to Waiting. Onboard trips stay with
that car until it can finish them; passengers are not teleported to another car.

## How dispatch chooses a car

Selection and FIFO are different decisions. Dispatch chooses **which car**; FIFO
chooses **which assigned trip that car serves next**.

AssignWaiting orders waiting trips using arrival tick, a 20-tick VIP advantage,
timestamp, and submission order. For each trip it:

1. Filters cars in Normal mode.
2. Checks passenger/cargo type, pickup/destination service floors, and weight capacity.
3. Estimates the cost to reach the new pickup by walking through each candidate's route.
4. Picks the lowest cost, then the fewest assigned trips, then the lowest car ID.
5. Reserves capacity and records assignment under the same fleet lock.

EstimatePickup is a dry run: it sums floor distances and door actions without moving
the real car. It retains the routing interface so custom policies can still be injected.
It estimates current work, not future arrivals, and does not guarantee globally optimal routes.
Reserved weight includes both assigned and onboard trips.

Local carries passengers on floors 1–20; Express carries passengers on 1, 10, 15, and
20; Freight carries cargo on 1–20. Their capacities are 1000, 1000, and 3000 kg.
VIP affects priority but does not bypass floor permissions or FIFO after assignment.

## What one tick does

ProcessTick holds the fleet lock, checks timeouts, assigns waiting work, increments
the tick counter, and calls AdvanceCar once for every car.

AdvanceCar reads like a sequence of decisions:

- Maintenance or emergency: count an unavailable tick; do nothing physical.
- Doors open: close them; finish maintenance if draining is complete.
- No assigned trips: count an idle tick.
- Not at the next stop: MoveToward moves exactly one floor.
- At the stop: ServeStop opens the doors and calls PickUp or DropOff.

PickUp and DropOff update the trip lifecycle, counters, and event history. A tick is
not one real second. All cars get a turn in a tick, but the hard simulation uses a
coordinated loop rather than one thread per car.

/simulation/process repeats ticks until none of the cars can advance. It can return
with pending trips when compatible cars are unavailable. The API yields between
ticks so submissions and controls can run. Work submitted after the final check
needs another processing call.

## Why there are multiple locks

| Protection | Purpose |
| --- | --- |
| SimulationSession command semaphore | Coordinates API commands, coherent responses, and its event buffer |
| SimulationSession processor semaphore | Prevents overlapping full drains and reset during a drain |
| EnterpriseElevatorSystem fleet lock | Makes selection, capacity reservation, transitions, and metrics consistent |
| Elevator lock | Protects domain movement/door operations, including use outside the hard coordinator |
| FileLogProvider lock | Prevents concurrent file writes and rotation from colliding |

A lock protects short synchronous work. A semaphore lets asynchronous callers wait.
The session releases its command gate between ticks. TXT I/O runs outside the fleet
lock, although it still holds the session command gate and contributes to HTTP latency.
The library also serializes its own ProcessRequestsAsync for callers using it directly;
the API runs ProcessTick itself so it can flush logs after every tick.

Ordinary lists, queues, and dictionaries are safe here because access is coordinated.
A ConcurrentDictionary alone would not make selecting a car and reserving its capacity
one atomic operation. Snapshots let callers inspect state without changing internal cars.

## Logging, without the plumbing

Read [LOGGING.md](LOGGING.md) for file locations, sample lines, errors, and recovery.
The normal path is: domain event -> memory buffer -> TXT file after the command/tick.
GET /events is only a recent in-memory window. GET /logs/status identifies the active
file and reports current write errors. Neither logs nor the event window restore state.

## Operational controls

- Maintenance: stop accepting new trips, requeue unboarded trips, finish onboard trips,
  then enter Maintenance with doors closed.
- Emergency: stop future movement/door actions immediately between ticks; requeue only
  unboarded trips. Resume explicitly to continue onboard trips.
- Timeout: detect assigned work with no progress using monotonic elapsed time; trigger
  emergency. Default is 30 seconds in the library and 300 in the presentation API.
  Detection runs on ticks or /simulation/check-timeouts, not in a background timer.
- Reset: wait for active processing, recreate the fleet, clear in-memory trips/metrics,
  preserve files, and log the reset.

## Architecture and patterns to explain

The domain owns physical rules and immutable request/configuration data. Application
code coordinates trips and defines strategy/event contracts. The API is the input
adapter, and the TXT writer is an output adapter. Factories create a consistent fleet.
These are logical layers; the core layers share an assembly.

Strategy allows routing/selection policies to be injected. Simple Factory centralizes
fleet creation. Dependency injection supplies a shared session and logging adapter.
There is no class-per-state State pattern and no Event Sourcing. The easy and medium
coordinators remain for the earlier exercise levels; the REST API uses the hard coordinator.

## Limits and possible improvements

- State is in memory and resets on restart. Permissions are demo inputs, not authentication.
- Pending trips and histories are bounded. Assignment timing excludes queue/lock waiting
  and file writes, so it is not an end-to-end HTTP SLA.
- File logs are diagnostic, not transactional or durable exactly-once storage.
- Future work: batch submission, automatic processing/watchdog, persistence, real authorization,
  and sustained latency/memory benchmarks. Batch submission is intentionally not implemented.

## Which document should I use?

- This guide: understand the implementation and prepare an explanation.
- [samples/README.md](samples/README.md): presentation sequence and JSON files.
- [API_DEMO.md](API_DEMO.md): run instructions, endpoints, and manual scenarios.
- [LOGGING.md](LOGGING.md): understand and troubleshoot logs.
- [HARD_LEVEL.md](HARD_LEVEL.md): detailed hard-level rules and limitations.
- [ARCHITECTURE.md](ARCHITECTURE.md): architectural choices and SOLID mapping.
