using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Die NATÜRLICHEN Züge einer Stellung für die Fehler-Erklärungen (1.003.0, Befund Vereinspartie 259, Vergleich 2839: nach dem
/// besten Zug 44.g3 stand nur 44...hxg3 da, das natürliche 44...h3 fehlte — Maia gibt ihm bei 1600 rund drei Viertel):
/// was ein Mensch der Stärke der Seite am Zug laut Maia-3 (<see cref="MaiaService"/>) mit mindestens
/// <see cref="MinProbability"/> spielt, absteigend. Genutzt an den Entscheidungen des Verteidigers
/// (<see cref="MoveComparisonDefences"/>) und für die Antworten auf den besten Zug (<see cref="MoveComparisonBestMoveTries"/>).
///
/// <para><b>Stärke</b> = die Elo der Seite aus der Partie (<see cref="ForAnalysisAsync"/>: Vereinspartie, sonst die gespeicherte
/// Partie, sonst die PGN-Kopfzeilen), fehlt sie, <see cref="DefaultElo"/>; der Gegner entsprechend (<c>elo_oppo</c>).</para>
/// <para>Ohne Maia (aus, Modell fehlt, Laufzeit scheitert) ist die Liste leer — dann gilt die Auswahl wie vorher.</para>
/// </summary>
internal sealed class MaiaNaturalMoves
{
    public const double DefaultMinProbability = 0.15;
    public const int DefaultElo = 1600;

    /// <summary>Nie ein natürlicher Zug (kein Maia).</summary>
    public static readonly MaiaNaturalMoves None = new((_, _, _) => null, DefaultElo, DefaultElo, 1.0);

    private readonly Func<string, int, int, IReadOnlyList<MaiaMove>?> _predict;
    private readonly Dictionary<string, IReadOnlyList<Natural>> _seen = new();

    public int WhiteElo { get; }
    public int BlackElo { get; }
    public double MinProbability { get; }

    /// <param name="predict">(FEN, Elo am Zug, Elo des Gegners) → Zugverteilung oder <c>null</c>.</param>
    public MaiaNaturalMoves(Func<string, int, int, IReadOnlyList<MaiaMove>?> predict, int whiteElo, int blackElo, double minProbability)
    {
        _predict = predict;
        WhiteElo = whiteElo;
        BlackElo = blackElo;
        MinProbability = Math.Clamp(minProbability, 0.01, 1.0);
    }

    /// <summary>Ein natürlicher Zug: UCI, Wahrscheinlichkeit, Stärke der Seite am Zug.</summary>
    internal sealed record Natural(string Uci, double P, int Elo)
    {
        /// <summary>„a natural move for a ~1600 player (Maia: 38 %)“ — der Vermerk im Faktenblock.</summary>
        public string Note => $"a natural move for a ~{Elo} player (Maia: {Math.Round(P * 100).ToString(CultureInfo.InvariantCulture)} %)";
    }

    /// <summary>Die natürlichen Züge der Seite am Zug, absteigend nach Wahrscheinlichkeit; leer ohne Maia.</summary>
    public IReadOnlyList<Natural> At(string fen)
    {
        if (string.IsNullOrEmpty(fen)) return [];
        if (_seen.TryGetValue(fen, out var known)) return known;
        var white = FenFields.WhiteToMove(fen);
        var self = white ? WhiteElo : BlackElo;
        var policy = _predict(fen, self, white ? BlackElo : WhiteElo);
        var result = (policy ?? []).Where(m => m.P >= MinProbability).OrderByDescending(m => m.P)
            .Select(m => new Natural(m.Uci, m.P, self)).ToList();
        _seen[fen] = result;
        return result;
    }

    /// <summary>Der Eintrag zu einem Zug, falls er natürlich ist.</summary>
    public Natural? Of(string fen, string uci) => At(fen).FirstOrDefault(n => string.Equals(n.Uci, uci, StringComparison.OrdinalIgnoreCase));

    /// <summary>Für den Vergleich einer Partie-Analyse: Stärken aus der Partie, Schwelle <c>ClubMistakeComparisons:MaiaMinProbability</c>
    /// (Vorgabe 0,15). Ohne verfügbares Maia bzw. ohne Analyse → <see cref="None"/>.</summary>
    public static async Task<MaiaNaturalMoves> ForAsync(AppDbContext db, MaiaService? maia, double minProbability, MoveComparison c, CancellationToken ct = default)
    {
        if (maia is not { Available: true } || c.GameAnalysisId is not int id) return None;
        var (white, black) = await ForAnalysisAsync(db, id, ct);
        return new MaiaNaturalMoves(maia.Predict, white ?? DefaultElo, black ?? DefaultElo, minProbability);
    }

    private static readonly Regex EloHeader = new(@"\[(White|Black)Elo\s+""(\d{3,4})""\]", RegexOptions.Compiled);

    /// <summary>Elo von Weiß und Schwarz einer Analyse: Vereinspartie (<see cref="LeagueClubGame.WhiteElo"/>), sonst die
    /// gespeicherte Partie (<see cref="SavedGame.WhiteElo"/>), sonst die Kopfzeilen der PGN; je Seite <c>null</c>, wenn nichts da ist.</summary>
    public static async Task<(int? White, int? Black)> ForAnalysisAsync(AppDbContext db, int analysisId, CancellationToken ct = default)
    {
        var a = await db.GameAnalyses.AsNoTracking().Where(x => x.Id == analysisId)
            .Select(x => new { x.LeagueClubGameId, x.Pgn }).FirstOrDefaultAsync(ct);
        if (a is null) return (null, null);
        int? white = null, black = null;
        if (a.LeagueClubGameId is int gameId
            && await db.LeagueClubGames.AsNoTracking().Where(g => g.Id == gameId).Select(g => new { g.WhiteElo, g.BlackElo }).FirstOrDefaultAsync(ct) is { } club)
            (white, black) = (Usable(club.WhiteElo), Usable(club.BlackElo));
        if (white is null || black is null)
        {
            var saved = await db.SavedGames.AsNoTracking().Where(g => g.GameAnalysisId == analysisId)
                .Select(g => new { g.WhiteElo, g.BlackElo }).FirstOrDefaultAsync(ct);
            white ??= Usable(saved?.WhiteElo);
            black ??= Usable(saved?.BlackElo);
        }
        if (white is null || black is null)
            foreach (Match m in EloHeader.Matches(a.Pgn ?? ""))
            {
                var v = Usable(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
                if (m.Groups[1].Value == "White") white ??= v; else black ??= v;
            }
        return (white, black);
    }

    private static int? Usable(int? elo) => elo is >= 100 and <= 3500 ? elo : null;
}
