# Architecture and engineering decisions

The easy level extends the request-assignment foundation with single-elevator
movement, FIFO stops, door transitions, and logging. The next two levels remain
unspecified; timeouts and maintenance are not implemented yet.

## Clean architecture

The library has four logical areas, with the existing `ElevatorSystem` namespace
and constructor preserved for compatibility:

| Area | Responsibility | Dependencies |
| --- | --- | --- |
| `Domain` | Elevator queues, movement/door invariants, immutable action records, passenger requests, floor bounds | .NET only |
| `Application` | Coordinate assignment and processing; define selection and logging contracts | Domain and application contracts |
| `Composition` | Wire the default or injected policy and construct a fresh fleet | Domain and application |
| `Infrastructure` | Write action records to the console | Application logging contract and domain records |

These are logical boundaries within one assembly, not compiler-enforced project
boundaries. Constructor wiring lives in a composition partial of the controller to
retain its public API; its application partial contains the use case. This is a
pragmatic compatibility compromise, not complete assembly-level isolation. The
console adapter lives in an infrastructure folder; a separate project is not
needed for this small example. The demo is a separate executable project.

## SOLID applied

| Principle | Concrete application |
| --- | --- |
| Single responsibility | `FloorRange` validates bounds; `Elevator` owns state and FIFO stops; the factory constructs fleets; strategies select; the controller coordinates; the logger writes actions. |
| Open/closed | Add an `IElevatorSelectionStrategy` implementation and inject it without changing `SubmitRequest`. |
| Liskov substitution | Both built-in policies accept the same request and snapshot contract and return a candidate ID. Contract checks cover ties, singleton fleets, and invalid inputs. An invalid ID is rejected before mutation. |
| Interface segregation | Selection and logging are separate one-method contracts. A logger needs only the immutable action; a strategy has no movement or queue mutation API. |
| Dependency inversion | Assignment depends on `IElevatorSelectionStrategy` and processing depends on `IElevatorLogger`. Composition supplies concrete implementations. Domain code never calls console I/O. |

SOLID does not require an interface on every class. Domain objects remain concrete,
and the factory has no interface because construction currently has one policy.

## Patterns and clean code

`ElevatorFactory` is a **Simple Factory**, not the inheritance-based Factory Method
pattern. It creates a fresh read-only fleet, consecutive IDs starting at zero, and
elevators at the minimum floor in `IDLE`. This removes construction loops from the
assignment use case and avoids shared elevator ownership across controllers.

**Strategy** separates scheduling from coordination. `ShortestQueueStrategy`
preserves the previous algorithm and explicitly breaks ties by ID, independent of
input ordering. `NearestPickupStrategy` demonstrates replacement using pickup
distance, with `long` arithmetic to avoid overflow across integer floor limits.
It is opt-in and is not a load-balancing or trip-time optimizer. The easy level has
only one elevator, so car selection is trivial; the existing strategy extension
point remains available for subsequent levels.

Stop ordering and car selection are distinct. `Queue<FloorStop>` implements the
easy level's strict FIFO order without another strategy interface for a single
policy. Stops retain optional pickup direction. A paired `PassengerRequest` adds
two adjacent stops atomically, with completion recorded on the destination stop.
The original paired-request snapshot stays available until completion; pending
counts include standalone calls and count each paired request once.

`IElevatorLogger` is an application output port, with console and no-op adapters.
The easy constructor defaults to console logging; existing configurable
constructors stay quiet. Action records contain the floor and state from the
transition, so logging does not have to reread mutable elevator state.

Clean code changes centralize duplicated floor validation, keep methods focused,
use immutable request and snapshot types, and name policy decisions explicitly.
Comments describe contracts and ownership rather than narrating each statement.

## Atomicity and failure behavior

The controller holds its assignment lock while taking immutable snapshots,
selecting an ID, validating membership, and enqueueing. This prevents concurrent
submissions from selecting against stale queue counts. Processing also takes this
lock for each atomic simulation step. Controller operations acquire locks in
controller-to-elevator order; direct elevator methods take only the domain lock.
Elevators never call back outward. Individual floor and state reads are locked;
action records capture both values atomically for a transition.

