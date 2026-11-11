using ApexPost.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ApexPost.UnitTests;

/// <summary>Fast unit tests for partner API key validation. No SQL Server is required.</summary>
public sealed class PartnerApiKeyValidatorTests
{
    [Theory]
    [InlineData("partner-secret", true)]
    [InlineData("PARTNER-SECRET", false)]
    [InlineData("partner-secret ", false)]
    [InlineData("wrong", false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(null, false)]
    public void Key_validation_is_exact_and_rejects_missing_values(string? supplied, bool expected)
    {
        var configuration = new ConfigurationManager { ["Partner:ApiKey"] = "partner-secret" };
        var validator = new PartnerApiKeyValidator(configuration);
        Assert.Equal(expected, validator.IsValid(supplied));
    }

    [Fact]
    public void Missing_configured_key_fails_closed()
    {
        var configuration = new ConfigurationManager();
        var validator = new PartnerApiKeyValidator(configuration);
        Assert.False(validator.IsValid("any-supplied-key"));
    }
}
