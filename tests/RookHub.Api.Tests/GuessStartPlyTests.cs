using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Ab welchem Halbzug lohnt das Raten? Bis 0.467.0 war das eine Konstante; jetzt entscheidet der
/// FRUEHERE von zwei Hinweisen — die Eroeffnung verlaesst das Buch, oder der Kommentator faengt an
/// zu reden.
/// </summary>
public class GuessStartPlyTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GuessStartPly _svc;

    public GuessStartPlyTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new GuessStartPly(_db);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Ein Bestand, in dem ALLE Partien dieselben ersten <paramref name="common"/> Halbzuege
    /// spielen und danach auseinanderlaufen.</summary>
    private async Task SeedLibraryAsync(string commonLine, int games = GuessStartPly.MinLibrarySize + 50)
    {
        for (var i = 0; i < games; i++)
        {
            _db.LibraryGames.Add(new LibraryGame
            {
                // Ab hier ist jede Partie eine andere — der Bestand „endet" also nach der Zugfolge.
                OpeningLine = commonLine + " Zug" + i,
                Pgn = "x",
            });
        }
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Suggest_ohneBestand_gibtNichts()
    {
        var suggestion = await _svc.SuggestAsync("1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6", 40);

        Assert.Null(suggestion);
    }

    /// <summary>Der Bestand sagt, wo das Buch endet — hier nach sechs Halbzuegen.</summary>
    [Fact]
    public async Task Suggest_folgtDerEroeffnungsstatistik()
    {
        await SeedLibraryAsync("e4 e5 Nf3 Nc6 Bb5 a6");

        var suggestion = await _svc.SuggestAsync("1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7", 60);

        Assert.Equal(6, suggestion);
    }

    /// <summary>Bleibt die Eroeffnung lange im Buch, faengt die Uebung spaeter an — aber nur bis zur
    /// Obergrenze, sonst ueberspringt sie die halbe Partie.</summary>
    [Fact]
    public async Task Suggest_langeImBuch_startetSpaeter()
    {
        await SeedLibraryAsync("d4 d5 c4 e6 Nc3 Nf6 Bg5 Be7 e3 O-O");

        var suggestion = await _svc.SuggestAsync(
            "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 5. e3 O-O 6. Nf3 h6 7. Bh4 b6", 80);

        Assert.Equal(10, suggestion);
    }

    /// <summary>Der frueheste Hinweis gewinnt: hier redet der Kommentator, bevor der Bestand duenn
    /// wird — der Bestand traegt die Zugfolge noch zehn Halbzuege weit, der Kommentar haengt am
    /// neunten.</summary>
    [Fact]
    public async Task Suggest_ersterKommentarSchlaegtDieStatistik()
    {
        await SeedLibraryAsync("e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7");

        var withComment = await _svc.SuggestAsync(
            "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O {hier verlaesst die Partie den Pfad} Be7 " +
            "6. Re1 b5 7. Bb3 d6", 80);

        // Kommentar am 9. Halbzug (O-O) — genau der soll geraten werden, 0-basiert Index 8.
        // Der Bestands-Hinweis zeigte auf 10.
        Assert.Equal(8, withComment);
    }

    /// <summary>Und umgekehrt: die Untergrenze schlaegt einen sehr fruehen Kommentar. Ein Kommentar
    /// am dritten Zug ist meist eine Eroeffnungsbezeichnung, keine Stelle zum Rechnen.</summary>
    [Fact]
    public async Task Suggest_sehrfrueherKommentar_wirdVonDerUntergrenzeGehalten()
    {
        await SeedLibraryAsync("e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7");

        var suggestion = await _svc.SuggestAsync(
            "1. e4 e5 2. Nf3 Nc6 3. Bb5 {die Spanische} a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5", 80);

        Assert.Equal(GuessStartPly.Earliest, suggestion);
    }

    /// <summary>
    /// Die Untergrenze ist kein Schoenheitsfehler, sondern noetig: Sammlungen setzen den ersten
    /// Kommentar oft an den ERSTEN Zug, und dort steht eine Quellenangabe statt einer Erklaerung.
    /// Ohne sie begaenne die Uebung mit „rate 1. e4".
    /// </summary>
    [Fact]
    public async Task Suggest_kommentarAmErstenZug_faelltNichtVorDieUntergrenze()
    {
        await SeedLibraryAsync("e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6");

        var suggestion = await _svc.SuggestAsync(
            "1. e4 {1)Skinner: Alexander Alekhines Chess Games 1902-1946. p.14} e5 2. Nf3 Nc6 " +
            "3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7", 60);

        Assert.Equal(GuessStartPly.Earliest, suggestion);
    }

    /// <summary>Bei einer Miniatur liegt die Obergrenze am Partieende, nicht bei 40 — eine Sitzung
    /// ohne einen einzigen zu ratenden Zug waere keine.</summary>
    [Fact]
    public async Task Suggest_kurzePartie_bleibtInnerhalb()
    {
        await SeedLibraryAsync("f4 e5 g4 Qh4");

        var suggestion = await _svc.SuggestAsync("1. f4 e5 2. g4 Qh4#", 4);

        Assert.NotNull(suggestion);
        Assert.InRange(suggestion!.Value, 0, 4);
    }

    /// <summary>Eine Zugfolge muss an der WORTGRENZE passen — sonst zaehlt „e4 e" auch „e4 e6" mit
    /// und der Bestand sieht dicker aus, als er ist.</summary>
    [Fact]
    public async Task Suggest_zaehltNurGanzeZuege()
    {
        // 1100 Partien mit e4 e6, KEINE mit e4 e5 — nach zwei Halbzuegen ist „e4 e5" also selten.
        await SeedLibraryAsync("e4 e6 d4 d5");

        var suggestion = await _svc.SuggestAsync("1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6", 40);

        Assert.Equal(GuessStartPly.Earliest, suggestion);
    }

    /// <summary>Kopfzeilen sind keine Zuege. Ein Tag-Wert kann alles enthalten, auch etwas, das wie
    /// ein Zug aussieht.</summary>
    [Fact]
    public void MoveTextOf_laesstDieKopfzeilenWeg()
    {
        var moves = GuessStartPly.MoveTextOf("[Event \"e4 e5 Open\"]\n[White \"A\"]\n\n1. d4 d5 2. c4");

        Assert.DoesNotContain("Event", moves);
        Assert.DoesNotContain("e5 Open", moves);
        Assert.Contains("1. d4 d5", moves);
    }
}
