using BookingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingService.Infrastructure.Persistence.Configurations;

public sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("event_zones");

        builder.HasKey(z => z.Id);
        builder.Property(z => z.Id).HasColumnName("zone_id").ValueGeneratedNever();
        builder.Property(z => z.EventId).HasColumnName("event_id");
        builder.Property(z => z.Price).HasColumnName("zone_price").HasPrecision(12, 2);

        builder.HasMany(z => z.Seats)
            .WithOne(s => s.Zone)
            .HasForeignKey(s => s.ZoneId);
    }
}
