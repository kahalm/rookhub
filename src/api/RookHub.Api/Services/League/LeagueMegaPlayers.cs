using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Das Spielerverzeichnis der ganzen ChessBase-Megabase (<see cref="LeagueMegaPlayer"/>): einspielen und durchsuchen.
/// Gebraucht beim Korrigieren eines Spielernamens in der Vereins-Datenbank, wenn der Gegner KEIN Ligaspieler ist —
/// dann bekommt die Partie wenigstens seinen richtigen Namen und seine FIDE-ID.
/// </summary>
public sealed class LeagueMegaPlayers
{
    public const int BatchSize = 5000;
    private readonly AppDbContext _db;

    public LeagueMegaPlayers(AppDbContext db) => _db = db;

    public static string KeyOf(string name) => LeagueRosterIndex.Fold(LeagueNames.Clean(name), false);

    /// <summary>Alles ersetzen: TSV-Zeilen <c>name \t fide \t games \t last_year \t max_elo</c>. Liefert die Zeilenzahl.</summary>
    public async Task<int> ReplaceAsync(TextReader tsv, CancellationToken ct)
    {
        if (_db.Database.IsRelational()) await _db.LeagueMegaPlayers.ExecuteDeleteAsync(ct);
        else { _db.LeagueMegaPlayers.RemoveRange(_db.LeagueMegaPlayers); await _db.SaveChangesAsync(ct); }
        var auto = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        var n = 0;
        var batch = new List<LeagueMegaPlayer>(BatchSize);
        try
        {
            string? line;
            while ((line = await tsv.ReadLineAsync(ct)) != null)
            {
                var f = line.Split('\t');
                if (f.Length < 3 || string.IsNullOrWhiteSpace(f[0])) continue;
                var name = LeagueNames.Clean(f[0]);
                if (name.Length > 120) name = name[..120];
                batch.Add(new LeagueMegaPlayer
                {
                    Name = name,
                    NameKey = KeyOf(name) is { Length: > 120 } k ? k[..120] : KeyOf(name),
                    FideId = f[1].Trim() is { Length: > 0 and <= 16 } fide ? fide : null,
                    Games = int.TryParse(f[2], out var g) ? g : 0,
                    LastYear = f.Length > 3 && int.TryParse(f[3], out var y) ? y : null,
                    MaxElo = f.Length > 4 && int.TryParse(f[4], out var e) ? e : null,
                });
                if (batch.Count >= BatchSize) { n += await FlushAsync(batch, ct); }
            }
            n += await FlushAsync(batch, ct);
        }
        finally { _db.ChangeTracker.AutoDetectChangesEnabled = auto; }
        return n;
    }

    private async Task<int> FlushAsync(List<LeagueMegaPlayer> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return 0;
        _db.LeagueMegaPlayers.AddRange(batch);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        var n = batch.Count;
        batch.Clear();
        return n;
    }

    /// <summary>
    /// Suche: jedes getippte Wort muss als Wortanfang im Namen stehen (ohne Groß/klein, Akzente); vorgefiltert in der
    /// Datenbank über das Präfix des ersten Wortes (Index), meistgespielte zuerst.
    /// </summary>
    public async Task<List<LeagueMegaPlayer>> SearchAsync(string query, int take, CancellationToken ct)
    {
        var words = KeyOf(query).Split(new[] { ' ', ',', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2).Distinct().ToList();
        if (words.Count == 0) return new();
        // Nachname steht vorn („hengl, philip") — ein Wort davon trifft den Präfix; welches, weiß man nicht.
        var hits = new List<LeagueMegaPlayer>();
        foreach (var w in words.Take(3))
        {
            var prefix = w;
            hits.AddRange(await _db.LeagueMegaPlayers.AsNoTracking().Where(p => p.NameKey.StartsWith(prefix))
                .OrderByDescending(p => p.Games).Take(300).ToListAsync(ct));
        }
        bool All(LeagueMegaPlayer p)
        {
            var own = p.NameKey.Split(new[] { ' ', ',', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => own.Any(o => o.StartsWith(w, StringComparison.Ordinal)));
        }
        return hits.DistinctBy(p => p.Id).Where(All).OrderByDescending(p => p.Games).Take(take).ToList();
    }
}
