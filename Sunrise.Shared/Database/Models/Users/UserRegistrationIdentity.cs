using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Sunrise.Shared.Database.Models.Users;

[Table("user_registration_identity")]
[Index(nameof(UserId), IsUnique = true)]
[Index(nameof(DiscordSubjectHash), IsUnique = true)]
[Index(nameof(IpHash), IsUnique = true)]
[Index(nameof(InstallationIdHash), IsUnique = true)]
[Index(nameof(BrowserFingerprintHash))]
public sealed class UserRegistrationIdentity
{
    public int Id { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(64)]
    public required string DiscordSubjectHash { get; set; }

    [MaxLength(64)]
    public required string IpHash { get; set; }

    [MaxLength(64)]
    public required string InstallationIdHash { get; set; }

    [MaxLength(64)]
    public required string BrowserFingerprintHash { get; set; }

    public int FingerprintVersion { get; set; }
    public DateTime DiscordAccountCreatedAt { get; set; }
    public DateTime VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
