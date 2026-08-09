using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UsageBilling.Domain;
using UsageBilling.Domain.Services;
using UsageBilling.Infrastructure.Configuration;
using UsageBilling.Infrastructure.Messaging;
using UsageBilling.Infrastructure.Payments;
using UsageBilling.Infrastructure.Persistence;

namespace UsageBilling.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddUsageBillingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BillingStorageOptions>(configuration.GetSection(BillingStorageOptions.SectionName));
        services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.SectionName));
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));

        var storage = configuration.GetSection(BillingStorageOptions.SectionName).Get<BillingStorageOptions>() ?? new BillingStorageOptions();
        storage.ConnectionString = configuration.GetConnectionString("billing") ?? storage.ConnectionString;
        services.PostConfigure<BillingStorageOptions>(options => options.ConnectionString = configuration.GetConnectionString("billing") ?? options.ConnectionString);
        services.PostConfigure<RedisOptions>(options => options.ConnectionString = configuration.GetConnectionString("redis") ?? options.ConnectionString);
        if (string.Equals(storage.Provider, "Postgres", StringComparison.OrdinalIgnoreCase))
            services.AddDbContext<BillingDbContext>(options => options.UseNpgsql(storage.ConnectionString));
        else
            services.AddDbContext<BillingDbContext>(options => options.UseInMemoryDatabase("usage-billing"));

        services.AddScoped<IBillingRepository, EfBillingRepository>();
        services.AddSingleton<IUsageMessagePublisher, UsageMessagePublisher>();
        services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<UsageApplicationService>();
        services.AddScoped<BillingApplicationService>();
        services.AddScoped<BillingReadService>();
        services.AddSingleton<IOverviewCache, OverviewCache>();
        services.AddScoped<DatabaseSeeder>();
        return services;
    }
}