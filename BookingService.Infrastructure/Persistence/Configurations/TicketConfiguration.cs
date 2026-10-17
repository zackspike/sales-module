using BookingService.Domain.EventSeats;
using BookingService.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingService.Infrastructure.Persistence.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.EventSeatId)
            .IsRequired();

        builder.HasOne<EventSeat>()
            .WithMany()
            .HasForeignKey(t => t.EventSeatId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.EventSeatId)
            .IsUnique();

        builder.Property(t => t.TicketCode)
            .HasMaxLength(100);

        builder.Property(t => t.HolderName)
            .IsRequired()
            .HasMaxLength(Ticket.MaxHolderFullNameLength);

        builder.Property(t => t.HolderEmail)
            .IsRequired()
            .HasMaxLength(Ticket.MaxHolderEmailLength);

        builder.Property(t => t.SoldAtUtc)
            .IsRequired();

        builder.Property<Guid>("IdempotencyKey")
            .IsRequired();

        builder.HasIndex("IdempotencyKey")
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" != '00000000-0000-0000-0000-000000000000'");
    }
}