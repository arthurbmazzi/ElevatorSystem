using ElevatorSystem;

var controller = new ElevatorController();
controller.RequestElevator(3, Direction.UP);
controller.RequestDestination(8);
controller.RequestElevator(6, Direction.DOWN);
controller.RequestDestination(1);
controller.ProcessRequests();
Console.WriteLine($"Finished at floor {controller.Elevator.CurrentFloor}: {controller.Elevator.State}");
