using BookingService.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingService.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public const int MaxLastErrorLength = 1000;

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.Type)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(m => m.OccurredAtUtc)
            .IsRequired();

        builder.Property(m => m.Payload)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(m => m.LastError)
            .HasMaxLength(MaxLastErrorLength);

        // The relay only ever reads the pending messages, oldest first.
        builder.HasIndex(m => m.OccurredAtUtc)
            .HasFilter("\"PublishedAtUtc\" IS NULL");
    }
}
