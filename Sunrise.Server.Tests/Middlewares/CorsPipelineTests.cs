using System.Net;
using System.Threading.RateLimiting;
using DotNetEnv;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sunrise.API.Attributes;
using Sunrise.Server.Middlewares;
using Sunrise.Shared.Application;
using Sunrise.Shared.Database;
using Sunrise.Tests;

namespace Sunrise.Server.Tests.Middlewares;

[CollectionDefinition(CorsPipelineTestCollection.Name, DisableParallelization = true)]
public sealed class CorsPipelineTestCollection
{
    public const string Name = "CORS pipeline tests";
}

[Collection(CorsPipelineTestCollection.Name)]
public class CorsPipelineTests : EnvironmentFixture, IAsyncLifetime
{
    private const string PublicOrigin = "https://public-client.example";
    private const string TestClientIpHeader = "X-Test-Remote-IP";

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private static string RegistrationOrigin =>
        new Uri(Configuration.DiscordOAuthRegistrationUrl).GetLeftPart(UriPartial.Authority);

    public async Task InitializeAsync()
    {
        Env.TraversePath().Load(Bootstrap.GetEnvFilename());

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Bootstrap).Assembly.FullName,
            EnvironmentName = "Tests"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.AddMiddlewares();

        // These requests stop before user lookup, so the real middleware can be
        // exercised without bringing database and Redis infrastructure into CORS tests.
        builder.Services.AddTransient<Middleware>(services => new Middleware(
            services.GetRequiredService<IMemoryCache>(),
            null!));

        _app = builder.Build();

