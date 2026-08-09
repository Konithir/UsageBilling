using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UsageBilling.Contracts;
using UsageBilling.Infrastructure.Configuration;

namespace UsageBilling.Infrastructure.Persistence;

public interface IOverviewCache
{
    Task<OverviewDto?> GetAsync(CancellationToken cancellationToken);
    Task SetAsync(OverviewDto value, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

public sealed class OverviewCache : IOverviewCache, IAsyncDisposable
{
    private const string CacheKey = "usage-billing:overview:v1";
    private readonly RedisOptions options;
    private readonly Lazy<Task<ConnectionMultiplexer>> connection;

    public OverviewCache(IOptions<RedisOptions> options)
    {
        this.options = options.Value;
        connection = new Lazy<Task<ConnectionMultiplexer>>(() => ConnectionMultiplexer.ConnectAsync(this.options.ConnectionString));
    }

    public async Task<OverviewDto?> GetAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled) return null;
        try
        {
            var database = (await connection.Value).GetDatabase();
            var value = await database.StringGetAsync(CacheKey);
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<OverviewDto>(value.ToString());
        }
        catch { return null; }
    }

    public async Task SetAsync(OverviewDto value, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return;
        try
        {
            var database = (await connection.Value).GetDatabase();
            await database.StringSetAsync(CacheKey, JsonSerializer.Serialize(value), TimeSpan.FromSeconds(15));
        }
        catch { }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled) return;
        try { await (await connection.Value).GetDatabase().KeyDeleteAsync(CacheKey); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (connection.IsValueCreated) (await connection.Value).Dispose();
    }
}