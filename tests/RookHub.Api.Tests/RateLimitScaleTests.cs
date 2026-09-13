using Microsoft.Extensions.Configuration;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RateLimitScaleTests
{
    private static IConfiguration Config(string? value) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [RateLimitScale.ConfigKey] = value })
        .Build();

    [Fact]
    public void FromConfig_Missing_IsOne()
    {
        Assert.Equal(1, RateLimitScale.FromConfig(new ConfigurationBuilder().Build()));
    }

    [Fact]
    public void FromConfig_ReadsTheConfiguredFactor()
    {
        Assert.Equal(20, RateLimitScale.FromConfig(Config("20")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("zwanzig")]
    [InlineData("")]
    public void FromConfig_CannotDisableOrTightenTheLimiter(string value)
    {
        Assert.Equal(1, RateLimitScale.FromConfig(Config(value)));
    }

    [Fact]
    public void FromConfig_IsCappedAtMax()
    {
        Assert.Equal(RateLimitScale.Max, RateLimitScale.FromConfig(Config("100000")));
    }
}
