namespace BookingService.Domain;

/// <summary>
/// Represents a ticket for an event seat.
/// This entity resembles a physical ticket that is issued to a customer when they purchase a seat for an event.
/// This means that a ticket is associated with a specific event seat, and it contains information about the customer who purchased the ticket, as well as the time of purchase.
/// </summary>
public class Ticket
{
    public Guid Id { get; private set; }
    public string TicketCode { get; private set; } = string.Empty; // A unique code that identifies the ticket. This code is generated when the ticket is generated.
    public Guid EventSeatId { get; private set; } // The event seat associated with this ticket
    public string HolderName { get; private set; } = string.Empty; // The full name of the person who holds the ticket. Not necessarily the same as the buyer of the ticket (if eventually it is possile to buy more than one ticket per order). This is the name that would be printed on the ticket.
    public string HolderEmail { get; private set; } = string.Empty; // The email of the person who holds the ticket. Not necessarily the same as the buyer of the ticket (if eventually it is possile to buy more than one ticket per order). 
    public DateTime SoldAtUtc { get; private set; }

    internal static Ticket Issue(Guid eventSeatId, string holderName, string holderEmail, DateTime soldAtUtc)
    {
        return new Ticket
        {
            Id = Guid.NewGuid(),
            TicketCode = TicketCodeGenerator.GenerateTicketCode(),
            EventSeatId = eventSeatId,
            HolderName = holderName,
            HolderEmail = holderEmail,
            SoldAtUtc = soldAtUtc
        };
    }

}
