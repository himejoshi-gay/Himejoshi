using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Sunrise.Shared.Application;

namespace Sunrise.API.Services;

public sealed class DiscordOAuthClient(HttpClient httpClient) : IDiscordOAuthClient
{
    public string CreateAuthorizationUrl(string state, string codeChallenge)
    {
        return QueryHelpers.AddQueryString("https://discord.com/oauth2/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = Configuration.DiscordOAuthClientId,
            ["redirect_uri"] = Configuration.DiscordOAuthCallbackUrl,
            ["scope"] = "identify email",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        });
    }

    public async Task<DiscordOAuthToken> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct = default)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = Configuration.DiscordOAuthCallbackUrl,
                ["client_id"] = Configuration.DiscordOAuthClientId,
                ["client_secret"] = Configuration.DiscordOAuthClientSecret,
                ["code_verifier"] = codeVerifier
            });

            using var response = await httpClient.PostAsync("oauth2/token", content, ct);
            if (!response.IsSuccessStatusCode)
                throw new DiscordOAuthException($"Discord token exchange failed with status {(int)response.StatusCode}.");

            var token = await response.Content.ReadFromJsonAsync<DiscordTokenResponse>(cancellationToken: ct);
            if (token == null || string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.TokenType))
                throw new DiscordOAuthException("Discord returned an invalid token response.");

            return new DiscordOAuthToken(token.AccessToken, token.TokenType, token.Scope ?? string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            throw new DiscordOAuthException("Discord token exchange failed.", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new DiscordOAuthException("Discord token exchange timed out.", ex);
        }
    }

    public async Task<DiscordOAuthUser> GetCurrentUserAsync(string accessToken, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "users/@me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new DiscordOAuthException($"Discord user lookup failed with status {(int)response.StatusCode}.");

            var user = await response.Content.ReadFromJsonAsync<DiscordUserResponse>(cancellationToken: ct);
            if (user == null || string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Username))
                throw new DiscordOAuthException("Discord returned an invalid user response.");

            return new DiscordOAuthUser(
                user.Id,
                user.Username,
                user.GlobalName,
                user.Email,
                user.Verified,
                user.Bot,
                user.System);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            throw new DiscordOAuthException("Discord user lookup failed.", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new DiscordOAuthException("Discord user lookup timed out.", ex);
        }
    }

    public async Task RevokeTokenAsync(string accessToken, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = accessToken,
            ["token_type_hint"] = "access_token",
            ["client_id"] = Configuration.DiscordOAuthClientId,
            ["client_secret"] = Configuration.DiscordOAuthClientSecret
        });

        // Revocation is best-effort. The access token is never persisted and naturally expires.
        using var response = await httpClient.PostAsync("oauth2/token/revoke", content, ct);
    }

    private sealed class DiscordTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class DiscordUserResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("global_name")]
        public string? GlobalName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("verified")]
        public bool Verified { get; set; }

        [JsonPropertyName("bot")]
        public bool Bot { get; set; }

        [JsonPropertyName("system")]
        public bool System { get; set; }
    }
}
