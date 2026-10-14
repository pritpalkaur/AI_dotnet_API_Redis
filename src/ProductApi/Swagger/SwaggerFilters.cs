using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using ProductApi.Dtos;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ProductApi.Swagger;

/// <summary>Pre-fills request bodies in Swagger UI with realistic values instead of "string" / 0.</summary>
public class ProductExamplesSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(CreateProductRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["sku"] = new OpenApiString("KB-001"),
                ["name"] = new OpenApiString("Mechanical Keyboard"),
                ["description"] = new OpenApiString("87-key, brown switches"),
                ["category"] = new OpenApiString("Peripherals"),
                ["price"] = new OpenApiDouble(89.99),
                ["quantity"] = new OpenApiInteger(25),
            };
        }
        else if (context.Type == typeof(UpdateProductRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["name"] = new OpenApiString("Mechanical Keyboard (TKL)"),
                ["description"] = new OpenApiString("87-key, brown switches"),
                ["category"] = new OpenApiString("Peripherals"),
                ["price"] = new OpenApiDouble(79.99),
                ["quantity"] = new OpenApiInteger(30),
            };
        }
    }
}

/// <summary>Documents the X-Cache header on cached GET endpoints so it shows in Swagger UI.</summary>
public class CacheHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.HttpMethod != "GET" ||
            !operation.Responses.TryGetValue("200", out var ok)) return;

        ok.Headers["X-Cache"] = new OpenApiHeader
        {
            Description = "HIT when served from Redis, MISS when read from SQL Server.",
            Schema = new OpenApiSchema { Type = "string", Enum = [new OpenApiString("HIT"), new OpenApiString("MISS")] },
        };
    }
}
