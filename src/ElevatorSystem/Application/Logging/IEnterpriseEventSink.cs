namespace ElevatorSystem;

/// <summary>
/// Receives events under the fleet lock. Implementations must only buffer in memory,
/// must not perform I/O or call back into the coordinator, and must return promptly.
/// The host is responsible for flushing its buffer outside the fleet lock.
/// </summary>
public interface IEnterpriseEventSink
{
    void Record(EnterpriseEvent entry);
}
