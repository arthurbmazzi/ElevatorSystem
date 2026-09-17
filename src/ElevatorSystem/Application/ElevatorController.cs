namespace ElevatorSystem;

/// <summary>Owns the fleet and makes selection plus enqueue one atomic assignment.</summary>
public sealed partial class ElevatorController
{
    private readonly object _assignmentLock = new();
    private readonly FloorRange _floors;
    private readonly IElevatorSelectionStrategy _selectionStrategy;

    public int MinFloor => _floors.MinFloor;
    public int MaxFloor => _floors.MaxFloor;
    public IReadOnlyList<Elevator> Elevators { get; }

    /// <summary>Validates and atomically queues a request using the configured policy.</summary>
    /// <returns>The assigned elevator's ID.</returns>
    public int SubmitRequest(PassengerRequest request)
    {
        _floors.Validate(request);
        lock (_assignmentLock)
        {
            var snapshots = Array.AsReadOnly(Elevators.Select(elevator => new ElevatorSnapshot(
                elevator.Id, elevator.CurrentFloor, elevator.PendingRequestCount)).ToArray());
            int selectedId = _selectionStrategy.SelectElevator(request, snapshots);
            var selected = Elevators.FirstOrDefault(elevator => elevator.Id == selectedId)
                ?? throw new InvalidOperationException("The selection strategy returned an unknown elevator ID.");
            selected.Enqueue(request);
            return selected.Id;
        }
    }
}
