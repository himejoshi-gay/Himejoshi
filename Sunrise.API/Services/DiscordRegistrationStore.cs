using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using StackExchange.Redis;
using Sunrise.Shared.Application;

namespace Sunrise.API.Services;

public sealed class DiscordRegistrationStore(ConnectionMultiplexer redis, TimeProvider timeProvider)
{
    private const string StatePrefix = "registration:discord:state:";
    private const string GrantPrefix = "registration:discord:grant:";
    private const string ReservationPrefix = "registration:discord:reservation:";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase _database = redis.GetDatabase(0);

    public async Task<DiscordOAuthChallenge> CreateChallengeAsync(
        string ipHash,
        string installationIdHash,
        string browserFingerprintHash,
        int fingerprintVersion,
        CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var state = CreateOpaqueToken();
            var codeVerifier = CreateCodeVerifier();
            var codeChallenge = WebEncoders.Base64UrlEncode(
                SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
            var expiresAt = timeProvider.GetUtcNow().Add(Configuration.DiscordOAuthStateLifetime).UtcDateTime;

            var data = new DiscordOAuthChallengeData(
                ipHash,
                installationIdHash,
                browserFingerprintHash,
                fingerprintVersion,
                codeVerifier,
                expiresAt);

            var stored = await _database.StringSetAsync(
                    StateKey(state),
                    JsonSerializer.Serialize(data, JsonOptions),
                    Configuration.DiscordOAuthStateLifetime,
                    When.NotExists)
                .WaitAsync(ct);

            if (stored)
                return new DiscordOAuthChallenge(state, codeChallenge);
        }

        throw new InvalidOperationException("Unable to allocate a Discord OAuth state.");
    }

    public async Task<DiscordOAuthChallengeData?> ConsumeChallengeAsync(string state, CancellationToken ct = default)
    {
        var value = await _database.ScriptEvaluateAsync(
                "local value = redis.call('GET', KEYS[1]); if value then redis.call('DEL', KEYS[1]); end; return value;",
                [StateKey(state)])
            .WaitAsync(ct);

        if (value.IsNull)
            return null;

        return JsonSerializer.Deserialize<DiscordOAuthChallengeData>((string)value!, JsonOptions);
    }

    public async Task<string> IssueGrantAsync(DiscordRegistrationGrantData data, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var token = CreateOpaqueToken();
            var stored = await _database.StringSetAsync(
                    GrantKey(token),
                    JsonSerializer.Serialize(data, JsonOptions),
                    Configuration.DiscordOAuthGrantLifetime,
                    When.NotExists)
                .WaitAsync(ct);

            if (stored)
                return token;
        }

        throw new InvalidOperationException("Unable to allocate a Discord registration grant.");
    }

    public async Task<DiscordRegistrationGrantData?> GetGrantAsync(string token, CancellationToken ct = default)
    {
        var value = await _database.StringGetAsync(GrantKey(token)).WaitAsync(ct);
        return value.HasValue
            ? JsonSerializer.Deserialize<DiscordRegistrationGrantData>(value!, JsonOptions)
            : null;
    }

    public async Task<ReservedDiscordRegistrationGrant?> ReserveGrantAsync(string token, CancellationToken ct = default)
    {
        var reservationId = CreateOpaqueToken();
        const int reservationLifetimeMilliseconds = 60_000;

        var result = await _database.ScriptEvaluateAsync(
                "local grant = redis.call('GET', KEYS[1]); " +
                "if not grant then return nil; end; " +
                "local locked = redis.call('SET', KEYS[2], ARGV[1], 'NX', 'PX', ARGV[2]); " +
                "if not locked then return nil; end; return grant;",
                [GrantKey(token), ReservationKey(token)],
                [reservationId, reservationLifetimeMilliseconds])
            .WaitAsync(ct);

        if (result.IsNull)
            return null;

        var data = JsonSerializer.Deserialize<DiscordRegistrationGrantData>((string)result!, JsonOptions);
        return data == null ? null : new ReservedDiscordRegistrationGrant(reservationId, data);
    }

    public async Task ReleaseReservationAsync(string token, string reservationId, CancellationToken ct = default)
    {
        await _database.ScriptEvaluateAsync(
                "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]); end; return 0;",
                [ReservationKey(token)],
                [reservationId])
            .WaitAsync(ct);
    }

    public async Task CompleteGrantAsync(string token, CancellationToken ct = default)
    {
        await _database.KeyDeleteAsync([GrantKey(token), ReservationKey(token)]).WaitAsync(ct);
    }

    private static string CreateOpaqueToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeVerifier() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));

    private static RedisKey StateKey(string state) => $"{StatePrefix}{HashToken(state)}";
    private static RedisKey GrantKey(string token) => $"{GrantPrefix}{HashToken(token)}";
    private static RedisKey ReservationKey(string token) => $"{ReservationPrefix}{HashToken(token)}";

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public sealed record DiscordOAuthChallenge(string State, string CodeChallenge);

public sealed record DiscordOAuthChallengeData(
    string IpHash,
    string InstallationIdHash,
    string BrowserFingerprintHash,
    int FingerprintVersion,
    string CodeVerifier,
    DateTime ExpiresAt);

public sealed record DiscordRegistrationGrantData(
    string DiscordSubjectHash,
    string DiscordUsername,
    string Email,
    DateTime DiscordAccountCreatedAt,
    string IpHash,
    string InstallationIdHash,
    string BrowserFingerprintHash,
    int FingerprintVersion,
    DateTime VerifiedAt,
    DateTime ExpiresAt);

public sealed record ReservedDiscordRegistrationGrant(string ReservationId, DiscordRegistrationGrantData Data);
