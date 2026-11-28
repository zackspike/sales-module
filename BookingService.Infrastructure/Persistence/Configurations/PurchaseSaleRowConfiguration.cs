using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingService.Infrastructure.Persistence.Configurations;

public class PurchaseSaleRowConfiguration : IEntityTypeConfiguration<PurchaseSaleRow>
{
    public void Configure(EntityTypeBuilder<PurchaseSaleRow> builder)
    {
        builder.ToTable("purchase_sales");

        builder.HasKey(s => s.IdempotencyKey);

        builder.Property(s => s.IdempotencyKey)
            .ValueGeneratedNever();

        builder.Property(s => s.PurchaseId)
            .IsRequired();

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired();
    }
}
