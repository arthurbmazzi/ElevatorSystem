using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ElevatorSystem.Api;

public sealed class TripExample : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(CreateTripCommand)) return;
        var floors = new OpenApiArray();
        foreach (int floor in Enumerable.Range(1, 20)) floors.Add(new OpenApiInteger(floor));
        schema.Example = new OpenApiObject
        {
            ["pickupFloor"] = new OpenApiInteger(3), ["destinationFloor"] = new OpenApiInteger(15),
            ["kind"] = new OpenApiString("Passenger"), ["weightKg"] = new OpenApiInteger(75),
            ["isVip"] = new OpenApiBoolean(false), ["allowedFloors"] = floors
        };
    }
}