        _app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue(TestClientIpHeader, out var value) &&
                IPAddress.TryParse(value.ToString(), out var clientIp))
            {
                context.Connection.RemoteIpAddress = clientIp;
            }

            return next(context);
        });

        _app.UseRequestPipeline();

        _app.MapGet("/ping", () => Results.Ok("Sunrise API"))
            .WithMetadata(new IgnoreMaintenanceAttribute());
        _app.MapGet("/user/self", () => Results.Ok())
            .RequireAuthorization();
        _app.MapMethods("/auth/register", ["POST"], () => Results.Ok())
            .RequireCors("Registration");

        await _app.StartAsync();

        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("https://api.sunrise.test");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task AllowedRegistrationPreflightSucceeds()
    {
        using var request = CreatePreflightRequest(
            "/auth/register",
            RegistrationOrigin,
            "POST",
            IPAddress.Parse("203.0.113.10"));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertRegistrationCors(response);
        AssertHeaderContains(response, "Access-Control-Allow-Methods", "POST");
    }

    [Fact]
    public async Task DisallowedRegistrationOriginIsRejected()
    {
        using var request = CreatePreflightRequest(
            "/auth/register",
            "https://disallowed-origin.example",
            "POST",
            IPAddress.Parse("203.0.113.11"));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task RegistrationPreflightDoesNotConsumeGenericRateLimit()
    {
        var clientIp = IPAddress.Parse("203.0.113.12");
        using var limiter = CreateRateLimiter();
        var cache = _app.Services.GetRequiredService<IMemoryCache>();
        cache.Set(clientIp, limiter);

        try
        {
            using var preflight = CreatePreflightRequest(
                "/auth/register",
                RegistrationOrigin,
                "POST",
                clientIp);

            using var preflightResponse = await _client.SendAsync(preflight);

            Assert.Equal(HttpStatusCode.NoContent, preflightResponse.StatusCode);
            Assert.Equal(1, limiter.GetStatistics()?.CurrentAvailablePermits);

            using var publicRequest = CreateCorsRequest(HttpMethod.Get, "/ping", PublicOrigin, clientIp);
            using var publicResponse = await _client.SendAsync(publicRequest);

            Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
            Assert.Equal(0, limiter.GetStatistics()?.CurrentAvailablePermits);
        }
        finally
        {
            cache.Remove(clientIp);
        }
    }

    [Fact]
    public async Task EarlyUnauthorizedResponseHasPublicCorsHeaders()
    {
        using var request = CreateCorsRequest(
            HttpMethod.Get,
            "/user/self",
            PublicOrigin,
            IPAddress.Parse("203.0.113.13"));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertPublicCors(response);
    }

    [Fact]
    public async Task EarlyForbiddenRegistrationResponseHasCredentialedCorsHeaders()
    {
        var bannedIp = IPAddress.Parse(Configuration.BannedIps.First());
        using var request = CreateCorsRequest(
            HttpMethod.Post,
            "/auth/register",
            RegistrationOrigin,
            bannedIp);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertRegistrationCors(response);
    }

    [Fact]
    public async Task EarlyRateLimitedRegistrationResponseHasCredentialedCorsHeaders()
    {
        var clientIp = IPAddress.Parse("203.0.113.14");
        using var limiter = CreateRateLimiter();
        using var consumedPermit = limiter.AttemptAcquire(1);
        Assert.True(consumedPermit.IsAcquired);

        var cache = _app.Services.GetRequiredService<IMemoryCache>();
        cache.Set(clientIp, limiter);

        try
        {
            using var request = CreateCorsRequest(
                HttpMethod.Post,
                "/auth/register",
                RegistrationOrigin,
                clientIp);

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            AssertRegistrationCors(response);
        }
        finally
        {
            cache.Remove(clientIp);
        }
    }

    [Fact]
    public async Task EarlyMaintenanceRegistrationResponseHasCredentialedCorsHeaders()
    {
        Configuration.OnMaintenance = true;

        try
        {
            using var request = CreateCorsRequest(
                HttpMethod.Post,
                "/auth/register",
                RegistrationOrigin,
                IPAddress.Parse("203.0.113.15"));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            AssertRegistrationCors(response);
        }
        finally
        {
            Configuration.OnMaintenance = false;
        }
    }

    [Fact]
    public async Task PublicEndpointRetainsWildcardCorsWithoutCredentials()
    {
        using var request = CreateCorsRequest(
            HttpMethod.Get,
            "/ping",
            PublicOrigin,
            IPAddress.Parse("203.0.113.16"));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPublicCors(response);
    }

    private static HttpRequestMessage CreatePreflightRequest(
        string path,
        string origin,
        string requestedMethod,
        IPAddress clientIp)
    {
        var request = CreateCorsRequest(HttpMethod.Options, path, origin, clientIp);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", requestedMethod);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "content-type");
        return request;
    }

    private static HttpRequestMessage CreateCorsRequest(
        HttpMethod method,
        string path,
        string origin,
        IPAddress clientIp)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation(TestClientIpHeader, clientIp.ToString());
        return request;
    }

    private static TokenBucketRateLimiter CreateRateLimiter()
    {
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            AutoReplenishment = false,
            TokenLimit = 1,
            TokensPerPeriod = 1,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            ReplenishmentPeriod = TimeSpan.FromHours(1)
        });
    }

    private static void AssertRegistrationCors(HttpResponseMessage response)
    {
        AssertHeaderEquals(response, "Access-Control-Allow-Origin", RegistrationOrigin);
        AssertHeaderEquals(response, "Access-Control-Allow-Credentials", "true");
    }

    private static void AssertPublicCors(HttpResponseMessage response)
    {
        AssertHeaderEquals(response, "Access-Control-Allow-Origin", "*");
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    private static void AssertHeaderEquals(HttpResponseMessage response, string name, string expected)
    {
        Assert.True(response.Headers.TryGetValues(name, out var values));
        Assert.Equal(expected, Assert.Single(values));
    }

    private static void AssertHeaderContains(HttpResponseMessage response, string name, string expected)
    {
        Assert.True(response.Headers.TryGetValues(name, out var values));
        Assert.Contains(values, value => value.Split(',').Any(item =>
            string.Equals(item.Trim(), expected, StringComparison.OrdinalIgnoreCase)));
    }
}
