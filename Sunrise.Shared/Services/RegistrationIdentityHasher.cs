using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Sunrise.Shared.Application;

namespace Sunrise.Shared.Services;

public sealed class RegistrationIdentityHasher
{
    private readonly byte[] _secret;

    public RegistrationIdentityHasher()
    {
        var secret = Configuration.RegistrationIdentitySecret;
        if (Configuration.DiscordOAuthEnabled && Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("REGISTRATION_IDENTITY_SECRET must be at least 32 bytes.");

        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public string HashDiscordSubject(string subject) => Hash("discord", subject);

    public string HashIpAddress(IPAddress ipAddress) => Hash("ip", NormalizeIpAddress(ipAddress));

    public string HashInstallationId(string installationId) => Hash("installation", installationId.ToLowerInvariant());

    public string HashBrowserFingerprint(string fingerprint, int version) =>
        Hash("fingerprint", $"{version}:{fingerprint.ToLowerInvariant()}");

    public string CreateSignedInstallationCookie()
    {
        var installationId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return $"{installationId}.{Hash("installation-cookie", installationId)}";
    }

    public bool TryValidateInstallationCookie(string? value, out string installationId)
    {
        installationId = string.Empty;
        if (value is not { Length: 129 } || value[64] != '.')
            return false;

        var candidate = value[..64];
        if (candidate.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return false;

        var signature = value[65..];
        if (!FixedTimeEquals(signature, Hash("installation-cookie", candidate)))
            return false;

        installationId = candidate;
        return true;
    }

    public static string NormalizeIpAddress(IPAddress ipAddress)
    {
        if (ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            return ipAddress.ToString();

        if (ipAddress.AddressFamily != AddressFamily.InterNetworkV6)
            throw new ArgumentException("Unsupported IP address family.", nameof(ipAddress));

        // Treat an IPv6 /64 as one registration origin. Temporary IPv6 interface
        // addresses otherwise make a same-device registration limit ineffective.
        var bytes = ipAddress.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    public static bool FixedTimeEquals(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }

    private string Hash(string purpose, string value)
    {
        if (_secret.Length == 0)
            throw new InvalidOperationException("Registration identity hashing is not configured.");

        return Convert.ToHexString(HMACSHA256.HashData(
                _secret,
                Encoding.UTF8.GetBytes($"himejoshi-registration:{purpose}:{value}")))
            .ToLowerInvariant();
    }
}
