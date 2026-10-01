using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Teilen-Links (Partie, Blatt, Linie, Rekonstruktion, Liga) aus EINER Erzeugung (Codereview 2026-09-29, A2-015).
/// Vorher stand <c>NewToken</c> samt Eindeutigkeits-Schleife viermal wortgleich in den Diensten. Das Format bleibt:
/// 128 Bit, Base64URL ohne Padding, 22 Zeichen — die Dienst-Tests pruefen es je mit <see cref="Format"/>.
/// </summary>
public class ShareTokensTests
{
    /// <summary>Format der 128-Bit-Teilen-Links, wie sie in der Datenbank liegen und in gedruckten QR-Codes stehen.</summary>
    public const string Format = "^[A-Za-z0-9_-]{22}$";

    [Fact]
    public void New_HatDasBisherigeFormat()
    {
        var a = ShareTokens.New();
        Assert.Matches(Format, a);
        Assert.NotEqual(a, ShareTokens.New());
        Assert.Matches("^[A-Za-z0-9_-]{24}$", ShareTokens.New(18));   // Liga: 144 Bit
    }

    [Fact]
    public async Task NewUniqueAsync_WuerfeltBeiKollisionNeu()
    {
        var gefragt = new List<string>();
        var token = await ShareTokens.NewUniqueAsync(t =>
        {
            gefragt.Add(t);
            return Task.FromResult(gefragt.Count < 3);   // die ersten beiden „gibt es schon"
        });

        Assert.Equal(3, gefragt.Count);
        Assert.Equal(gefragt[2], token);
        Assert.Matches(Format, token);
    }

    [Fact]
    public async Task NewUniqueAsync_GibtNachFuenfKollisionenUngeprueftEinWeiteres()
    {
        var gefragt = new List<string>();
        var token = await ShareTokens.NewUniqueAsync(t => { gefragt.Add(t); return Task.FromResult(true); });

        Assert.Equal(ShareTokens.MaxAttempts, gefragt.Count);
        Assert.Equal(5, ShareTokens.MaxAttempts);
        Assert.DoesNotContain(token, gefragt);
        Assert.Matches(Format, token);
    }

    /// <summary>Wachhund: kein Dienst wuerfelt seinen Teilen-Link wieder selbst.</summary>
    [Theory]
    [InlineData("Services/SavedGameService.cs")]
    [InlineData("Services/WorksheetService.cs")]
    [InlineData("Services/SharedLineService.cs")]
    [InlineData("Services/GameReconstructionService.cs")]
    [InlineData("Services/League/LeagueService.cs")]
    public void TeilenDienste_NutzenShareTokens(string pfad)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "api", "RookHub.Api", pfad));

        Assert.DoesNotContain("RandomNumberGenerator", text);
        Assert.Contains("ShareTokens.", text);
    }
}
