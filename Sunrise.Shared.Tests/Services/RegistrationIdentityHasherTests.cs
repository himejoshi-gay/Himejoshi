using System.Net;
using Sunrise.Shared.Services;

namespace Sunrise.Shared.Tests.Services;

public sealed class RegistrationIdentityHasherTests
{
    [Fact]
    public void NormalizeIpAddressKeepsIpv4Exact()
    {
        var normalized = RegistrationIdentityHasher.NormalizeIpAddress(IPAddress.Parse("203.0.113.10"));

        Assert.Equal("203.0.113.10", normalized);
    }

    [Fact]
    public void NormalizeIpAddressGroupsTemporaryIpv6AddressesByPrefix64()
    {
        var first = RegistrationIdentityHasher.NormalizeIpAddress(IPAddress.Parse("2001:db8:1234:5678::1"));
        var second = RegistrationIdentityHasher.NormalizeIpAddress(IPAddress.Parse("2001:db8:1234:5678:ffff::abcd"));

        Assert.Equal("2001:db8:1234:5678::/64", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void NormalizeIpAddressKeepsDifferentIpv6PrefixesSeparate()
    {
        var first = RegistrationIdentityHasher.NormalizeIpAddress(IPAddress.Parse("2001:db8:1234:5678::1"));
        var second = RegistrationIdentityHasher.NormalizeIpAddress(IPAddress.Parse("2001:db8:1234:5679::1"));

        Assert.NotEqual(first, second);
    }
}
