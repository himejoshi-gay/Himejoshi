using System.Net;
using Microsoft.AspNetCore.Http;
using Sunrise.Shared.Services;

namespace Sunrise.Shared.Tests.Services;

public sealed class RegionServiceIpAddressTests
{
    [Fact]
    public void GetUserIpAddressUsesTheForwardedHeadersMiddlewareResultOnly()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.42";

        var result = RegionService.GetUserIpAddress(context.Request);

        Assert.Equal(IPAddress.Parse("203.0.113.10"), result);
    }

    [Fact]
    public void GetUserIpAddressRejectsRequestsWithoutASourceAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = null;

        Assert.Throws<BadHttpRequestException>(() => RegionService.GetUserIpAddress(context.Request));
    }

    [Fact]
    public void GetUserIpAddressNormalizesIpv4MappedAddresses()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:203.0.113.10");

        var result = RegionService.GetUserIpAddress(context.Request);

        Assert.Equal(IPAddress.Parse("203.0.113.10"), result);
    }
}
