using ElevatorSystem;

if (args.Length == 1 && args[0] == "--medium")
{
    var system = new ElevatorSystem.ElevatorSystem();
    await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() =>
        system.SubmitRequest(new Request(i % 10 + 1, 20 - i % 10)))));
    await system.ProcessRequestsAsync();
    var status = system.GetStatus();
    Console.WriteLine($"Completed: {status.CompletedRequests}/{status.SubmittedRequests}; pending: {status.PendingRequests}");
    foreach (var car in status.Elevators)
        Console.WriteLine($"Elevator {car.Id}: floor {car.CurrentFloor}, {car.State}");
    return;
}

var controller = new ElevatorController();
if (args.Length == 1 && args[0] == "--interactive")
{
    RunInteractive(controller);
    return;
}
if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project src/ElevatorSystem.Demo -- [--interactive|--medium]");
    Environment.ExitCode = 1;
    return;
}

controller.RequestElevator(3, Direction.UP);
controller.RequestDestination(8);
controller.RequestElevator(6, Direction.DOWN);
controller.RequestDestination(1);
controller.ProcessRequests();
Console.WriteLine($"Finished at floor {controller.Elevator.CurrentFloor}: {controller.Elevator.State}");

static void RunInteractive(ElevatorController controller)
{
    Console.WriteLine("Elevator simulator — floors 1 to 10. Type Help to list commands.");
    PrintHelp();
    while (true)
    {
        Console.Write("> ");
        var line = Console.ReadLine();
        if (line is null) return;
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) continue;
        var command = parts[0].ToLowerInvariant();
        try
        {
            switch (command)
            {
                case "requestelevator" when parts.Length == 3:
                    if (!int.TryParse(parts[1], out var pickupFloor) ||
                        !TryDirection(parts[2], out var direction))
                    {
                        Console.WriteLine("Usage: RequestElevator <floor> <UP|DOWN>. Example: RequestElevator 3 UP");
                        break;
                    }
                    controller.RequestElevator(pickupFloor, direction);
                    Console.WriteLine("Pickup queued. Type ProcessRequests to process the queue.");
                    break;
                case "requestdestination" when parts.Length == 2:
                    if (!int.TryParse(parts[1], out var destination))
                    {
                        Console.WriteLine("Usage: RequestDestination <floor>. Example: RequestDestination 8");
                        break;
                    }
                    controller.RequestDestination(destination);
                    Console.WriteLine("Destination queued. Type ProcessRequests to process the queue.");
                    break;
                case "processrequests" when parts.Length == 1:
                    controller.ProcessRequests();
                    PrintStatus(controller);
                    break;
                case "status" when parts.Length == 1:
                    PrintStatus(controller);
                    break;
                case "help" when parts.Length == 1:
                    PrintHelp();
                    break;
                case "exit" when parts.Length == 1:
                    return;
                default:
                    Console.WriteLine("Invalid command. Type Help to list commands.");
                    break;
            }
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"Invalid input: {exception.Message}");
        }
    }
}

static bool TryDirection(string value, out Direction direction)
{
    switch (value.ToLowerInvariant())
    {
        case "up": direction = Direction.UP; return true;
        case "down": direction = Direction.DOWN; return true;
        default: direction = default; return false;
    }
}

static void PrintStatus(ElevatorController controller)
{
    var elevator = controller.Elevator;
    var floors = elevator.TargetFloors;
    Console.WriteLine($"CurrentFloor: {elevator.CurrentFloor} | State: {elevator.State} | TargetFloors: {(floors.Count == 0 ? "empty" : string.Join(" -> ", floors))}");
}

static void PrintHelp()
{
    Console.WriteLine("RequestElevator <floor> <UP|DOWN>  Queue a pickup request");
    Console.WriteLine("RequestDestination <floor>        Queue a destination");
    Console.WriteLine("ProcessRequests                   Process the queue and print logs");
    Console.WriteLine("Status                            Show current floor, state, and queue");
    Console.WriteLine("Help                              List commands");
    Console.WriteLine("Exit                              Exit the simulator");
}
