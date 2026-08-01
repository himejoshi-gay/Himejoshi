using System.Text.Json.Serialization;

namespace Sunrise.API.Serializable.Response;

public sealed class DiscordRegistrationStartResponse(string authorizationUrl)
{
    [JsonPropertyName("authorization_url")]
    public string AuthorizationUrl { get; set; } = authorizationUrl;
}
