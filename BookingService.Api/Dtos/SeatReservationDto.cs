namespace BookingService.Api.Dtos;

/// <summary>
/// Seat locked for a buyer until <see cref="ExpiresAtUtc"/>.
/// </summary>
public sealed record SeatReservationDto(
    Guid TicketId,
    Guid EventId,
    string SeatNumber,
    Guid UserId,
    DateTime ExpiresAtUtc);
