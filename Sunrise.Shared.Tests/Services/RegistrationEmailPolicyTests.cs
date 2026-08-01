using Sunrise.Shared.Services;

namespace Sunrise.Shared.Tests.Services;

public sealed class RegistrationEmailPolicyTests
{
    [Theory]
    [InlineData("user@gmail.com")]
    [InlineData("USER@PROTON.ME")]
    [InlineData("person@outlook.com")]
    public void AllowsEstablishedProviders(string email)
    {
        Assert.True(RegistrationEmailPolicy.IsAllowedProvider(email));
    }

    [Theory]
    [InlineData("user@disposable.example")]
    [InlineData("user@gmail.com.attacker.example")]
    [InlineData("missing-domain@")]
    public void RejectsUnapprovedOrDeceptiveDomains(string email)
    {
        Assert.False(RegistrationEmailPolicy.IsAllowedProvider(email));
    }

    [Theory]
    [InlineData("First.Last+registry@gmail.com", "firstlast@gmail.com")]
    [InlineData("first.last@googlemail.com", "firstlast@gmail.com")]
    [InlineData("person@outlook.com", "person@outlook.com")]
    public void CanonicalizesKnownMailboxAliases(string email, string expected)
    {
        Assert.Equal(expected, RegistrationEmailPolicy.NormalizeVerifiedEmail(email));
    }
}
