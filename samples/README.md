# Presentation samples

Copy the complete contents of a JSON file into POST /requests in Swagger.
All files have the same six fields and types as Try it out: pickupFloor,
destinationFloor, kind, weightKg, isVip, and allowedFloors.
Accepted trips are processed automatically; no simulation commands are needed.

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

## Main presentation

1. Start the API with a clean fleet and inspect GET /elevators/configuration.
2. Submit samples 01, 02, and 03 one at a time through POST /requests.
3. Refresh GET /trips and GET /elevators to observe automatic movement.
4. Wait for completed=3 and pending=0 in GET /analytics (roughly a minute at default speed).
5. Submit samples 05, 06, and 07. Expect 400, 400, and 403 without changing submitted totals.
6. Read GET /events and use GET /logs/status to locate the TXT file.

VIP priority applies to requests waiting together, not to trips already assigned.
Assignments may differ depending on when each HTTP request arrives.

## Express

Start a fresh API instance. Put car 0 in maintenance using POST /elevators/0/maintenance.
Submit sample 04. Car 1 automatically serves the trip to floor 15. Resume car 0 afterward.

## Emergency

Stop car 2 with POST /elevators/2/emergency-stop, then submit sample 03.
The cargo stays Waiting because no other car supports it. Call POST /elevators/2/resume
and observe the trip complete automatically. To demonstrate an onboard emergency,
stop car 2 after GET /trips shows Onboard; the cargo stays with that car until resumed.

## FIFO

Start a fresh API instance and put car 1 in maintenance. Submit sample 08 then 09.
The Local car completes the trip to 12 before picking up the passenger for floor 3.
GET /events shows pickup 1, drop-off 12, pickup 1, drop-off 3. Resume car 1 afterward.

## Notes

Restart the API for a clean scenario; there is no reset endpoint. Logs are preserved.
File settings and processing speed are in appsettings.json. Assignment metrics exclude
queue/lock waiting and file writes; they are not an HTTP latency SLA.
