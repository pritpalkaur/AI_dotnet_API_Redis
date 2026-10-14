using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using ProductApi.Data;
using ProductApi.Services;
using ProductApi.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Stateless by design: no sessions, no in-memory data stores. Every request carries what it
// needs, SQL Server is the source of truth and Redis is a shared cache, so any number of API
// instances can run behind a load balancer and any instance can serve any request.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Product API",
        Version = "v1",
        Description = """
            Stateless CRUD API for products. SQL Server is the source of truth and Redis is a cache-aside layer.

            **Try it:** POST a product, then GET it twice and check the `X-Cache` response header (MISS → HIT).
            Any PUT or DELETE evicts the cached copy.
            """,
    });
    o.SchemaFilter<ProductExamplesSchemaFilter>();
    o.OperationFilter<CacheHeaderOperationFilter>();

    var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddStackExchangeRedisCache(o =>
{
    o.Configuration = builder.Configuration.GetConnectionString("Redis");
    o.InstanceName = "ProductApi:";
});

builder.Services.AddSingleton<IAppCache, RedisCache>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("sqlserver");

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:EnsureCreated"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.DocumentTitle = "Product API";
        o.EnableTryItOutByDefault();
        o.DisplayRequestDuration();
        o.EnableFilter();
    });
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
