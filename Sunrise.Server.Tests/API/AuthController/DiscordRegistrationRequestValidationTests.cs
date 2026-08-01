using System.ComponentModel.DataAnnotations;
using Sunrise.API.Serializable.Request;

namespace Sunrise.Server.Tests.API.AuthController;

public sealed class DiscordRegistrationRequestValidationTests
{
    private const string LowercaseHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void StartRequestAcceptsTheSupportedFingerprintVersion()
    {
        var request = new DiscordRegistrationStartRequest
        {
            BrowserFingerprint = LowercaseHex,
            FingerprintVersion = 1
        };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void StartRequestRejectsFingerprintVersionChangesThatCouldEvadeHistory()
    {
        var request = new DiscordRegistrationStartRequest
        {
            BrowserFingerprint = LowercaseHex,
            FingerprintVersion = 2
        };

        Assert.Contains(Validate(request), result =>
            result.MemberNames.Contains(nameof(DiscordRegistrationStartRequest.FingerprintVersion)));
    }

    [Fact]
    public void StartRequestRejectsNonCanonicalBrowserIdentifiers()
    {
        var request = new DiscordRegistrationStartRequest
        {
            BrowserFingerprint = LowercaseHex.ToUpperInvariant(),
            FingerprintVersion = 1
        };

        Assert.Contains(Validate(request), result =>
            result.MemberNames.Contains(nameof(DiscordRegistrationStartRequest.BrowserFingerprint)));
    }

    private static List<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return results;
    }
}
