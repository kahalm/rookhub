using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using Xunit.Abstractions;

namespace RookHub.Api.Tests;

/// <summary>
/// Gleichheit mit der Python-Fassung auf dem ECHTEN Bestand (Tor für den Umzug: dieselben Prozente).
/// Läuft nur mit <c>LEAGUE_BUNDLE</c> (export_bundle.py data.json.gz) und <c>LEAGUE_PY_DATA</c>
/// (Ordner mit den l&lt;tnr&gt;.json der Python-Fassung); sonst übersprungen — die CI hat den Bestand nicht.
/// </summary>
public sealed class LeagueParityFactAttribute : FactAttribute
{
    public LeagueParityFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LEAGUE_BUNDLE"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LEAGUE_PY_DATA")))
            Skip = "LEAGUE_BUNDLE/LEAGUE_PY_DATA nicht gesetzt — Paritätstest nur lokal gegen den echten Bestand.";
    }
}

public class LeaguePythonParityTests
{
    private readonly ITestOutputHelper _out;
    public LeaguePythonParityTests(ITestOutputHelper output) => _out = output;

    [LeagueParityFact]
    public void CSharpForecast_EqualsPython()
    {
        using var f = File.OpenRead(Environment.GetEnvironmentVariable("LEAGUE_BUNDLE")!);
        using var gz = new GZipStream(f, CompressionMode.Decompress);
        var b = JsonSerializer.Deserialize<LeagueImportService.Bundle>(gz, LeagueImportService.Json)!;
        var id = 0;
        var w = new LeagueWorld(
            b.Tournaments!.Select(t => new LeagueTournament { Tnr = t.Tnr, Name = t.Name, Season = t.Season, Level = t.Level, League = t.League, Grp = t.Grp ?? "", Stage = t.Stage, Aborted = t.Aborted }),
            b.Rounds!.Select(r => new LeagueRound { Tnr = r.Tnr, Round = r.Round, Date = DateOnly.TryParseExact(r.Date ?? "", "dd.MM.yyyy", out var d) ? d : null }),
            b.Matches!.Select(m => new LeagueMatch { Id = ++id, Tnr = m.Tnr, Round = m.Round, MatchNo = m.MatchNo, Home = m.Home, Away = m.Away, HomePts = m.HomePts, AwayPts = m.AwayPts, Date = m.Date, Time = m.Time, Venue = m.Venue }),
            b.Games!.Select(g => new LeagueGame { Id = ++id, Tnr = g.Tnr, Round = g.Round, MatchNo = g.MatchNo, Board = g.Board, HomeTeam = g.HomeTeam, AwayTeam = g.AwayTeam, HomePlayer = g.HomePlayer, AwayPlayer = g.AwayPlayer, Result = g.Result ?? "", HomeScore = g.HomeScore, AwayScore = g.AwayScore, Forfeit = g.Forfeit, HomeFide = g.HomeFide, AwayFide = g.AwayFide, HomeElo = g.HomeElo, AwayElo = g.AwayElo }),
            b.Players!.Select(p => new LeaguePlayer { Id = ++id, Tnr = p.Tnr, Team = p.Team, RosterBoard = p.RosterBoard, Name = p.Name, NameKey = p.NameKey, FideId = string.IsNullOrEmpty(p.FideId) ? null : p.FideId, EloI = p.EloI, EloN = p.EloN }));
        var builder = new LeagueViewBuilder(w, LeagueModel.FromEmbedded(), new Dictionary<string, int>(), new Dictionary<string, List<LeagueOnlineAccount>>());
        var season = w.Seasons[^1];
        int fixtures = 0, probs = 0, candDiff = 0;
        double maxDiff = 0;
        foreach (var t in w.T.Values.Where(t => t.Season == season && t.Stage == "Liga"))
        {
            var cs = builder.Build(t.Tnr, w.Games);
            var py = JsonNode.Parse(File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("LEAGUE_PY_DATA")!, $"l{t.Tnr}.json")))!;
            foreach (var (team, fxNode) in py["fixtures"]!.AsObject())
            {
                foreach (var (rnd, pe) in fxNode!.AsObject())
                {
                    var ce = cs["fixtures"]![team]![rnd]!;
                    Assert.Equal(pe!["status"]?.GetValue<string>(), ce["status"]?.GetValue<string>());
                    Assert.Equal(pe["phase"]?.GetValue<string>(), ce["phase"]?.GetValue<string>());
                    if (pe["roster"] is not JsonArray pr) continue;
                    fixtures++;
                    var cr = ce["roster"]!.AsArray();
                    Assert.Equal(pr.Count, cr.Count);
                    for (var i = 0; i < pr.Count; i++)
                    {
                        Assert.Equal(pr[i]!["n"]!.GetValue<string>(), cr[i]!["n"]!.GetValue<string>());
                        var d = Math.Abs(pr[i]!["p"]!.GetValue<double>() - cr[i]!["p"]!.GetValue<double>());
                        maxDiff = Math.Max(maxDiff, d);
                        probs++;
                        Assert.Equal(pr[i]!["prev"]!.GetValue<string>(), cr[i]!["prev"]!.GetValue<string>());
                    }
                    var pb = pe["boards"]!.AsArray();
                    var cb = ce["boards"]!.AsArray();
                    for (var k = 0; k < pb.Count; k++)
                    {
                        var pc = pb[k]!["cand"]!.AsArray();
                        var cc = cb[k]!["cand"]!.AsArray();
                        for (var j = 0; j < Math.Min(pc.Count, cc.Count); j++)
                        {
                            maxDiff = Math.Max(maxDiff, Math.Abs(pc[j]!["p"]!.GetValue<double>() - cc[j]!["p"]!.GetValue<double>()));
                            if (pc[j]!["n"]!.GetValue<string>() != cc[j]!["n"]!.GetValue<string>()) candDiff++;
                        }
                        Assert.Equal(pb[k]!["opp_color"]!.GetValue<string>(), cb[k]!["opp_color"]!.GetValue<string>());
                    }
                }
            }
        }
        _out.WriteLine($"{fixtures} Begegnungen, {probs} Einsatz-Wahrscheinlichkeiten, größte Abweichung {maxDiff:0.000000}, abweichende Kandidaten-Reihenfolge {candDiff}");
        Assert.True(fixtures > 0);
        Assert.True(maxDiff <= 0.0011, $"größte Abweichung {maxDiff}");   // 3 Nachkommastellen gerundet
    }
}
