using System.Text.Json;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.DTOs.Puzzles;
using RookHub.Api.Services.Puzzles;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Vermerk „Warum diese Aufgabe“ einer Linie im Kurs „Persönliche Taktiken“ (1.001.0, Wunsch 2026-11-22: „kannst du dort immer
/// vermerken, warum du glaubst, dass die Linie relevant ist?“). Rein und deterministisch aus den Daten der Empfohlenen Übungen
/// (<see cref="GameExercises"/>): die Motivstelle der Partie (Zug, Seite, Grund, Thema/Mattlänge, Figuren des Motivs) und die Art
/// des Treffers (<c>strict</c>/<c>family</c>/<c>theme</c>, Buchdiagramm oder Lichess) — nichts wird erfunden.
///
/// <para><b>Sprachneutral gespeichert</b> (<c>BookPuzzle.Why</c>, kompaktes JSON): das Frontend baut den Satz in der UI-Sprache
/// (<c>book.why.*</c>), so geht er in allen Sprachen und folgt einem Sprachwechsel. Kaputtes JSON liest sich als „kein Vermerk“.</para>
/// </summary>
public static class PersonalTacticsWhy
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Höchstens so viele weitere Stellen nennt der Vermerk (das Feld ist begrenzt).</summary>
    public const int MaxAlso = 8;

    /// <summary>Motive, deren strenge Signatur Figuren nennt, die der Satz ausschreibt (<c>fork|N|KQ</c>, <c>pin|B|N&gt;K</c>, <c>skewer|R|K&gt;Q</c>).</summary>
    private static readonly HashSet<string> PieceMotifs = ["fork", "pin", "skewer"];

    /// <summary>Vermerk für die Aufgabe <paramref name="e"/> der Stelle <paramref name="g"/>.</summary>
    public static BookPuzzleWhyDto Build(GameExerciseGroupDto g, GameExerciseDto e)
    {
        // „Zugelassen“: der Satz nennt den Fehler davor (der Zug, der die Taktik des Gegners zuließ), sonst den Zug an der Stelle.
        var allowed = g.Reason == GameExercises.AllowedTactic && g.Ply > 0;
        var why = new BookPuzzleWhyDto
        {
            Ply = g.Ply, White = g.White, Reason = g.Reason,
            MovePly = allowed ? g.Ply - 1 : g.Ply,
            MoveSan = allowed ? g.PrevMoveSan : g.MoveSan,
            Theme = g.Theme, MateIn = g.MateIn,
            Match = e.Match, Book = e.Book is not null,
            Also = g.AlsoPlies is { Count: > 0 } also ? also.Take(MaxAlso).ToList() : null,
        };
        if (e.Match == "theme")
            why.MatchTheme = g.Theme is { } t && (g.MateIn is null || e.Themes.Contains(t)) ? t
                : g.MateIn is int m ? $"mateIn{Math.Clamp(m, 1, GameExercises.MaxMateIn)}" : g.Theme;
        // Figuren nur bei gleicher strenger Signatur — bei „family“ hat die Aufgabe dasselbe Motiv mit ANDEREN Figuren.
        if (e.Match == "strict" && g.Motif?.Split('|') is { Length: 3 } parts && PieceMotifs.Contains(parts[0])
            && parts[1].Length == 1 && "KQRBNP".Contains(parts[1][0]))
        {
            var targets = parts[2].Replace(">", "", StringComparison.Ordinal);
            if (targets.Length > 0 && targets.All(c => "KQRBNP".Contains(c)))
            {
                why.Piece = parts[1];
                why.Targets = targets;
            }
        }
        return why;
    }

    public static string Serialize(BookPuzzleWhyDto why) => JsonSerializer.Serialize(why, Json);

    /// <summary>Gespeicherter Vermerk; <c>null</c> = keiner oder unlesbar.</summary>
    public static BookPuzzleWhyDto? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var why = JsonSerializer.Deserialize<BookPuzzleWhyDto>(json, Json);
            return why is { Reason.Length: > 0, Match.Length: > 0 } ? why : null;
        }
        catch (JsonException) { return null; }
    }
}
