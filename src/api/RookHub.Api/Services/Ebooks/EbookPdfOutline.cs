using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Tokens;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Liest die Lesezeichen (Gliederung) eines PDF ohne Absturzgefahr (G2-005). PdfPig liest die Gliederung rekursiv; eine
/// hochgeladene Datei mit zehntausenden verschachtelten Ebenen ließ den Stapel überlaufen, und ein .NET-Stapelüberlauf
/// beendet den ganzen API-Prozess (nicht fangbar). Deshalb drei Schutzschichten:
/// <list type="number">
/// <item>Vorprüfung auf den rohen PDF-Objekten, ohne Rekursion: ist die Gliederung tiefer als <see cref="MaxRawDepth"/>
/// oder größer als <see cref="MaxRawNodes"/>, gibt es keine Lesezeichen (PdfPig wird gar nicht erst gefragt).</item>
/// <item>PdfPig liest in einem eigenen Thread mit großem Stapel (<see cref="StackBytes"/>); jeder Fehler = keine
/// Lesezeichen.</item>
/// <item><see cref="Flatten"/> läuft ohne Rekursion und übernimmt nur <see cref="MaxDepth"/> Ebenen und höchstens
/// <see cref="MaxNodes"/> Einträge.</item>
/// </list>
/// </summary>
public static class EbookPdfOutline
{
    /// <summary>Tiefste übernommene Ebene (1 = oberste); tiefere Einträge samt Unterbaum fallen weg.</summary>
    public const int MaxDepth = 64;
    /// <summary>Höchstens so viele Einträge werden übernommen.</summary>
    public const int MaxNodes = 10_000;
    /// <summary>Rohe Gliederung tiefer als das → keine Lesezeichen (kein echtes Buch ist so tief).</summary>
    internal const int MaxRawDepth = 1_000;
    /// <summary>Rohe Gliederung mit mehr Knoten als das → keine Lesezeichen.</summary>
    internal const int MaxRawNodes = 200_000;
    /// <summary>Stapel des Lese-Threads; reicht für PdfPig weit über <see cref="MaxRawDepth"/> hinaus.</summary>
    internal const int StackBytes = 64 * 1024 * 1024;

    /// <summary>Die übernommenen Lesezeichen in Lesereihenfolge (Tiefensuche) mit ihrer Ebene (1 = oberste).</summary>
    public readonly record struct Item(BookmarkNode Node, int Depth);

    /// <summary>Lesezeichen von <paramref name="doc"/>, gedeckelt; leer ohne Gliederung, bei zu tiefer/großer Gliederung
    /// oder bei jedem Lesefehler.</summary>
    public static IReadOnlyList<Item> Read(PdfDocument doc)
    {
        IReadOnlyList<Item> result = [];
        var thread = new Thread(() =>
        {
            try
            {
                if (RawOutlineTooLarge(doc)) return;
                if (doc.TryGetBookmarks(out var bookmarks)) result = Flatten(bookmarks.Roots);
            }
            catch (Exception) { /* kaputte Lesezeichen: ohne Gliederung weiter */ }
        }, StackBytes) { IsBackground = true, Name = "pdf-outline" };
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>Baum → Liste in Lesereihenfolge, ohne Rekursion; nur bis <paramref name="maxDepth"/> Ebenen und
    /// <paramref name="maxNodes"/> Einträge.</summary>
    public static IReadOnlyList<Item> Flatten(IReadOnlyList<BookmarkNode> roots, int maxDepth = MaxDepth, int maxNodes = MaxNodes)
    {
        var list = new List<Item>();
        var stack = new Stack<Item>();
        for (var i = roots.Count - 1; i >= 0; i--) stack.Push(new Item(roots[i], 1));
        while (stack.Count > 0 && list.Count < maxNodes)
        {
            var it = stack.Pop();
            list.Add(it);
            if (it.Depth >= maxDepth) continue;
            var kids = it.Node.Children;
            for (var i = kids.Count - 1; i >= 0; i--) stack.Push(new Item(kids[i], it.Depth + 1));
        }
        return list;
    }

    private static readonly NameToken OutlinesName = NameToken.Create("Outlines");
    private static readonly NameToken FirstName = NameToken.Create("First");
    private static readonly NameToken NextName = NameToken.Create("Next");

    /// <summary>Misst die Gliederung auf den rohen Objekten (/Outlines → /First → /Next), ohne Rekursion und ohne Ring
    /// (besuchte Objekte zählen einmal). <c>true</c> = tiefer als <see cref="MaxRawDepth"/> oder mehr als
    /// <see cref="MaxRawNodes"/> Knoten.</summary>
    internal static bool RawOutlineTooLarge(PdfDocument doc)
    {
        if (!doc.Structure.Catalog.CatalogDictionary.TryGet(OutlinesName, out var outlinesToken)
            || Resolve(doc, outlinesToken) is not DictionaryToken outlines
            || !outlines.TryGet(FirstName, out var first)) return false;
        var seen = new HashSet<IndirectReference>();
        var stack = new Stack<(IToken Token, int Depth)>();
        stack.Push((first, 1));
        var nodes = 0;
        while (stack.Count > 0)
        {
            var (token, depth) = stack.Pop();
            if (depth > MaxRawDepth) return true;
            // Geschwister über /Next ablaufen, Kinder (/First) eine Ebene tiefer auf den Stapel.
            for (IToken? cur = token; cur != null;)
            {
                if (cur is IndirectReferenceToken r && !seen.Add(r.Data)) break;
                if (++nodes > MaxRawNodes) return true;
                if (Resolve(doc, cur) is not DictionaryToken d) break;
                if (d.TryGet(FirstName, out var child)) stack.Push((child, depth + 1));
                cur = d.TryGet(NextName, out var next) ? next : null;
            }
        }
        return false;
    }

    private static IToken? Resolve(PdfDocument doc, IToken token)
    {
        for (var hops = 0; token is IndirectReferenceToken r; hops++)
        {
            if (hops > 8) return null;
            token = doc.Structure.GetObject(r.Data)?.Data!;
        }
        return token;
    }
}
