using System.Net;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Grenzen „je IP" gelten je Anschluss: IPv4 die Adresse, IPv6 das /64 (Codereview 2026-09-29, A6-003/F7-005).</summary>
public class IpBucketTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    [InlineData("::1", "::/64")]
    public void Key_GroupsIpv6ByTheSlash64(string ip, string expected) => Assert.Equal(expected, IpBucket.Key(IPAddress.Parse(ip)));

    [Fact]
    public void Key_SameSlash64_SameKey_NeighbourNet_DifferentKey()
    {
        Assert.Equal(IpBucket.Key(IPAddress.Parse("2001:db8:1:2::1")), IpBucket.Key(IPAddress.Parse("2001:db8:1:2:ffff::9")));
        Assert.NotEqual(IpBucket.Key(IPAddress.Parse("2001:db8:1:2::1")), IpBucket.Key(IPAddress.Parse("2001:db8:1:3::1")));
        // Gleiches Ende, anderes Netz: früher (MapToIPv4) dieselbe „IP".
        Assert.NotEqual(IpBucket.Key(IPAddress.Parse("2001:db8:1:2::c000:207")), IpBucket.Key(IPAddress.Parse("2001:db8:9:9::c000:207")));
    }

    [Fact]
    public void Key_Null_IsQuestionMark() => Assert.Equal("?", IpBucket.Key(null));
}
