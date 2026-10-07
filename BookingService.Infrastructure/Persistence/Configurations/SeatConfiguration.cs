using BookingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingService.Infrastructure.Persistence.Configurations;

public sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("zone_seats");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("seat_id").ValueGeneratedNever();
        builder.Property(s => s.ZoneId).HasColumnName("zone_id");
        builder.Property(s => s.SeatNumber).HasColumnName("seat_number").HasMaxLength(20).IsRequired();
    }
}
