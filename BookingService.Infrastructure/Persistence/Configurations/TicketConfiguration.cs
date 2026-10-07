using BookingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingService.Infrastructure.Persistence.Configurations;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("ticket_id").ValueGeneratedNever();
        builder.Property(t => t.SeatId).HasColumnName("seat_id");
        builder.Property(t => t.UserId).HasColumnName("user_id");
        builder.Property(t => t.TicketCode).HasColumnName("ticket_code").HasMaxLength(40);
        builder.Property(t => t.CreatedAtUtc).HasColumnName("ticket_created_at_utc");
        builder.Property(t => t.PurchasedAtUtc).HasColumnName("ticket_purchased_at_utc");
        builder.Property(t => t.IdempotencyKey).HasColumnName("purchase_idempotency_key");

        // The table stores availability as a flag (diagram: "available").
        builder.Property(t => t.Status)
            .HasColumnName("is_ticket_available")
            .HasConversion(
                status => status == TicketStatus.Available,
                isAvailable => isAvailable ? TicketStatus.Available : TicketStatus.Sold);

        builder.HasOne(t => t.Seat)
            .WithOne()
            .HasForeignKey<Ticket>(t => t.SeatId);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId);
    }
}
