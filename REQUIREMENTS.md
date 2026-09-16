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

## Difficulty levels

1. Awaiting the user's specification.
2. Awaiting the user's specification.
3. Awaiting the user's specification.
