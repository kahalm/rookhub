using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using RookHub.Api.Services.Library;
using UglyToad.PdfPig;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Ergebnis der Spracherkennung eines Buchs: <see cref="Language"/> nur bei eindeutigem Text.</summary>
/// <param name="Source"><c>text</c> (erkannt), <c>unclear</c> (gemischt/zu wenig eindeutig) oder <c>notext</c> (kein Text).</param>
public sealed record EbookLanguageResult(string? Language, string Source)
{
    public static readonly EbookLanguageResult NoText = new(null, EbookLanguageDetector.SourceNoText);
    public static readonly EbookLanguageResult Unclear = new(null, EbookLanguageDetector.SourceUnclear);
}

/// <summary>
/// In welcher Sprache ist ein BUCH geschrieben (0.876.0, Grundlage der Übersetzungs-Reihenfolge)? Entschieden wird am Text
/// selbst — Funktionswörter wie bei den Partie-Kommentaren (<see cref="CommentLanguage"/>), Kyrillisch an der Schrift, dazu
/// eine eigene Liste fürs Kroatische (die Kommentar-Listen kennen es nicht, es läse sich sonst als Tschechisch).
/// <c>dc:language</c> aus dem OPF ist nur ein HINWEIS: viele EPUBs tragen dort „en“ als Vorgabe des Werkzeugs, auch wenn
/// das Buch spanisch ist; er entscheidet nur, wenn der Text zwischen ihm und einer zweiten Sprache schwankt.
///
/// <para>Die Probe ist über das GANZE Buch verteilt (jeder n-te Absatz, kurze Zeilen wie Züge und Überschriften fallen
/// weg) — Vorwort und Impressum stehen oft in einer anderen Sprache als der Rest. Gemischt (zweitbeste Sprache mit mehr als
/// <see cref="MixedShare"/> der Treffer der besten) oder zu wenig Treffer → <c>unclear</c>, das Buch wird nicht übersetzt.</para>
/// </summary>
public static class EbookLanguageDetector
{
    public const string SourceText = "text";
    public const string SourceManual = "manual";
    public const string SourceUnclear = "unclear";
    public const string SourceNoText = "notext";

    /// <summary>So viele Zeichen Probe — reichlich für Funktionswörter, klein genug für einen schnellen Durchlauf.</summary>
    public const int SampleChars = 60_000;

    /// <summary>Absätze unter dieser Länge zählen nicht (Züge, Überschriften, Diagrammnummern).</summary>
    public const int MinParagraphChars = 60;

    /// <summary>Unter so vielen Zeichen Prosa gilt ein Buch als ohne Text (Scan, reines Aufgabenheft).</summary>
    public const int MinTextChars = 2_000;

    /// <summary>Mindestzahl Funktionswort-Treffer der besten Sprache.</summary>
    public const int MinHits = 40;

    /// <summary>Hat die zweitbeste Sprache mehr als diesen Anteil der Treffer der besten, ist der Text gemischt.</summary>
    public const double MixedShare = 0.45;

    /// <summary>Kroatisch (fehlt in <see cref="CommentLanguage"/>): Wörter, die es im Tschechischen/Slowakischen so nicht gibt.</summary>
    private static readonly string[] Croatian =
    [
        "ali", "koji", "koja", "koje", "nije", "bijeli", "crni", "potez", "pozicija", "ako", "sto", "biti", "nakon",
        "vrlo", "takoder", "mora", "moze", "samo", "kako", "ovaj", "ima", "jer", "nego", "bijelih", "crnih", "kralj",
        "su", "smo", "bio", "bila", "kad", "kada", "gdje", "zbog", "prema", "dakle", "onda", "kroz", "jedan", "drugi",
        "dobro", "bolje", "bolju", "lovac", "skakac", "pjesak", "sah", "igra", "igrati", "polje", "sada", "tako", "ovog", "ovo",
    ];

    /// <summary>Sprache aus Absätzen (in Lesereihenfolge) und dem Hinweis aus dem OPF.</summary>
    public static EbookLanguageResult Detect(IReadOnlyList<string> paragraphs, string? hint = null)
    {
        var sample = Sample(paragraphs);
        if (sample.Length < MinTextChars) return EbookLanguageResult.NoText;

        var letters = 0;
        var cyrillic = 0;
        foreach (var c in sample)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (c is >= 'Ѐ' and <= 'ӿ') cyrillic++;
        }
        if (letters > 0 && cyrillic > letters * 0.3)
            return cyrillic > letters * 0.6 ? new EbookLanguageResult(CyrillicLanguage(sample), SourceText) : EbookLanguageResult.Unclear;

