using BookingService.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Persistence;

/// <summary>
/// EF Core session over the PostgreSQL schema defined in <c>database/init/001_create_booking_schema.sql</c>.
/// The SQL script owns the schema; the configurations in <c>Persistence/Configurations</c> only map to it.
/// </summary>
public sealed class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Event> Events => Set<Event>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);
    }
}
