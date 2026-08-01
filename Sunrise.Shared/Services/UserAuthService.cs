using System.Net;
using Microsoft.EntityFrameworkCore;
using Sunrise.Shared.Application;
using Sunrise.Shared.Database;
using Sunrise.Shared.Database.Models.Users;
using Sunrise.Shared.Enums.Users;
using Sunrise.Shared.Extensions.Users;
using Sunrise.Shared.Objects;

namespace Sunrise.Shared.Services;

public class UserAuthService(
    RegionService regionService,
    DatabaseService database,
    RegistrationIdentityHasher identityHasher,
    TimeProvider timeProvider)
{
    public async Task<(User?, Dictionary<string, List<string>>?)> RegisterUser(
        string username,
        string password,
        string email,
        IPAddress ip,
        RegistrationIdentityData? registrationIdentity = null,
        CancellationToken ct = default)
    {
        var errors = new Dictionary<string, List<string>>
        {
            ["discord_verification"] = [],
            ["user_email"] = [],
            ["password"] = [],
            ["username"] = []
        };

        if (Configuration.DiscordOAuthEnabled && registrationIdentity == null)
            errors["discord_verification"].Add("Discord verification is required. Please register on the website.");

        if (registrationIdentity != null &&
            !RegistrationIdentityHasher.FixedTimeEquals(registrationIdentity.IpHash, identityHasher.HashIpAddress(ip)))
        {
            errors["discord_verification"].Add("Registration verification does not match this connection.");
        }

        if (Configuration.BannedIps.Contains(ip.ToString()))
            errors["username"].Add("Your IP address is banned. Please contact support.");

        var (isUsernameValid, usernameError) = username.IsValidUsername();
        if (!isUsernameValid)
            errors["username"].Add(usernameError ?? "Invalid username");

        if (string.IsNullOrWhiteSpace(email) || email.Length > 255 || !email.IsValidEmailCharacters())
            errors["user_email"].Add("Invalid email. It should be a valid email address.");
        else if (!Configuration.IsDevelopment && !Configuration.IsTestingEnv &&
                 !RegistrationEmailPolicy.IsAllowedProvider(email))
            errors["user_email"].Add("Please use an email address from an approved provider.");
        else
            email = RegistrationEmailPolicy.NormalizeVerifiedEmail(email);

        var (isPasswordValid, passwordError) = password.IsValidPassword();
        if (!isPasswordValid)
            errors["password"].Add(passwordError ?? "Invalid password");

        var foundUserByEmail = await database.Users.GetUser(email: email, ct: ct);
        if (foundUserByEmail != null)
            errors["user_email"].Add("User with this email already exists.");

        var foundUserByUsername = await database.Users.GetUser(username: username, ct: ct);
        if (foundUserByUsername != null && foundUserByUsername.IsActive())
            errors["username"].Add("User with this username already exists.");

        var legacyIp = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
        var accountCreatedFromSameIp = await database.Events.Users.IsIpHasAnyRegisteredAccounts(legacyIp, ct);
        if (accountCreatedFromSameIp != null && !Configuration.IsDevelopment)
            errors[registrationIdentity == null ? "username" : "discord_verification"]
                .Add("An account has already been registered from this connection. Contact support if you need help recovering it.");

        if (registrationIdentity != null)
        {
            var existingIdentity = await database.DbContext.UserRegistrationIdentities
                .AsNoTracking()
                .AnyAsync(identity =>
                        identity.DiscordSubjectHash == registrationIdentity.DiscordSubjectHash ||
                        identity.IpHash == registrationIdentity.IpHash ||
                        identity.InstallationIdHash == registrationIdentity.InstallationIdHash,
                    ct);

            if (existingIdentity)
                errors["discord_verification"].Add("This Discord account, connection, or browser installation has already been used to register.");

        }

        if (errors.Any(x => x.Value.Count > 0))
            return (null, errors);

        var passhash = password.GetPassHash();
        var location = await regionService.GetRegion(ip, ct);

        var newUser = new User
        {
            Username = username,
            Email = email,
            Passhash = passhash,
            Country = RegionService.GetCountryCode(location.Country),
            Privilege = UserPrivilege.User
        };

        var transactionResult = await database.CommitAsTransactionAsync(async () =>
        {
            if (foundUserByUsername != null && foundUserByUsername.IsActive() == false)
            {
                var updateUsernameResult = await database.Users.UpdateUserUsername(
                    new UserEventAction(foundUserByUsername, legacyIp, foundUserByUsername.Id),
                    foundUserByUsername.Username,
                    foundUserByUsername.Username.SetUsernameAsOld());

                if (updateUsernameResult.IsFailure)
                    throw new ApplicationException(updateUsernameResult.Error);
            }

            var addUserResult = await database.Users.AddUser(newUser);
            if (addUserResult.IsFailure)
                throw new ApplicationException(addUserResult.Error);

            if (registrationIdentity != null)
            {
                database.DbContext.UserRegistrationIdentities.Add(new UserRegistrationIdentity
                {
                    UserId = newUser.Id,
                    DiscordSubjectHash = registrationIdentity.DiscordSubjectHash,
                    IpHash = registrationIdentity.IpHash,
                    InstallationIdHash = registrationIdentity.InstallationIdHash,
                    BrowserFingerprintHash = registrationIdentity.BrowserFingerprintHash,
                    FingerprintVersion = registrationIdentity.FingerprintVersion,
                    DiscordAccountCreatedAt = registrationIdentity.DiscordAccountCreatedAt,
                    VerifiedAt = registrationIdentity.VerifiedAt,
                    CreatedAt = timeProvider.GetUtcNow().UtcDateTime
                });
            }

            var registerEventResult = await database.Events.Users.AddUserRegisterEvent(
                new UserEventAction(newUser, legacyIp, newUser.Id),
                newUser);

            if (registerEventResult.IsFailure)
                throw new ApplicationException(registerEventResult.Error);
        }, ct);

        if (transactionResult.IsFailure)
        {
            errors[registrationIdentity == null ? "username" : "discord_verification"]
                .Add(registrationIdentity == null
                    ? transactionResult.Error
                    : "Unable to register with these verification details. They may already have been used.");
            return (null, errors);
        }

        return (newUser, null);
    }
}
