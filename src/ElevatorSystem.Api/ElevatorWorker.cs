namespace ElevatorSystem.Api;

/// <summary>Advances the shared fleet automatically while the API is running.</summary>
public sealed class ElevatorWorker(FleetSession fleet, IConfiguration configuration,
    ILogger<ElevatorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int interval = configuration.GetValue<int?>("Elevators:StepIntervalMilliseconds") ?? 500;
        if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval));
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        bool writeFailureReported = false;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await fleet.ExecuteAsync(s => s.ProcessTick(), stoppingToken);
                    writeFailureReported = false;
                }
                catch (IOException exception)
                {
                    // Retain buffered events and retry next step without terminating the host.
                    if (!writeFailureReported)
                        logger.LogError(exception, "Elevator processing paused until file logging recovers. Check /logs/status.");
                    writeFailureReported = true;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
