using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Migrations;

/// <summary>
/// Seeds the agreed test event (same data as <c>InMemoryEventCatalog.DefaultEvent</c>) with 50 available
/// event seats, each referencing a deterministic venue seat (same as <c>InMemoryVenueSeatCatalog</c>).
/// </summary>
public partial class SeedDefaultEvent : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO events ("Id", "Name", "ArtistId", "ArtistName", "VenueId", "VenueName",
                                "EventDateTime", "EventSalesStartDateTime", "EventSalesEndDateTime")
            VALUES ('11111111-1111-1111-1111-111111111111', 'Rock Fest 2026',
                    '99999999-9999-9999-9999-999999999999', 'The Rockers',
                    '22222222-2222-2222-2222-222222222222', 'Estadio Nacional',
                    '2026-11-20T20:00:00Z', '2026-09-01T00:00:00Z', '2026-11-20T19:00:00Z')
            ON CONFLICT ("Id") DO NOTHING;

            INSERT INTO event_seats ("Id", "EventId", "VenueSeatId", "ZoneId", "Status")
            SELECT gen_random_uuid(), '11111111-1111-1111-1111-111111111111',
                   (lpad(n::text, 8, '0') || '-0000-0000-0000-000000000000')::uuid,
                   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Available'
            FROM generate_series(1, 50) AS n
            ON CONFLICT ("EventId", "VenueSeatId") DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM tickets
            WHERE "EventSeatId" IN (
                SELECT "Id" FROM event_seats
                WHERE "EventId" = '11111111-1111-1111-1111-111111111111'
            );
            DELETE FROM event_seats WHERE "EventId" = '11111111-1111-1111-1111-111111111111';
            DELETE FROM events WHERE "Id" = '11111111-1111-1111-1111-111111111111';
            """);
    }
}
