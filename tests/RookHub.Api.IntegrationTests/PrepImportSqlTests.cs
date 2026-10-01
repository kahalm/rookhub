using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.Prep;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Der Partiebestand der Spielervorbereitung gegen ECHTES MariaDB: relational schreibt <see cref="PrepImportService"/>
/// Sammel-SQL (<c>INSERT … ON DUPLICATE KEY UPDATE</c>, <c>IN (…)</c>-Listen, mehrere Anweisungen je Befehl) statt EF —
/// das sieht InMemory nicht. Geprüft: neue Partien und Spieler-Zähler, Dubletten über beide Quellen (ein Bit dazu, die
/// Seite wandert zum Spieler mit FIDE-ID), das doppelt geschickte Paket und ein Paket über mehrere Portionen.
/// </summary>
public class PrepImportSqlTests(PrepImportSqlFixture fixture) : IAsyncLifetime, IClassFixture<PrepImportSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Najdorf = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7";

    private static string Game(string white, string black, string moves = Najdorf, string date = "2024.05.17", string? whiteFide = null,
        string elo = "2210", string evt = "Open Schwaz") =>
        $"[Event \"{evt}\"]\n[Site \"Schwaz\"]\n[Date \"{date}\"]\n[Round \"3\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n"
        + $"[WhiteElo \"{elo}\"]\n[ECO \"B90\"]\n" + (whiteFide is null ? "" : $"[WhiteFideId \"{whiteFide}\"]\n") + "\n" + moves + " 1-0\n\n";

    private async Task<PrepImportService.ChunkResult> Import(byte source, int chunk, string pgn)
    {
        await using var db = fixture.Schema.NewContext();
        return await new PrepImportService(db).ImportChunkAsync(source, chunk, chunk * 5000L, pgn, default);
    }

    [MySqlFact]
    public async Task ImportChunk_BothSources_OneRowFidePlayerAndCounters()
    {
        var mega = await Import(PrepSources.Mega, 0,
            Game("Obukhov, Alexander", "Kaunonen, Jouni", date: "1997.??.??", elo: "2415")
            + Game("Kaunonen, Jouni", "Hermannsson, Tomas", moves: "1. d4 d5 2. c4 e6", date: "2000.??.??")
            + Game("Kaunonen, Jouni", "Hermannsson, Tomas", moves: "1. d4 d5 2. c4 e6", date: "2000.??.??"));   // doppelt im Paket
        Assert.Equal((3, 2, 1), (mega.Read, mega.Added, mega.Duplicates));

        var lumbra = await Import(PrepSources.Lumbra, 0,
            Game("Obukhov, Alexander", "Kaunonen, Jouni", date: "1997.03.02", whiteFide: "990003", elo: "2430")   // dieselbe, mit FIDE
            + Game("Huber, Franz", "Kaunonen, Jouni", whiteFide: "990004"));                                        // neu
        Assert.Equal((2, 1, 1), (lumbra.Read, lumbra.Added, lumbra.Duplicates));

        await using var db = fixture.Schema.NewContext();
        var games = await db.PrepGames.OrderBy(g => g.Id).ToListAsync();
        Assert.Equal(3, games.Count);
        Assert.Equal(PrepSources.Mega | PrepSources.Lumbra, games[0].Sources);
        Assert.Equal(PrepSources.Mega, games[1].Sources);
        Assert.Equal(PrepSources.Lumbra, games[2].Sources);
        Assert.Equal("e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 a6 Be3 e5 Nb3 Be6 f3 Be7", games[0].Moves);
        Assert.Equal(19970000, games[0].PlayedOn);

        var fide = await db.PrepPlayers.SingleAsync(p => p.FideId == "990003");
        Assert.Equal(fide.Id, games[0].WhiteId);                                       // zum Spieler mit FIDE-ID gewandert
        Assert.Equal((1, (short?)1997, (short?)2430), (fide.Games, fide.FirstYear, fide.MaxElo));
        var nameOnly = await db.PrepPlayers.SingleAsync(p => p.FideId == null && p.NameKey == "obukhov, alexander");
        Assert.Equal(0, nameOnly.Games);
        var kaunonen = await db.PrepPlayers.SingleAsync(p => p.NameKey == "kaunonen, jouni");
        Assert.Equal((3, (short?)1997, (short?)2024), (kaunonen.Games, kaunonen.FirstYear, kaunonen.LastYear));
        Assert.Single(await db.PrepEvents.ToListAsync());

        var again = await Import(PrepSources.Lumbra, 0, "egal — das Paket ist schon da");
        Assert.True(again.Already);
        Assert.Equal(3, await db.PrepGames.CountAsync());
    }

    /// <summary>Mehr als eine INSERT-Portion (1000 Zeilen) und mehr als eine <c>IN</c>-Liste (2000 Hashes) — beim zweiten Mal
    /// sind alle Partien Dubletten der anderen Quelle.</summary>
    [MySqlFact]
    public async Task ImportChunk_ManyGames_BatchedAndDeduplicated()
    {
        var files = "abcdefgh";
        var sb = new StringBuilder();
        var n = 0;
        for (var a = 0; a < 8 && n < 2600; a++)
            for (var b = 0; b < 8 && n < 2600; b++)
                for (var c = 1; c <= 8 && n < 2600; c++)
                    for (var d = 1; d <= 6 && n < 2600; d++, n++)
                        sb.Append(Game($"Spieler{n % 300}, A", $"Gegner{n % 250}, B",
                            moves: $"1. {files[a]}3 {files[b]}6 2. N{files[(c + 1) % 8]}3 N{files[d]}6", evt: $"Turnier {n % 40}"));
        var pgn = sb.ToString();

        var first = await Import(PrepSources.Mega, 0, pgn);
        Assert.Equal((2600, 2600, 0), (first.Read, first.Added, first.Duplicates));
        var second = await Import(PrepSources.Lumbra, 0, pgn);
        Assert.Equal((2600, 0, 2600), (second.Read, second.Added, second.Duplicates));

        await using var db = fixture.Schema.NewContext();
        Assert.Equal(2600, await db.PrepGames.CountAsync(g => g.Sources == (PrepSources.Mega | PrepSources.Lumbra)));
        Assert.Equal(550, await db.PrepPlayers.CountAsync());
        Assert.Equal(2 * 2600, await db.PrepPlayers.SumAsync(p => p.Games));
        Assert.Equal(40, await db.PrepEvents.CountAsync());
    }
}

public sealed class PrepImportSqlFixture() : MariaDbClassFixture("prep", withApp: false);
