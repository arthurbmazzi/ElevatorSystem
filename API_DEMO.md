# API presentation guide

## Start

Set **ElevatorSystem.Api** as the Visual Studio startup project and press F5, or run:

```powershell
dotnet run --project src/ElevatorSystem.Api
```

Open [Swagger](http://localhost:5080/swagger). Submit a JSON body to POST /requests.
The API returns 201 with the trip ID and initial Waiting state. A background worker
assigns and processes trips automatically. Refresh GET /trips/{id} or GET /elevators
to observe progress. There are no assign, tick, process, timeout-check, or reset endpoints.

## JSON format

Every file in [samples](samples/README.md) now includes exactly the six fields shown
in Swagger's Try it out example:

```json
{
  "pickupFloor": 3,
  "destinationFloor": 15,
  "kind": "Passenger",
  "weightKg": 75,
  "isVip": false,
  "allowedFloors": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20]
}
```

Use a single object, not an array. kind accepts Passenger or Freight as strings.
VIP and floor permissions are demonstration inputs, not authentication.

## Endpoints

- POST /requests: accept a trip for automatic processing.
- GET /trips and GET /trips/{id}: inspect trip states.
- GET /elevators and GET /elevators/configuration: inspect the fleet.
- POST /elevators/{id}/maintenance: requeue unboarded trips and finish onboard trips.
- POST /elevators/{id}/emergency-stop: stop the car and retain onboard passengers.
- POST /elevators/{id}/resume: restore service; processing resumes automatically.
- GET /analytics, GET /events, GET /logs/status: metrics, event history, and log diagnostics.

## Timing and errors

Elevators:StepIntervalMilliseconds defaults to 500 in appsettings.json. Each interval
advances every car by one movement or door action. Elevators:StuckTimeoutSeconds
defaults to 30. The worker checks timeouts automatically as part of each step.
Idle cars without work do not time out. A blocked callback cannot be interrupted by
this watchdog; callbacks must be fast and perform no I/O.

Invalid floors, types, or unsupported weights return 400; denied floor access returns
403; unknown cars/trips return 404; invalid operating transitions return 409.
File-writing failures return 503 for affected commands. The worker pauses progression
while buffered events cannot be written and retries on its next interval. Recovery
can trigger a timeout if assigned cars have been without progress too long.

## Presentation and logs

Follow [samples/README.md](samples/README.md). Restart the API between independent
scenarios when you need a clean fleet; in-memory state is not persisted.
Logs remain in src/ElevatorSystem.Api/logs. GET /logs/status identifies the current
TXT file. See [LOGGING.md](LOGGING.md) and [SYSTEM_GUIDE.md](SYSTEM_GUIDE.md).

## Verification

Run dotnet test ElevatorSystem.sln -m:1 for unit tests.
For HTTP smoke testing, start a fresh disposable API instance with
Elevators:StepIntervalMilliseconds=10, then run node tests/api-smoke.mjs.
The script submits test trips and does not reset the fleet; restart afterward.
