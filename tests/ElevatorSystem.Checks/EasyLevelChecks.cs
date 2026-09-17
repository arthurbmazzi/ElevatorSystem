using ElevatorSystem;

internal static class EasyLevelChecks
{
    public static void Run()
    {
        MovementAndFifo();
        ValidationAndManualControls();
        PairedRequests();
        LoggingFailureAndRecovery();
        ConcurrentSubmissionAndProcessing();
        ConsoleLogging();
        Console.WriteLine("Easy-level checks passed: movement, FIFO, doors, validation, logging, and concurrent processing.");
    }

    private static void MovementAndFifo()
    {
        var logger = new RecordingLogger();
        var controller = new ElevatorController(logger);
        var elevator = controller.Elevator;
        Check(controller.Elevators.Count == 1 && controller.MinFloor == 1 && controller.MaxFloor == 10,
            "Easy configuration has one elevator on floors 1-10");
        Check(elevator.CurrentFloor == 1 && elevator.State == ElevatorState.IDLE, "Starts idle on floor 1");
        controller.ProcessRequests();
        Check(logger.Actions.Count == 0, "Empty processing performs no actions");
        controller.RequestElevator(3, Direction.UP);
        controller.RequestDestination(8);
        controller.RequestElevator(6, Direction.DOWN);
        controller.RequestDestination(1);
        controller.RequestDestination(1);
        var targets = elevator.TargetFloors;
        Check(targets.SequenceEqual(new[] { 3, 8, 6, 1, 1 }), "Pickup and destination requests share FIFO order");
        Expect<NotSupportedException>(() => ((IList<int>)targets).Clear());
        controller.ProcessRequests();
        var opened = logger.Actions.Where(a => a.State == ElevatorState.DOOR_OPEN).ToArray();
        Check(opened.Select(a => a.Floor).SequenceEqual(targets), "Stops preserve order and duplicates");
        Check(opened[0].RequestedDirection == Direction.UP && opened[2].RequestedDirection == Direction.DOWN,
            "Pickup directions survive through service and logging");
        Check(opened[1].RequestedDirection is null, "Destination is distinct from a pickup call");
        Check(logger.Actions.Count(a => a.State == ElevatorState.IDLE) == 5, "Every stop closes its doors");
        int floor = 1;
        bool doorsOpen = false;
        foreach (var action in logger.Actions)
        {
            if (action.State is ElevatorState.MOVING_UP or ElevatorState.MOVING_DOWN)
            {
                Check(!doorsOpen, "No movement with doors open");
                int expected = action.State == ElevatorState.MOVING_UP ? floor + 1 : floor - 1;
                Check(action.Floor == expected, "Movement advances exactly one floor in the stated direction");
                floor = action.Floor;
            }
            else
            {
                Check(action.Floor == floor, "Doors operate at the current floor");
                doorsOpen = action.State == ElevatorState.DOOR_OPEN;
            }
        }
        Check(elevator.CurrentFloor == 1 && elevator.State == ElevatorState.IDLE, "Ends idle at final destination");
        Check(elevator.TargetFloors.Count == 0 && elevator.PendingRequestCount == 0, "Queues drain completely");
        Check(targets.Count == 5, "Previously captured target snapshot is unchanged");
        int actionCount = logger.Actions.Count;
        controller.ProcessRequests();
        Check(logger.Actions.Count == actionCount, "Processing again does not replay completed stops");
    }

    private static void ValidationAndManualControls()
    {
        var controller = new ElevatorController(new RecordingLogger());
        Expect<ArgumentNullException>(() => new ElevatorController((IElevatorLogger)null!));
        foreach (int invalidFloor in new[] { 0, 11, int.MinValue, int.MaxValue })
        {
            Expect<ArgumentOutOfRangeException>(() => controller.RequestDestination(invalidFloor));
            Expect<ArgumentOutOfRangeException>(() => controller.RequestElevator(invalidFloor, Direction.UP));
            Expect<ArgumentOutOfRangeException>(() => controller.Elevator.AddRequest(invalidFloor));
        }
        Expect<ArgumentOutOfRangeException>(() => controller.RequestElevator(5, (Direction)42));
        Expect<ArgumentException>(() => controller.RequestElevator(1, Direction.DOWN));
        Expect<ArgumentException>(() => controller.RequestElevator(10, Direction.UP));
        Check(controller.Elevator.TargetFloors.Count == 0, "Invalid calls do not modify queues");
        controller.RequestElevator(1, Direction.UP);
        controller.RequestElevator(10, Direction.DOWN);
        controller.ProcessRequests();
        Check(controller.Elevator.CurrentFloor == 10, "Inclusive boundary pickups work");
        Expect<InvalidOperationException>(() => controller.Elevator.MoveUp());
        Check(controller.Elevator.State == ElevatorState.IDLE, "Failed move leaves state unchanged");
        Check(controller.Elevator.MoveDown().State == ElevatorState.MOVING_DOWN, "Manual downward movement");
        controller.Elevator.OpenDoor();
        Expect<InvalidOperationException>(() => controller.Elevator.MoveUp());
        Expect<InvalidOperationException>(() => controller.Elevator.MoveDown());
        Check(controller.Elevator.CurrentFloor == 9, "Blocked moves leave floor unchanged");
        controller.Elevator.CloseDoor();
        Expect<InvalidOperationException>(() => controller.Elevator.CloseDoor());
        Check(controller.Elevator.MoveUp().Floor == 10, "Manual upward movement");
        var bottom = new Elevator(0, 1, 10, 1);
        Expect<InvalidOperationException>(() => bottom.MoveDown());
        bottom.AddRequest(2);
        Check(bottom.TargetFloors.SequenceEqual(new[] { 2 }), "Direct AddRequest queues a destination");
        var multiple = new ElevatorController(2, 1, 10);
        Expect<InvalidOperationException>(() => multiple.RequestElevator(3, Direction.UP));
        Expect<InvalidOperationException>(() => multiple.RequestDestination(3));
        Expect<InvalidOperationException>(() => multiple.ProcessRequests());
    }

