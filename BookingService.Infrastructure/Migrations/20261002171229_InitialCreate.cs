using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Migrations;

/// <inheritdoc />
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
                Artist = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                VenueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                TotalSeats = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "tickets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                SeatNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                TicketCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PurchasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tickets", x => x.Id);
                table.ForeignKey(
                    name: "FK_tickets_events_EventId",
                    column: x => x.EventId,
                    principalTable: "events",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_tickets_EventId_SeatNumber",
            table: "tickets",
            columns: new[] { "EventId", "SeatNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_tickets_IdempotencyKey",
            table: "tickets",
            column: "IdempotencyKey");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "tickets");

        migrationBuilder.DropTable(
            name: "events");
    }
}
