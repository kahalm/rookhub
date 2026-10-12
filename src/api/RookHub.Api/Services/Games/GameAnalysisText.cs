using System.Text;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Texte und Kennzeilen einer Partie-Analyse (Titel, Knoten-Kürzel, Eröffnungszeile, Endstellung, Auftragstitel) —
/// aus <see cref="GameAnalysisService"/> herausgezogen (Hausregel „große Dateien beim Anfassen verkleinern"),
/// Verhalten unverändert.
/// </summary>
internal static class GameAnalysisText
{
    /// <summary>Knotenzahl kurz für den Titel: 50000 → „50k", 1500000 → „1.5M", sonst die Zahl.</summary>
    internal static string NodesLabel(long nodes)
    {
        string Trim(double v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return nodes >= 1_000_000 ? Trim(nodes / 1_000_000.0) + "M"
            : nodes >= 1_000 ? Trim(nodes / 1_000.0) + "k"
            : nodes.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Die ersten Halbzuege als normalisierte Zeile („e4 e5 Nf3 Nc6") — dieselbe Form wie
    /// <see cref="LibraryGame.OpeningLine"/>, damit der Stellungsfilter beide Quellen gleich
    /// behandeln kann. Bewertungszeichen und Schach-Zeichen fallen weg: „Nf3+" und „Nf3" sind
    /// derselbe Zug, und ein Baum, der sie trennt, hat zwei Aeste fuer eine Stellung.
    /// </summary>
    internal static string OpeningLineOf(IReadOnlyList<GamePlies.Ply> plies)
    {
        var sb = new StringBuilder(LibraryGameReader.OpeningPlies * 6);
        foreach (var p in plies.Take(LibraryGameReader.OpeningPlies))
        {
            if (sb.Length > 0) sb.Append(' ');
            // DIESELBE Funktion wie der Textweg der Bibliothek (LibraryGameReader.AppendOpeningSan):
            // die beiden Spalten werden vom selben Praefix durchsucht, ihre Form darf nicht an zwei
            // Stellen entstehen. Bis 0.499.12 stand hier eine zweite Fassung — sie entfernte die
            // Schachzeichen, die der Textweg behielt.
            LibraryGameReader.AppendOpeningSan(sb, p.San);
        }
        var line = sb.ToString();
        return line.Length > 200 ? line[..200] : line;
    }

    /// <summary>
    /// Titel aus den Kopfdaten: „Weiß – Schwarz". PGN-Platzhalter („?", „??", „-") gelten als fehlend
    /// (v1-rk-analysis-pgn-placeholder-title: vorher hieß die Analyse „? – ?"). Fehlt ein Name, steht dort „?" —
    /// das Frontend zeigt „ohne Name"; fehlen beide, gilt das Turnier, sonst bleibt der Titel leer („Ohne Titel"
    /// in der Oberflächensprache statt des deutschen Festwerts „Partie").
    /// </summary>
    internal static string BuildTitle(GamePlies.GameHeader h) => BuildTitle(h.White, h.Black, h.Event);

    internal static string BuildTitle(string? white, string? black, string? evt)
    {
        var w = IsPgnPlaceholder(white) ? null : white!.Trim();
        var b = IsPgnPlaceholder(black) ? null : black!.Trim();
        if (w is not null || b is not null) return $"{w ?? "?"} – {b ?? "?"}";
        return IsPgnPlaceholder(evt) ? string.Empty : evt!.Trim();
    }

    /// <summary>Leer oder ein PGN-Platzhalter für „unbekannt" („?", „??", „-").</summary>
    internal static bool IsPgnPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();
        return v == "-" || v.All(c => c == '?');
    }

    /// <summary>Die Stellung nach dem letzten Zug — <c>null</c> bei Matt/Patt oder wenn sich der Zug nicht spielen lässt.</summary>
    internal static string? FinalPositionOf(GamePlies.Ply last)
    {
        var fen = MoveComparisonService.PlayUci(last.Fen, last.Uci);
        if (fen is null) return null;
        try { return Chess.ChessBoard.LoadFromFen(fen).Moves().Length > 0 ? fen : null; }
        catch { return null; }
    }

    internal static string JobTitle(GameAnalysis analysis, GameAnalysisPosition pos)
    {
        var moveNo = pos.Ply / 2 + 1;
        var side = pos.Ply % 2 == 0 ? "w" : "s";
        var title = $"{analysis.Title} · {moveNo}{side} {pos.GameMoveSan}";
        return title.Length > 200 ? title[..200] : title;
    }
}
