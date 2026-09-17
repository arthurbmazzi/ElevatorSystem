# Elevator System Design — Coding Interview

This document records the user's baseline for subsequent development. The exercise
will progress through three difficulty levels, to be specified by the user.
Do not invent the requirements for those levels in advance.

Implementation language and platform: **C# and .NET**.

## Initial foundation

- `Elevator` represents a single elevator.
- `ElevatorController` manages elevator operations.
- Passenger requests contain pickup and destination floors.
- `ElevatorState`: `IDLE`, `MOVING_UP`, `MOVING_DOWN`, `DOOR_OPENING`,
  `DOOR_OPEN`, `DOOR_CLOSING`, `MAINTENANCE`.
- `Direction`: `UP`, `DOWN`.

## System requirements

- Handle multiple requests with a basic scheduling algorithm.
- Support concurrent requests with thread-safe operations.
- Protect shared resources with appropriate locks or mutexes.
- Make elevator state changes atomic.
- Prevent race conditions in request assignment.
- Consider thread-safe collections.
- Handle invalid floor requests gracefully.
- Implement timeouts for stuck elevators.
- Handle exceptions in concurrent operations.

## Performance targets

- Handle at least 100 concurrent requests efficiently.
- Elevator assignment response time below 100 ms.
- Reasonable memory usage under load.

These are targets to verify as behavior is implemented, not claims that a class
skeleton already satisfies them.

## Engineering guidelines

- Use clean code: descriptive names, small cohesive methods, immutable inputs,
  explicit validation, and comments that explain contracts or concurrency decisions.
- Apply SOLID: separate domain invariants, construction, scheduling policy, and
  application coordination; extend policies through narrow interfaces rather than
  editing the controller. Implementations must honor the same interface contract.
- Use clean architecture dependency direction: domain code has no application or
  external dependencies; application use cases depend on domain types and policy
  abstractions. Keep composition separate from scheduling. Logical folders are
  sufficient for this foundation; separate assemblies when stronger isolation is needed.
- Use a factory for consistent fleet creation and Strategy for interchangeable
  assignment policies. Add abstractions only where there is a concrete reason.
- Preserve the existing public entry points and default behavior: shortest queue,
  lowest-ID tie breaking, FIFO queues, inclusive floor bounds, and atomic assignment.
- Strategies receive immutable snapshots, return an available elevator ID, and
  must be fast, deterministic, and free of side effects or blocking I/O. Selection
  and enqueue must remain inside the same assignment lock. A failed strategy must
  leave queues unchanged and surface its exception to the caller.
- Verify default behavior, alternate policies, validation, extension failures, and
  concurrent assignment. Document architecture decisions and the SOLID mapping.
- Refactoring does not authorize implementing the unspecified difficulty levels.

## Difficulty levels

1. Awaiting the user's specification.
2. Awaiting the user's specification.
3. Awaiting the user's specification.
