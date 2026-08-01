using StackExchange.Redis;

namespace Sunrise.Shared.Services;

public sealed class RegistrationAbuseService(ConnectionMultiplexer redis)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private readonly IDatabase _database = redis.GetDatabase(0);

    public Task<RegistrationRateLimitResult> CheckOAuthStartAsync(
        string ipHash,
        string installationIdHash,
        string fingerprintHash,
        CancellationToken ct = default) =>
        CheckAsync("oauth-start", [("ip", ipHash, 10), ("installation", installationIdHash, 10), ("fingerprint", fingerprintHash, 20)], ct);

    public Task<RegistrationRateLimitResult> CheckRegistrationAsync(
        string ipHash,
        string installationIdHash,
        string fingerprintHash,
        CancellationToken ct = default) =>
        CheckAsync("register", [("ip", ipHash, 5), ("installation", installationIdHash, 5), ("fingerprint", fingerprintHash, 10)], ct);

    private async Task<RegistrationRateLimitResult> CheckAsync(
        string action,
        IEnumerable<(string Type, string Hash, int Limit)> limits,
        CancellationToken ct)
    {
        var retryAfter = TimeSpan.Zero;
        var limited = false;

        foreach (var (type, hash, limit) in limits)
        {
            var result = (RedisResult[]?)await _database.ScriptEvaluateAsync(
                    "local count = redis.call('INCR', KEYS[1]); " +
                    "if count == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]); end; " +
                    "return {count, redis.call('PTTL', KEYS[1])};",
                    [$"registration:rate:{action}:{type}:{hash}"],
                    [(long)Window.TotalMilliseconds])
                .WaitAsync(ct);

            if (result is not { Length: 2 })
                throw new InvalidOperationException("Registration rate limiter returned an invalid response.");

            var count = (long)result[0];
            var ttl = TimeSpan.FromMilliseconds(Math.Max(0, (long)result[1]));
            if (count > limit)
            {
                limited = true;
                if (ttl > retryAfter)
                    retryAfter = ttl;
            }
        }

        return new RegistrationRateLimitResult(limited, retryAfter);
    }
}

public sealed record RegistrationRateLimitResult(bool IsLimited, TimeSpan RetryAfter);
