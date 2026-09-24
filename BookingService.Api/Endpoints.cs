using BookingService.Api.Dtos;
using BookingService.Domain;
using BookingService.Application.Tickets;
using BookingService.Application.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Api.Endpoints;

public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this WebApplication app)
    {
        // SP-05: POST /events/{eventId}/tickets
        app.MapPost("/events/{eventId:guid}/tickets", (
            Guid eventId,
            PurchaseTicketCommand command,
            [FromHeader(Name = "X-Idempotency-Key")] Guid? idempotencyHeader,
            TicketPurchaseValidator validator,
            ITicketRepository ticketRepository) =>
        {
            // 1. Validar usando el validador oficial de la capa Application (VAL-02)
            var validation = validator.Validate(command with { EventId = eventId });
            if (!validation.IsValid)
            {
                if (validation.Status == TicketPurchaseValidationStatus.EventNotFound)
                {
                    return Results.NotFound(new { error = $"Event with id '{eventId}' not found." });
                }

                return Results.BadRequest(new { errors = validation.Errors });
            }

            // 2. Resolver idempotencia (SP-06)
            var idempotencyKey = idempotencyHeader ?? Guid.NewGuid();

            // 3. Generar ticket con el generador de dominio sin truncar (VAL-03 / SP-08)
            var newTicket = new Ticket
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                FullName = command.FullName!.Trim(),
                Email = command.Email!.Trim(),
                TicketCode = TicketCodeGenerator.Generate(),
                IdempotencyKey = idempotencyKey,
                CreatedAtUtc = DateTime.UtcNow
            };

            var ticket = ticketRepository.GetOrAdd(idempotencyKey, newTicket, out var wasCreated);

            // 4. Retornar DTO del ticket (SP-07)
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