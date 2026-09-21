using BookingService.Api.Dtos;
using BookingService.Domain;
using BookingService.Infrastructure;

namespace BookingService.Api.Endpoints;

public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this WebApplication app)
    {
        var eventsGroup = app.MapGroup("/events/{eventId:guid}");

        // 1. GET /events/{eventId}/tickets
        eventsGroup.MapGet("/tickets", (Guid eventId, BookingMemoryStore store) =>
        {
            var summary = store.Seats.Values
                .Where(s => s.EventId == eventId && s.Status == "Available")
                .GroupBy(s => new { s.Section, s.Price })
                .Select(g => new TicketSummaryDto(g.Key.Section, g.Key.Price, g.Count()))
                .ToList();

            return Results.Ok(summary);
        });

        // 2. GET /events/{eventId}/seats
        eventsGroup.MapGet("/seats", (Guid eventId, BookingMemoryStore store) =>
        {
            var availableSeats = store.Seats.Values
                .Where(s => s.EventId == eventId && s.Status == "Available")
                .Select(s => new SeatDto(s.Id, s.Section, s.SeatNumber, s.Price, s.Status))
                .ToList();

            return Results.Ok(availableSeats);
        });

        // 3. POST /events/{eventId}/reservations
        eventsGroup.MapPost("/reservations", (Guid eventId, CreateReservationDto dto, BookingMemoryStore store) =>
        {
            var selectedSeats = dto.SeatIds
                .Select(id => store.Seats.GetValueOrDefault(id))
                .Where(s => s != null && s.EventId == eventId && s.Status == "Available")
                .ToList();

            if (selectedSeats.Count != dto.SeatIds.Count)
            {
                return Results.BadRequest(new { error = "Uno o más asientos no están disponibles o no existen." });
            }

            foreach (var seat in selectedSeats)
            {
                seat!.Status = "Reserved";
            }

            var reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                SeatIds = dto.SeatIds,
                UserEmail = dto.UserEmail,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
                IsConfirmed = false
            };

            store.Reservations[reservation.Id] = reservation;

            var response = new ReservationDto(reservation.Id, reservation.EventId, reservation.SeatIds, reservation.ExpiresAtUtc);
            return Results.Created($"/reservations/{reservation.Id}", response);
        });

        // 4. POST /events/{eventId}/orders
        eventsGroup.MapPost("/orders", (Guid eventId, CreateOrderDto dto, BookingMemoryStore store) =>
        {
            if (!store.Reservations.TryGetValue(dto.ReservationId, out var reservation) || reservation.EventId != eventId)
            {
                return Results.NotFound(new { error = "Reserva no encontrada." });
            }

            if (DateTime.UtcNow > reservation.ExpiresAtUtc)
            {
                return Results.BadRequest(new { error = "La reserva ha expirado." });
            }

            var totalAmount = reservation.SeatIds.Sum(id => store.Seats[id].Price);

            var order = new Order
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                ReservationId = reservation.Id,
                TotalAmount = totalAmount,
                Status = "PendingPayment",
                CreatedAtUtc = DateTime.UtcNow
            };

            store.Orders[order.Id] = order;

            var response = new OrderDto(order.Id, order.ReservationId, order.TotalAmount, order.Status, order.CreatedAtUtc);
            return Results.Created($"/orders/{order.Id}", response);
        });

        // --- Rutas de Orders ---
        var ordersGroup = app.MapGroup("/orders/{orderId:guid}");

        // 5. GET /orders/{orderId}
        ordersGroup.MapGet("/", (Guid orderId, BookingMemoryStore store) =>
        {
            if (!store.Orders.TryGetValue(orderId, out var order))
            {
                return Results.NotFound(new { error = "Orden no encontrada." });
            }

            var response = new OrderDto(order.Id, order.ReservationId, order.TotalAmount, order.Status, order.CreatedAtUtc);
            return Results.Ok(response);
        });

        // 6. POST /orders/{orderId}/confirm-payment
        ordersGroup.MapPost("/confirm-payment", (Guid orderId, PaymentConfirmationDto dto, BookingMemoryStore store) =>
        {
            if (!store.Orders.TryGetValue(orderId, out var order))
            {
                return Results.NotFound(new { error = "Orden no encontrada." });
            }

            if (order.Status == "Paid")
            {
                return Results.BadRequest(new { error = "La orden ya ha sido pagada previamente." });
            }

            var reservation = store.Reservations[order.ReservationId];
            if (DateTime.UtcNow > reservation.ExpiresAtUtc)
            {
                return Results.BadRequest(new { error = "La reserva ha expirado." });
            }

            order.Status = "Paid";
            order.ExternalTransactionId = dto.ExternalTransactionId;
            reservation.IsConfirmed = true;

            var issuedTickets = new List<TicketDto>();

            foreach (var seatId in reservation.SeatIds)
            {
                var seat = store.Seats[seatId];
                seat.Status = "Sold";

                var ticket = new Ticket
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    SeatId = seat.Id,
                    TicketNumber = $"TK-{Random.Shared.Next(10000, 99999)}",
                    Section = seat.Section,
                    Price = seat.Price,
                    CreatedAtUtc = DateTime.UtcNow
                };

                store.Tickets[ticket.Id] = ticket;
                issuedTickets.Add(new TicketDto(ticket.Id, ticket.TicketNumber, ticket.Section, ticket.Price));
            }

            return Results.Ok(new { message = "Pago exitoso", tickets = issuedTickets });
        });
    }
}