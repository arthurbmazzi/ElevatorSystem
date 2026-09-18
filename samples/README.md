# Presentation samples

Open [Swagger](http://localhost:5080/swagger). Each JSON file is a complete body
for **POST /requests**. Copy its contents into the request body and click Execute.
Do not send the filename or an array of samples. Other commands below have **no body**;
enter the car ID in Swagger's path parameter when required.

If omitted, isVip defaults to false and allowedFloors defaults to floors 1 through 20.
These fields simulate permissions and priority; there is no login in this demo.

| File | Expected result |
| --- | --- |
| [01-local-passenger.json](01-local-passenger.json) | 201; only Local serves this passenger trip |
| [02-vip-passenger.json](02-vip-passenger.json) | 201; VIP priority at assignment, not guaranteed Express selection |
| [03-freight.json](03-freight.json) | 201; 1500 kg cargo requires Freight, car 2 |
| [04-express-compatible.json](04-express-compatible.json) | 201; use the Express scenario below to force car 1 |
| [05-invalid-floor.json](05-invalid-floor.json) | 400; invalid pickup floor |
| [06-overweight-freight.json](06-overweight-freight.json) | 400; no car supports 3001 kg |
| [07-vip-access-denied.json](07-vip-access-denied.json) | 403; VIP does not grant access to floor 20 |
| [08-fifo-first.json](08-fifo-first.json) | 201; first trip for the FIFO scenario |
| [09-fifo-second.json](09-fifo-second.json) | 201; second trip for the FIFO scenario |

## Main presentation: about five minutes

1. **POST /simulation/reset**. This clears the demonstration state and preserves logs.
2. **GET /elevators/configuration**. Show the three car types, floors, and capacities.
3. **POST /requests** with samples **01**, **02**, and **03**, in that order.
   All three return 201 with state Waiting. Do not process between submissions.
4. **POST /simulation/assign**, then **GET /trips**. Show assigned car IDs.
   In this clean scenario, VIP sample 02 is assigned first to Local (lowest-ID tie),
   sample 01 also requires Local, and cargo sample 03 goes to Freight.
   Explain: "Dispatch checks compatibility and capacity, then estimates pickup cost."
5. **POST /simulation/tick**, then **GET /elevators**. Show door state and floor.
   Explain: "One tick is one movement or door action per elevator."
6. **POST /simulation/process**. Expect submitted 3, completed 3, pending 0.
7. **GET /events**, then **GET /analytics**. Show pickups before drop-offs and totals.
8. Submit invalid samples **05**, **06**, and **07**. Expect 400, 400, and 403.
   **GET /analytics** should still show submitted 3 and completed 3.
9. Open the latest TXT file in src/ElevatorSystem.Api/logs to show the event trail.

## Optional: emergency recovery

1. Reset; submit only sample **03**.
2. Execute one tick. The cargo boards car 2 at floor 1.
3. **POST /elevators/2/emergency-stop**.
4. **POST /simulation/process**. Expect pending 1, completed 0, and car 2 in
   EmergencyStopped. **GET /trips** shows Onboard: the cargo has not disappeared.
5. **POST /elevators/2/resume**, then process. Expect completed 1 and pending 0.

## Optional: Express and maintenance

1. Reset, then **POST /elevators/0/maintenance**. Empty car 0 enters Maintenance.
2. Submit sample **04**, then process.
3. **GET /trips** shows elevatorId 1. Car 1 ends at floor 15; door-opening events
   occur only at floors 1 and 15 for this trip.
4. **POST /elevators/0/resume** returns Local to Normal.

## Optional: explain FIFO in one sentence

1. Reset and put car 1 in maintenance, so passenger trips use Local only.
2. Submit sample **08**, then sample **09**, without processing between them.
3. Process and inspect **GET /events**. Passenger service order is:
   pickup at 1, drop-off at 12, pickup at 1, drop-off at 3.
4. Explain: "Each car completes its first assigned trip before serving its next trip,
   even when the next destination is closer." Expect completed 2 and pending 0.

## Presentation notes

- Use reset between scenarios so earlier trips do not affect assignments.
- The API timeout is 300 seconds. A long pause after assignment can trigger emergency
  on the next tick; resume the affected car or reset before repeating the scenario.
- Assignment metrics exclude queue/lock waiting and file logging. Do not present them
  as a measured HTTP response-time SLA.
- For setup, timeout testing, and log configuration, see [API_DEMO.md](../API_DEMO.md).
