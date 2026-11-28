using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Migrations;

/// <summary>
/// Adds purchase_sales (the idempotency key of each paid purchase, linked from its tickets) and outbox_messages
/// (the ticketSold messages the relay publishes to the broker).
/// </summary>
public partial class AddPurchaseSalesAndOutbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "PurchaseSaleIdempotencyKey",
            table: "tickets",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Payload = table.Column<string>(type: "jsonb", nullable: false),
                PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "purchase_sales",
            columns: table => new
            {
                IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                PurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchase_sales", x => x.IdempotencyKey);
            });

        migrationBuilder.CreateIndex(
            name: "IX_tickets_PurchaseSaleIdempotencyKey",
            table: "tickets",
            column: "PurchaseSaleIdempotencyKey");

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_OccurredAtUtc",
            table: "outbox_messages",
            column: "OccurredAtUtc",
            filter: "\"PublishedAtUtc\" IS NULL");

        migrationBuilder.AddForeignKey(
            name: "FK_tickets_purchase_sales_PurchaseSaleIdempotencyKey",
            table: "tickets",
            column: "PurchaseSaleIdempotencyKey",
            principalTable: "purchase_sales",
            principalColumn: "IdempotencyKey",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_tickets_purchase_sales_PurchaseSaleIdempotencyKey",
            table: "tickets");

        migrationBuilder.DropTable(
            name: "outbox_messages");

        migrationBuilder.DropTable(
            name: "purchase_sales");

        migrationBuilder.DropIndex(
            name: "IX_tickets_PurchaseSaleIdempotencyKey",
            table: "tickets");

        migrationBuilder.DropColumn(
            name: "PurchaseSaleIdempotencyKey",
            table: "tickets");
    }
}
