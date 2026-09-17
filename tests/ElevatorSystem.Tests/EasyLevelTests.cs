using ElevatorSystem;

public class EasyLevelTests
{
    [Fact]
    public void MovementAndFifo()
    {
        var logger = new RecordingLogger();
        var controller = new ElevatorController(logger);
        var elevator = controller.Elevator;
        Assert.True(controller.Elevators.Count == 1 && controller.MinFloor == 1 && controller.MaxFloor == 10,
            "Easy configuration has one elevator on floors 1-10");
        Assert.True(elevator.CurrentFloor == 1 && elevator.State == ElevatorState.IDLE, "Starts idle on floor 1");
        controller.ProcessRequests();
        Assert.True(logger.Actions.Count == 0, "Empty processing performs no actions");
        controller.RequestElevator(3, Direction.UP);
        controller.RequestDestination(8);
        controller.RequestElevator(6, Direction.DOWN);
        controller.RequestDestination(1);
        controller.RequestDestination(1);
        var targets = elevator.TargetFloors;
        Assert.True(targets.SequenceEqual(new[] { 3, 8, 6, 1, 1 }), "Pickup and destination requests share FIFO order");
        Assert.ThrowsAny<NotSupportedException>(() => ((IList<int>)targets).Clear());
        controller.ProcessRequests();
        var opened = logger.Actions.Where(a => a.State == ElevatorState.DOOR_OPEN).ToArray();
        Assert.True(opened.Select(a => a.Floor).SequenceEqual(targets), "Stops preserve order and duplicates");
        Assert.True(opened[0].RequestedDirection == Direction.UP && opened[2].RequestedDirection == Direction.DOWN,
            "Pickup directions survive through service and logging");
        Assert.True(opened[1].RequestedDirection is null, "Destination is distinct from a pickup call");
        Assert.True(logger.Actions.Count(a => a.State == ElevatorState.IDLE) == 5, "Every stop closes its doors");
        int floor = 1;
        bool doorsOpen = false;
        foreach (var action in logger.Actions)
        {
            if (action.State is ElevatorState.MOVING_UP or ElevatorState.MOVING_DOWN)
            {
                Assert.True(!doorsOpen, "No movement with doors open");
                int expected = action.State == ElevatorState.MOVING_UP ? floor + 1 : floor - 1;
                Assert.True(action.Floor == expected, "Movement advances exactly one floor in the stated direction");
                floor = action.Floor;
            }
            else
            {
                Assert.True(action.Floor == floor, "Doors operate at the current floor");
                doorsOpen = action.State == ElevatorState.DOOR_OPEN;
            }
        }
        Assert.True(elevator.CurrentFloor == 1 && elevator.State == ElevatorState.IDLE, "Ends idle at final destination");
        Assert.True(elevator.TargetFloors.Count == 0 && elevator.PendingRequestCount == 0, "Queues drain completely");
        Assert.True(targets.Count == 5, "Previously captured target snapshot is unchanged");
        int actionCount = logger.Actions.Count;
        controller.ProcessRequests();
        Assert.True(logger.Actions.Count == actionCount, "Processing again does not replay completed stops");
    }

