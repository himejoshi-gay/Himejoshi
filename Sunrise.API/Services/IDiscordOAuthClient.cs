namespace Sunrise.API.Services;

public interface IDiscordOAuthClient
{
    string CreateAuthorizationUrl(string state, string codeChallenge);
    Task<DiscordOAuthToken> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct = default);
    Task<DiscordOAuthUser> GetCurrentUserAsync(string accessToken, CancellationToken ct = default);
    Task RevokeTokenAsync(string accessToken, CancellationToken ct = default);
}

public sealed record DiscordOAuthToken(string AccessToken, string TokenType, string Scope);

public sealed record DiscordOAuthUser(
    string Id,
    string Username,
    string? GlobalName,
    string? Email,
    bool Verified,
    bool Bot,
    bool System);

public sealed class DiscordOAuthException : Exception
{
    public DiscordOAuthException(string message) : base(message)
    {
    }

    public DiscordOAuthException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
