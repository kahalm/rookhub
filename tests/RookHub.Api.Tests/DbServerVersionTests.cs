using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RookHub.Api.Data;

namespace RookHub.Api.Tests;

/// <summary>
/// EINE Server-Fassung fuer Laufzeit, Design-Time und Tests (<see cref="DbServerVersion"/>, Codereview 2026-09-29,
/// A9-010): vorher AutoDetect je Scope zur Laufzeit (ausserhalb der Retry-Strategie), MySQL 11.0 in Design-Time und
/// den meisten SQL-Tests, MariaDB 11.4 in einzelnen — die Integrationstests uebersetzten mit einem anderen Dialekt
/// als Prod.
/// </summary>
public class DbServerVersionTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void Fassung_IstMariaDb_UndPasstZumImageDerComposeDateien()
    {
        Assert.IsType<MariaDbServerVersion>(DbServerVersion.Current);

        // Die gepinnten Images (Dev/Prod) muessen genau die deklarierte Fassung tragen, die schwebenden
        // (CI/E2E: mariadb:11) dieselbe Hauptversion.
        var root = RepoRoot();
        var image = new Regex(@"image:\s*mariadb:(\d+)(?:\.(\d+))?\b");
        var funde = new[] { "compose.dev.yml", "compose.vpn.yml", "compose.dev.vpn.yml", "compose.yml.example", "compose.vpn.example",
                     "compose.e2e.yml", ".github/workflows/test.yml" }
            .SelectMany(f => image.Matches(File.ReadAllText(Path.Combine(root, f))).Select(m => (Datei: f, m)))
            .ToList();
        Assert.True(funde.Count >= 7, $"zu wenige mariadb-Images gefunden ({funde.Count})");
        foreach (var (datei, m) in funde)
        {
            Assert.True(int.Parse(m.Groups[1].Value) == DbServerVersion.Current.Version.Major, $"{datei}: {m.Value}");
            if (m.Groups[2].Success)
                Assert.True(int.Parse(m.Groups[2].Value) == DbServerVersion.Current.Version.Minor, $"{datei}: {m.Value}");
        }
    }

    [Fact]
    public void DesignTimeFactory_NutztDieselbeFassung()
    {
        var alt = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "server=localhost;database=x;user=x;password=x");
        try
        {
            using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
            // MySqlOptionsExtension ist Provider-intern — per Name/Reflection statt Typbezug.
            var ext = db.GetService<IDbContextOptions>().Extensions.Single(e => e.GetType().Name == "MySqlOptionsExtension");
            Assert.Same(DbServerVersion.Current, ext.GetType().GetProperty("ServerVersion")!.GetValue(ext));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", alt);
        }
    }

    private static readonly Regex BlockKommentar = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ZeilenKommentar = new(@"(^|\s)//.*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex EigeneFassung = new(@"\b(MySqlServerVersion|MariaDbServerVersion)\s*\(|ServerVersion\s*\.\s*(AutoDetect|Create|Parse)\s*\(", RegexOptions.Compiled);

    [Fact]
    public void NurDbServerVersion_LegtEineFassungFest()
    {
        var root = RepoRoot();
        var treffer = new[] { Path.Combine("src", "api", "RookHub.Api"), "tests" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
            .Select(f => (Pfad: Path.GetRelativePath(root, f).Replace('\\', '/'), Voll: f))
            .Where(f => !f.Pfad.Contains("/bin/") && !f.Pfad.Contains("/obj/") && !f.Pfad.Contains("/Migrations/"))
            .Where(f => EigeneFassung.IsMatch(ZeilenKommentar.Replace(BlockKommentar.Replace(File.ReadAllText(f.Voll), ""), "$1")))
            .Select(f => f.Pfad)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "src/api/RookHub.Api/Data/DbServerVersion.cs" }, treffer);
    }
}
