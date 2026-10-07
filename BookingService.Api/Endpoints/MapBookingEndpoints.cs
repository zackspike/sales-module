using BookingService.Api.Dtos;
using BookingService.Application.Tickets;
using BookingService.Application.Tickets.Queries;
using BookingService.Domain;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Api.Endpoints;

public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this WebApplication app)
    {
        // SP-04 / APP-01: GET /events/{eventId}/tickets/available
        app.MapGet("/events/{eventId:guid}/tickets/available", async (
            Guid eventId,
            GetAvailableTicketsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var seats = await handler.HandleAsync(new GetAvailableTicketsQuery(eventId), cancellationToken);

            return seats is null
                ? Results.NotFound(new { error = $"Event with id '{eventId}' not found." })
                : Results.Ok(seats);
        });

        // SP-04 / API-03: GET /events/{eventId}/tickets/{ticketId}/availability
        app.MapGet("/events/{eventId:guid}/tickets/{ticketId:guid}/availability", async (
            Guid eventId,
            Guid ticketId,
            CheckTicketAvailabilityHandler handler,
            CancellationToken cancellationToken) =>
        {
            var seat = await handler.HandleAsync(new CheckTicketAvailabilityQuery(eventId, ticketId), cancellationToken);

            return seat is null
                ? Results.NotFound(new { error = $"Ticket '{ticketId}' not found in event '{eventId}'." })
                : Results.Ok(seat);
        });

        // SP-05 / SP-06 / APP-04: POST /events/{eventId}/tickets/{ticketId}/purchase
        app.MapPost("/events/{eventId:guid}/tickets/{ticketId:guid}/purchase", async (
            Guid eventId,
            Guid ticketId,
            PurchaseTicketRequest request,
            [FromHeader(Name = "X-Idempotency-Key")] Guid? idempotencyKey,
            PurchaseTicketHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new PurchaseTicketCommand(
                eventId,
                ticketId,
                request.FullName,
                request.Email,
                idempotencyKey ?? Guid.Empty);

            var result = await handler.HandleAsync(command, cancellationToken);

            return result.Status switch
            {
                PurchaseTicketStatus.Purchased => Results.Created(
                    $"/events/{eventId}/tickets/{ticketId}",
                    ToDto(result.Ticket!)),
                PurchaseTicketStatus.Replayed => Results.Ok(ToDto(result.Ticket!)),
                PurchaseTicketStatus.Invalid => Results.BadRequest(new { errors = result.Errors }),
                PurchaseTicketStatus.EventNotFound => Results.NotFound(
                    new { error = $"Event with id '{eventId}' not found." }),
                PurchaseTicketStatus.TicketNotFound => Results.NotFound(
                    new { error = $"Ticket '{ticketId}' not found in event '{eventId}'." }),
                PurchaseTicketStatus.AlreadySold => Results.Conflict(
                    new { error = "The requested seat has already been purchased." }),
                PurchaseTicketStatus.IdempotencyKeyConflict => Results.Conflict(
                    new { error = "X-Idempotency-Key was already used to purchase a different seat." }),
                PurchaseTicketStatus.ReservationRequired => Results.Json(
                    new { error = "The seat must be reserved by this buyer before purchasing it, or the reservation expired." },
                    statusCode: StatusCodes.Status403Forbidden),
                _ => throw new InvalidOperationException($"Unexpected purchase status '{result.Status}'.")
            };
        });

        // Seat lock: POST /events/{eventId}/tickets/{ticketId}/reserve
        app.MapPost("/events/{eventId:guid}/tickets/{ticketId:guid}/reserve", async (
            Guid eventId,
            Guid ticketId,
            ReserveSeatRequest request,
            ReserveSeatHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ReserveSeatCommand(eventId, ticketId, request.FullName, request.Email);

            var result = await handler.HandleAsync(command, cancellationToken);

            return result.Status switch
            {
                ReserveSeatStatus.Reserved => Results.Ok(new SeatReservationDto(
                    result.Ticket!.Id,
                    eventId,
                    result.Ticket.Seat!.SeatNumber,
                    result.BuyerId!.Value,
                    result.ExpiresAtUtc!.Value)),
                ReserveSeatStatus.Invalid => Results.BadRequest(new { errors = result.Errors }),
                ReserveSeatStatus.EventNotFound => Results.NotFound(
                    new { error = $"Event with id '{eventId}' not found." }),
                ReserveSeatStatus.TicketNotFound => Results.NotFound(
                    new { error = $"Ticket '{ticketId}' not found in event '{eventId}'." }),
                ReserveSeatStatus.AlreadySold => Results.Conflict(
                    new { error = "The requested seat has already been purchased." }),
                ReserveSeatStatus.LockedByAnotherUser => Results.Conflict(
                    new { error = "The requested seat is reserved by another buyer." }),
                _ => throw new InvalidOperationException($"Unexpected reservation status '{result.Status}'.")
            };
        });
    }

    private static TicketDto ToDto(Ticket ticket)
    {
        return new TicketDto(
            ticket.Id,
            ticket.Seat!.Zone!.EventId,
            ticket.Seat.SeatNumber,
            ticket.User!.FullName,
            ticket.User.Email,
            ticket.TicketCode!,
            ticket.PurchasedAtUtc!.Value);
    }
}
