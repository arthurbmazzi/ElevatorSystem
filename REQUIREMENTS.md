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

1. Easy: single elevator system (specified below).
2. Medium: multiple elevator system (specified below).
3. Hard: advanced enterprise system (specified below).

## Easy level: single elevator system

- The default system has one elevator serving floors 1–10, starting at floor 1.
- Simulate upward and downward movement one floor at a time.
- Use `IDLE`, `MOVING_UP`, `MOVING_DOWN`, and `DOOR_OPEN`. Other existing enum
  values are reserved for later levels.
- Accept pickup calls containing a floor and direction, and separate destination
  floor selections. Reject out-of-range floors, undefined directions, UP at floor
  10, and DOWN at floor 1 without modifying the queue.
- Serve floor requests strictly FIFO, including duplicate and current-floor
  requests. Direction describes passenger intent; it does not reorder stops.
- Open and close the doors at every queued stop; finish in `IDLE` with doors closed.
- Expose elevator movement, door controls, request addition, and read-only target
  floors. Use C# PascalCase names (`MoveUp`, `RequestElevator`, etc.).
- `ElevatorController.RequestElevator(floor, direction)` queues a pickup;
  `RequestDestination(floor)` queues a destination; `ProcessRequests()` synchronously
  drains requests. No real-time sleeps or background workers are required.
- Log movement and door actions through an injectable logging abstraction, with
  console output in the default easy-level configuration.
- Preserve the earlier constructors and paired passenger requests. A paired
  request adds pickup then destination together to the FIFO queue.
- Keep assignment and state transitions thread-safe; serialize processors without
  holding the assignment lock for a whole trip or during logging. Requests arriving
  after a processor observes an empty queue wait for the next processing call.
- Verify movement, door safety, FIFO order, invalid input, logging, and concurrent
  request handling. Make one implementation commit for this level.

Maintenance, timed movement, stuck-elevator timeouts, and advanced dispatch remain
outside the easy-level scope. Performance targets remain to be benchmarked.

## Medium level: multiple elevator system

- 3–5 elevators serving floors 1–20.
- Intelligent dispatch using closest pickup, same-direction priority, queued travel,
  and load balancing to reduce wait time.
- Concurrent passenger submissions and multi-threaded elevator processing.
- Thread-safe assignment, movement, queues, and status.
- Timestamp-based request prioritization.
- ElevatorSystem exposes AssignRequest, FindBestElevator, and BalanceLoad.
- Request contains pickup floor, destination floor, direction, and timestamp.
- Comprehensive action logging and fleet status reporting.

## Hard level: advanced enterprise system

- Local, express and freight elevator types, with configured service floors and weight limits.
- Planned maintenance, emergency stop and explicit recovery.
- FIFO routing per car: complete pickup and destination before the next trip.
  Advanced routing was removed at the user's request; destination never precedes pickup.
- Floor authorization independent of VIP dispatch priority; bounded VIP head start.
- Monitoring, structured event history, wait/travel/throughput/utilization and real assignment latency.
- Stuck-elevator timeout using an injectable monotonic clock.
- Preserve previous public APIs and FIFO behavior for easy/medium.
- Replace the console checks with discoverable xUnit tests for all levels.
- Follow the supplied request.pdf and existing C#/.NET, SOLID and concurrency guidelines.

Detailed policies, simulation limits and usage are documented in HARD_LEVEL.md.