    [Fact]
    public void ValidationAndManualControls()
    {
        var controller = new ElevatorController(new RecordingLogger());
        Assert.ThrowsAny<ArgumentNullException>(() => new ElevatorController((IElevatorLogger)null!));
        foreach (int invalidFloor in new[] { 0, 11, int.MinValue, int.MaxValue })
        {
            Assert.ThrowsAny<ArgumentOutOfRangeException>(() => controller.RequestDestination(invalidFloor));
            Assert.ThrowsAny<ArgumentOutOfRangeException>(() => controller.RequestElevator(invalidFloor, Direction.UP));
            Assert.ThrowsAny<ArgumentOutOfRangeException>(() => controller.Elevator.AddRequest(invalidFloor));
        }
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => controller.RequestElevator(5, (Direction)42));
        Assert.ThrowsAny<ArgumentException>(() => controller.RequestElevator(1, Direction.DOWN));
        Assert.ThrowsAny<ArgumentException>(() => controller.RequestElevator(10, Direction.UP));
        Assert.True(controller.Elevator.TargetFloors.Count == 0, "Invalid calls do not modify queues");
        controller.RequestElevator(1, Direction.UP);
        controller.RequestElevator(10, Direction.DOWN);
        controller.ProcessRequests();
        Assert.True(controller.Elevator.CurrentFloor == 10, "Inclusive boundary pickups work");
        Assert.ThrowsAny<InvalidOperationException>(() => controller.Elevator.MoveUp());
        Assert.True(controller.Elevator.State == ElevatorState.IDLE, "Failed move leaves state unchanged");
        Assert.True(controller.Elevator.MoveDown().State == ElevatorState.MOVING_DOWN, "Manual downward movement");
        controller.Elevator.OpenDoor();
        Assert.ThrowsAny<InvalidOperationException>(() => controller.Elevator.MoveUp());
        Assert.ThrowsAny<InvalidOperationException>(() => controller.Elevator.MoveDown());
        Assert.True(controller.Elevator.CurrentFloor == 9, "Blocked moves leave floor unchanged");
        controller.Elevator.CloseDoor();
        Assert.ThrowsAny<InvalidOperationException>(() => controller.Elevator.CloseDoor());
        Assert.True(controller.Elevator.MoveUp().Floor == 10, "Manual upward movement");
        var bottom = new Elevator(0, 1, 10, 1);
        Assert.ThrowsAny<InvalidOperationException>(() => bottom.MoveDown());
        bottom.AddRequest(2);
        Assert.True(bottom.TargetFloors.SequenceEqual(new[] { 2 }), "Direct AddRequest queues a destination");
        var multiple = new ElevatorController(2, 1, 10);
        Assert.ThrowsAny<InvalidOperationException>(() => multiple.RequestElevator(3, Direction.UP));
        Assert.ThrowsAny<InvalidOperationException>(() => multiple.RequestDestination(3));
        Assert.ThrowsAny<InvalidOperationException>(() => multiple.ProcessRequests());
    }

    [Fact]
    public void PairedRequests()
    {
        var logger = new RecordingLogger();
        var controller = new ElevatorController(logger);
        var first = new PassengerRequest(4, 9);
        var second = new PassengerRequest(2, 7);
        controller.SubmitRequest(first);
        controller.RequestDestination(1);
        controller.SubmitRequest(second);
        Assert.True(controller.Elevator.PendingRequestCount == 3, "Paired request counts once for assignment");
        Assert.True(controller.Elevator.GetPendingRequests().SequenceEqual(new[] { first, second }), "Paired request compatibility");
        controller.ProcessRequests();
        Assert.True(logger.Actions.Where(a => a.State == ElevatorState.DOOR_OPEN).Select(a => a.Floor)
            .SequenceEqual(new[] { 4, 9, 1, 2, 7 }), "Pairs are enqueued atomically, pickup before destination");
        Assert.True(controller.Elevator.GetPendingRequests().Count == 0 && controller.Elevator.PendingRequestCount == 0,
            "Completed paired and standalone requests leave no stale pending entries");
    }

    [Fact]
    public void LoggingFailureAndRecovery()
    {
        var logger = new FailOnceLogger();
        var controller = new ElevatorController(logger);
        controller.RequestDestination(1);
        Assert.ThrowsAny<IOException>(controller.ProcessRequests);
        Assert.True(controller.Elevator.State == ElevatorState.DOOR_OPEN && controller.Elevator.PendingRequestCount == 1,
            "Logger failure preserves the completed transition and outstanding stop");
        controller.ProcessRequests();
        Assert.True(controller.Elevator.State == ElevatorState.IDLE && controller.Elevator.PendingRequestCount == 0,
            "Processing resumes safely after logging failure");
    }

    [Fact]
    public void ConcurrentSubmissionAndProcessing()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var logger = new GatedLogger(entered, release);
        var controller = new ElevatorController(logger);
        controller.RequestDestination(2);
        var processor = Task.Run(controller.ProcessRequests);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "Processor reached logging outside domain locks");
            var producers = Task.Run(() => Parallel.For(0, 128, i => controller.RequestDestination(i % 10 + 1)));
            Assert.True(producers.Wait(TimeSpan.FromSeconds(10)), "128 requests enqueue while logging is blocked");
            var expected = controller.Elevator.TargetFloors;
            Assert.True(expected.Count == 129, "All requests accepted during processing");
            var secondProcessor = Task.Run(controller.ProcessRequests);
            release.Set();
            Assert.True(Task.WaitAll(new[] { processor, secondProcessor }, TimeSpan.FromSeconds(15)),
                "Concurrent processors finish without deadlock");
            var served = logger.Actions.Where(a => a.State == ElevatorState.DOOR_OPEN).Select(a => a.Floor);
            Assert.True(served.SequenceEqual(expected), "Concurrent processors neither lose, duplicate, nor reorder stops");
            Assert.True(controller.Elevator.State == ElevatorState.IDLE && controller.Elevator.PendingRequestCount == 0,
                "Concurrent workload drains to idle");
        }
        finally
        {
            release.Set();
            processor.Wait(TimeSpan.FromSeconds(15));
        }
    }

    [Fact]
    public void ConsoleLogging()
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
        Assert.True(text.Contains("Moved up at floor 2 [MOVING_UP]") && text.Contains("pickup UP")
            && text.Contains("Doors closed at floor 2 [IDLE]"), "Default configuration logs meaningful console actions");
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
