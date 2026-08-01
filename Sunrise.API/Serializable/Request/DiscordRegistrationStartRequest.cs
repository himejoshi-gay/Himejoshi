using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sunrise.API.Serializable.Request;

public class DiscordRegistrationStartRequest
{
    [JsonPropertyName("browser_fingerprint")]
    [Required]
    [RegularExpression("^[a-f0-9]{64}$", ErrorMessage = "Browser fingerprint must be 64 lowercase hexadecimal characters.")]
    public required string BrowserFingerprint { get; set; }

    [JsonPropertyName("fingerprint_version")]
    [Range(1, 1, ErrorMessage = "Unsupported browser fingerprint version.")]
    public int FingerprintVersion { get; set; }
}
