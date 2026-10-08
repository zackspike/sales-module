using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Migrations;

/// <summary>
/// Seeds the agreed test event (same data as <c>InMemoryEventCatalog.DefaultEvent</c>) with 50 available seats A-1..A-50.
/// </summary>
public partial class SeedDefaultEvent : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO events ("Id", "Name", "Artist", "VenueId", "VenueName", "Date", "TotalSeats")
            VALUES ('11111111-1111-1111-1111-111111111111', 'Rock Fest 2026', 'The Rockers',
                    '22222222-2222-2222-2222-222222222222', 'Estadio Nacional', '2026-11-20T20:00:00Z', 50)
            ON CONFLICT ("Id") DO NOTHING;

            INSERT INTO tickets ("Id", "EventId", "SeatNumber", "Status", "FullName", "Email", "TicketCode", "IdempotencyKey", "CreatedAtUtc")
            SELECT gen_random_uuid(), '11111111-1111-1111-1111-111111111111', 'A-' || n, 'Available', '', '', '',
                   '00000000-0000-0000-0000-000000000000', now()
            FROM generate_series(1, 50) AS n
            ON CONFLICT ("EventId", "SeatNumber") DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM tickets WHERE "EventId" = '11111111-1111-1111-1111-111111111111';
            DELETE FROM events WHERE "Id" = '11111111-1111-1111-1111-111111111111';
            """);
    }
}
