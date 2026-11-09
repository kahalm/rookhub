using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Engines;

/// <summary>Eine Fundstelle eines Merksatzes in der E-Book-Bibliothek: Buch, Stelle (PDF-Seite bzw. Anteil im EPUB), Zitat.</summary>
public sealed record ChessMaximSource(int EbookId, string? Author, string? Title, string? Year, string? Location, int? Page,
    int? Percent, string? Quote);

/// <summary>Ein Merksatz der kuratierten Liste: feste Kennung, fester Wortlaut, wann er passt, Belege. Die Felder <c>…En</c> sind die
/// englische Fassung für die Anzeige; Modell-Auftrag und gespeicherte Erklärungen bleiben beim deutschen Wortlaut.</summary>
public sealed record ChessMaxim(string Id, string Text, string Category, string When, string TextEn, string CategoryEn, string WhenEn,
    IReadOnlyList<ChessMaximSource> Sources);

/// <summary>
/// Die vom User freigegebene Liste der Merksätze (0.917.0, Wunsch 2026-11-09: „nicht selbst erfinden die Merksätze, sondern aus
/// der Literatur holen“, „sie sollten immer gleich sein“). Jeder Satz ist mit Zitaten aus der E-Book-Bibliothek belegt;
/// Quelle und Werkzeuge: <c>~/claude/merksaetze/</c> bzw. <c>/srv/media/claude-sessions/merksaetze/</c>, das Asset
/// <c>Assets/chess-maxims.json</c> erzeugt <c>export_rookhub.py</c>. Fable wählt höchstens einen per Kennung
/// (<see cref="ClubFableExplanations"/>), gespeichert und angezeigt wird immer der Wortlaut von hier.
/// </summary>
public static class ChessMaxims
{
    /// <summary>Präfix der Merksatz-Zeile am Ende einer Begründung.</summary>
    public const string LinePrefix = "Merksatz:";

    private static readonly Lazy<IReadOnlyList<ChessMaxim>> _all = new(Load);

    public static IReadOnlyList<ChessMaxim> All => _all.Value;

    public static ChessMaxim? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(m => string.Equals(m.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ChessMaxim> Load()
    {
        using var s = typeof(ChessMaxims).Assembly.GetManifestResourceStream("RookHub.Api.Assets.chess-maxims.json")
            ?? throw new InvalidOperationException("Asset chess-maxims.json fehlt");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
        return JsonSerializer.Deserialize<List<ChessMaxim>>(s, opts) ?? [];
    }

    /// <summary>Die Liste für den Auftrag an das Modell: je Zeile Kennung, Wortlaut und wann er passt.</summary>
    public static string PromptList() =>
        string.Join('\n', All.Select(m => $"- {m.Id}: „{m.Text}“ — passt, wenn: {m.When}"));

    private static readonly Regex Line = new(@"^\s*\**\s*Merksatz\s*\**\s*:\s*\**\s*(?<v>.*?)\s*\**\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Die Merksatz-Zeile(n) der Modell-Antwort auf den festen Wortlaut bringen: steht dort eine bekannte Kennung (oder schon
    /// der genaue Wortlaut), wird sie zu „Merksatz: &lt;Wortlaut&gt;“; alles andere — ein frei erfundener Satz, eine unbekannte
    /// Kennung, „keiner“ — fällt weg. Höchstens ein Merksatz bleibt, am Ende.
    /// </summary>
    public static string Canonicalize(string text)
    {
        ChessMaxim? found = null;
        var body = Line.Replace(text, m =>
        {
            var v = m.Groups["v"].Value.Trim().Trim('„', '“', '"', '`', '.', ' ');
            found ??= ById(v) ?? All.FirstOrDefault(x => string.Equals(x.Text.TrimEnd('.'), v, StringComparison.Ordinal));
            return "";
        });
        body = Regex.Replace(body.TrimEnd(), @"\n{3,}", "\n\n");
        return found is null ? body : body + "\n\n" + LinePrefix + " " + found.Text;
    }
}
