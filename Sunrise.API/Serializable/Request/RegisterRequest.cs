using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sunrise.API.Serializable.Request;

public class RegisterRequest
{
    [JsonPropertyName("username")]
    [Required]
    public required string Username { get; set; }

    [JsonPropertyName("password")]
    [Required]
    public required string Password { get; set; }

    [JsonPropertyName("email")]
    [RegularExpression("^\\S+@\\S+\\.\\S+$", ErrorMessage = "Invalid email format")]
    public string? Email { get; set; }

    [JsonPropertyName("discord_verification_token")]
    [StringLength(128, MinimumLength = 32)]
    public string? DiscordVerificationToken { get; set; }

    [JsonPropertyName("browser_fingerprint")]
    [RegularExpression("^[a-f0-9]{64}$", ErrorMessage = "Browser fingerprint must be 64 lowercase hexadecimal characters.")]
    public string? BrowserFingerprint { get; set; }

    [JsonPropertyName("fingerprint_version")]
    [Range(1, 1, ErrorMessage = "Unsupported browser fingerprint version.")]
    public int? FingerprintVersion { get; set; }
}
