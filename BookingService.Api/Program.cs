using System.Reflection;
using BookingService.Api.Endpoints;
using BookingService.Api.Middleware;
using BookingService.Application;
using BookingService.Infrastructure;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BookingService API (Sap-atitos)",
        Version = "v1",
        Description = "Backend API responsible for ticket purchasing, idempotency handling, and issuance (MVP-02)."
    });

    var apiXml = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var apiXmlPath = Path.Combine(AppContext.BaseDirectory, apiXml);
    if (File.Exists(apiXmlPath))
    {
        options.IncludeXmlComments(apiXmlPath);
    }

    var appXml = "BookingService.Application.xml";
    var appXmlPath = Path.Combine(AppContext.BaseDirectory, appXml);
    if (File.Exists(appXmlPath))
    {
        options.IncludeXmlComments(appXmlPath);
    }
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services
    .AddApplication()
    .AddInfrastructure();

var app = builder.Build();

app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapGet("/", () => Results.Ok(new { status = "BookingService API Online", version = "0.0.1" }))
    .WithTags("System")
    .WithName("GetApiRoot")
    .WithSummary("Root API status")
    .WithDescription("Returns basic API identification and online status.")
    .Produces(StatusCodes.Status200OK);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithTags("System")
    .WithName("GetHealthStatus")
    .WithSummary("Health check probe")
    .WithDescription("Liveness and readiness probe reporting service health.")
    .Produces(StatusCodes.Status200OK);

app.MapTicketEndpoints();

app.Run();
