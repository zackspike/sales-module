using BookingService.Application.Abstractions;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BookingService.Infrastructure.Tests;

public class InfrastructureRegistrationTests
{
    private const string Postgres = "Host=localhost;Port=1;Database=x;Username=x;Password=x";
    private const string RabbitMq = "amqp://guest:guest@localhost:1/";

    [Fact]
    public void Postgres_and_RabbitMq_register_the_sale_the_outbox_and_the_relay()
    {
        var services = Register(new()
        {
            ["ConnectionStrings:DefaultConnection"] = Postgres,
            ["ConnectionStrings:RabbitMq"] = RabbitMq,
            ["Outbox:PollInterval"] = "00:00:02",
            ["RabbitMq:Exchange"] = "custom.events"
        });

        Assert.Contains(services, d => d.ServiceType == typeof(IPurchaseSaleRepository)
            && d.ImplementationType == typeof(PostgresPurchaseSaleRepository));
        Assert.Contains(services, d => d.ServiceType == typeof(ITicketSoldOutbox)
            && d.ImplementationType == typeof(PostgresTicketSoldOutbox));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(OutboxRelay));

        using var provider = services.BuildServiceProvider();
        Assert.Equal(TimeSpan.FromSeconds(2), provider.GetRequiredService<OutboxOptions>().PollInterval);
        Assert.Equal(100, provider.GetRequiredService<OutboxOptions>().BatchSize);
    }

    [Fact]
    public void Without_RabbitMq_the_outbox_is_written_but_no_relay_runs()
    {
        var services = Register(new() { ["ConnectionStrings:DefaultConnection"] = Postgres });

        Assert.Contains(services, d => d.ServiceType == typeof(ITicketSoldOutbox));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(OutboxRelay));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IOutboxPublisher));
    }

    [Fact]
    public void Without_Postgres_there_is_no_outbox_and_no_relay_even_with_RabbitMq()
    {
        var services = Register(new() { ["ConnectionStrings:RabbitMq"] = RabbitMq });

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ITicketSoldOutbox));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(OutboxRelay));
    }

    [Theory]
    [InlineData("Outbox:PollInterval", "00:00:00")]
    [InlineData("Outbox:PollInterval", "-00:00:01")]
    [InlineData("Outbox:BatchSize", "0")]
    [InlineData("Outbox:PublishTimeout", "00:00:00")]
    [InlineData("RabbitMq:Exchange", " ")]
    public void Invalid_relay_options_fail_at_startup(string key, string value)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => Register(new()
        {
            ["ConnectionStrings:DefaultConnection"] = Postgres,
            ["ConnectionStrings:RabbitMq"] = RabbitMq,
            [key] = value
        }));

        Assert.Contains(key, exception.Message);
    }

    private static ServiceCollection Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services;
    }
}
