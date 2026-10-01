using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Die Praefix-Suche ueber <c>OpeningLine</c> gegen ECHTES MariaDB (Codereview A6-017): das rohe
/// <c>GROUP BY</c> des Eroeffnungsbaums mit <c>LIKE {0} ESCAPE '!'</c> und die EF-Abfragen mit
/// <c>EF.Functions.Like(…, …, "!")</c> (Gesamtzahl des Baums, Liste des Rohbestands, kuratierter
/// Bestand). InMemory kennt weder <c>SUBSTRING_INDEX</c> noch die <c>ESCAPE</c>-Klausel.
/// </summary>
public class OpeningLinePrefixSqlTests(OpeningLinePrefixSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<OpeningLinePrefixSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static GameAnalysisService Analyses(AppDbContext db)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build();
        return new GameAnalysisService(db, new AnalysisJobService(db, new EncryptionService(config)),
            new CommentSetService(db, NullLogger<CommentSetService>.Instance), NullLogger<GameAnalysisService>.Instance);
    }

    [MySqlFact]
    public async Task PrefixSearch_EscapesWildcards_AndStillFindsRealLines()
    {
        await using (var db = fixture.Schema.NewContext())
        {
            var user = new AppUser { Username = "op", Email = "op@t.local", PasswordHash = "x" };
            db.AppUsers.Add(user);
            foreach (var line in new[] { "e4 e5 Nf3", "e4 c5", "d4 d5 c4" })
                db.LibraryGames.Add(new LibraryGame { SourceFile = "t.pgn", MovesHash = line, Pgn = "x", OpeningLine = line });
            await db.SaveChangesAsync();
            foreach (var line in new[] { "e4 e5 Nf3", "d4 d5 c4" })
            {
                var g = new GameAnalysis
                {
                    UserId = user.Id, Title = line, Pgn = "1. e4 e5 *", StartFen = GamePlies.StartFen(), PlyCount = 2,
                    IsPublic = true, Status = GameAnalysisStatus.Done, OpeningLine = line,
                };
                g.Positions.Add(new GameAnalysisPosition
                {
                    Ply = 0, Fen = GamePlies.StartFen(), GameMoveUci = "e2e4", GameMoveSan = "e4",
                    CandidatesJson = "[{\"uci\":\"e2e4\",\"cp\":20}]",
                });
                db.GameAnalyses.Add(g);
            }
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.Schema.NewContext())
        {
            var tree = new GuessOpeningTree(db);
            foreach (var onlyPlayable in new[] { false, true })
            {
                foreach (var wildcard in new[] { "_4", "%", "e_" })
                {
                    var leer = await tree.BranchAsync(wildcard, onlyPlayable);
                    Assert.Equal(0, leer.Total);
                    Assert.Empty(leer.Moves);
                }
            }

            var e4 = await tree.BranchAsync("e4", onlyPlayable: false);
            Assert.Equal(2, e4.Total);
            Assert.Equal(new[] { "c5", "e5" }, e4.Moves.Select(m => m.San).OrderBy(s => s, StringComparer.Ordinal));
            var root = await tree.BranchAsync(null, onlyPlayable: false);
            Assert.Equal(3, root.Total);
            Assert.Equal(1, (await tree.BranchAsync("d4", onlyPlayable: true)).Total);

            var library = new LibraryGameService(db, Analyses(db));
            Assert.Equal(0, (await library.SearchAsync(0, null, null, null, 1, 50, line: "_4", byPosition: true)).Total);
            Assert.Equal(2, (await library.SearchAsync(0, null, null, null, 1, 50, line: "e4", byPosition: true)).Total);

            var analyses = Analyses(db);
            Assert.Empty(await analyses.ListPublicAsync(line: "_4"));
            Assert.Equal("e4 e5 Nf3", Assert.Single(await analyses.ListPublicAsync(line: "e4 e5")).Title);
        }
    }
}

public sealed class OpeningLinePrefixSqlFixture() : MariaDbClassFixture("oprefix", withApp: false);
