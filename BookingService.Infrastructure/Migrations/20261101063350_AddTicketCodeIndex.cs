using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Migrations;

/// <summary>
/// Adds a unique index on tickets.TicketCode, used to look up an issued ticket by its code.
/// </summary>
public partial class AddTicketCodeIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_tickets_TicketCode",
            table: "tickets",
            column: "TicketCode",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tickets_TicketCode",
            table: "tickets");
    }
}
