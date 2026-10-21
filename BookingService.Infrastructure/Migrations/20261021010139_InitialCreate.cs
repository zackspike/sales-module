using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Migrations;

/// <summary>
/// Creates the schema and seeds the agreed test event (same data as <c>InMemoryEventCatalog.DefaultEvent</c>)
/// with 50 available event seats, each referencing a deterministic venue seat (same as
/// <c>InMemoryVenueSeatCatalog</c>).
/// </summary>
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtistName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                VenueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                EventDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EventSalesStartDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EventSalesEndDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "event_seats",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                VenueSeatId = table.Column<Guid>(type: "uuid", nullable: false),
                ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_event_seats", x => x.Id);
                table.ForeignKey(
                    name: "FK_event_seats_events_EventId",
                    column: x => x.EventId,
                    principalTable: "events",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "tickets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TicketCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                EventSeatId = table.Column<Guid>(type: "uuid", nullable: false),
                HolderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                HolderEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                SoldAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tickets", x => x.Id);
                table.ForeignKey(
                    name: "FK_tickets_event_seats_EventSeatId",
                    column: x => x.EventSeatId,
                    principalTable: "event_seats",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_event_seats_EventId_VenueSeatId",
            table: "event_seats",
            columns: new[] { "EventId", "VenueSeatId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_tickets_EventSeatId",
            table: "tickets",
            column: "EventSeatId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_tickets_IdempotencyKey",
            table: "tickets",
            column: "IdempotencyKey",
            unique: true,
            filter: "\"IdempotencyKey\" != '00000000-0000-0000-0000-000000000000'");

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

        migrationBuilder.DropTable(
            name: "tickets");

        migrationBuilder.DropTable(
            name: "event_seats");

        migrationBuilder.DropTable(
            name: "events");
    }
}
