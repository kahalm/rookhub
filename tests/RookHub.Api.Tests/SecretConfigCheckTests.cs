using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RookHub.Api.Authorization;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// I2-001: Platzhalter-Geheimnisse aus den öffentlichen Beispiel-Dateien gelten nirgends mehr als echter
/// Schlüssel. Vorher bestand „change_me_to_a_secure_key_at_least_32_chars" (43 Byte) die einzige
/// JWT-Startprüfung, und Discord-Link- wie Bot-Stats-Geheimnis teilten sich einen Platzhalter.
/// </summary>
public class SecretConfigCheckTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "RookHub.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // --- Erkennung ------------------------------------------------------------------------

    [Theory]
    [InlineData("change_me")]
    [InlineData("change_me_to_a_secure_key_at_least_32_chars")]   // JWT_KEY der alten Beispiele (43 Byte)
    [InlineData("change_me_exactly_32_chars_long_!")]             // ENCRYPTION_KEY
    [InlineData("change_me_shared_with_schach_bot")]              // Discord-Link + Bot-Stats
    [InlineData("change_me_must_match_piratechess")]
    [InlineData("change_me_to_a_secure_key")]                     // CRAWLER_API_KEY
    [InlineData("CHANGE_ME_strong_root_password")]                // chessresults_crawler/.env.example
    [InlineData("change_me_min_32_characters_long_secret_key")]   // piratechess_docker/.env.example
    [InlineData("your_private_key_here")]
    [InlineData("<min-32-char-secret-for-jwt-signing>")]          // Kommentar der Beispiel-Composes
    [InlineData("  change_me_shared_with_rookhub ")]
    [InlineData("changeme")]
    public void IsPlaceholder_ErkenntDieBeispielwerte(string value)
    {
        Assert.True(SecretConfigCheck.IsPlaceholder(value));
        Assert.Null(SecretConfigCheck.Usable(value));
    }

    [Theory]
    [InlineData("TestSecretKeyThatIsAtLeast32Characters!")]
    [InlineData("E2eTestKeyThatIsAtLeast32CharsLongForHMACSHA256!")]
    [InlineData("shared_bot_stats_secret_value")]
    [InlineData("q2Vb9xkLr0P+7s/0mZ1cT4uYw3eN8aHjKd5fGiBoQvE=")]
    [InlineData("my_change_me_key")]          // nur der ANFANG zählt
    [InlineData("<unbalanced")]
    public void IsPlaceholder_LaesstEchteWerteDurch(string value)
    {
        Assert.False(SecretConfigCheck.IsPlaceholder(value));
        Assert.Equal(value, SecretConfigCheck.Usable(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Leer_istKeinPlatzhalter_aberAuchNichtBrauchbar(string? value)
    {
        Assert.False(SecretConfigCheck.IsPlaceholder(value));
        Assert.Null(SecretConfigCheck.Usable(value));
    }

    // --- Startprüfung JWT/Encryption ------------------------------------------------------

    [Theory]
    [InlineData("Jwt:Key", "change_me_to_a_secure_key_at_least_32_chars")]
    [InlineData("Encryption:Key", "change_me_exactly_32_chars_long_!")]
    public void Production_mitPlatzhalterSchluessel_brichtDenStartAb(string key, string placeholder)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "q2Vb9xkLr0P+7s/0mZ1cT4uYw3eN8aHjKd5fGiBoQvE=",
            ["Encryption:Key"] = "e2e-test-encryption-key-not-a-real-secret",
        };
        values[key] = placeholder;

        var ex = Assert.Throws<InvalidOperationException>(
            () => SecretConfigCheck.ThrowIfCryptoKeyIsPlaceholder(Config(values), new Env(Environments.Production)));
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void Development_mitPlatzhalter_startet_meldetAberAufError()
    {
        var config = Config(new() { ["Jwt:Key"] = "change_me_to_a_secure_key_at_least_32_chars" });

        SecretConfigCheck.ThrowIfCryptoKeyIsPlaceholder(config, new Env(Environments.Development));

        var log = new CapturingLogger<SecretConfigCheckTests>();
        SecretConfigCheck.LogStartupFindings(config, log);
        var entry = Assert.Single(log.Events, e => e.Level == LogLevel.Error);
        Assert.Equal("Jwt:Key", entry.State["ConfigKey"]);
    }

    [Fact]
    public void Production_mitEchtenSchluesseln_oderOhneEncryption_startet()
    {
        SecretConfigCheck.ThrowIfCryptoKeyIsPlaceholder(
            Config(new() { ["Jwt:Key"] = "q2Vb9xkLr0P+7s/0mZ1cT4uYw3eN8aHjKd5fGiBoQvE=" }),
            new Env(Environments.Production));
    }

    // --- Dienst-/Bot-Geheimnisse ----------------------------------------------------------

    [Fact]
    public void LogStartupFindings_meldetJedenPlatzhalterEinmalAufError()
    {
        var config = Config(new()
        {
            ["Discord:LinkSecret"] = "change_me_shared_with_schach_bot",
            ["SchachBot:StatsSecret"] = "change_me_shared_with_schach_bot",
            ["Crawler:ApiKey"] = "change_me_to_a_secure_key",
            ["Chessable:ServiceKey"] = "change_me_must_match_piratechess",
            ["SchachBot:WebhookSecret"] = "echter-wert-1234567890",
        });
        var log = new CapturingLogger<SecretConfigCheckTests>();

        SecretConfigCheck.LogStartupFindings(config, log);

        Assert.All(log.Events, e => Assert.Equal(LogLevel.Error, e.Level));
        Assert.Equal(
            new[] { "Chessable:ServiceKey", "Crawler:ApiKey", "Discord:LinkSecret", "SchachBot:StatsSecret" },
            log.Events.Select(e => (string)e.State["ConfigKey"]!).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void LogStartupFindings_schweigtBeiEchtenOderLeerenWerten()
    {
        var config = Config(new()
        {
            ["Jwt:Key"] = "q2Vb9xkLr0P+7s/0mZ1cT4uYw3eN8aHjKd5fGiBoQvE=",
            ["Discord:LinkSecret"] = "",
            ["SchachBot:StatsSecret"] = "echtes-stats-secret-1234567890",
            ["Crawler:ApiKey"] = "echter-crawler-schluessel",
        });
        var log = new CapturingLogger<SecretConfigCheckTests>();

        SecretConfigCheck.LogStartupFindings(config, log);

        Assert.Empty(log.Events);
    }

    /// <summary>I2-002: ein leeres Bot-Stats-Geheimnis schaltet /api/bot/player-progress auf 503 — der Endpoint
    /// loggt je Aufruf nichts, also muss der Start es EINMAL sagen.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LogStartupFindings_warntEinmalBeiLeeremStatsSecret(string? secret)
    {
        var log = new CapturingLogger<SecretConfigCheckTests>();

        SecretConfigCheck.LogStartupFindings(Config(new() { ["SchachBot:StatsSecret"] = secret }), log);

        var entry = Assert.Single(log.Events);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("SchachBot:StatsSecret", entry.State["ConfigKey"]);
        Assert.Contains("503", entry.Message);
    }

    [Fact]
    public void BotSignatur_mitPlatzhalterSecret_giltAlsUngueltig()
    {
        // Eine korrekt mit dem öffentlich bekannten Platzhalter gebaute Pfad-Signatur darf die
        // Discord-Verknüpfung der Ergebnis-GETs nicht freischalten.
        const string placeholder = "change_me_shared_with_schach_bot";
        const string path = "/api/book-puzzles/1/results";
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Headers[BotRequestSignature.SignatureHeader] =
            "sha256=" + SchachBotWebhookService.ComputeHmacHex(placeholder, ts + "." + path);
        ctx.Request.Headers[BotRequestSignature.TimestampHeader] = ts;

        Assert.Equal(BotSignatureCheck.Invalid, BotRequestSignature.CheckPath(ctx.Request, placeholder));
        // Gegenprobe: mit einem echten Secret ist dieselbe Bauweise gültig.
        const string real = "echtes-stats-secret-1234567890";
        ctx.Request.Headers[BotRequestSignature.SignatureHeader] =
            "sha256=" + SchachBotWebhookService.ComputeHmacHex(real, ts + "." + path);
        Assert.Equal(BotSignatureCheck.Valid, BotRequestSignature.CheckPath(ctx.Request, real));
    }

    // --- Beispiel-Dateien -----------------------------------------------------------------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static Dictionary<string, string> ReadEnv(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), file));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, @"(?m)^([A-Z0-9_]+)=([^\r\n#]*)"))
            result[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        return result;
    }

    /// <summary>Fail-closed-Schlüssel stehen in den Vorlagen LEER (compose bricht ab bzw. Feature aus),
    /// jeder übrige Schlüssel ist ein erkennbarer Platzhalter, und keine zwei Geheimnisse teilen sich
    /// denselben Wert — sonst öffnet ein stehengebliebener Wert zwei Türen.</summary>
    [Theory]
    [InlineData(".env.vpn.example")]
    [InlineData(".env.dev.vpn.example")]
    public void BeispielEnv_ohneBenutzbarePlatzhalter(string file)
    {
        var env = ReadEnv(file);

        foreach (var failClosed in new[] { "JWT_KEY", "ENCRYPTION_KEY", "DISCORD_LINK_SECRET", "SCHACH_BOT_STATS_SECRET" })
        {
            Assert.True(env.ContainsKey(failClosed), $"{file}: {failClosed} fehlt in der Vorlage");
            Assert.True(env[failClosed].Length == 0, $"{file}: {failClosed} muss leer sein, steht auf '{env[failClosed]}'");
        }

        var secrets = env.Where(kv => (kv.Key.EndsWith("_KEY") || kv.Key.EndsWith("_SECRET")) && kv.Value.Length > 0).ToList();
        foreach (var (name, value) in secrets)
            Assert.True(SecretConfigCheck.IsPlaceholder(value), $"{file}: {name}='{value}' ist kein erkennbarer Platzhalter");
        var shared = secrets.GroupBy(kv => kv.Value).Where(g => g.Count() > 1).Select(g => string.Join("+", g.Select(kv => kv.Key))).ToList();
        Assert.True(shared.Count == 0, $"{file}: gemeinsamer Platzhalter für {string.Join(", ", shared)}");
    }
}
