using BookingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingService.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(u => u.FullName).HasColumnName("user_full_name").HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasColumnName("user_email_address").HasMaxLength(254).IsRequired();
    }
}
