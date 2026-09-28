namespace BookingService.Domain.Exceptions;

public sealed class TicketAlreadySoldException : DomainException
{
    public TicketAlreadySoldException(Guid ticketId)
        : base("TICKET_ALREADY_SOLD", "The requested seat has already been purchased.")
    {
        TicketId = ticketId;
    }

    public Guid TicketId { get; }
}
