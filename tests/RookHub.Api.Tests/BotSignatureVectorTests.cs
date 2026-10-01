using RookHub.Api.Authorization;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Codereview I2-008: sprachübergreifende Golden-Vektoren der Bot-Signaturen, wie beim Discord-Link
/// (<c>DiscordLinkServiceTests.Verify_AcceptsKnownPythonToken_RoundTrip</c>). Bis hierher prüften beide Seiten
/// nur gegen die EIGENE Implementierung: ändert eine Seite die signierte Nachricht (Trennzeichen, Form des
/// Zeitstempels), bleiben beide Testsuiten grün, und in Prod scheitert jeder Aufruf mit 401.
///
/// Die Hex-Werte sind LITERALE, erzeugt mit dem Python des schach-bots (2026-10-01): Webhook über
/// <c>core/webhook_server._verify_signature(secret, body, sig, ts, now=ts)</c> → True; Build-Info über
/// <c>_verify_build_info_auth(secret, ts, sig, now=ts)</c> → True; Stats und Ergebnis-GET wie
/// <c>puzzle/rookhub.py</c> (<c>get_player_progress</c>, <c>_bot_auth_headers</c>):
/// <c>hmac.new(secret, f'{ts}.{objekt}', sha256).hexdigest()</c>.
/// Dieselben Literale gehören in die Bot-Tests (offen im schach-bot-Repo).
/// </summary>
public class BotSignatureVectorTests
{
    private const string Secret = "shared-test-secret-1234567890";
    private const long Ts = 1700000000;

    /// <summary>Webhook rookhub → Bot: <c>X-Webhook-Signature: sha256=hex(HMAC(secret, "&lt;ts&gt;.&lt;body&gt;"))</c>.</summary>
    [Fact]
    public void Webhook_SignedRequest_MatchesThePythonVector()
    {
        const string body = "{\"date\":\"2026-10-01\",\"puzzleId\":5}";

        using var req = SchachBotWebhookService.BuildSignedRequest(
            "http://schach-bot:9000/webhook/daily-regenerate", Secret, body, "1700000000");

        Assert.Equal("1700000000", req.Headers.GetValues("X-Webhook-Timestamp").Single());
        Assert.Equal("sha256=8fda1beb1084c6ee1f22b5a907f343eaa9ef463abf2bb7bd49cd53a867e0c283",
            req.Headers.GetValues("X-Webhook-Signature").Single());
    }

    /// <summary>Stats-Pull Bot → rookhub: <c>X-Bot-Signature</c> über <c>"&lt;ts&gt;.&lt;discordId&gt;"</c>.</summary>
    [Fact]
    public void Stats_Verify_AcceptsThePythonVector()
    {
        const string sig = "sha256=45393ba2f71a4fe95bc7ca0c2891aa7828030ebe18bea1fcb5b19de73c0f2822";

        Assert.True(BotRequestSignature.Verify(Secret, "123456789012345678", sig, "1700000000", Ts));
        // Gegenproben: anderes Objekt, anderer Zeitstempel im Header, außerhalb des Fensters.
        Assert.False(BotRequestSignature.Verify(Secret, "123456789012345679", sig, "1700000000", Ts));
        Assert.False(BotRequestSignature.Verify(Secret, "123456789012345678", sig, "1700000001", Ts));
        Assert.False(BotRequestSignature.Verify(Secret, "123456789012345678", sig, "1700000000",
            Ts + BotRequestSignature.TimestampToleranceSeconds + 1));
    }

    /// <summary>Ergebnis-GETs Bot → rookhub: dieselbe Prüfung, signiertes Objekt = Pfad ohne Query.</summary>
    [Fact]
    public void ResultsPath_Verify_AcceptsThePythonVector()
    {
        Assert.True(BotRequestSignature.Verify(Secret, "/api/book-puzzles/123/results",
            "sha256=8a5d0ff5d7761dead43a0e0da49dc180070cc61bd156df469d700f9e82ea6bc2", "1700000000", Ts));
    }

    /// <summary>Build-Info rookhub → Bot: <c>X-Bot-Signature = "sha256=" + hex(HMAC(secret, "&lt;ts&gt;"))</c>.</summary>
    [Fact]
    public void BuildInfo_Headers_MatchThePythonVector()
    {
        var headers = GithubActionsService.BotBuildInfoHeaders(Secret, Ts);

        Assert.Equal(new[]
        {
            ("X-Bot-Timestamp", "1700000000"),
            ("X-Bot-Signature", "sha256=51f02a0648baeb93b20d938dd2107383ab6ab74f6fceb11302ea09d682d3417c"),
        }, headers);
        Assert.Empty(GithubActionsService.BotBuildInfoHeaders("  ", Ts));
    }
}
