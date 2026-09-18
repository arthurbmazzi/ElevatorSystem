# How logging works

## Find the correct file

In Swagger, execute **GET /logs/status**. It returns:

- directory: the absolute log directory.
- currentFile: the TXT file currently being written, or null before the first successful creation.
- lastWriteError: null after a successful write; otherwise the most recent file-writing error.
- retentionWarning: an old file could not be removed during the last rotation; new writes can still succeed.
- maxFileBytes and retainedFiles: rotation/retention settings.

The default directory is src/ElevatorSystem.Api/logs relative to the project when
started with dotnet run --project src/ElevatorSystem.Api. Use the returned absolute
path if launching from Visual Studio or another directory.

Old files contain old runs and test inputs. Their errors do not automatically mean
the current run is failing. Files and timestamps use UTC, which can differ from your
local date. Resetting the simulation preserves the log files.

## The three steps

1. The coordinator changes state and emits an event such as Moved or DoorsOpened.
2. The API's IEnterpriseEventSink implementation puts that event in a memory queue.
   This is fast and does not access disk while the fleet lock is held.
3. After each command or tick, SimulationSession writes the queue to TXT and removes
   each entry only after its write succeeds. The file writer uses its own lock.

The in-memory GET /events history keeps at most 1,000 events. The file buffer receives
events independently, so history expiration does not discard events before logging.
A process crash can still lose buffered events; log files are not a database.

Example event line:

```text
2026-09-18T00:00:00.0000000+00:00 tick=3 event=Moved car=0 trip= floor=4 state=MOVING_UP mode=Normal
```

This means car 0 moved to floor 4 on simulation tick 3. An empty trip field is normal
for car-level events. PassengerPickedUp and PassengerDroppedOff include the trip ID.
State describes movement/doors; mode describes Normal, Draining, Maintenance, or emergency.
Freight trips reuse the same pickup/drop-off event names.

## Warnings versus failures

A line containing **[Warning] ... Command rejected** can be expected: submitting floor
99, an overweight cargo request, or a forbidden floor deliberately exercises validation.
The corresponding HTTP 400/403/409 is not a failure to write the log.

**[Error]** with an exception describes an unexpected operation failure. Inspect the
message and the active file, rather than assuming every old stack trace is current.
An earlier development run produced a Windows Event Log permission error for
'.NET Runtime'. The API now calls ClearProviders and explicitly enables only console
and TXT providers; it does not require Windows Event Log permissions.

## Rotation

A new TXT file is created on process startup, a new UTC day, or after the current file
reaches approximately 10 MB. By default the latest seven files are retained. Settings
are in appsettings.json under FileLog. Each filename includes a timestamp and unique ID
to avoid collisions between application instances.

If the active file is removed, the next write creates a new one. If an old file is
locked and cannot be deleted, cleanup records retentionWarning and continues writing.
Retention is best effort in that case; extra files can remain until a later rotation.

## If writing really fails

An inaccessible directory, full disk, or locked active file can prevent a write.
The API returns **503 File logging unavailable** for commands affected by that failure.
GET /logs/status remains available because it does not need the simulation command gate
or a successful disk write.

Restore access/free space or release the file lock, then issue a read command such as
GET /analytics. The session first retries its buffered events. Once writing succeeds,
lastWriteError clears and normal processing resumes. New commands do not change fleet
state while a previous event remains unflushed.

The command that originally failed may already have changed the fleet before its log
write failed. Inspect trips/analytics after recovery before resubmitting; HTTP failure
is not a transaction rollback. Partial disk writes followed by retry can duplicate a
line. Diagnostic ILogger failures also report to stderr without recursively invoking
logging. Configuration errors such as a nonpositive retention limit fail startup.

## Read the last few lines

```powershell
$status = Invoke-RestMethod http://localhost:5080/logs/status
Get-Content -LiteralPath $status.currentFile -Tail 12
```

FileLogTests verifies missing-file recovery, failed-directory recovery, rotation limits,
and (on Windows) a locked old file. API smoke tests verify completed-trip records remain
in TXT even after the in-memory event history has expired.
