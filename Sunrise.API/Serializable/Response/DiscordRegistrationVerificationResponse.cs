using System.Text.Json.Serialization;

namespace Sunrise.API.Serializable.Response;

public sealed class DiscordRegistrationVerificationResponse(string username, string email, DateTime expiresAt)
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = username;

    [JsonPropertyName("email")]
    public string Email { get; set; } = email;

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; } = expiresAt;
}
