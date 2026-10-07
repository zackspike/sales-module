using BookingService.Api.Common;
using BookingService.Api.Endpoints;
using BookingService.Application.Tickets;
using BookingService.Application.Tickets.Queries;
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

builder.Services.AddInfrastructure();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TicketPurchaseValidator>();
builder.Services.AddScoped<ReserveSeatHandler>();
builder.Services.AddScoped<PurchaseTicketHandler>();
builder.Services.AddScoped<GetAvailableTicketsHandler>();
builder.Services.AddScoped<CheckTicketAvailabilityHandler>();

var app = builder.Build();

app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapGet("/test/error", () =>
    {
        throw new InvalidOperationException("Simulation of an unhandled error.");
    });
}

app.UseCors();

app.MapGet("/", () => Results.Ok(new { status = "BookingService API Online", version = "0.0.1" }));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapBookingEndpoints();

app.Run();

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;
