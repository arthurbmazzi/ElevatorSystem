using System.Text.Json.Serialization;
using ElevatorSystem;
using ElevatorSystem.Api;
using ElevatorSystem.Api.Infrastructure;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Elevator System — demonstration", Version = "v1",
        Description = "Manual FIFO simulation. Submit trips, advance ticks, or process until no progress is possible. " +
            "Fleet: 0 Local, 1 Express (1/10/15/20), 2 Freight. " +
            "isVip and allowedFloors are simulation inputs, not authentication. In-memory state; TXT logs."
    });
    options.SchemaFilter<TripExample>();
});
builder.Services.AddSingleton<FileLogProvider>();
builder.Services.AddSingleton<ILoggerProvider>(services => services.GetRequiredService<FileLogProvider>());
builder.Services.AddSingleton<SimulationSession>();
builder.Services.AddProblemDetails();
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        context.Response.StatusCode = 499;
    }
    catch (Exception exception)
    {
        var status = exception switch
        {
            BadHttpRequestException => 400,
            ArgumentException => 400,
            UnauthorizedAccessException => 403,
            KeyNotFoundException => 404,
            InvalidOperationException => 409,
            IOException => 503,
            _ => 500
        };
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApiErrors");
        if (status >= 500) logger.LogError(exception, "Failure in {Method} {Path}", context.Request.Method, context.Request.Path);
        else logger.LogWarning("Command rejected: {Method} {Path}: {Reason}", context.Request.Method, context.Request.Path, exception.Message);
        await Results.Problem(statusCode: status,
            title: status == 503 ? "File logging unavailable" : status == 500 ? "Internal error" : "Command rejected",
            detail: status == 500 ? "Check the log file. Previously committed state has been preserved." : exception.Message)
            .ExecuteAsync(context);
    }
});
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Elevator System v1");
    options.DocumentTitle = "Elevator System — manual testing";
    options.DefaultModelsExpandDepth(-1);
    options.EnableTryItOutByDefault();
});
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapPost("/requests", async (CreateTripCommand command, SimulationSession session, CancellationToken ct) =>
{
    var request = command.ToRequest();
    return await session.ExecuteAsync(system =>
    {
        system.SubmitRequest(request);
        var trip = system.Trips.Single(t => t.Id == request.Trip.Id);
        return Results.Created($"/trips/{trip.Id}", trip);
    }, ct);
}).WithTags("1. Trips").WithSummary("Submit a trip (remains Waiting until assignment or processing)")
    .Produces<TripSnapshot>(201).ProducesProblem(400).ProducesProblem(403).ProducesProblem(409);

app.MapGet("/trips", (SimulationSession session, CancellationToken ct) =>
    session.ExecuteAsync(s => s.Trips, ct)).WithTags("1. Trips").WithSummary("View trips and their states");
app.MapGet("/trips/{id:guid}", (Guid id, SimulationSession session, CancellationToken ct) =>
    session.ExecuteAsync(s => s.Trips.SingleOrDefault(t => t.Id == id)
        ?? throw new KeyNotFoundException("Trip not found in the current history."), ct))
    .WithTags("1. Trips").WithSummary("Find a trip by ID").ProducesProblem(404);

app.MapGet("/elevators", (SimulationSession session, CancellationToken ct) =>
    session.ExecuteAsync(s => s.Elevators, ct)).WithTags("2. Fleet").WithSummary("View floor, state, mode, and reserved weight");
app.MapGet("/elevators/configuration", () => EnterpriseFleetFactory.CreateDefault())
    .WithTags("2. Fleet").WithSummary("View types, capacities, and served floors");

MapCarCommand("maintenance", "Request maintenance: finish onboard trips and requeue the others", (s, id) => s.RequestMaintenance(id));
MapCarCommand("emergency-stop", "Emergency stop while preserving onboard passengers", (s, id) => s.EmergencyStop(id));
MapCarCommand("resume", "Resume an elevator in maintenance or emergency mode", (s, id) => s.ResumeService(id));

app.MapPost("/simulation/assign", (SimulationSession session, CancellationToken ct) =>
    session.ExecuteAsync(s => { s.BalanceLoad(); return SimulationSession.Status(s); }, ct))
    .WithTags("3. Simulation").WithSummary("Assign trips without moving elevators");
app.MapPost("/simulation/tick", (SimulationSession session, CancellationToken ct) =>
    session.ExecuteAsync(s => { s.ProcessTick(); return SimulationSession.Status(s); }, ct))
    .WithTags("3. Simulation").WithSummary("Advance one step: movement or a door action per elevator");
app.MapPost("/simulation/process", (SimulationSession session, CancellationToken ct) => session.ProcessAsync(ct))
    .WithTags("3. Simulation").WithSummary("Process until no progress is possible; check pending in the response");
app.MapPost("/simulation/check-timeouts", (SimulationSession session, CancellationToken ct) =>
    session.ExecuteAsync(s => { s.CheckTimeouts(); return SimulationSession.Status(s); }, ct))
    .WithTags("3. Simulation").WithSummary("Check elevators without progress (300 seconds by default in this demo)");
app.MapPost("/simulation/reset", (SimulationSession session, CancellationToken ct) => session.ResetAsync(ct))
    .WithTags("3. Simulation").WithSummary("Reset the demo: clear in-memory trips and metrics and recreate the fleet")
    .WithDescription("Log files are preserved and the reset is logged. Waits for active processing to finish.");

app.MapGet("/analytics", (SimulationSession session, CancellationToken ct) =>
    session.ExecuteAsync(s => s.GetAnalytics(), ct)).WithTags("4. Monitoring").WithSummary("View totals and metrics");
app.MapGet("/events", (SimulationSession session, CancellationToken ct) =>
    session.ExecuteAsync(s => s.Events, ct)).WithTags("4. Monitoring").WithSummary("View the latest 1,000 events");
app.MapGet("/logs/status", (FileLogProvider logs) => logs.GetStatus())
    .WithTags("4. Monitoring").WithSummary("Find the active TXT file and check write or retention errors");

app.Run();

void MapCarCommand(string route, string summary, Action<EnterpriseElevatorSystem, int> action)
{
    app.MapPost($"/elevators/{{id:int}}/{route}", (int id, SimulationSession session, CancellationToken ct) =>
        session.ExecuteAsync(s =>
        {
            if (!s.Elevators.Any(e => e.Id == id)) throw new KeyNotFoundException("Elevator not found. Available IDs: 0, 1, and 2.");
            action(s, id);
            return SimulationSession.Status(s);
        }, ct)).WithTags("2. Fleet").WithSummary(summary).ProducesProblem(404).ProducesProblem(409);
}
