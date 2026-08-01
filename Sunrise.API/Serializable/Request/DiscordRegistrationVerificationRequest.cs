using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sunrise.API.Serializable.Request;

public sealed class DiscordRegistrationVerificationRequest : DiscordRegistrationStartRequest
{
    [JsonPropertyName("verification_token")]
    [Required]
    [StringLength(128, MinimumLength = 32)]
    public required string VerificationToken { get; set; }
}
