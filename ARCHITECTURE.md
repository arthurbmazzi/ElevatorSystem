# Architecture and engineering decisions

This refactoring preserves the request-assignment foundation. It adds no movement,
door behavior, timeouts, or requirements for the three unspecified levels.

## Clean architecture

The library has three logical areas, with the existing `ElevatorSystem` namespace
and constructor preserved for compatibility:

| Area | Responsibility | Dependencies |
| --- | --- | --- |
| `Domain` | Elevator queue ownership, passenger requests, enums, floor invariants | .NET only |
| `Application` | Validate and coordinate atomic assignment; define and implement selection policies | Domain and the selection contract |
| `Composition` | Wire the default or injected policy and construct a fresh fleet | Domain and application |

These are logical boundaries within one assembly, not compiler-enforced project
boundaries. Constructor wiring lives in a composition partial of the controller to
retain its public API; its application partial contains the use case. This is a
pragmatic compatibility compromise, not complete assembly-level isolation. No
infrastructure project is needed until there is an actual external dependency.

## SOLID applied

| Principle | Concrete application |
| --- | --- |
| Single responsibility | `FloorRange` validates bounds; `Elevator` owns its queue; the factory constructs fleets; strategies select; the controller coordinates assignment. |
| Open/closed | Add an `IElevatorSelectionStrategy` implementation and inject it without changing `SubmitRequest`. |
| Liskov substitution | Both built-in policies accept the same request and snapshot contract and return a candidate ID. Contract checks cover ties, singleton fleets, and invalid inputs. An invalid ID is rejected before mutation. |
| Interface segregation | The strategy has one selection method and receives only ID, current floor, and queue count. It has no movement, persistence, or queue mutation API. |
| Dependency inversion | Assignment depends on `IElevatorSelectionStrategy`. The composition code chooses the concrete default; callers may inject another policy. |

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
It is opt-in and is not a load-balancing or trip-time optimizer. All elevators
currently remain at the minimum floor, so its ties select ID 0.

Clean code changes centralize duplicated floor validation, keep methods focused,
use immutable request and snapshot types, and name policy decisions explicitly.
Comments describe contracts and ownership rather than narrating each statement.

## Atomicity and failure behavior

The controller holds its assignment lock while taking immutable snapshots,
selecting an ID, validating membership, and enqueueing. This prevents concurrent
submissions from selecting against stale queue counts. Elevator queue locks are
always taken inside the controller lock; elevators never call back outward.

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

Run `dotnet build ElevatorSystem.sln` and
`dotnet run --project tests/ElevatorSystem.Checks` from the repository root.
