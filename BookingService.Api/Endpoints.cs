using System.Text.RegularExpressions;
using BookingService.Api.Dtos;
using BookingService.Domain;
using BookingService.Infrastructure;
using BookingService.Application.Tickets;
using BookingService.Application.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Api.Endpoints;

public static class BookingEndpoints
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void MapBookingEndpoints(this WebApplication app)
    {
        // SP-05-T1: POST /events/{event-id}/tickets
        app.MapPost("/events/{eventId:guid}/tickets", (
            Guid eventId,
            PurchaseTicketCommand command,
            [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyHeader,
            IEventCatalog eventCatalog,
            ITicketRepository ticketRepository) =>
        {
            // 1. SP-05-T2: Retorna 404 si el ID no corresponde a un evento conocido
            if (!eventCatalog.Exists(eventId))
            {
                return Results.NotFound(new { error = $"Event with id '{eventId}' not found." });
            }

            // 2. SP-05-T2: Validar presencia de campos requeridos y reportar cuál falló
            if (string.IsNullOrWhiteSpace(command.FullName))
            {
                return Results.BadRequest(new { field = "fullName", error = "Full name is required." });
            }

            if (string.IsNullOrWhiteSpace(command.Email))
            {
                return Results.BadRequest(new { field = "email", error = "Email is required." });
            }

            // 3. SP-05-T2: Validar formato del correo electrónico
            if (!EmailRegex.IsMatch(command.Email.Trim()))
            {
                return Results.BadRequest(new { field = "email", error = "Email format is invalid." });
            }

            // 4. SP-06-T1 & SP-08-T1: Generar ticket y resolver idempotencia
            var idempotencyKey = idempotencyHeader ?? Guid.NewGuid();
            var ticketCode = $"TCK-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

            var newTicket = new Ticket
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                FullName = command.FullName.Trim(),
                Email = command.Email.Trim(),
                TicketCode = ticketCode,
                IdempotencyKey = idempotencyKey,
                CreatedAtUtc = DateTime.UtcNow
            };

            var ticket = ticketRepository.GetOrAdd(idempotencyKey, newTicket, out var wasCreated);

        // 5. SP-07-T1: Retornar DTO del ticket
        var response = new TicketDto(
            ticket.Id,
            ticket.EventId,
            ticket.FullName,
            ticket.Email,
            ticket.TicketCode,
            ticket.CreatedAtUtc
        );

        return wasCreated 
            ? Results.Created($"/events/{eventId}/tickets/{ticket.Id}", response)
            : Results.Ok(response);

            
        });
    }
}