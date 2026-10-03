using BookingService.Domain.Events;
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

        builder.Property(t => t.EventId)
            .IsRequired();

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(t => t.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(t => t.SeatNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(t => t.FullName)
            .HasMaxLength(200);

        builder.Property(t => t.Email)
            .HasMaxLength(256);

        builder.Property(t => t.TicketCode)
            .HasMaxLength(100);

        builder.Property(t => t.IdempotencyKey);

        builder.Property(t => t.CreatedAtUtc)
            .IsRequired();

        builder.Property(t => t.PurchasedAtUtc);

        builder.HasIndex(t => new { t.EventId, t.SeatNumber })
            .IsUnique();

        // Unique filtered index: ensures no idempotency key is duplicated once claimed,
        // while allowing multiple NULLs for unsold seats.
        builder.HasIndex(t => t.IdempotencyKey)
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        // Optimistic concurrency control mapped to PostgreSQL xmin system column
        builder.Property<uint>("Version").IsRowVersion();
    }
}