    private static void PairedRequests()
    {
        var logger = new RecordingLogger();
        var controller = new ElevatorController(logger);
        var first = new PassengerRequest(4, 9);
        var second = new PassengerRequest(2, 7);
        controller.SubmitRequest(first);
        controller.RequestDestination(1);
        controller.SubmitRequest(second);
        Check(controller.Elevator.PendingRequestCount == 3, "Paired request counts once for assignment");
        Check(controller.Elevator.GetPendingRequests().SequenceEqual(new[] { first, second }), "Paired request compatibility");
        controller.ProcessRequests();
        Check(logger.Actions.Where(a => a.State == ElevatorState.DOOR_OPEN).Select(a => a.Floor)
            .SequenceEqual(new[] { 4, 9, 1, 2, 7 }), "Pairs are enqueued atomically, pickup before destination");
        Check(controller.Elevator.GetPendingRequests().Count == 0 && controller.Elevator.PendingRequestCount == 0,
            "Completed paired and standalone requests leave no stale pending entries");
    }

    private static void LoggingFailureAndRecovery()
    {
        var logger = new FailOnceLogger();
        var controller = new ElevatorController(logger);
        controller.RequestDestination(1);
        Expect<IOException>(controller.ProcessRequests);
        Check(controller.Elevator.State == ElevatorState.DOOR_OPEN && controller.Elevator.PendingRequestCount == 1,
            "Logger failure preserves the completed transition and outstanding stop");
        controller.ProcessRequests();
        Check(controller.Elevator.State == ElevatorState.IDLE && controller.Elevator.PendingRequestCount == 0,
            "Processing resumes safely after logging failure");
    }

    private static void ConcurrentSubmissionAndProcessing()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var logger = new GatedLogger(entered, release);
        var controller = new ElevatorController(logger);
        controller.RequestDestination(2);
        var processor = Task.Run(controller.ProcessRequests);
        try
        {
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "Processor reached logging outside domain locks");
            var producers = Task.Run(() => Parallel.For(0, 128, i => controller.RequestDestination(i % 10 + 1)));
            Check(producers.Wait(TimeSpan.FromSeconds(10)), "128 requests enqueue while logging is blocked");
            var expected = controller.Elevator.TargetFloors;
            Check(expected.Count == 129, "All requests accepted during processing");
            var secondProcessor = Task.Run(controller.ProcessRequests);
            release.Set();
            Check(Task.WaitAll(new[] { processor, secondProcessor }, TimeSpan.FromSeconds(15)),
                "Concurrent processors finish without deadlock");
            var served = logger.Actions.Where(a => a.State == ElevatorState.DOOR_OPEN).Select(a => a.Floor);
            Check(served.SequenceEqual(expected), "Concurrent processors neither lose, duplicate, nor reorder stops");
            Check(controller.Elevator.State == ElevatorState.IDLE && controller.Elevator.PendingRequestCount == 0,
                "Concurrent workload drains to idle");
        }
        finally
        {
            release.Set();
            processor.Wait(TimeSpan.FromSeconds(15));
        }
    }

    private static void ConsoleLogging()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var controller = new ElevatorController();
            controller.RequestElevator(1, Direction.UP);
            controller.RequestDestination(2);
            controller.ProcessRequests();
        }
        finally { Console.SetOut(original); }
        string text = output.ToString();
        Check(text.Contains("Moved up at floor 2 [MOVING_UP]") && text.Contains("pickup UP")
            && text.Contains("Doors closed at floor 2 [IDLE]"), "Default configuration logs meaningful console actions");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"Easy-level check failed: {description}");
    }

    private static void Expect<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private class RecordingLogger : IElevatorLogger
    {
        public List<ElevatorAction> Actions { get; } = new();
        public virtual void Log(ElevatorAction action) => Actions.Add(action);
    }

    private sealed class FailOnceLogger : IElevatorLogger
    {
        private bool _failed;
        public void Log(ElevatorAction action)
        {
            if (_failed) return;
            _failed = true;
            throw new IOException("Simulated log sink failure");
        }
    }

    private sealed class GatedLogger(ManualResetEventSlim entered, ManualResetEventSlim release) : RecordingLogger
    {
        public override void Log(ElevatorAction action)
        {
            if (Actions.Count == 0)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Logging gate timed out.");
            }
            base.Log(action);
        }
    }
}
