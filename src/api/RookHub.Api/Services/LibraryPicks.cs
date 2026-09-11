using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Welche Partien des Rohbestands soll die Engine als naechstes rechnen?"
///
/// <para>Die Eignungsnote (<see cref="LibraryGame.Score"/>) allein beantwortet das nicht: an der
/// Spitze stehen tausende Partien mit 100, und wer dann nach der Zeilennummer sortiert, bekommt
/// den Bestand in der Reihenfolge, in der er eingelesen wurde — also alphabetisch nach
/// Kommentator. Gemessen am echten Bestand (2026-09-11) trugen die ersten 25 Treffer 20
/// verschiedene Partien von Annotatoren mit „A".</para>
///
/// <para>Entschieden wird deshalb in dieser Reihenfolge: Note, dann die Zahl der kommentierten
/// HALBZUEGE (die Punktepartie haelt an kommentierten Zuegen an — drei lange Absaetze am Schluss
/// machen sie stumm), dann die Textmenge. Und je Kommentator hoechstens
/// <paramref name="maxPerAnnotator"/> Partien: die Spitze enthaelt sechs Partien desselben
/// Grossmeisters ueber sich selbst, und zwanzig davon waeren eine Auswahl ueber einen Menschen,
/// nicht ueber den Bestand.</para>
/// </summary>
public static class LibraryPicks
{
    /// <summary>Hoechstens so viele Partien je Kommentator, solange genug andere da sind.</summary>
    public const int DefaultPerAnnotator = 2;

    /// <summary>
    /// Die besten <paramref name="count"/> aus <paramref name="candidates"/>.
    /// <para>Der Deckel je Kommentator ist eine VORZUGSregel, keine Ausschlussregel: reicht der
    /// Rest nicht, wird mit den zurueckgestellten Partien in derselben Reihenfolge aufgefuellt —
    /// sonst laege bei einem kleinen Bestand (oder einem Deckel von 1) weniger in der
    /// Warteschlange, als verlangt wurde, ohne dass jemand erfaehrt warum.</para>
    /// </summary>
    public static List<LibraryGame> Best(IEnumerable<LibraryGame> candidates, int count,
        int maxPerAnnotator = DefaultPerAnnotator)
    {
        if (count <= 0) return [];

        var ordered = candidates
            .OrderByDescending(g => g.Score ?? 0)
            .ThenByDescending(g => g.CommentedPlies ?? 0)
            .ThenByDescending(g => g.CommentChars ?? 0)
            .ThenBy(g => g.Id)
            .ToList();

        var picked = new List<LibraryGame>(count);
        var held = new List<LibraryGame>();
        var perAnnotator = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var game in ordered)
        {
            if (picked.Count >= count) break;
            var annotator = game.Annotator?.Trim() ?? "";
            // Ohne Kommentator-Angabe gibt es nichts zu haeufen — solche Zeilen laufen am Deckel vorbei.
            if (annotator.Length > 0 && maxPerAnnotator > 0)
            {
                perAnnotator.TryGetValue(annotator, out var seen);
                if (seen >= maxPerAnnotator) { held.Add(game); continue; }
                perAnnotator[annotator] = seen + 1;
            }
            picked.Add(game);
        }

        foreach (var game in held)
        {
            if (picked.Count >= count) break;
            picked.Add(game);
        }
        return picked;
    }
}
