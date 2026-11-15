using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>Anonymer Link der Zusammenfassung einer eigenen Partie (<c>/ga/{token}/summary</c>): kein Name verlässt den Server —
/// nicht in der Partie, nicht im Text, nicht in den Feldern der Antwort.</summary>
public class AnonGameSharesTests : IDisposable
{
    private const string Pgn = "[Event \"Tiroler Liga\"]\n[Site \"https://lichess.org/abcdefgh\"]\n[Date \"2026.11.15\"]\n[Round \"3\"]\n"
        + "[White \"Kobold, Jonathan Dr.\"]\n[Black \"Monz, Dieter\"]\n[Result \"1-0\"]\n[WhiteElo \"2061\"]\n[BlackElo \"1890\"]\n\n"
        + "1. e4 {Monz überlegt lange} e5 (1... c5 2. Nf3) 2. Nf3 Nc6 1-0";
    private const string Text = "# Kobold, Jonathan Dr. – Monz, Dieter (1-0)\n\nSpanisch, 2 Züge, Runde 3, Brett 5 (Weiß Elo 2061).\n\n## Die Partie\n\n1.e4 e5 Weiß steht gut, der Bauer e4 auch.";
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private AnonGameShares Svc() => new(_db);

    private SavedGame Game(string? markdown = Text, string? lang = "de", int user = 7)
    {
        var g = new SavedGame { UserId = user, Source = "lichess", Pgn = Pgn, White = "Kobold, Jonathan Dr.", Black = "Monz, Dieter", Result = "1-0",
            ShareToken = "normal-" + Guid.NewGuid().ToString("N")[..8], SourceUrl = "https://lichess.org/abcdefgh", WhiteElo = 2061, BlackElo = 1890,
            PlayedAt = new DateTime(2026, 11, 15), OwnerSide = "black", ReviewLanguage = lang };
        _db.SavedGames.Add(g);
        _db.SaveChanges();
        if (markdown is not null)
        {
            _db.OwnGameSummaries.Add(new OwnGameSummary { SavedGameId = g.Id, UserId = user, Status = ClubGameSummaryStatus.Done, Markdown = markdown,
                DoneAt = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc), Model = "m", RequestedAt = DateTime.UtcNow, Error = "intern" });
            _db.SaveChanges();
        }
        return g;
    }

    [Fact]
    public async Task Create_fremdePartie_null_eigene_legtTokenAn_undBehaeltEs()
    {
        var g = Game();
        Assert.Null(await Svc().CreateAsync(8, g.Id));
        Assert.Null(_db.SavedGames.Single().AnonShareToken);
        var first = await Svc().CreateAsync(7, g.Id);
        Assert.False(string.IsNullOrWhiteSpace(first!.Token));
        Assert.NotEqual(g.ShareToken, first.Token);
        Assert.True(first.SummaryAvailable);
        Assert.Equal(first.Token, (await Svc().CreateAsync(7, g.Id))!.Token);
        Assert.Equal(first.Token, _db.SavedGames.Single().AnonShareToken);
    }

    [Fact]
    public async Task Create_ohneText_oderMitNameImText_meldetKeinenText()
    {
        Assert.False((await Svc().CreateAsync(7, Game(markdown: null).Id))!.SummaryAvailable);
        Assert.False((await Svc().CreateAsync(7, Game(markdown: "# Titel\n\nMonz stand besser.").Id))!.SummaryAvailable);
    }

    [Fact]
    public async Task Read_nurZuegeErgebnisSeite_keinNameKeineKopfdaten()
    {
        var g = Game();
        var token = (await Svc().CreateAsync(7, g.Id))!.Token;
        var dto = await Svc().ReadAsync(token);
        Assert.Equal("[Result \"1-0\"]\n\n1. e4 e5 2. Nf3 Nc6 1-0", dto!.Pgn);
        Assert.Equal("1-0", dto.Result);
        Assert.Equal("black", dto.OwnerSide);
        foreach (var secret in new[] { "Kobold", "Jonathan", "Monz", "Dieter", "2061", "1890", "lichess", "Tiroler", "2026" })
            Assert.DoesNotContain(secret, dto.Pgn);
        // Die Antwort hat genau diese Felder — ein neues Feld (Name, Elo, Datum, Quelle) macht den Test rot.
        Assert.Equal(new[] { "OwnerSide", "Pgn", "Result" }, typeof(AnonSharedGameDto).GetProperties().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task Read_undSummary_unbekanntesLeeresOderNormalesToken_null()
    {
        var g = Game();
        await Svc().CreateAsync(7, g.Id);
        foreach (var token in new[] { "gibt-es-nicht", " ", "", g.ShareToken })
        {
            Assert.Null(await Svc().ReadAsync(token));
            Assert.Null(await Svc().SummaryAsync(token));
        }
        // Eine nie anonym geteilte Partie (AnonShareToken null) ist über kein Token erreichbar.
        Game();
        Assert.Null(await Svc().ReadAsync(null!));
    }

    [Fact]
    public async Task Summary_UeberschriftOhneNamen_RundeBrettWeg_FliesstextBleibt()
    {
        var token = (await Svc().CreateAsync(7, Game().Id))!.Token;
        var dto = await Svc().SummaryAsync(token);
        Assert.Equal("done", dto!.Status);
        Assert.Equal("# Weiß – Schwarz (1-0)\n\nSpanisch, 2 Züge (Weiß Elo 2061).\n\n## Die Partie\n\n1.e4 e5 Weiß steht gut, der Bauer e4 auch.", dto.Markdown);
        Assert.Equal("m", dto.Model);
        Assert.Equal(new[] { "DoneAt", "Markdown", "Model", "Stale", "Status" },
            typeof(SharedOwnGameSummaryDto).GetProperties().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task Summary_englisch_WhiteBlack()
    {
        var token = (await Svc().CreateAsync(7, Game(markdown: "# Kobold, Jonathan Dr. – Monz, Dieter (1-0)\n\nWhite won.", lang: "en").Id))!.Token;
        Assert.Equal("# White – Black (1-0)\n\nWhite won.", (await Svc().SummaryAsync(token))!.Markdown);
    }

    [Theory]
    [InlineData("# Partie\n\nHier fand Monz den Gewinnzug.")]              // Nachname im Fließtext
    [InlineData("# Partie\n\nJonathans Plan ging auf.")]                    // Vorname im Genitiv
    [InlineData("# Partie\n\n```diagram\nply: 2\ncaption: DIETER am Zug\n```")] // in einer Bildunterschrift, andere Schreibung
    [InlineData("# Partie\n\nGut gespielt, patrik_o!")]                     // Kontoname des Besitzers
    [InlineData("# Partie\n\nGut gespielt, Oberhuber.")]                    // Nachname des Besitzers aus dem Profil
    [InlineData("# Partie\n\nlichessPatrik spielte stark.")]                // Plattformname des Besitzers
    public async Task Summary_NameBleibtStehen_none(string markdown)
    {
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "patrik_o", PasswordHash = "x" });
        _db.UserProfiles.Add(new UserProfile { UserId = 7, FirstName = "Patrik", LastName = "Oberhuber", LichessUsername = "lichessPatrik" });
        _db.SaveChanges();
        var token = (await Svc().CreateAsync(7, Game(markdown: markdown).Id))!.Token;
        Assert.Equal(new SharedOwnGameSummaryDto("none", null, null, false, null), await Svc().SummaryAsync(token));
    }

    [Fact]
    public async Task Summary_ohneText_none_auchBeiOffenerAnforderung()
    {
        var g = Game(markdown: null);
        _db.OwnGameSummaries.Add(new OwnGameSummary { SavedGameId = g.Id, UserId = 7, Status = ClubGameSummaryStatus.Requested, RequestedAt = DateTime.UtcNow });
        _db.SaveChanges();
        var token = (await Svc().CreateAsync(7, g.Id))!.Token;
        Assert.Equal("none", (await Svc().SummaryAsync(token))!.Status);
    }

    [Fact]
    public void SafeSummary_NameAlsWortteil_sperrtNicht_SchachwortAlsName_sperrt()
    {
        // „Jan“ steckt in „Januar“, „Tal“ in „Kapital“: kein Treffer (ganzes Wort verlangt).
        var jan = (new string?[] { "Tal, Jan" }, new string?[] { "Gegner, Otto" }, Array.Empty<string?>());
        Assert.Equal("# Weiß – Schwarz\n\nIm Januar schlug er Kapital daraus.", AnonGameShares.SafeSummary("# Tal, Jan – Gegner, Otto\n\nIm Januar schlug er Kapital daraus.", jan, german: true));
        // Ein Spieler „Bauer“: der Text meint die Figur, lässt sich aber nicht unterscheiden → kein Text.
        var bauer = (new string?[] { "Bauer, Max" }, new string?[] { "Gegner, Otto" }, Array.Empty<string?>());
        Assert.Null(AnonGameShares.SafeSummary("# Bauer, Max – Gegner, Otto\n\nDer Bauer e4 fällt.", bauer, german: true));
        // Ohne Namen (unbekannt, „?“) bleibt der Text, wie er ist.
        var none = (new string?[] { null, "?" }, new string?[] { "" }, Array.Empty<string?>());
        Assert.Equal("# Partie\n\nText", AnonGameShares.SafeSummary("# Partie\n\nText", none, german: true));
    }

    [Theory]
    [InlineData("Martinovic, Sasa", "De Vreugt, Dennis", "# Martinovic, Sasa – De Vreugt, Dennis (1-0)")]   // kurzer Namensteil vorn
    [InlineData("Chen, Yan Xi", "Oberhuber, Patrik", "# Chen, Yan Xi – Oberhuber, Patrik (1-0)")]              // kurzer Namensteil hinten
    [InlineData("Li, Yu", "GM Carlsen, Magnus", "# Li, Yu – GM Carlsen, Magnus (1-0)")]                        // nur kurze Teile; Titel vorn
    [InlineData("kahalm", "farnattie", "# kahalm - farnattie (1-0)")]                                          // Plattformnamen
    public void SafeSummary_Ueberschrift_ganzerNameWeg(string white, string black, string heading)
    {
        var text = AnonGameShares.SafeSummary(heading + "\n\nDe facto stand Weiß besser.", (new string?[] { white }, new string?[] { black }, Array.Empty<string?>()), german: true);
        Assert.Equal("# Weiß " + (heading.Contains(" - ") ? "-" : "–") + " Schwarz (1-0)\n\nDe facto stand Weiß besser.", text);
    }

    [Fact]
    public void BarePgn_abweichendeAusgangsstellung_bleibtLesbar()
    {
        const string fen = "8/8/8/4k3/8/4K3/4P3/8 b - - 0 40";
        var pgn = AnonGameShares.BarePgn($"[White \"A\"]\n[SetUp \"1\"]\n[FEN \"{fen}\"]\n\n40... Kd5 41. Kd3 *", "*");
        Assert.Equal($"[Result \"*\"]\n[SetUp \"1\"]\n[FEN \"{fen}\"]\n\n40... Kd5 41. Kd3 *", pgn);
    }

    [Fact]
    public async Task Widerruf_desTeilenLinks_loeschtAuchDenAnonymen()
    {
        var g = Game();
        var token = (await Svc().CreateAsync(7, g.Id))!.Token;
        Assert.NotNull(await TestServices.SavedGames(_db).RotateShareTokenAsync(7, g.Id));
        Assert.Null(await Svc().ReadAsync(token));
        Assert.Null(await Svc().SummaryAsync(token));
        // Neu teilen vergibt ein neues Token.
        Assert.NotEqual(token, (await Svc().CreateAsync(7, g.Id))!.Token);
    }
}
