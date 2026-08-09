using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UsageBilling.Infrastructure.Persistence;

public sealed class DesignTimeBillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=usage_billing;Username=usage_billing;Password=usage_billing")
            .Options;
        return new BillingDbContext(options);
    }
}