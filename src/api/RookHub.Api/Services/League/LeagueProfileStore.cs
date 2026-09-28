using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Die Spielerkarte aus ALLEN Quellen: gespeichert liegen in <see cref="LeaguePlayerProfile.Pgn"/> nur die fremden
/// Partien (Lumbra, chess-results), die Vereinspartien in <see cref="LeagueClubGame"/>. Karte, Partienzahl und
/// PGN-Download nehmen beides zusammen — so lässt sich eine Vereinspartie löschen, ohne das PGN umzuschreiben, und ein
/// neuer chess-results-Abruf überschreibt keine Vereinspartie.
///
/// <para><b>Doppelte über die ZÜGE</b>, nicht über die Namen: eine anonymisierte Vereinspartie heißt auf einer Seite
/// „Schwaz", dieselbe Partie von chess-results trägt den echten Namen. Gleiches Jahr + gleiche Hauptvariante = dieselbe
/// Partie; die fremde Fassung gewinnt (sie trägt das volle Datum).</para>
/// </summary>
public sealed class LeagueProfileStore
{
    public const string ClubSource = "Verein";
    private readonly AppDbContext _db;

    public LeagueProfileStore(AppDbContext db) => _db = db;

    /// <summary>Jahr + Hauptvariante — der Schlüssel, an dem dieselbe Partie in zwei Quellen erkannt wird.</summary>
    public static string MovesKey(IReadOnlyDictionary<string, string> headers, string raw)
    {
        headers.TryGetValue("Date", out var date);
        var year = date is { Length: >= 4 } && date[..4].All(char.IsDigit) ? date[..4] : "????";
        var moveText = PgnParser.SplitGames(raw).Select(g => g.MoveText).FirstOrDefault() ?? "";
        return year + "|" + string.Join(' ', PgnParser.ExtractMainlineSans(moveText));
    }

    /// <summary>Vereinspartien dazunehmen, die nicht schon unter den fremden stehen.</summary>
    public static List<LeagueProfileBuilder.Game> WithClub(List<LeagueProfileBuilder.Game> external, IEnumerable<LeagueClubGame> club)
    {
        var known = external.Select(g => MovesKey(g.Headers, g.Raw)).ToHashSet(StringComparer.Ordinal);
        var all = new List<LeagueProfileBuilder.Game>(external);
        foreach (var c in club)
        {
            var parsed = LeagueProfileBuilder.Parse(c.Pgn, ClubSource).FirstOrDefault();
            if (parsed is null || !known.Add(MovesKey(parsed.Headers, parsed.Raw))) continue;
            all.Add(parsed);
        }
        return all.OrderByDescending(g => g.Headers.TryGetValue("Date", out var d) ? d : "", StringComparer.Ordinal).ToList();
    }

    private Task<List<LeagueClubGame>> ClubGamesAsync(string fide, CancellationToken ct) =>
        _db.LeagueClubGames.AsNoTracking().Where(g => g.WhiteFide == fide || g.BlackFide == fide).ToListAsync(ct);

    private static List<LeagueProfileBuilder.Game> Stored(string? pgn) =>
        string.IsNullOrEmpty(pgn) ? new() : PgnParser.SplitGameBlocks(pgn)
            .Select(b => new LeagueProfileBuilder.Game(b.Headers, b.Raw.Trim(), LeagueProfileBuilder.StoredSource(b.Headers))).ToList();

    private async Task<string> NameAsync(string fide, string? stored, CancellationToken ct) =>
        !string.IsNullOrEmpty(stored) ? stored
            : await _db.LeaguePlayers.Where(x => x.FideId == fide).OrderByDescending(x => x.Tnr).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "";

    /// <summary>Karte eines Spielers neu rechnen: fremde Partien (optional um <paramref name="fresh"/> ergänzt, dann auch
    /// gespeichert) + Vereinspartien. Legt die Zeile an, wenn es noch keine gibt. Speichert NICHT — der Aufrufer tut es.</summary>
    public async Task RebuildAsync(string fide, CancellationToken ct, IEnumerable<LeagueProfileBuilder.Game>? fresh = null,
        DateTime? crFetchedAt = null, DateTime? now = null)
    {
        var row = await _db.LeaguePlayerProfiles.FindAsync(new object[] { fide }, ct);
        var name = await NameAsync(fide, row?.Name, ct);
        var external = Stored(row?.Pgn);
        string? externalPgn = null;
        if (fresh is not null)
        {
            external = LeagueProfileBuilder.Merge(external, fresh);
            externalPgn = LeagueProfileBuilder.Build(fide, name, external).Pgn;
        }
        var club = await ClubGamesAsync(fide, ct);
        if (row is null && club.Count == 0 && fresh is null) return;
        var (profile, _, count) = LeagueProfileBuilder.Build(fide, name, WithClub(external, club));
        if (row is null)
        {
            row = new LeaguePlayerProfile { FideId = fide };
            _db.LeaguePlayerProfiles.Add(row);
        }
        row.Name = name;
        row.GameCount = count;
        row.ProfileJson = profile.ToJsonString();
        if (externalPgn is not null) row.Pgn = externalPgn;
        if (crFetchedAt is not null) row.CrFetchedAt = crFetchedAt;
        row.UpdatedAt = now ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Die Partienzahl in den fertig gerechneten Liga-Ansichten nachziehen (Meldeliste, Feld <c>g</c>), ohne die Ligen
    /// neu zu rechnen — das bräuchte die ganze Historie im Speicher und gehört zum Knopf „Daten aktualisieren", nicht zu
    /// jeder hochgeladenen Vereinspartie. Speichert selbst.
    /// </summary>
    public async Task PatchViewCountsAsync(IReadOnlyCollection<string> fides, CancellationToken ct)
    {
        if (fides.Count == 0) return;
        var ids = fides.ToList();
        var counts = await _db.LeaguePlayerProfiles.AsNoTracking().Where(p => ids.Contains(p.FideId))
            .Select(p => new { p.FideId, p.GameCount }).ToDictionaryAsync(p => p.FideId, p => p.GameCount, ct);
        foreach (var view in await _db.LeagueViews.ToListAsync(ct))
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(view.Json) as System.Text.Json.Nodes.JsonObject;
            if (root?["fixtures"] is not System.Text.Json.Nodes.JsonObject teams) continue;
            var changed = false;
            foreach (var (_, rounds) in teams)
                foreach (var (_, fx) in rounds?.AsObject() ?? new System.Text.Json.Nodes.JsonObject())
                    foreach (var r in fx?["roster"] as System.Text.Json.Nodes.JsonArray ?? new System.Text.Json.Nodes.JsonArray())
                    {
                        var f = r?["fide"]?.GetValue<string>();
                        if (f is null || !ids.Contains(f)) continue;
                        var g = counts.GetValueOrDefault(f);
                        if (r!["g"]?.GetValue<int>() == g) continue;
                        r["g"] = g;
                        changed = true;
                    }
            if (changed) view.Json = root.ToJsonString();
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Alle Partien eines Spielers als PGN (fremde + Vereinspartien) — für den Download.</summary>
    public async Task<(string Name, string Pgn)?> PgnAsync(string fide, CancellationToken ct)
    {
        var p = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.Name, x.Pgn }).FirstOrDefaultAsync(ct);
        var club = await ClubGamesAsync(fide, ct);
        if ((p is null || string.IsNullOrEmpty(p.Pgn)) && club.Count == 0) return null;
        var games = WithClub(Stored(p?.Pgn), club);
        var name = await NameAsync(fide, p?.Name, ct);
        return (name, string.Join("\n\n", games.Select(g => g.Raw)) + "\n");
    }
}
