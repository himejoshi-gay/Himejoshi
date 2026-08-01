namespace Sunrise.Shared.Objects;

public sealed record RegistrationIdentityData(
    string DiscordSubjectHash,
    string IpHash,
    string InstallationIdHash,
    string BrowserFingerprintHash,
    int FingerprintVersion,
    DateTime DiscordAccountCreatedAt,
    DateTime VerifiedAt);
