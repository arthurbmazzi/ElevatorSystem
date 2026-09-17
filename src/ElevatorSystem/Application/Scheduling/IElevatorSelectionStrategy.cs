namespace ElevatorSystem;

public interface IElevatorSelectionStrategy
{
    /// <summary>
    /// Selects an ID from a nonempty, read-only fleet snapshot for a valid request.
    /// Must be deterministic, fast, side-effect free, and safe to share across
    /// controllers. Must not perform I/O or call back into a controller.
    /// Exceptions propagate to the submitter without enqueueing the request.
    /// </summary>
    int SelectElevator(PassengerRequest request, IReadOnlyList<ElevatorSnapshot> elevators);
}
