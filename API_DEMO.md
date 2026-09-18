# Swagger presentation guide

Ready-to-copy JSON files and a presentation walkthrough: [samples/README.md](samples/README.md).

## Start the API

From the repository root:

```powershell
dotnet run --project src/ElevatorSystem.Api
```

Open http://localhost:5080/swagger. In Visual Studio, set **ElevatorSystem.Api**
as the startup project and press F5; the launch profile opens Swagger.
The original console remains available in ElevatorSystem.Demo.

Each endpoint has a description. Expand the operation, edit its JSON, and click
**Execute**. Responses use enum names and trip IDs. An accepted request returns
**201** and remains `Waiting`; elevators do not move automatically.
Use the simulation commands to advance them.

## Short walkthrough: elevator types

1. Execute `POST /simulation/reset` to start with a clean fleet.
2. Read `GET /elevators/configuration`: 0 is Local (1–20, 1000 kg),
   1 is Express (1, 10, 15, 20, 1000 kg), and 2 is Freight (1–20, 3000 kg).
3. Submit the following JSON bodies to `POST /requests`, one at a time.

Regular passenger; these floors require the Local elevator:

```json
{ "pickupFloor": 3, "destinationFloor": 9, "kind": "Passenger", "weightKg": 75 }
```

VIP passenger; dispatch prioritizes this trip, and the initial tie may select Local:

```json
{ "pickupFloor": 1, "destinationFloor": 20, "kind": "Passenger", "weightKg": 75, "isVip": true }
```

Cargo; requires the Freight elevator:

```json
{ "pickupFloor": 1, "destinationFloor": 10, "kind": "Freight", "weightKg": 1500 }
```

4. Execute `POST /simulation/assign`. Read `GET /trips` to inspect assignments.
   The trip type determines compatibility; the client does not select a car.
5. Execute `POST /simulation/tick` a few times to show movement and door actions.
6. Execute `POST /simulation/process` to finish. Expect
   `submitted: 3`, `completed: 3`, and `pending: 0`.
7. Read `GET /events` to explain the sequence and `GET /analytics` for metrics.

To demonstrate **Express** specifically, reset, place car 0 in maintenance using
`POST /elevators/0/maintenance`, and submit a trip from 1 to 15. Process it and
verify that car 1 completed the trip. It passes through intermediate floors but
opens its doors only at configured service floors.

## Emergency stop and recovery

1. Reset and submit only the cargo trip from 1 to 10 shown above.
2. Execute one tick: the cargo boards Freight, car 2.
3. Execute `POST /elevators/2/emergency-stop`.
4. Process: `pending` remains 1 and the trip remains `Onboard`; the car cannot advance.
5. Execute `POST /elevators/2/resume` and process again: `completed` becomes 1.

Maintenance finishes onboard trips and returns trips that have not boarded to
the waiting queue before entering `Maintenance`.

## Invalid input

- Floor 99 or identical pickup and destination: **400**.
- `kind: "Unknown"`, malformed JSON, or a numeric enum: **400**.
- Freight cargo weighing 3001 kg: **400**, because no compatible car exists.
- `allowedFloors: [1, 2]` for a trip from 1 to 20: **403**, even for a VIP.
- Operating elevator 99: **404**.
- Resuming an elevator already in Normal mode: **409**.

Errors return Problem Details with `status`, `title`, and `detail`. The API
continues accepting commands. Completed trip IDs expire with the bounded history.

## Timeouts and presentation pauses

The library keeps its 30-second default. **This API uses 300 seconds**, configured
in `src/ElevatorSystem.Api/appsettings.json`, so a short explanation between steps
does not trigger an emergency. To demonstrate timeouts, set
`Simulation:StuckTimeoutSeconds` to 30 and restart the API. Submit a trip, execute
`/simulation/assign`, wait more than 30 seconds without progress, and call
`/simulation/check-timeouts`. The car enters emergency mode and the trip that has
not boarded returns to Waiting. Resume the car and process it.

Neither movement nor the watchdog runs automatically: checks happen on ticks and
through the timeout endpoint. `/simulation/process` returns when no progress is
possible, including when trips remain blocked by emergency or maintenance.

## TXT logs

Files are written to **src/ElevatorSystem.Api/logs/** when started with the command
above. The path is relative to the API content root and can be changed through
`FileLog:Directory`. Each event line contains a UTC timestamp, tick, action,
elevator, trip, floor, state, and mode. Warnings and exceptions also use the file
logger. Console output is limited to warnings, errors, and startup messages.

Files rotate on a new UTC day, restart, or at approximately 10 MB. The latest
7 files are retained. Both limits are configurable; the directory is ignored by Git.
Events are captured during operations and written **outside the fleet lock** after
each command or tick, independently of the 1,000-event history exposed by
`/events`. Synchronous writes add to HTTP response time; the library's assignment
metric does not include file writes.

```powershell
Get-ChildItem src/ElevatorSystem.Api/logs/*.txt | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Tail 12
```

Reset preserves the files and logs the reset. Fleet state and trips are held in
memory and are lost on process restart. Logs are diagnostic records, not a database
for restoring trips. An abrupt shutdown can lose events still in the buffer.

## Code organization

- `Program.cs`: endpoints, Swagger, JSON, and HTTP exception translation.
- `Contracts.cs`: request JSON separate from domain objects.
- `SimulationSession.cs`: a singleton fleet; serializes commands, allows submissions
  between ticks, and coordinates processing/reset. It flushes the buffer after each
  operation. A failed write prevents new mutations until the buffer can be flushed.
- `Infrastructure/FileLogProvider.cs`: TXT writing and rotation.
- `IEnterpriseEventSink`: a port for capturing events in memory without I/O under the lock.

Manual processing remains FIFO per car. VIP priority affects assignment, without
reordering trips already assigned. This is a local demo without authentication:
`isVip` and `allowedFloors` are simulation inputs. The default listener is localhost.
Swagger uses the [Swashbuckle integration documented by Microsoft](https://learn.microsoft.com/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-8.0).

## Optional automated verification

`dotnet test ElevatorSystem.sln -m:1` runs the library tests.
With the local API running, `node tests/api-smoke.mjs` checks HTTP/Swagger,
validation, emergency recovery, Express, 128 concurrent submissions, and complete logs.
This script **resets the demonstration fleet** before and after its scenarios;
run it before your presentation. Node is required only for this smoke test.
