using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// „Ergänzen statt ersetzen“ (0.811.0): führt eine neue Erkennung mit den vorhandenen Diagrammen eines Buchs zusammen.
/// Vorhandene Diagramme bleiben UNVERÄNDERT (FEN, Familie, Lösungen, Ids) — einzige Ausnahme: EPUB-Diagrammen ohne Höhe
/// (Y = 0, vor 0.811.0 erkannt) wird die Blockreihenfolge aus der neuen Erkennung nachgetragen. Ein neu erkanntes Diagramm
/// kommt nur dazu, wenn an seiner Stelle noch keines liegt:
/// <list type="bullet">
/// <item>mit Bildpfad (EPUB oder mit dem EPUB verknüpftes PDF-Diagramm): gleicher <see cref="EbookDiagram.ImageRef"/>, oder</item>
/// <item>PDF/Scan (mit Lage): gleiche Seite und deutliche Überlappung (IoU &gt; <see cref="MinIou"/> oder der Mittelpunkt
/// liegt in einer vorhandenen Box);</item>
/// <item>EPUB ohne Bildpfad (Pfad zu lang): gleiches Kapitel und gleiche Platzierung.</item>
/// </list>
/// </summary>
public static class EbookDiagramMerge
{
    public const double MinIou = 0.4;

    public sealed record Outcome(List<EbookDiagram> Added, int YFilled);

    /// <summary>Welche der <paramref name="found"/> neu sind (Rückgabe, noch nicht in der DB) und wie vielen
    /// <paramref name="existing"/> Y nachgetragen wurde (direkt an den Objekten geändert).</summary>
    public static Outcome Merge(IReadOnlyList<EbookDiagram> existing, IReadOnlyList<EbookDiagram> found)
    {
        // Y nachtragen: EPUB-Diagramme (keine Lage) mit Y = 0, deren Bild die neue Erkennung wiederfindet.
        var yByRef = new Dictionary<(int, string), double>();
        foreach (var f in found)
            if (f.W <= 0 && f.ImageRef != null) yByRef.TryAdd((f.Page, f.ImageRef), f.Y);
        var filled = 0;
        foreach (var e in existing)
        {
            if (e.W > 0 || e.Y != 0 || e.ImageRef == null) continue;
            if (yByRef.TryGetValue((e.Page, e.ImageRef), out var y) && y != 0) { e.Y = y; filled++; }
        }

        var refs = existing.Where(e => e.ImageRef != null).Select(e => e.ImageRef!).ToHashSet(StringComparer.Ordinal);
        var boxes = existing.Where(e => e.W > 0 && e.H > 0).GroupBy(e => e.Page).ToDictionary(g => g.Key, g => g.ToList());
        var epubKeys = existing.Where(e => e.W <= 0).Select(e => (e.Page, Placement(e.Fen))).ToHashSet();
        var added = new List<EbookDiagram>();
        foreach (var f in found)
        {
            var hasBox = f.W > 0 && f.H > 0;
            var taken = (f.ImageRef != null && refs.Contains(f.ImageRef))
                || (hasBox && boxes.TryGetValue(f.Page, out var onPage) && onPage.Any(e => SamePlace(e, f)))
                || (!hasBox && f.ImageRef == null && epubKeys.Contains((f.Page, Placement(f.Fen))));
            if (taken) continue;
            added.Add(f);
            // Auch innerhalb der neuen Erkennung nichts doppelt anlegen.
            if (f.ImageRef != null) refs.Add(f.ImageRef);
            if (hasBox)
            {
                if (!boxes.TryGetValue(f.Page, out var list)) boxes[f.Page] = list = new();
                list.Add(f);
            }
            else epubKeys.Add((f.Page, Placement(f.Fen)));
        }
        return new Outcome(added, filled);
    }

    /// <summary>Zwei Boxen (Seitenanteile) zeigen dasselbe Brett: IoU &gt; <see cref="MinIou"/> oder ein Mittelpunkt liegt in
    /// der anderen Box.</summary>
    public static bool SamePlace(EbookDiagram a, EbookDiagram b)
    {
        double ix = Math.Max(0, Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X));
        double iy = Math.Max(0, Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y));
        var inter = ix * iy;
        var union = a.W * a.H + b.W * b.H - inter;
        if (union > 0 && inter / union > MinIou) return true;
        return Inside(a, b.X + b.W / 2, b.Y + b.H / 2) || Inside(b, a.X + a.W / 2, a.Y + a.H / 2);
    }

    private static bool Inside(EbookDiagram d, double x, double y) => x >= d.X && x <= d.X + d.W && y >= d.Y && y <= d.Y + d.H;

    private static string Placement(string fen) => fen.Split(' ')[0];
}
