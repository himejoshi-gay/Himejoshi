using System.Data.Common;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Sunrise.API.Attributes;
using Sunrise.API.Objects.Keys;
using Sunrise.API.Serializable.Request;
using Sunrise.API.Serializable.Response;
using Sunrise.API.Services;
using Sunrise.Shared.Application;
using Sunrise.Shared.Attributes;
using Sunrise.Shared.Database;
using Sunrise.Shared.Database.Models.Users;
using Sunrise.Shared.Extensions.Users;
using Sunrise.Shared.Objects;
using Sunrise.Shared.Services;
using AuthService = Sunrise.API.Services.AuthService;

namespace Sunrise.API.Controllers;

[ApiController]
[ApiHttpTrace]
[Route("/auth")]
[Subdomain("api")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status400BadRequest)]
public class AuthController(
    UserAuthService userAuthService,
    RegionService regionService,
    AuthService authService,
    DatabaseService database,
    IDiscordOAuthClient discordOAuthClient,
    DiscordRegistrationStore discordRegistrationStore,
    RegistrationIdentityHasher registrationIdentityHasher,
    RegistrationAbuseService registrationAbuseService,
    TimeProvider timeProvider,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string RegistrationCookieName = "__Host-hime-registration-device";

    [HttpPost("token")]
    [EndpointDescription("Generate user auth tokens")]
    [IgnoreMaintenance]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserToken([FromBody] TokenRequest request, CancellationToken ct = default)
    {
        var user = await database.Users.GetUser(username: request.Username, passhash: request.Password.GetPassHash(), ct: ct);

        if (user == null || user.IsUserSunriseBot())
            return Problem(title: ApiErrorResponse.Title.UnableToAuthenticate, detail: ApiErrorResponse.Detail.InvalidCredentialsProvided, statusCode: StatusCodes.Status401Unauthorized);

        var ip = RegionService.GetUserIpAddress(Request);

        if (user.IsRestricted())
        {
            var restriction = await database.Users.Moderation.GetActiveRestrictionReason(user.Id, ct);
            return Problem(title: ApiErrorResponse.Title.UnableToAuthenticate, detail: ApiErrorResponse.Detail.YourAccountIsRestricted(restriction), statusCode: StatusCodes.Status403Forbidden);
        }

        var location = await regionService.GetRegion(RegionService.GetUserIpAddress(Request), ct);

        var tokenResult = await authService.GenerateTokens(user.Id);
        if (tokenResult.IsFailure)
            return Problem(title: ApiErrorResponse.Title.UnableToAuthenticate, detail: tokenResult.Error, statusCode: StatusCodes.Status400BadRequest);

        var token = tokenResult.Value;

        var loginData = new
        {
            RequestHeader = Request.Headers.UserAgent,
            RequestIp = location.Ip,
            RequestCountry = location.Country,
            RequestTime = DateTime.UtcNow
        };

        await database.Events.Users.AddUserLoginEvent(new UserEventAction(user, ip.ToString(), user.Id), false, loginData);

        return Ok(new TokenResponse(token.Item1, token.Item2, token.Item3));
    }

    [HttpPost("refresh")]
    [IgnoreMaintenance]
    [EndpointDescription("Refresh user auth token")]
    [ProducesResponseType(typeof(RefreshTokenResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var newTokenResult = await authService.RefreshToken(request.RefreshToken);
        if (newTokenResult.IsFailure)
            return Problem(title: ApiErrorResponse.Title.UnableToRefreshAuthToken, detail: newTokenResult.Error, statusCode: StatusCodes.Status400BadRequest);

        var newToken = newTokenResult.Value;

        return Ok(new RefreshTokenResponse(newToken.Item1, newToken.Item2));
    }

    [HttpPost("register")]
    [EnableCors("Registration")]
    [EndpointDescription("Register new user")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RegisterUser([FromBody] RegisterRequest request, CancellationToken ct = default)
    {
        var ip = RegionService.GetUserIpAddress(Request);
        ReservedDiscordRegistrationGrant? reservedGrant = null;
        RegistrationIdentityData? identity = null;
        var email = request.Email;

        if (Configuration.DiscordOAuthEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.DiscordVerificationToken) ||
                string.IsNullOrWhiteSpace(request.BrowserFingerprint) ||
                request.FingerprintVersion is null or < 1)
            {
                return Problem(
                    title: ApiErrorResponse.Title.UnableToRegisterUser,
                    detail: "Discord verification and browser identity are required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var ipHash = registrationIdentityHasher.HashIpAddress(ip);
            if (!TryGetRegistrationInstallationId(out var installationId))
                return Problem(
                    title: ApiErrorResponse.Title.UnableToRegisterUser,
                    detail: "Registration browser verification is missing or invalid.",
                    statusCode: StatusCodes.Status401Unauthorized);

            var installationHash = registrationIdentityHasher.HashInstallationId(installationId);
            var fingerprintHash = registrationIdentityHasher.HashBrowserFingerprint(
                request.BrowserFingerprint,
                request.FingerprintVersion.Value);

            try
            {
                reservedGrant = await discordRegistrationStore.ReserveGrantAsync(request.DiscordVerificationToken, ct);
            }
            catch (RedisException ex)
            {
                logger.LogError(ex, "Registration verification storage is unavailable.");
                return Problem(
                    title: ApiErrorResponse.Title.UnableToRegisterUser,
                    detail: "Registration verification is temporarily unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (reservedGrant == null)
            {
                return Problem(
                    title: ApiErrorResponse.Title.UnableToRegisterUser,
                    detail: "Discord verification is invalid or expired.",
                    statusCode: StatusCodes.Status410Gone);
            }

            if (reservedGrant.Data.ExpiresAt <= timeProvider.GetUtcNow().UtcDateTime)
            {
                await TryReleaseGrantReservationAsync(
                    request.DiscordVerificationToken,
                    reservedGrant.ReservationId);

                return Problem(
                    title: ApiErrorResponse.Title.UnableToRegisterUser,
                    detail: "Discord verification is invalid or expired.",
                    statusCode: StatusCodes.Status410Gone);
            }

            if (!IsGrantBoundToRequest(reservedGrant.Data, ipHash, installationHash, fingerprintHash,
                    request.FingerprintVersion.Value))
            {
                await TryReleaseGrantReservationAsync(
                    request.DiscordVerificationToken,
                    reservedGrant.ReservationId);

                return Problem(
                    title: ApiErrorResponse.Title.UnableToRegisterUser,
                    detail: "Discord verification belongs to another connection or browser.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            email = reservedGrant.Data.Email;
            if (!string.IsNullOrWhiteSpace(request.Email) &&
                !string.Equals(request.Email.Trim(), email, StringComparison.OrdinalIgnoreCase))
            {
                await TryReleaseGrantReservationAsync(
                    request.DiscordVerificationToken,
                    reservedGrant.ReservationId);

                return Problem(
                    title: ApiErrorResponse.Title.UnableToRegisterUser,
                    detail: "The supplied email does not match the verified Discord email.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var rateLimit = await registrationAbuseService.CheckRegistrationAsync(
                    ipHash,
                    installationHash,
                    fingerprintHash,
                    ct);

                if (rateLimit.IsLimited)
                {
                    await TryReleaseGrantReservationAsync(
                        request.DiscordVerificationToken,
                        reservedGrant.ReservationId);
                    Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(rateLimit.RetryAfter.TotalSeconds)).ToString();
                    return Problem(
                        title: ApiErrorResponse.Title.UnableToRegisterUser,
                        detail: "Too many registration attempts. Please try again later.",
                        statusCode: StatusCodes.Status429TooManyRequests);
                }
            }
            catch (RedisException ex)
            {
                await TryReleaseGrantReservationAsync(
                    request.DiscordVerificationToken,
                    reservedGrant.ReservationId);
                logger.LogError(ex, "Registration rate limiting is unavailable.");
                return Problem(
                    title: ApiErrorResponse.Title.UnableToRegisterUser,
                    detail: "Registration verification is temporarily unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            identity = new RegistrationIdentityData(
                reservedGrant.Data.DiscordSubjectHash,
                reservedGrant.Data.IpHash,
                reservedGrant.Data.InstallationIdHash,
                reservedGrant.Data.BrowserFingerprintHash,
                reservedGrant.Data.FingerprintVersion,
                reservedGrant.Data.DiscordAccountCreatedAt,
                reservedGrant.Data.VerifiedAt);
        }

        if (string.IsNullOrWhiteSpace(email))
            return Problem(
                title: ApiErrorResponse.Title.UnableToRegisterUser,
                detail: "Email is required.",
                statusCode: StatusCodes.Status400BadRequest);

        User? newUser;
        Dictionary<string, List<string>>? errors;
        try
        {
            (newUser, errors) = await userAuthService.RegisterUser(
                request.Username,
                request.Password,
                email.Trim().ToLowerInvariant(),
                ip,
                identity,
                ct);
        }
        catch
        {
            if (reservedGrant != null && request.DiscordVerificationToken != null)
                await TryReleaseGrantReservationAsync(
                    request.DiscordVerificationToken,
                    reservedGrant.ReservationId);
            throw;
        }

        if (newUser == null)
        {
            if (reservedGrant != null && request.DiscordVerificationToken != null)
                await TryReleaseGrantReservationAsync(
                    request.DiscordVerificationToken,
                    reservedGrant.ReservationId);

            var errorString = errors?.SelectMany(error => error.Value).FirstOrDefault();
            var isIdentityConflict = errors?.GetValueOrDefault("discord_verification")
                .Any(error => error.Contains("already", StringComparison.OrdinalIgnoreCase) ||
                              error.Contains("too many accounts", StringComparison.OrdinalIgnoreCase)) == true;
            return Problem(
                title: ApiErrorResponse.Title.UnableToRegisterUser,
                detail: errorString ?? ApiErrorResponse.Detail.UnknownErrorOccurred,
                statusCode: isIdentityConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        }

        if (reservedGrant != null && request.DiscordVerificationToken != null)
            await TryCompleteGrantAsync(request.DiscordVerificationToken);

        var tokenResult = await authService.GenerateTokens(newUser.Id);
        if (tokenResult.IsFailure)
            return Problem(title: ApiErrorResponse.Title.UnableToRegisterUser, detail: tokenResult.Error, statusCode: StatusCodes.Status400BadRequest);

        var token = tokenResult.Value;

        return Ok(new TokenResponse(token.Item1, token.Item2, token.Item3));
    }

    [HttpPost("discord/start")]
    [EnableCors("Registration")]
    [IgnoreMaintenance]
    [EndpointDescription("Start Discord verification for registration")]
    [ProducesResponseType(typeof(DiscordRegistrationStartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StartDiscordRegistration(
        [FromBody] DiscordRegistrationStartRequest request,
        CancellationToken ct = default)
    {
        if (!Configuration.DiscordOAuthEnabled)
            return Problem(
                title: ApiErrorResponse.Title.UnableToRegisterUser,
                detail: "Discord registration verification is disabled.",
                statusCode: StatusCodes.Status404NotFound);

        var ip = RegionService.GetUserIpAddress(Request);
        var ipHash = registrationIdentityHasher.HashIpAddress(ip);
        var installationHash = registrationIdentityHasher.HashInstallationId(GetOrCreateRegistrationInstallationId());
        var fingerprintHash = registrationIdentityHasher.HashBrowserFingerprint(
            request.BrowserFingerprint,
            request.FingerprintVersion);

        try
        {
            var rateLimit = await registrationAbuseService.CheckOAuthStartAsync(
                ipHash,
                installationHash,
                fingerprintHash,
                ct);

            if (rateLimit.IsLimited)
            {
                Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(rateLimit.RetryAfter.TotalSeconds)).ToString();
                return Problem(
                    title: ApiErrorResponse.Title.UnableToRegisterUser,
                    detail: "Too many Discord verification attempts. Please try again later.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var challenge = await discordRegistrationStore.CreateChallengeAsync(
                ipHash,
                installationHash,
                fingerprintHash,
                request.FingerprintVersion,
                ct);

            SetNoStoreHeaders();
            return Ok(new DiscordRegistrationStartResponse(
                discordOAuthClient.CreateAuthorizationUrl(challenge.State, challenge.CodeChallenge)));
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Discord verification storage is unavailable.");
            return Problem(
                title: ApiErrorResponse.Title.UnableToRegisterUser,
                detail: "Discord verification is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("discord/verification")]
    [EnableCors("Registration")]
    [IgnoreMaintenance]
    [EndpointDescription("Inspect a Discord registration verification")]
    [ProducesResponseType(typeof(DiscordRegistrationVerificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetailsResponseType), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetDiscordRegistrationVerification(
        [FromBody] DiscordRegistrationVerificationRequest request,
        CancellationToken ct = default)
    {
        if (!Configuration.DiscordOAuthEnabled)
            return Problem(
                title: ApiErrorResponse.Title.UnableToRegisterUser,
                detail: "Discord registration verification is disabled.",
                statusCode: StatusCodes.Status404NotFound);

        if (!TryGetRegistrationInstallationId(out var installationId))
            return Problem(
                title: ApiErrorResponse.Title.UnableToRegisterUser,
                detail: "Registration browser verification is missing or invalid.",
                statusCode: StatusCodes.Status401Unauthorized);

        var ipHash = registrationIdentityHasher.HashIpAddress(RegionService.GetUserIpAddress(Request));
        var installationHash = registrationIdentityHasher.HashInstallationId(installationId);
        var fingerprintHash = registrationIdentityHasher.HashBrowserFingerprint(
            request.BrowserFingerprint,
            request.FingerprintVersion);

        DiscordRegistrationGrantData? grant;
        try
        {
            grant = await discordRegistrationStore.GetGrantAsync(request.VerificationToken, ct);
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Discord verification storage is unavailable.");
            return Problem(
                title: ApiErrorResponse.Title.UnableToRegisterUser,
                detail: "Discord verification is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (grant == null || grant.ExpiresAt <= timeProvider.GetUtcNow().UtcDateTime)
        {
            return Problem(
                title: ApiErrorResponse.Title.UnableToRegisterUser,
                detail: "Discord verification is invalid or expired.",
                statusCode: StatusCodes.Status410Gone);
        }

        if (!IsGrantBoundToRequest(grant, ipHash, installationHash, fingerprintHash, request.FingerprintVersion))
        {
            return Problem(
                title: ApiErrorResponse.Title.UnableToRegisterUser,
                detail: "Discord verification belongs to another connection or browser.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        SetNoStoreHeaders();
        return Ok(new DiscordRegistrationVerificationResponse(grant.DiscordUsername, grant.Email, grant.ExpiresAt));
    }

    [HttpGet("discord/callback")]
    [IgnoreMaintenance]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> DiscordRegistrationCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct = default)
    {
        SetNoStoreHeaders();

        if (!Configuration.DiscordOAuthEnabled || string.IsNullOrWhiteSpace(state) || state.Length > 128)
            return DiscordRegistrationRedirect("discord_error", "invalid_state");

        DiscordOAuthChallengeData? challenge;
        try
        {
            challenge = await discordRegistrationStore.ConsumeChallengeAsync(state, ct);
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Discord OAuth state storage is unavailable.");
            return DiscordRegistrationRedirect("discord_error", "temporarily_unavailable");
        }

        if (!TryGetRegistrationInstallationId(out var callbackInstallationId))
            return DiscordRegistrationRedirect("discord_error", "invalid_state");

        var callbackIpHash = registrationIdentityHasher.HashIpAddress(RegionService.GetUserIpAddress(Request));
        var callbackInstallationHash = registrationIdentityHasher.HashInstallationId(callbackInstallationId);
        if (challenge == null ||
            challenge.ExpiresAt <= timeProvider.GetUtcNow().UtcDateTime ||
            !RegistrationIdentityHasher.FixedTimeEquals(challenge.IpHash, callbackIpHash) ||
            !RegistrationIdentityHasher.FixedTimeEquals(challenge.InstallationIdHash, callbackInstallationHash))
        {
            return DiscordRegistrationRedirect("discord_error", "invalid_state");
        }

        if (!string.IsNullOrWhiteSpace(error))
            return DiscordRegistrationRedirect("discord_error", "authorization_denied");

        if (string.IsNullOrWhiteSpace(code) || code.Length > 2048)
            return DiscordRegistrationRedirect("discord_error", "invalid_response");

        DiscordOAuthToken? token = null;
        try
        {
            token = await discordOAuthClient.ExchangeCodeAsync(code, challenge.CodeVerifier, ct);
            if (!string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                return DiscordRegistrationRedirect("discord_error", "invalid_token_type");

            var scopes = token.Scope.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!scopes.Contains("identify", StringComparer.Ordinal) ||
                !scopes.Contains("email", StringComparer.Ordinal))
            {
                return DiscordRegistrationRedirect("discord_error", "missing_scope");
            }

            var discordUser = await discordOAuthClient.GetCurrentUserAsync(token.AccessToken, ct);
            if (discordUser.Bot || discordUser.System || !discordUser.Verified || string.IsNullOrWhiteSpace(discordUser.Email))
                return DiscordRegistrationRedirect("discord_error", "account_not_verified");

            if (!ulong.TryParse(discordUser.Id, out var discordId))
                return DiscordRegistrationRedirect("discord_error", "invalid_discord_account");

            var discordCreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                checked((long)((discordId >> 22) + 1_420_070_400_000UL)));
            if (discordCreatedAt > timeProvider.GetUtcNow().AddDays(-Configuration.DiscordOAuthMinimumAccountAgeDays))
                return DiscordRegistrationRedirect("discord_error", "discord_account_too_new");

            var email = discordUser.Email.Trim().ToLowerInvariant();
            if (email.Length > 255 || !email.IsValidEmailCharacters())
                return DiscordRegistrationRedirect("discord_error", "invalid_discord_email");

            if (!RegistrationEmailPolicy.IsAllowedProvider(email))
                return DiscordRegistrationRedirect("discord_error", "unsupported_email_provider");

            email = RegistrationEmailPolicy.NormalizeVerifiedEmail(email);

            var discordSubjectHash = registrationIdentityHasher.HashDiscordSubject(discordUser.Id);
            var identityAlreadyUsed = await database.DbContext.UserRegistrationIdentities
                .AsNoTracking()
                .AnyAsync(identity => identity.DiscordSubjectHash == discordSubjectHash, ct);
            if (identityAlreadyUsed)
                return DiscordRegistrationRedirect("discord_error", "discord_account_already_used");

            var now = timeProvider.GetUtcNow();
            var grant = new DiscordRegistrationGrantData(
                discordSubjectHash,
                discordUser.GlobalName ?? discordUser.Username,
                email,
                discordCreatedAt.UtcDateTime,
                challenge.IpHash,
                challenge.InstallationIdHash,
                challenge.BrowserFingerprintHash,
                challenge.FingerprintVersion,
                now.UtcDateTime,
                now.Add(Configuration.DiscordOAuthGrantLifetime).UtcDateTime);

            var verificationToken = await discordRegistrationStore.IssueGrantAsync(grant, ct);
            return DiscordRegistrationRedirect("discord_verification", verificationToken);
        }
        catch (DiscordOAuthException ex)
        {
            logger.LogWarning(ex, "Discord OAuth registration failed.");
            return DiscordRegistrationRedirect("discord_error", "discord_unavailable");
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Discord verification storage is unavailable.");
            return DiscordRegistrationRedirect("discord_error", "temporarily_unavailable");
        }
        catch (DbException ex)
        {
            logger.LogError(ex, "Registration identity storage is unavailable during Discord OAuth.");
            return DiscordRegistrationRedirect("discord_error", "temporarily_unavailable");
        }
        catch (OverflowException ex)
        {
            logger.LogWarning(ex, "Discord returned an invalid user ID.");
            return DiscordRegistrationRedirect("discord_error", "invalid_discord_account");
        }
        finally
        {
            if (token != null)
            {
                try
                {
                    using var revocationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await discordOAuthClient.RevokeTokenAsync(token.AccessToken, revocationTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    logger.LogDebug("Timed out revoking a transient Discord OAuth token.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Unable to revoke a transient Discord OAuth token.");
                }
            }
        }
    }

    private static bool IsGrantBoundToRequest(
        DiscordRegistrationGrantData grant,
        string ipHash,
        string installationHash,
        string fingerprintHash,
        int fingerprintVersion)
    {
        return grant.FingerprintVersion == fingerprintVersion &&
               RegistrationIdentityHasher.FixedTimeEquals(grant.IpHash, ipHash) &&
               RegistrationIdentityHasher.FixedTimeEquals(grant.InstallationIdHash, installationHash) &&
               RegistrationIdentityHasher.FixedTimeEquals(grant.BrowserFingerprintHash, fingerprintHash);
    }

    private IActionResult DiscordRegistrationRedirect(string fragmentName, string fragmentValue)
    {
        var redirect = new UriBuilder(Configuration.DiscordOAuthRegistrationUrl)
        {
            Fragment = $"{fragmentName}={Uri.EscapeDataString(fragmentValue)}"
        };

        return Redirect(redirect.Uri.AbsoluteUri);
    }

    private void SetNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private string GetOrCreateRegistrationInstallationId()
    {
        if (TryGetRegistrationInstallationId(out var installationId))
            return installationId;

        var cookieValue = registrationIdentityHasher.CreateSignedInstallationCookie();
        installationId = cookieValue[..64];
        Response.Cookies.Append(RegistrationCookieName, cookieValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/",
            MaxAge = TimeSpan.FromDays(365)
        });
        return installationId;
    }

    private bool TryGetRegistrationInstallationId(out string installationId)
    {
        Request.Cookies.TryGetValue(RegistrationCookieName, out var cookieValue);
        return registrationIdentityHasher.TryValidateInstallationCookie(cookieValue, out installationId);
    }

    private async Task TryReleaseGrantReservationAsync(string token, string reservationId)
    {
        try
        {
            await discordRegistrationStore.ReleaseReservationAsync(token, reservationId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to release a Discord registration grant reservation.");
        }
    }

    private async Task TryCompleteGrantAsync(string token)
    {
        try
        {
            await discordRegistrationStore.CompleteGrantAsync(token, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The MySQL uniqueness constraints are authoritative after commit. A stale
            // short-lived Redis grant cannot create another account.
            logger.LogWarning(ex, "Unable to remove a completed Discord registration grant.");
        }
    }
}
