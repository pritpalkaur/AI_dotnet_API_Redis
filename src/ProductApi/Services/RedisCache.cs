using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace ProductApi.Services;

public interface IAppCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Thin JSON wrapper over Redis (IDistributedCache). Cache failures never fail a request:
/// the API keeps serving from SQL Server, and after an error Redis is skipped for a short
/// cool-down so a down Redis doesn't add a connect timeout to every call.
/// </summary>
public class RedisCache(IDistributedCache cache, ILogger<RedisCache> logger) : IAppCache
{
    private static readonly TimeSpan CoolDown = TimeSpan.FromSeconds(30);
    private static long _skipUntilTicks;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (IsCoolingDown()) return default;
        try
        {
            var bytes = await cache.GetAsync(key, ct);
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trip(ex, "read", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        if (IsCoolingDown()) return;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            await cache.SetAsync(key, bytes, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trip(ex, "write", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        if (IsCoolingDown()) return;
        try
        {
            await cache.RemoveAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trip(ex, "remove", key);
        }
    }

    private static bool IsCoolingDown() => DateTime.UtcNow.Ticks < Interlocked.Read(ref _skipUntilTicks);

    private void Trip(Exception ex, string op, string key)
    {
        Interlocked.Exchange(ref _skipUntilTicks, DateTime.UtcNow.Add(CoolDown).Ticks);
        logger.LogWarning(ex, "Redis {Op} failed for key {Key}; bypassing cache for {Seconds}s", op, key, CoolDown.TotalSeconds);
    }
}
