using BookingService.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingService.Infrastructure.Persistence.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Artist)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.VenueId)
            .IsRequired();

        builder.Property(e => e.VenueName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Date)
            .IsRequired();

        builder.Property(e => e.TotalSeats)
            .IsRequired();
    }
}
