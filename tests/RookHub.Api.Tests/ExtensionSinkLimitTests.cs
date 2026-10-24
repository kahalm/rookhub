using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Controllers;
using RookHub.Api.Controllers.Chessable;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Chessable-Roh-Senken der Extension (problem-moves, session-moves, review-lines): eine eigene Rate-Limit-Partition
/// je NUTZER und ein Rumpf-Deckel von 16 MB. Vorher galt nur der globale Deckel von 100 Anfragen je Minute und IP, dazu
/// Kestrels 30 MB bzw. 64 MB bei review-lines — ein Gratis-Konto schrieb damit ~1,3 GB je Minute in die Datenbank.
/// </summary>
public class ExtensionSinkLimitTests
{
    [Theory]
    [InlineData(nameof(ExtensionController.ChessableProblemMoves))]
    [InlineData(nameof(ExtensionController.ChessableSessionMoves))]
    [InlineData(nameof(ExtensionController.ChessableReviewLines))]
    public void SinkEndpoints_HavePerUserRateLimit_AndBodyLimit(string action)
    {
        var method = typeof(ExtensionController).GetMethod(action)!;

        Assert.Equal("extension-sink", method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        var limit = method.GetCustomAttributesData()
            .Single(a => a.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(16_000_000L, Convert.ToInt64(limit.ConstructorArguments[0].Value));
    }

    /// <summary>Je NUTZER, nicht je IP — sonst teilten sich alle hinter einem NAT das Fenster, und ein einzelnes Konto
    /// bekäme weiter das volle IP-Kontingent.</summary>
    [Fact]
    public void ExtensionSinkPolicy_IsPartitionedPerUser()
    {
        var src = StartupSource.All();
        var start = src.IndexOf(@"AddPolicy(""extension-sink""", StringComparison.Ordinal);
        Assert.True(start >= 0, "Policy extension-sink fehlt in Program.cs");
        var next = src.IndexOf("AddPolicy(", start + 1, StringComparison.Ordinal);
        var block = src[start..(next < 0 ? src.Length : next)];
        Assert.Contains("ClaimTypes.NameIdentifier", block);
        Assert.Contains("PermitLimit = 60 * permitScale", block);
    }
}
