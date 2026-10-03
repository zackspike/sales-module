using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Migrations;

/// <inheritdoc />
public partial class EncapsulateTicketAndAddConcurrency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tickets_IdempotencyKey",
            table: "tickets");

        migrationBuilder.AlterColumn<string>(
            name: "TicketCode",
            table: "tickets",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100);

        migrationBuilder.AlterColumn<Guid>(
            name: "IdempotencyKey",
            table: "tickets",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<string>(
            name: "FullName",
            table: "tickets",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200);

        migrationBuilder.AlterColumn<string>(
            name: "Email",
            table: "tickets",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256);

        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            table: "tickets",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

        // Legacy rows stored sentinel values for unsold seats. Convert them to NULL before
        // creating the filtered unique index; otherwise every Guid.Empty key collides (23505).
        migrationBuilder.Sql(
            "UPDATE tickets SET \"IdempotencyKey\" = NULL WHERE \"IdempotencyKey\" = '00000000-0000-0000-0000-000000000000';");
        migrationBuilder.Sql(
            "UPDATE tickets SET \"FullName\" = NULL WHERE \"FullName\" = '';");
        migrationBuilder.Sql(
            "UPDATE tickets SET \"Email\" = NULL WHERE \"Email\" = '';");
        migrationBuilder.Sql(
            "UPDATE tickets SET \"TicketCode\" = NULL WHERE \"TicketCode\" = '';");

        migrationBuilder.CreateIndex(
            name: "IX_tickets_IdempotencyKey",
            table: "tickets",
            column: "IdempotencyKey",
            unique: true,
            filter: "\"IdempotencyKey\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tickets_IdempotencyKey",
            table: "tickets");

        migrationBuilder.DropColumn(
            name: "xmin",
            table: "tickets");

        migrationBuilder.AlterColumn<string>(
            name: "TicketCode",
            table: "tickets",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100,
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "IdempotencyKey",
            table: "tickets",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "FullName",
            table: "tickets",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Email",
            table: "tickets",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_tickets_IdempotencyKey",
            table: "tickets",
            column: "IdempotencyKey");
    }
}
