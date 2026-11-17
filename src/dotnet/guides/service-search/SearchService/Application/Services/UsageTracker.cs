using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using SearchService.Application.Options;
using SearchService.Application.Services.Interfaces;
using StackExchange.Redis;

namespace SearchService.Application.Services;

/// <summary>
/// Counts tokens and cost per UTC day. With Redis the counters are shared by every instance
/// (INCRBY is atomic); without it each instance counts on its own. Register as a singleton. If Redis fails, the request is
/// allowed and the failure logged: an outage of the counter should not take answers down.
/// </summary>
public sealed class UsageTracker : IUsageTracker
{
    private readonly ConcurrentDictionary<string, long> local = new();

    private readonly IConnectionMultiplexer? redis;
    private readonly CostCapOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<UsageTracker> logger;

    public UsageTracker(
        IOptions<CostCapOptions> options,
        TimeProvider clock,
        ILogger<UsageTracker> logger,
        IConnectionMultiplexer? redis = null
    )
    {
        this.redis = redis;
        this.options = options.Value;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task EnsureWithinCapAsync(CancellationToken cancellationToken)
    {
        if (options.DailyTokenCap <= 0 && options.DailyCostCapUsd <= 0)
        {
            return;
        }

        var (tokens, microUsd) = await ReadAsync();
        if (options.DailyTokenCap > 0 && tokens >= options.DailyTokenCap)
        {
            throw new CostCapExceededException($"The daily limit of {options.DailyTokenCap:N0} tokens is used up.");
        }

        if (options.DailyCostCapUsd > 0 && microUsd / 1_000_000m >= options.DailyCostCapUsd)
        {
            throw new CostCapExceededException($"The daily budget of ${options.DailyCostCapUsd} is used up.");
        }
    }

    public async Task RecordAsync(long tokens, decimal costUsd, CancellationToken cancellationToken)
    {
        var microUsd = (long)Math.Ceiling(costUsd * 1_000_000m);
        var (tokensKey, costKey) = Keys();
        if (redis is null)
        {
            local.AddOrUpdate(tokensKey, tokens, (_, v) => v + tokens);
            local.AddOrUpdate(costKey, microUsd, (_, v) => v + microUsd);
            return;
        }

        try
        {
            var db = redis.GetDatabase();
            await db.StringIncrementAsync(tokensKey, tokens);
            await db.StringIncrementAsync(costKey, microUsd);
            await db.KeyExpireAsync(tokensKey, TimeSpan.FromDays(2));
            await db.KeyExpireAsync(costKey, TimeSpan.FromDays(2));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Could not record usage in Redis");
        }
    }

    private async Task<(long Tokens, long MicroUsd)> ReadAsync()
    {
        var (tokensKey, costKey) = Keys();
        if (redis is null)
        {
            return (local.GetValueOrDefault(tokensKey), local.GetValueOrDefault(costKey));
        }

        try
        {
            var db = redis.GetDatabase();
            var values = await db.StringGetAsync([tokensKey, costKey]);
            return ((long?)values[0] ?? 0, (long?)values[1] ?? 0);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Could not read usage from Redis; allowing the request");
            return (0, 0);
        }
    }

    private (string Tokens, string Cost) Keys()
    {
        var day = clock.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return ($"wayfinder:usage:{day}:tokens", $"wayfinder:usage:{day}:microusd");
    }
}
