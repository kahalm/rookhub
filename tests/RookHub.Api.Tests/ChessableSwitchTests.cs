using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Schalter <c>Chessable:Enabled</c> hat EINE Lesestelle mit EINER Vorgabe (<see cref="ChessableSwitch"/>,
/// Codereview 2026-09-29, A10-017). Vorher las ihn jede von sieben Stellen selbst mit eigener Literal-Vorgabe —
/// die Quelltext-Wache unten macht jede neue Kopie rot.
/// </summary>
public class ChessableSwitchTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Vorgabe_IstAn_OhneKonfigurationUndOhneSchluessel()
    {
        // Spiegel der bisherigen Literal-Vorgabe an allen sieben Stellen: GetValue("Chessable:Enabled", true).
        Assert.True(ChessableSwitch.Default);
        Assert.Equal("Chessable:Enabled", ChessableSwitch.Key);
        Assert.True(ChessableSwitch.IsEnabled(null));
        Assert.True(ChessableSwitch.IsEnabled(Config(new())));
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("true", true)]
    public void Schluessel_WirdGelesen(string value, bool expected) =>
        Assert.Equal(expected, ChessableSwitch.IsEnabled(Config(new() { ["Chessable:Enabled"] = value })));

    private static readonly Regex BlockKommentar = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ZeilenKommentar = new(@"(^|\s)//.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void NurChessableSwitch_LiestDenSchluessel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var root = Path.Combine(dir!.FullName, "src", "api", "RookHub.Api");

        var treffer = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Pfad: Path.GetRelativePath(root, f).Replace('\\', '/'), Voll: f))
            .Where(f => !f.Pfad.StartsWith("bin/") && !f.Pfad.StartsWith("obj/") && !f.Pfad.StartsWith("Migrations/"))
            .Where(f => ZeilenKommentar.Replace(BlockKommentar.Replace(File.ReadAllText(f.Voll), ""), "$1")
                .Contains("\"Chessable:Enabled\""))
            .Select(f => f.Pfad)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "Services/ChessableSwitch.cs" }, treffer);
    }
}
