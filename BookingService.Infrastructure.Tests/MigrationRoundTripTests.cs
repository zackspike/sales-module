using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace BookingService.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MigrationRoundTripTests
{
    private const string PreviousMigration = "20261101063350_AddTicketCodeIndex";

    private readonly PostgresFixture _postgres;

    public MigrationRoundTripTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task AddPurchaseSalesAndOutbox_goes_down_and_up_cleanly()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString)
        {
            Database = $"roundtrip_{Guid.NewGuid():N}"
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(connectionString).Options;

        await using var db = new BookingDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            Assert.True(await TablesExistAsync(db));

            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            Assert.False(await TablesExistAsync(db));
            Assert.False(await TicketColumnExistsAsync(db));

            await db.Database.MigrateAsync();
            Assert.True(await TablesExistAsync(db));
            Assert.True(await TicketColumnExistsAsync(db));
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<bool> TablesExistAsync(BookingDbContext db) =>
        await ScalarAsync<bool>(db, "SELECT to_regclass('outbox_messages') IS NOT NULL AND to_regclass('purchase_sales') IS NOT NULL");

    private static async Task<bool> TicketColumnExistsAsync(BookingDbContext db) =>
        await ScalarAsync<bool>(db, """
            SELECT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_name = 'tickets' AND column_name = 'PurchaseSaleIdempotencyKey')
            """);

    // The SQL is a constant of this test, not user input; SqlQuery would send it as a parameter instead of SQL.
#pragma warning disable EF1002
    private static async Task<T> ScalarAsync<T>(BookingDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<T>($"SELECT ({sql}) AS \"Value\"").SingleAsync();
#pragma warning restore EF1002
}
