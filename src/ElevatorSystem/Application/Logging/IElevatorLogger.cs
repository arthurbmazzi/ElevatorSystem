namespace ElevatorSystem;

/// <summary>
/// Receives completed transitions outside domain and assignment locks.
/// Implementations must not call ProcessRequests recursively. Exceptions propagate;
/// the completed transition is not rolled back or logged again on a later run.
/// </summary>
public interface IElevatorLogger
{
    void Log(ElevatorAction action);
}