One processing lock serializes `ProcessRequests` callers. Each step moves one
floor, opens doors at the head stop, or closes doors and completes that stop.
The active stop remains queued until closure. Logging runs outside assignment and
domain locks, but inside the processing lock to preserve action order. This permits
concurrent submissions while logging. Recursive processing is rejected. Loggers
must not wait for another processor, since that caller waits on the processing lock.
When a logger throws, processing releases its lock and propagates the error; the
completed domain transition remains committed, and a later call resumes the queue.
There is no delivery retry or durable log guarantee in this simulation.

Direct movement and door methods are atomic simulation primitives, not an
independent worker loop. Each move finishes at the next floor; opening doors stops
there immediately. The controller owns the normal service sequence. Empty-queue
observation ends processing; submissions after that observation await another call.

A strategy receives a read-only collection of immutable records, not live
elevators. It must be fast, deterministic, free of side effects and blocking I/O,
and safe to share between controllers. The controller cannot enforce those
behavioral obligations for arbitrary injected code. Stateful test doubles are
used only to inspect calls or simulate failures, not as production policies.

Invalid requests fail before policy invocation. A policy exception propagates;
an unknown ID raises `InvalidOperationException`. Both occur before enqueue, and
the lock is released so later requests can succeed. There is no silent fallback.

Snapshot creation and selection take O(elevator count) time and temporary memory
per assignment. Queue growth remains unbounded. The 100 ms assignment target is
still unbenchmarked; correctness checks do not establish latency guarantees.

## Verification

The standalone checks retain original validation, FIFO, read-only access, and 128
simultaneous-caller coverage. Added checks exercise both strategies, explicit
tie-breaking with unsorted IDs, extreme floor distances, injected policy behavior,
snapshot isolation, failures before mutation, recovery, independent fleets, and
alternate-policy concurrent submission.

`EasyLevelChecks` additionally verifies exact FIFO stop order, one-floor movement,
both directions, door interlocks and bounds, duplicate/current-floor calls,
paired-request completion, logging failure recovery, and actual console output.
A gated logger blocks a processing call while 128 requests are submitted, then
two processors drain the captured queue with no lost, duplicated, or reordered
stops. This verifies that logging holds neither assignment nor domain locks.

Run `dotnet build ElevatorSystem.sln` and
`dotnet run --project tests/ElevatorSystem.Checks` from the repository root.

## Medium-level architecture

`ElevatorSystem` adds fleet coordination without changing the easy controller.
It owns the cars and returns immutable scheduling/status snapshots so external
movement cannot invalidate dispatch assumptions. A private fleet lock protects the
central priority queue, selection plus enqueue, counters, and individual car steps.
Lock order is fleet then domain. One asynchronous semaphore serializes drain calls;
per-car thread-pool workers permit independent progress. No delay or logger callback
runs under the fleet lock. A separate logger lock serializes callbacks. Code never
acquires that logger lock while holding the fleet lock.

`OptimizedDispatchStrategy` implements the existing strategy port. It models FIFO
route distance and two door transitions per queued stop, adds a load penalty, and
uses same-direction approach as a tie preference. Route snapshots extend the existing
three-field snapshot compatibly. Factory construction and domain transitions reuse
the existing implementation. Central priority uses timestamp then arrival sequence;
already dispatched trips retain FIFO ordering. This avoids unsafe reassignment of
onboard passengers, at the cost of not pooling compatible trips.

A failed policy leaves the current central queue head untouched; earlier successful
assignments stay committed. Cancellation and logging failure preserve committed
state, release processing ownership, and allow later resumption. Other car workers
may finish before a worker failure is surfaced by Task.WhenAll. Assignment logs are
buffered until processing. Status counters are fleet-wide atomic observations.
The worker lifetime is bounded by queue draining, so callers must invoke processing
again for submissions after all workers become idle.

Medium checks exercise 3–5 cars, bounds, estimated-route and direction selection,
oldest-first priority, even identical-load distribution, cancellation/resumption,
invalid policy atomicity, and submissions overlapping workers and two drain calls.
The 100 ms assignment target remains unbenchmarked.
