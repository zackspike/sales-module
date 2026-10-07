using BookingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingService.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(e => e.Name).HasColumnName("event_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Artist).HasColumnName("artist_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.VenueId).HasColumnName("venue_id");
        builder.Property(e => e.VenueName).HasColumnName("venue_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Date).HasColumnName("event_date_utc");
        builder.Property(e => e.TotalSeats).HasColumnName("total_seat_count");

        builder.HasMany(e => e.Zones)
            .WithOne()
            .HasForeignKey(z => z.EventId);
    }
}
