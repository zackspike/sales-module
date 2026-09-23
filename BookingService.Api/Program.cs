using Microsoft.OpenApi;
using BookingService.Api.Endpoints;
using BookingService.Infrastructure;
using BookingService.Infrastructure.Repositories;
using BookingService.Application.Repositories;
using BookingService.Application.Tickets;
using BookingService.Api.Common;

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

builder.Services.AddSingleton<BookingMemoryStore>();
builder.Services.AddSingleton<ITicketRepository, InMemoryTicketRepository>();
builder.Services.AddSingleton<IEventCatalog, InMemoryEventCatalog>();
builder.Services.AddTransient<TicketPurchaseValidator>();

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

app.MapGet("/", () => Results.Ok(new { status = "BookingService API Online", version = "0.0.1"}));

app.MapBookingEndpoints();

app.Run();
