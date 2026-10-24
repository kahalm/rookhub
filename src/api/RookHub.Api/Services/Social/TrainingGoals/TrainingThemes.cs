using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>Theme-Klassifikation (best-effort) der Trainingszeit — reine Funktionen.</summary>
internal static class TrainingThemes
{
    /// <summary>Erkennt eine Partiephase aus freiem Text (Lichess-Themes, BookPuzzle-Tags/Chapter).
    /// Reihenfolge Endspiel → Mittelspiel → Eröffnung (Lichess <c>rookEndgame</c> etc. matchen „endgame").
    /// Gibt null zurück, wenn kein Phasen-Signal gefunden wurde.</summary>
    public static TrainingTheme? PhaseFromText(params string?[] texts)
    {
        foreach (var raw in texts)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var t = raw.ToLowerInvariant();
            // Freitext-Heuristik über mehrere Sprachen (en/de/es/hr) — die App läuft mit de/en/hr,
            // und es wurden ES-Inhalte importiert. HINWEIS: der robuste Fix wäre ein strukturiertes
            // Phasen-Feld am Kurs/Repertoire statt Namens-Sniffing; bis dahin die geläufigen Begriffe.
            if (t.Contains("endgame") || t.Contains("endspiel") || t.Contains("finales") || t.Contains("završnic") || t.Contains("zavrsnic")) return TrainingTheme.Endgame;
            if (t.Contains("middlegame") || t.Contains("middle game") || t.Contains("mittelspiel") || t.Contains("medio juego") || t.Contains("srednjica")) return TrainingTheme.Middlegame;
            if (t.Contains("opening") || t.Contains("eröffnung") || t.Contains("eroeffnung") || t.Contains("apertura") || t.Contains("otvaranj")) return TrainingTheme.Opening;
        }
        return null;
    }

    public static TrainingTheme FromCourseKind(RepertoireKind? k) => k switch
    {
        RepertoireKind.Opening => TrainingTheme.Opening,
        RepertoireKind.Middlegame => TrainingTheme.Middlegame,
        RepertoireKind.Endgame => TrainingTheme.Endgame,
        _ => TrainingTheme.Other,
    };

    public static TrainingTheme FromChessable(ChessableTheme t) => t switch
    {
        ChessableTheme.Opening => TrainingTheme.Opening,
        ChessableTheme.Middlegame => TrainingTheme.Middlegame,
        ChessableTheme.Endgame => TrainingTheme.Endgame,
        ChessableTheme.Tactics => TrainingTheme.Tactics,
        _ => TrainingTheme.Other,
    };

    /// <summary>Parst die CSV-Themen-Tags eines Kurs-Buchs (<see cref="Models.Book.Themes"/>) zur Themen-Liste
    /// (Reihenfolge stabil, dedupliziert). Unset/leer/nur-ungültig → Default <c>[Taktik]</c> — jedes Buch
    /// ist standardmäßig Taktik.</summary>
    public static List<TrainingTheme> BookThemes(string? csv) =>
        // Keys/Dedupe/Default zentral in BookThemeTags (geteilt mit CourseService) — hier nur
        // noch das Mapping Key → Thema fürs Zeit-Routing.
        BookThemeTags.ParseKeys(csv).Select(k => k switch
        {
            "opening" => TrainingTheme.Opening,
            "middlegame" => TrainingTheme.Middlegame,
            "endgame" => TrainingTheme.Endgame,
            "tactics" => TrainingTheme.Tactics,
            _ => TrainingTheme.Other,
        }).ToList();

    /// <summary>API-Name eines manuell zugeordneten Chessable-Themas.</summary>
    public static string Name(ChessableTheme t) => t switch
    {
        ChessableTheme.Opening => "opening",
        ChessableTheme.Middlegame => "middlegame",
        ChessableTheme.Endgame => "endgame",
        ChessableTheme.Tactics => "tactics",
        _ => "opening",
    };
}
