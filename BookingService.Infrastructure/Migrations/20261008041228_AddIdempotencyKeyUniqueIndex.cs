using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddIdempotencyKeyUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tickets_IdempotencyKey",
            table: "tickets");

        migrationBuilder.CreateIndex(
            name: "IX_tickets_IdempotencyKey",
            table: "tickets",
            column: "IdempotencyKey",
            unique: true,
            filter: "\"IdempotencyKey\" != '00000000-0000-0000-0000-000000000000'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tickets_IdempotencyKey",
            table: "tickets");

        migrationBuilder.CreateIndex(
            name: "IX_tickets_IdempotencyKey",
            table: "tickets",
            column: "IdempotencyKey");
    }
}
