using System.Diagnostics;
using BookingService.Api.Contracts;
using BookingService.Application.Tickets.Commands;
using BookingService.Application.Tickets.Dtos;
using BookingService.Application.Tickets.Queries;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Api.Endpoints;

/// <summary>
/// HTTP surface of the ticket use cases. Each endpoint only binds input, calls its handler
/// and maps the outcome to a status code.
/// </summary>
public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var tickets = app.MapGroup("/events/{eventId:guid}/tickets").WithTags("Tickets");

        tickets.MapGet("/", GetAvailableTickets)
            .Produces<IReadOnlyList<SeatAvailabilityDto>>()
            .Produces(StatusCodes.Status404NotFound);

        tickets.MapGet("/{ticketId:guid}/availability", CheckTicketAvailability)
            .Produces<SeatAvailabilityDto>()
            .Produces(StatusCodes.Status404NotFound);

        tickets.MapPost("/{ticketId:guid}/purchase", PurchaseTicket)
            .Produces<TicketResponse>(StatusCodes.Status201Created)
            .Produces<TicketResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static IResult GetAvailableTickets(Guid eventId, GetAvailableTicketsHandler handler) =>
        handler.Handle(new GetAvailableTicketsQuery(eventId)) is { } seats
            ? Results.Ok(seats)
            : Results.NotFound();

    private static IResult CheckTicketAvailability(Guid eventId, Guid ticketId, CheckTicketAvailabilityHandler handler) =>
        handler.Handle(new CheckTicketAvailabilityQuery(eventId, ticketId)) is { } seat
            ? Results.Ok(seat)
            : Results.NotFound();

    private static IResult PurchaseTicket(
        Guid eventId,
        Guid ticketId,
        PurchaseTicketRequest request,
        [FromHeader(Name = "X-Idempotency-Key")] Guid? idempotencyKey,
        PurchaseTicketHandler handler)
    {
        var result = handler.Handle(new PurchaseTicketCommand(
            eventId,
            ticketId,
            request.FullName,
            request.Email,
            idempotencyKey ?? Guid.Empty));

        return result.Status switch
        {
            PurchaseTicketStatus.Purchased => Results.Created(
                $"/events/{eventId}/tickets/{ticketId}/availability", TicketResponse.From(result.Ticket!)),
            PurchaseTicketStatus.Replayed => Results.Ok(TicketResponse.From(result.Ticket!)),
            PurchaseTicketStatus.Invalid => Results.BadRequest(new { errors = result.Errors }),
            PurchaseTicketStatus.EventNotFound => Results.NotFound(new { error = $"Event '{eventId}' not found." }),
            PurchaseTicketStatus.TicketNotFound => Results.NotFound(new { error = $"Ticket '{ticketId}' not found in event '{eventId}'." }),
            PurchaseTicketStatus.AlreadySold => Results.Conflict(new { error = "The requested seat has already been purchased." }),
            PurchaseTicketStatus.IdempotencyKeyConflict => Results.Conflict(new { error = "X-Idempotency-Key was already used for a different seat." }),
            _ => throw new UnreachableException($"Unhandled purchase status '{result.Status}'.")
        };
    }
}
