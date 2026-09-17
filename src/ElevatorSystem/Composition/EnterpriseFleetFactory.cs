namespace ElevatorSystem;

public static class EnterpriseFleetFactory
{
    public static IReadOnlyList<ElevatorConfiguration> CreateDefault() => Array.AsReadOnly(new[]
    {
        new ElevatorConfiguration(0, ElevatorType.Local, Enumerable.Range(1, 20)),
        new ElevatorConfiguration(1, ElevatorType.Express, new[] { 1, 10, 15, 20 }),
        new ElevatorConfiguration(2, ElevatorType.Freight, Enumerable.Range(1, 20), 3000)
    });
}
