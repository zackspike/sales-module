using System.Diagnostics;
using BookingService.Api.Contracts;
using BookingService.Api.Middleware;
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
            .WithName("GetAvailableTickets")
            .WithSummary("List available seats for an event")
            .WithDescription("Returns all seats currently in 'Available' status and not reserved by a buyer for the specified event ID.")
            .Produces<IReadOnlyList<SeatAvailabilityDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        tickets.MapGet("/{ticketId:guid}/availability", CheckTicketAvailability)
            .WithName("CheckTicketAvailability")
            .WithSummary("Check seat availability")
            .WithDescription("Checks whether a specific seat in an event is available, reserved or sold.")
            .Produces<SeatAvailabilityDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        tickets.MapPost("/{ticketId:guid}/reserve", ReserveSeat)
            .WithName("ReserveSeat")
            .WithSummary("Reserve an event seat")
            .WithDescription("Locks a seat for the buyer identified by email for 10 minutes. Reserving again with the same email renews the reservation. The seat must be reserved before it can be purchased.")
            .Produces<SeatReservationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        tickets.MapPost("/{ticketId:guid}/purchase", PurchaseTicket)
            .WithName("PurchaseTicket")
            .WithSummary("Purchase an event seat")
            .WithDescription("Purchases a seat previously reserved with the same email. Requires an X-Idempotency-Key header. If the key was already used for this seat, safely returns the existing ticket (200 OK).")
            .Produces<TicketResponse>(StatusCodes.Status201Created)
            .Produces<TicketResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Retrieves all available seats for the specified event.
    /// </summary>
    /// <param name="eventId">The unique identifier of the event.</param>
    /// <param name="handler">Application query handler.</param>
    /// <response code="200">List of available seats successfully retrieved.</response>
    /// <response code="404">Event was not found.</response>
    private static IResult GetAvailableTickets(Guid eventId, GetAvailableTicketsHandler handler) =>
        handler.Handle(new GetAvailableTicketsQuery(eventId)) is { } seats
            ? Results.Ok(seats)
            : Results.NotFound();

    /// <summary>
    /// Checks the current availability of a specific seat in an event.
    /// </summary>
    /// <param name="eventId">The unique identifier of the event.</param>
    /// <param name="ticketId">The unique identifier of the ticket (seat).</param>
    /// <param name="handler">Application query handler.</param>
    /// <response code="200">Seat availability status successfully retrieved.</response>
    /// <response code="404">Event or ticket was not found.</response>
    private static IResult CheckTicketAvailability(Guid eventId, Guid ticketId, CheckTicketAvailabilityHandler handler) =>
        handler.Handle(new CheckTicketAvailabilityQuery(eventId, ticketId)) is { } seat
            ? Results.Ok(seat)
            : Results.NotFound();

    /// <summary>
    /// Reserves (locks) a specific seat for the buyer during 10 minutes.
    /// </summary>
    /// <param name="eventId">The unique identifier of the event.</param>
    /// <param name="ticketId">The unique identifier of the ticket (seat) to reserve.</param>
    /// <param name="request">Reservation request containing the buyer's full name and email.</param>
    /// <param name="handler">Application command handler.</param>
    /// <response code="200">Seat reserved (or reservation renewed) for the buyer.</response>
    /// <response code="400">Request validation failed.</response>
    /// <response code="404">Event or ticket not found in catalog.</response>
    /// <response code="409">Seat is already sold or reserved by another buyer.</response>
    /// <response code="500">Unhandled server error captured by global exception middleware.</response>
    private static IResult ReserveSeat(
        Guid eventId,
        Guid ticketId,
        ReserveSeatRequest request,
        ReserveSeatHandler handler)
    {
        var result = handler.Handle(new ReserveSeatCommand(eventId, ticketId, request.FullName, request.Email));

        return result.Status switch
        {
            ReserveSeatStatus.Reserved => Results.Ok(new SeatReservationResponse(
                result.Ticket!.Id,
                eventId,
                result.Ticket.SeatNumber,
                request.Email.Trim(),
                result.ExpiresAtUtc!.Value)),
            ReserveSeatStatus.Invalid => Results.BadRequest(new { errors = result.Errors }),
            ReserveSeatStatus.EventNotFound => Results.NotFound(new { error = $"Event '{eventId}' not found." }),
            ReserveSeatStatus.TicketNotFound => Results.NotFound(new { error = $"Ticket '{ticketId}' not found in event '{eventId}'." }),
            ReserveSeatStatus.AlreadySold => Results.Conflict(new { error = "The requested seat has already been purchased." }),
            ReserveSeatStatus.LockedByAnotherBuyer => Results.Conflict(new { error = "The requested seat is reserved by another buyer." }),
            _ => throw new UnreachableException($"Unhandled reservation status '{result.Status}'.")
        };
    }

    /// <summary>
    /// Purchases a specific seat for an event with idempotency protection.
    /// </summary>
    /// <param name="eventId">The unique identifier of the event.</param>
    /// <param name="ticketId">The unique identifier of the ticket (seat) to purchase.</param>
    /// <param name="request">Purchase request containing attendee's full name and email.</param>
    /// <param name="idempotencyKey">Client-provided UUID in the X-Idempotency-Key header. Required to ensure safe replay.</param>
    /// <param name="handler">Application command handler.</param>
    /// <response code="201">New ticket successfully purchased and issued.</response>
    /// <response code="200">Ticket successfully returned on idempotent replay of an already processed purchase.</response>
    /// <response code="400">Request validation failed or X-Idempotency-Key header is missing/invalid.</response>
    /// <response code="403">The seat is not reserved by this buyer, or the reservation expired.</response>
    /// <response code="404">Event or ticket not found in catalog.</response>
    /// <response code="409">Seat is already sold or the idempotency key was previously used with different details.</response>
    /// <response code="500">Unhandled server error captured by global exception middleware.</response>
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
            PurchaseTicketStatus.ReservationRequired => Results.Json(
                new { error = "The seat must be reserved by this buyer before purchasing it, or the reservation expired." },
                statusCode: StatusCodes.Status403Forbidden),
            _ => throw new UnreachableException($"Unhandled purchase status '{result.Status}'.")
        };
    }
}
