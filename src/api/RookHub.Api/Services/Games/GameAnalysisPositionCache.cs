using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Games;

/// <summary>Ein schon gerechnetes Ergebnis derselben Stellung.</summary>
internal sealed record CachedPosition(string CandidatesJson, string? EvalText, int Depth);

/// <summary>
/// Eine Stellung nur einmal rechnen (0.921.0, Wunsch 2026-11-09): bevor die Pumpe einen Auftrag anlegt, schaut sie in den
/// fertigen Stellungen anderer Stockfish-Analysen nach. Gemessen waren 18 % aller Stellungen doppelt gerechnet, bei einer
/// Partie, die zugleich gespeichert und Vereinspartie ist, alle.
///
/// <list type="bullet">
/// <item><b>Gleich</b> = Figuren, Seite am Zug, Rochaderechte, En-passant-Feld; die beiden Zugzähler zählen nicht.</item>
/// <item><b>Gut genug</b> = mindestens so tief und mit mindestens so vielen Linien wie verlangt. Eine Stellung mit weniger
/// legalen Zügen als Linien findet deshalb nichts und wird wie bisher gerechnet.</item>
/// <item><b>Nie</b> Lc0 oder Knotenziel, weder als Quelle noch als Ziel: deren Zahlen sind eine andere Aussage.</item>
/// </list>
/// Die Spalte <c>Fen</c> vergleicht ohne Rücksicht auf Groß-/Kleinschreibung, „K" und „k" wären für sie gleich — darum prüft
/// der zweite Schritt den Schlüssel im Speicher genau.
/// </summary>
internal static class GameAnalysisPositionCache
{
    private const int MaxRows = 20;

    /// <summary>Die FEN ohne Zugzähler, mit abschließendem Leerzeichen; <c>null</c> = keine vollständige FEN.</summary>
    internal static string? KeyOf(string fen)
    {
        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 4 ? null : string.Join(' ', parts.Take(4)) + " ";
    }

    internal static bool Applies(GameAnalysis analysis) => analysis.EngineId == null && analysis.TargetNodes == null;

    internal static async Task<CachedPosition?> FindAsync(AppDbContext db, GameAnalysis analysis, string fen, int depth, int multiPv,
        CancellationToken ct)
    {
        if (!Applies(analysis) || KeyOf(fen) is not { } key) return null;
        var rows = await db.GameAnalysisPositions.AsNoTracking()
            .Where(p => EF.Functions.Like(p.Fen, key + "%") && p.Depth >= depth && p.CandidatesJson != null
                && p.GameAnalysisId != analysis.Id && p.GameAnalysis!.EngineId == null && p.GameAnalysis.TargetNodes == null)
            .Select(p => new { p.Fen, p.CandidatesJson, p.EvalText, p.Depth })
            .Take(MaxRows)
            .ToListAsync(ct);
        var hit = rows.FirstOrDefault(r => KeyOf(r.Fen) == key && LineCount(r.CandidatesJson!) >= multiPv);
        return hit is null ? null : new CachedPosition(hit.CandidatesJson!, hit.EvalText, hit.Depth);
    }

    private static int LineCount(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException) { return 0; }
    }

    internal static void Apply(GameAnalysisPosition pos, CachedPosition hit)
    {
        pos.CandidatesJson = hit.CandidatesJson;
        pos.EvalText = hit.EvalText;
        pos.Depth = hit.Depth;
        pos.AnalyzedAt = DateTime.UtcNow;
    }
}
