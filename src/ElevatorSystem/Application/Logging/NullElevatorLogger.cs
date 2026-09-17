namespace ElevatorSystem;

internal sealed class NullElevatorLogger : IElevatorLogger
{
    public void Log(ElevatorAction action) { }
}