        var ranked = Rank(sample);
        if (ranked.Count == 0 || ranked[0].Hits < MinHits) return EbookLanguageResult.Unclear;
        var best = ranked[0];
        var second = ranked.Count > 1 ? ranked[1] : (Lang: "", Hits: 0);
        if (second.Hits <= best.Hits * MixedShare) return new EbookLanguageResult(best.Lang, SourceText);
        // Zwei Sprachen nah beieinander: der Hinweis aus dem OPF entscheidet, wenn er eine der beiden ist.
        var h = NormalizeHint(hint);
        if (h != null && (h == best.Lang || h == second.Lang) && second.Hits <= best.Hits * 0.8)
            return new EbookLanguageResult(h, SourceText);
        return EbookLanguageResult.Unclear;
    }

    /// <summary>Russisch, außer die Probe trägt eindeutig ukrainische/bulgarische/serbische Buchstaben.</summary>
    private static string CyrillicLanguage(string sample)
    {
        int Count(params char[] cs) => sample.Count(c => Array.IndexOf(cs, char.ToLowerInvariant(c)) >= 0);
        var total = sample.Length;
        if (Count('і', 'ї', 'є', 'ґ') * 200 > total) return "uk";
        if (Count('ђ', 'ћ', 'џ', 'љ', 'њ', 'ј') * 300 > total) return "sr";
        if (Count('ы', 'э') == 0 && Count('ъ') * 200 > total) return "bg";
        return "ru";
    }

    /// <summary>„en-US“ / „ger“ / „Deutsch“ → <c>en</c>/<c>de</c>; unbekannt → <c>null</c>.</summary>
    public static string? NormalizeHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;
        var h = hint.Trim().ToLowerInvariant();
        var dash = h.IndexOfAny(['-', '_']);
        if (dash > 0) h = h[..dash];
        return h switch
        {
            "ger" or "deu" or "deutsch" or "german" => "de",
            "eng" or "english" => "en",
            "spa" or "esp" or "spanish" => "es",
            "fre" or "fra" or "french" => "fr",
            "rus" or "russian" => "ru",
            _ => h.Length == 2 && h.All(char.IsAsciiLetterLower) ? h : null,
        };
    }

    /// <summary>Jeder n-te längere Absatz, bis <see cref="SampleChars"/> — über das ganze Buch verteilt.</summary>
    internal static string Sample(IReadOnlyList<string> paragraphs)
    {
        var prose = paragraphs.Where(p => p.Length >= MinParagraphChars).ToList();
        var total = prose.Sum(p => (long)p.Length);
        var step = Math.Max(1, (int)(total / SampleChars) + 1);
        var sb = new StringBuilder();
        for (var i = 0; i < prose.Count && sb.Length < SampleChars; i += step)
            sb.Append(prose[i]).Append('\n');
        return sb.ToString();
    }

    /// <summary>Treffer je Sprache: die Kommentar-Listen plus Kroatisch.</summary>
    internal static List<(string Lang, int Hits)> Rank(string text)
    {
        var ranked = CommentLanguage.Rank(text).ToList();
        var hr = Words(text).Count(w => Array.IndexOf(Croatian, w) >= 0);
        if (hr > 0) ranked.Add(("hr", hr));
        return ranked.OrderByDescending(r => r.Hits).ThenBy(r => r.Lang, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> Words(string text)
    {
        var sb = new StringBuilder(24);
        foreach (var raw in text)
        {
            var c = raw == 'đ' || raw == 'Đ' ? 'd' : CommentLanguage.FoldChar(raw);
            if (c is >= 'a' and <= 'z') { sb.Append(c); continue; }
            if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    // ── Proben aus Dateien ──────────────────────────────────────────────────────────────────────

    /// <summary>Absätze + <c>dc:language</c> eines EPUB.</summary>
    public static (List<string> Paragraphs, string? Hint) ReadEpub(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var paragraphs = EbookEpubContent.Read(zip).Where(b => b.Text != null).Select(b => b.Text!).ToList();
        return (paragraphs, OpfLanguage(zip));
    }

    /// <summary><c>dc:language</c> aus dem OPF (erstes Vorkommen), sonst <c>null</c>.</summary>
    public static string? OpfLanguage(ZipArchive zip)
    {
        try
        {
            var container = zip.GetEntry("META-INF/container.xml");
            if (container == null) return null;
            XDocument c;
            using (var s = container.Open()) c = XDocument.Load(s);
            var opfPath = c.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
            var opf = opfPath == null ? null : zip.GetEntry(opfPath);
            if (opf == null) return null;
            XDocument o;
            using (var s = opf.Open()) o = XDocument.Load(s);
            return o.Descendants().FirstOrDefault(e => e.Name.LocalName == "language")?.Value;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException) { return null; }
    }

    /// <summary>Textprobe eines PDF: bis zu 40 Seiten, gleichmäßig über das Buch (die ersten 5 % übersprungen).</summary>
    public static List<string> ReadPdf(string path, CancellationToken ct = default)
    {
        var result = new List<string>();
        using var doc = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        var pages = doc.NumberOfPages;
        if (pages == 0) return result;
        var first = Math.Max(1, pages / 20);
        var step = Math.Max(1, (pages - first) / 40);
        for (var p = first; p <= pages && result.Count < 40; p += step)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var text = string.Join(' ', doc.GetPage(p).GetWords().Select(w => w.Text));
                if (text.Length > 0) result.Add(text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* eine kaputte Seite */ }
        }
        return result;
    }
}
