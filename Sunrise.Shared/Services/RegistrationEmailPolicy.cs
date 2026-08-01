using Sunrise.Shared.Application;

namespace Sunrise.Shared.Services;

public static class RegistrationEmailPolicy
{
    public static bool IsAllowedProvider(string email)
    {
        var separator = email.IndexOf('@');
        if (separator <= 0 || separator != email.LastIndexOf('@') || separator == email.Length - 1 ||
            email.Any(char.IsWhiteSpace))
            return false;

        var domain = email[(separator + 1)..].ToLowerInvariant();
        if (domain.Any(character =>
                character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') && character is not '.' and not '-'))
            return false;

        return Configuration.RegistrationAllowedEmailDomains.Contains(domain, StringComparer.Ordinal);
    }

    public static string NormalizeVerifiedEmail(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var separator = normalized.LastIndexOf('@');
        if (separator <= 0 || separator == normalized.Length - 1)
            return normalized;

        var localPart = normalized[..separator];
        var domain = normalized[(separator + 1)..];

        if (domain is "gmail.com" or "googlemail.com")
        {
            localPart = localPart.Split('+', 2)[0].Replace(".", string.Empty, StringComparison.Ordinal);
            domain = "gmail.com";
        }

        return $"{localPart}@{domain}";
    }
}
