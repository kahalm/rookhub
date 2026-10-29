using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Ein übersetzbarer Block eines EPUB-Dokuments (0.876.0): Absatz, Überschrift, Listenpunkt, Tabellenzelle …
/// <see cref="ModelText"/> geht ans Modell — der Text mit Platzhaltern <c>&lt;tN&gt;…&lt;/tN&gt;</c> (umschließende Formatierung)
/// und <c>&lt;tN/&gt;</c> (alles andere: Bild, Zeilenumbruch, Schachnotation samt ihrer Spannen). Dieselbe
/// <see cref="ModelText"/> in zwei Blöcken = EIN Modellaufruf (Bücher wiederholen viel: „Weiß am Zug“, „<c>&lt;t1/&gt;</c> ist stark“).
/// </summary>
public sealed class EbookTextSegment
{
    /// <summary>Was vor dem ersten Textzeichen steht (Tags, Leerraum) — bleibt unverändert.</summary>
    public string Prefix { get; init; } = "";
    /// <summary>Der übersetzbare Teil im Original (Rückfall, wenn die Übersetzung nicht passt).</summary>
    public string Original { get; init; } = "";
    /// <summary>Was nach dem letzten Textzeichen steht.</summary>
    public string Suffix { get; init; } = "";
    public string ModelText { get; init; } = "";
    /// <summary>Platzhalter-Nummer → Original-HTML (bei Paaren: öffnendes Tag unter der Nummer, schließendes unter <see cref="Closers"/>).</summary>
    public Dictionary<int, string> Raw { get; } = new();
    public Dictionary<int, string> Closers { get; } = new();
}

/// <summary>Ein EPUB-Dokument zerlegt: abwechselnd unverändertes HTML (string) und übersetzbare Blöcke.</summary>
public sealed class EbookTextDocument
{
    public List<object> Parts { get; } = new();
    public IEnumerable<EbookTextSegment> Segments => Parts.OfType<EbookTextSegment>();

    /// <summary>Wieder zusammensetzen; <paramref name="translate"/> liefert die Übersetzung eines <see cref="EbookTextSegment.ModelText"/>
    /// (<c>null</c> = keine → Original). Eine Übersetzung, die nicht passt (Platzhalter, Verschachtelung), fällt ebenfalls aufs
    /// Original zurück und zählt in <paramref name="rejected"/>.</summary>
    public string Rebuild(Func<string, string?> translate, out int rejected)
    {
        rejected = 0;
        var sb = new StringBuilder();
        foreach (var p in Parts)
        {
            if (p is string s) { sb.Append(s); continue; }
            var seg = (EbookTextSegment)p;
            var t = translate(seg.ModelText);
            var middle = t == null ? null : EbookTextSegmenter.Apply(seg, t);
            if (t != null && middle == null) rejected++;
            sb.Append(seg.Prefix).Append(middle ?? seg.Original).Append(seg.Suffix);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Zerlegt ein (X)HTML-Kapitel eines EPUB in übersetzbare Blöcke und setzt es mit Übersetzungen wieder zusammen
/// (0.876.0). Wie <see cref="EbookEpubContent"/> ohne XML-Parser (viele EPUBs sind kein wohlgeformtes XHTML): Block-Tags
/// (<c>p</c>, <c>h1</c>…, <c>li</c>, <c>td</c> …) trennen, Inline-Tags (<c>span</c>, <c>b</c>, <c>a</c>, <c>img</c>, <c>br</c> …)
/// bleiben im Block als Platzhalter. ALLES außerhalb der Blöcke — Kopf, Attribute, ids, Anker, Bilder, CSS — bleibt Byte für
/// Byte, darum gelten Diagramm-Verweise (<c>ImageRef</c>, Kapitel) und Lesezeichen weiter.
///
/// <para><b>Schachnotation geht nie ans Modell</b>: Züge („12.Sf3“, „Kxe4+“, „0-0-0“, „e2-e4“, Figurinen ♔…♞), Zugnummern
/// („12…“), Bewertungszeichen (± ∓ ⩲ = ∞ !? …), Ergebnisse und FEN werden — Folge für Folge, auch über Inline-Tags wie
/// „33.&lt;span&gt;♖&lt;/span&gt;d1“ hinweg — zu EINEM Platzhalter. Ein Block, der danach keine Wörter mehr hat (reine
/// Zugfolge), wird gar nicht übersetzt; ebenso PGN-Kopfzeilen und Partieköpfe „Name – Name, Ort 1985“.</para>
///
/// <para><b>Zurück</b> (<see cref="Apply"/>): die Platzhalter müssen genau einmal wiederkommen (Paare in der richtigen
/// Reihenfolge), und das Ergebnis muss genauso verschachtelt sein wie das Original — sonst bliebe das Kapitel als XHTML
/// unlesbar. Passt etwas nicht, bleibt der Block im Original.</para>
/// </summary>
public static class EbookTextSegmenter
{
    private static readonly Regex Tag = new(
        @"<!--.*?-->|<!\[CDATA\[.*?\]\]>|<[!?][^>]*>|<(/?)([a-zA-Z][a-zA-Z0-9:_\-.]*)((?:[^>""']|""[^""]*""|'[^']*')*)>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "body", "p", "div", "li", "ul", "ol", "dl", "dt", "dd", "tr", "td", "th", "table", "thead", "tbody", "tfoot",
        "caption", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "section", "article", "aside", "header", "footer", "nav",
        "main", "figure", "figcaption", "hr", "address", "center", "details", "summary", "hgroup", "text", "navlabel",
        "doctitle", "docauthor", "navpoint", "navmap", "ncx", "option", "legend", "fieldset", "form", "dir", "menu",
    };

    /// <summary>Inhalt wird nie angefasst (Kopf, Skripte, eingebettete Grafik, vorformatierter Text).</summary>
    private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "script", "style", "title", "svg", "math", "pre", "code", "textarea", "object", "template",
    };

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "image", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>Platzhalter in einer Modellantwort (großzügig gelesen: <c>&lt;t3 /&gt;</c>, <c>&lt;T3&gt;</c>).</summary>
    private static readonly Regex Placeholder = new(@"<\s*(/)?\s*[tT](\d{1,4})\s*(/)?\s*>", RegexOptions.Compiled);

    /// <summary>Tags stehen im Arbeitstext als Zeichen aus dem Privatbereich (U+E000–U+F8FF) — ab einer Basis, die der Text
    /// selbst nicht benutzt (Schachschriften legen Figurinen gern dorthin).</summary>
    private const char PuaFirst = '\uE000';
    private const char PuaLast = '\uF8FF';
    private const int MaxTagsPerBlock = 3000;

    private static bool IsPua(char c) => c >= PuaFirst && c <= PuaLast;

    private sealed record Item(bool IsTag, string Raw, string Name, bool Closing, bool SelfClosing);

    // ── Zerlegen ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Ein Dokument zerlegen (XHTML-Kapitel, Navigation oder NCX — dort steht der Text in <c>&lt;text&gt;</c>).</summary>
    public static EbookTextDocument Parse(string html)
    {
        var doc = new EbookTextDocument();
        var literal = new StringBuilder();
        var block = new List<Item>();
        var skip = 0;
        string? skipName = null;
        var pos = 0;

        void Flush()
        {
            if (block.Count == 0) return;
            var items = block.ToList();
            block.Clear();
            var seg = BuildSegment(items);
            if (seg == null)
            {
                foreach (var it in items) literal.Append(it.Raw);
                return;
            }
            if (literal.Length > 0) { doc.Parts.Add(literal.ToString()); literal.Clear(); }
            doc.Parts.Add(seg);
        }

        foreach (Match m in Tag.Matches(html))
        {
            if (m.Index > pos)
            {
                var text = html.Substring(pos, m.Index - pos);
                if (skip > 0) literal.Append(text);
                else block.Add(new Item(false, text, "", false, false));
            }
            pos = m.Index + m.Length;
            var raw = m.Value;
            if (!m.Groups[2].Success)
            {
                // Kommentar, CDATA, Verarbeitungsanweisung, DOCTYPE: nie Teil eines Blocks.
                if (skip == 0) Flush();
                literal.Append(raw);
                continue;
            }
            var name = m.Groups[2].Value;
            var local = name.Contains(':') ? name[(name.LastIndexOf(':') + 1)..] : name;
            var closing = m.Groups[1].Value == "/";
            var selfClosing = !closing && m.Groups[3].Value.TrimEnd().EndsWith('/');
            if (skip > 0)
            {
                literal.Append(raw);
                if (string.Equals(local, skipName, StringComparison.OrdinalIgnoreCase) && !selfClosing)
                {
                    skip += closing ? -1 : 1;
                    if (skip == 0) skipName = null;
                }
                continue;
            }
            if (SkipTags.Contains(local))
            {
                Flush();
                literal.Append(raw);
                if (!closing && !selfClosing) { skip = 1; skipName = local; }
                continue;
            }
            if (BlockTags.Contains(local))
            {
                Flush();
                literal.Append(raw);
                continue;
            }
            block.Add(new Item(true, raw, local.ToLowerInvariant(), closing, selfClosing || VoidTags.Contains(local)));
        }
        if (pos < html.Length)
        {
            var tail = html[pos..];
            if (skip > 0) literal.Append(tail);
            else block.Add(new Item(false, tail, "", false, false));
        }
        Flush();
        if (literal.Length > 0) doc.Parts.Add(literal.ToString());
        return doc;
    }

    /// <summary>Aus den Bausteinen eines Blocks den Modelltext bauen — <c>null</c>, wenn es nichts zu übersetzen gibt.</summary>
    private static EbookTextSegment? BuildSegment(List<Item> items)
    {
        var tags = items.Where(i => i.IsTag).ToList();
        if (tags.Count > MaxTagsPerBlock) return null;
        if (!items.Any(i => !i.IsTag && i.Raw.Any(char.IsLetter))) return null;

        // Eine Zeichenkette mit EINEM Zeichen je Tag (Privatbereich) und dem dekodierten Text dazwischen.
        var decoded = items.Select(it => it.IsTag ? null : WebUtility.HtmlDecode(it.Raw)).ToList();
        var used = decoded.Where(d => d != null).SelectMany(d => d!).Where(IsPua).Distinct().OrderBy(c => c).ToList();
        int sentinelBase = PuaFirst;
        foreach (var u in used)
            if (u >= sentinelBase && u < sentinelBase + tags.Count) sentinelBase = u + 1;
        if (sentinelBase + tags.Count - 1 > PuaLast) return null;
        var s = new StringBuilder();
        var tagRaw = new List<Item>();
        for (var k = 0; k < items.Count; k++)
        {
            if (items[k].IsTag) { s.Append((char)(sentinelBase + tagRaw.Count)); tagRaw.Add(items[k]); }
            else s.Append(decoded[k]);
        }
        var str = s.ToString();
        var sb0 = (char)sentinelBase;
        bool IsSentinel(char c) => c >= sb0 && c < sb0 + tagRaw.Count;
        int TagOf(char c) => c - sb0;

        // Paare (öffnend ↔ schließend) über einen Stapel je Name.
        var partner = new int[tagRaw.Count];
        Array.Fill(partner, -1);
        var stack = new List<int>();
        for (var i = 0; i < tagRaw.Count; i++)
        {
            var t = tagRaw[i];
            if (t.SelfClosing) continue;
            if (!t.Closing) { stack.Add(i); continue; }
            for (var k = stack.Count - 1; k >= 0; k--)
            {
                if (tagRaw[stack[k]].Name != t.Name) continue;
                partner[i] = stack[k];
                partner[stack[k]] = i;
                stack.RemoveAt(k);
                break;
            }
        }

        // Vorn und hinten: alles bis zum ersten / ab dem letzten echten Textzeichen bleibt draußen.
        var first = -1; var last = -1;
        for (var i = 0; i < str.Length; i++)
            if (!IsSentinel(str[i]) && !char.IsWhiteSpace(str[i])) { if (first < 0) first = i; last = i; }
        if (first < 0) return null;
        // Ein Tag davor, das erst MITTEN im Text schließt („<b>Sehr</b> gut“), gehört hinein — draußen bleiben nur Hüllen,
        // die den ganzen Text umschließen (und Tags ohne Partner im Text). Dasselbe hinten.
        var textFirst = first; var textLast = last;
        for (var k = 0; k < textFirst; k++)
            if (IsSentinel(str[k]) && partner[TagOf(str[k])] is var pk && pk >= 0
                && PositionOf(str, (char)(sb0 + pk), textFirst, textLast) != null) { first = k; break; }
        for (var k = str.Length - 1; k > textLast; k--)
            if (IsSentinel(str[k]) && partner[TagOf(str[k])] is var pk && pk >= 0
                && PositionOf(str, (char)(sb0 + pk), textFirst, textLast) != null) { last = k; break; }

        // Notation: Folgen von Notations-Wörtern (Leerraum dazwischen) werden EIN Platzhalter.
        var runs = NotationRuns(str, first, last + 1, IsSentinel, c => partner[TagOf(c)], sb0);

        var raw = new Dictionary<int, string>();
        var closers = new Dictionary<int, string>();
        var model = new StringBuilder();
        var next = 1;
        var openId = new Dictionary<int, int>();   // Tag-Index → Platzhalter-Nummer (offene Paare)
        var run = 0;
        var i2 = first;
        while (i2 <= last)
        {
            if (run < runs.Count && runs[run].Start == i2)
            {
                var (rs, re) = runs[run++];
                var id = next++;
                raw[id] = Encode(str, rs, re, tagRaw, sb0);
                model.Append("<t").Append(id).Append("/>");
                i2 = re;
                continue;
            }
            var c = str[i2];
            if (IsSentinel(c))
            {
                // Benachbarte Tags ohne Text dazwischen sind EIN Platzhalter; ein einzelnes Tag mit eigenem Partner wird ein Paar.
                var gs = i2;
                while (i2 <= last && IsSentinel(str[i2]) && !(run < runs.Count && runs[run].Start == i2)) i2++;
                var ge = i2;
                if (ge - gs == 1)
                {
                    var ti = TagOf(str[gs]);
                    var p = partner[ti];
                    var partnerInside = p >= 0 && PositionOf(str, (char)(sb0 + p), first, last) is int pp && IsLoneTag(str, pp, first, last, runs, IsSentinel);
                    if (partnerInside && !tagRaw[ti].Closing)
                    {
                        var id = next++;
                        openId[ti] = id;
                        raw[id] = tagRaw[ti].Raw;
                        model.Append("<t").Append(id).Append('>');
                        continue;
                    }
                    if (partnerInside && tagRaw[ti].Closing && openId.TryGetValue(p, out var oid))
                    {
                        closers[oid] = tagRaw[ti].Raw;
                        model.Append("</t").Append(oid).Append('>');
                        continue;
                    }
                }
                var vid = next++;
                raw[vid] = Encode(str, gs, ge, tagRaw, sb0);
                model.Append("<t").Append(vid).Append("/>");
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                while (i2 <= last && char.IsWhiteSpace(str[i2])) i2++;
                model.Append(' ');
                continue;
            }
            model.Append(c);
            i2++;
        }

        var modelText = model.ToString();
        if (!HasWords(modelText) || IsGameHeader(modelText)) return null;
        return new EbookTextSegment
        {
            Prefix = Encode(str, 0, first, tagRaw, sb0),
            Original = Encode(str, first, last + 1, tagRaw, sb0),
            Suffix = Encode(str, last + 1, str.Length, tagRaw, sb0),
            ModelText = modelText,
        }.With(raw, closers);
    }

    private static EbookTextSegment With(this EbookTextSegment seg, Dictionary<int, string> raw, Dictionary<int, string> closers)
    {
        foreach (var (k, v) in raw) seg.Raw[k] = v;
        foreach (var (k, v) in closers) seg.Closers[k] = v;
        return seg;
    }

    private static int? PositionOf(string str, char c, int from, int to)
    {
        for (var i = from; i <= to; i++) if (str[i] == c) return i;
        return null;
    }

    /// <summary>Steht das Tag an <paramref name="pos"/> allein (ohne direkt benachbarte Tags) und nicht in einer Notationsfolge?</summary>
    private static bool IsLoneTag(string str, int pos, int first, int last, List<(int Start, int End)> runs, Func<char, bool> isSentinel)
    {
        if (runs.Any(r => pos >= r.Start && pos < r.End)) return false;
        if (pos > first && isSentinel(str[pos - 1]) && !runs.Any(r => r.End == pos)) return false;
        if (pos < last && isSentinel(str[pos + 1]) && !runs.Any(r => r.Start == pos + 1)) return false;
        return true;
    }

    /// <summary>Zeichenkette zurück in HTML: Text minimal kodiert (&amp; &lt; &gt;), Tags wie im Original.</summary>
    private static string Encode(string str, int from, int to, List<Item> tags, char sentinelBase)
    {
        var sb = new StringBuilder(Math.Max(0, to - from) + 16);
        for (var i = from; i < to; i++)
        {
            var c = str[i];
            if (c >= sentinelBase && c < sentinelBase + tags.Count) sb.Append(tags[c - sentinelBase].Raw);
            else AppendEscaped(sb, c);
        }
        return sb.ToString();
    }

    private static void AppendEscaped(StringBuilder sb, char c)
    {
        switch (c)
        {
            case '&': sb.Append("&amp;"); break;
            case '<': sb.Append("&lt;"); break;
            case '>': sb.Append("&gt;"); break;
            default: sb.Append(c); break;
        }
    }

    // ── Notation ────────────────────────────────────────────────────────────────────────────────

    private const string Pieces = "KQRBNSDTLCPAFHVWGJ♔♕♖♗♘♙♚♛♜♝♞♟";
    private static readonly string PieceClass = "[" + Pieces + "]|Кр|[КФЛС]";
    private static readonly string Promo = "(?:=?(?:[QRBNDTLSCAFHVWGJ♕♖♗♘♛♜♝♞]|[ФЛСК]))?";
    private static readonly string San = $@"(?:(?:{PieceClass})?[a-h]?[1-8]?[x:×]?[a-h][1-8]{Promo}(?:e\.p\.)?|(?:{PieceClass})?[a-h][1-8][\-–x:×][a-h][1-8]{Promo}|[O0](?:[\-–][O0]){{1,2}})[+#‡†]*(?:[!?]{{1,2}})?";
    private static readonly Regex NotationToken = new(
        $@"^(?:\d{{1,3}}\.(?:\.\.|…)?|\d{{1,3}}…|(?:\d{{1,3}}(?:\.(?:\.\.)?|…)\s?)?{San}|[±∓⩲⩱∞=⇆→↑↗↓⊕⨀□△▲∆Δ⊥○●⟳⟲⇄⌓∩⊂]+|\+[\-–]|[\-–]\+|\+/[\-–=]|[\-–=]/\+|=/∞|!!|\?\?|!\?|\?!|1[\-–]0|0[\-–]1|½[\-–:]½|1/2[\-–]1/2|[pnbrqkPNBRQK1-8]{{1,8}}(?:/[pnbrqkPNBRQK1-8]{{1,8}}){{7}})$",
        RegexOptions.Compiled);

    /// <summary>Ist dieses Wort Schachnotation (Zug, Zugnummer, Zeichen, Ergebnis, FEN)?</summary>
    public static bool IsNotation(string token)
    {
        if (token.Length == 0 || token.Length > 90) return false;
        if (NotationToken.IsMatch(token)) return true;
        // Satzzeichen am Ende („Sf3.“) — aber eine Zugnummer braucht ihren Punkt.
        return token.Length > 1 && token[^1] == '.' && NotationToken.IsMatch(token[..^1]);
    }

    private const string OpenPunct = "([{\"'“‘«„»‹";
    private const string ClosePunct = ")]}\"'”’»«›,;:";

    private static List<(int Start, int End)> NotationRuns(string s, int from, int to, Func<char, bool> isSentinel,
        Func<char, int> partnerOf, char sentinelBase)
    {
        var tokens = new List<(int Start, int End, bool Notation)>();
        var i = from;
        while (i < to)
        {
            if (char.IsWhiteSpace(s[i])) { i++; continue; }
            var ts = i;
            while (i < to && !char.IsWhiteSpace(s[i])) i++;
            var te = i;
            // Ränder: Tags und Satzzeichen bleiben draußen.
            var a = ts; var b = te;
            while (a < b && (isSentinel(s[a]) || OpenPunct.Contains(s[a]))) a++;
            while (b > a && (isSentinel(s[b - 1]) || ClosePunct.Contains(s[b - 1]))) b--;
            if (a >= b) continue;
            var core = new StringBuilder();
            var ok = true;
            for (var k = a; k < b; k++)
            {
                // Figurinen einer Schachschrift im Privatbereich zählen wie ♘.
                if (!isSentinel(s[k])) { core.Append(IsPua(s[k]) ? '♘' : s[k]); continue; }
                // Ein Tag IN der Notation, dessen Partner außerhalb liegt, würde beim Zurücksetzen die Verschachtelung brechen.
                var p = partnerOf(s[k]);
                if (p >= 0)
                {
                    var pc = (char)(sentinelBase + p);
                    var inside = false;
                    for (var q = a; q < b; q++) if (s[q] == pc) { inside = true; break; }
                    if (!inside) { ok = false; break; }
                }
            }
            if (!ok || !IsNotation(core.ToString())) continue;
            // Ein Punkt am Ende gehört zum Satz, außer bei der Zugnummer.
            if (s[b - 1] == '.' && !Regex.IsMatch(core.ToString(), @"^\d{1,3}\.$") && !core.ToString().EndsWith("..", StringComparison.Ordinal)) b--;
            tokens.Add((a, b, true));
        }
        // Benachbarte Notations-Wörter (nur Leerraum dazwischen) zusammenfassen.
        var runs = new List<(int Start, int End)>();
        foreach (var t in tokens)
        {
            if (runs.Count > 0)
            {
                var (ps, pe) = runs[^1];
                var gap = true;
                for (var k = pe; k < t.Start; k++) if (!char.IsWhiteSpace(s[k])) { gap = false; break; }
                if (gap) { runs[^1] = (ps, t.End); continue; }
            }
            runs.Add((t.Start, t.End));
        }
        return runs;
    }

    /// <summary>Bleibt außerhalb der Platzhalter ein Wort mit mindestens zwei Buchstaben?</summary>
    internal static bool HasWords(string modelText)
    {
        var plain = Placeholder.Replace(modelText, " ");
        var run = 0;
        foreach (var c in plain)
        {
            if (char.IsLetter(c)) { if (++run >= 2) return true; }
            else run = 0;
        }
        return false;
    }

    private static readonly Regex PgnTag = new(@"^\s*\[\w+\s+""[^""]*""\]\s*$", RegexOptions.Compiled);
    private static readonly Regex GameHeader = new(
        @"^[\p{Lu}][\p{L}.'’]*(?:[ \-](?:\p{Lu}[\p{L}.'’]*|van|von|de|der|den|di|da|del|la|le|ten|ter)){0,3},?(?: [\p{Lu}][.\p{L}]*)?\s*[–—\-]\s*[\p{Lu}][\p{L}.'’]*(?:[ \-](?:\p{Lu}[\p{L}.'’]*|van|von|de|der|den|di|da|del|la|le|ten|ter)){0,3}(?:,? [\p{Lu}][.\p{L}]*)?\s*(?:[,(]?\s*[^\d]{0,60}?)?\b(?:1[5-9]|20)\d\d\b.{0,20}$",
        RegexOptions.Compiled);

    /// <summary>PGN-Kopfzeile oder Partiekopf „Karpow – Kasparow, Moskau 1985“ — Namen bleiben, wie sie sind.</summary>
    internal static bool IsGameHeader(string modelText)
    {
        var plain = Placeholder.Replace(modelText, " ").Trim();
        if (PgnTag.IsMatch(plain)) return true;
        return plain.Length <= 140 && GameHeader.IsMatch(plain);
    }

    // ── Zurück ──────────────────────────────────────────────────────────────────────────────────

    private sealed record Mark(int Id, char Kind, int Index, int Length);   // Kind: o(pen) c(lose) v(oid)

    private static List<Mark> Marks(string text)
    {
        var list = new List<Mark>();
        foreach (Match m in Placeholder.Matches(text))
        {
            var kind = m.Groups[1].Success ? 'c' : m.Groups[3].Success ? 'v' : 'o';
            list.Add(new Mark(int.Parse(m.Groups[2].Value), kind, m.Index, m.Length));
        }
        return list;
    }

    /// <summary>
    /// Kommen in <paramref name="translation"/> genau die Platzhalter von <paramref name="modelText"/> wieder — jeder einmal,
    /// Paare öffnend vor schließend? (Ohne das HTML zu kennen; die Verschachtelung prüft <see cref="Apply"/>.)
    /// </summary>
    public static bool SamePlaceholders(string modelText, string translation)
    {
        var want = Marks(modelText).Select(m => (m.Id, m.Kind)).OrderBy(x => x.Id).ThenBy(x => x.Kind).ToList();
        var got = Marks(translation);
        var have = got.Select(m => (m.Id, m.Kind)).OrderBy(x => x.Id).ThenBy(x => x.Kind).ToList();
        if (!want.SequenceEqual(have)) return false;
        foreach (var o in got.Where(m => m.Kind == 'o'))
        {
            var c = got.FirstOrDefault(m => m.Kind == 'c' && m.Id == o.Id);
            if (c != null && c.Index < o.Index) return false;
        }
        return true;
    }

    /// <summary>Die Übersetzung in den Block einsetzen → HTML des übersetzbaren Teils, oder <c>null</c>, wenn sie nicht passt.</summary>
    public static string? Apply(EbookTextSegment seg, string translation)
    {
        translation = translation.Trim();
        if (translation.Length == 0 || !SamePlaceholders(seg.ModelText, translation)) return null;
        var sb = new StringBuilder(translation.Length + 64);
        var pos = 0;
        foreach (var m in Marks(translation))
        {
            for (var i = pos; i < m.Index; i++) AppendEscaped(sb, translation[i]);
            pos = m.Index + m.Length;
            if (m.Kind == 'c') { if (!seg.Closers.TryGetValue(m.Id, out var cl)) return null; sb.Append(cl); }
            else { if (!seg.Raw.TryGetValue(m.Id, out var r)) return null; sb.Append(r); }
        }
        for (var i = pos; i < translation.Length; i++) AppendEscaped(sb, translation[i]);
        var middle = sb.ToString();

        // Verschachtelung: das Ergebnis muss sich verhalten wie das Original — sonst wird das Kapitel unlesbar.
        var before = Nesting(seg.Prefix + seg.Original + seg.Suffix);
        var after = Nesting(seg.Prefix + middle + seg.Suffix);
        if (before.Ok)
        {
            if (!after.Ok || !before.Open.SequenceEqual(after.Open)) return null;
        }
        else if (!Marks(seg.ModelText).Select(m => (m.Id, m.Kind)).SequenceEqual(Marks(translation).Select(m => (m.Id, m.Kind))))
        {
            return null;   // schon das Original ist schief verschachtelt: nur in unveränderter Platzhalter-Reihenfolge
        }
        return middle;
    }

    /// <summary>Tags eines HTML-Stücks über einen Stapel: passt jedes schließende zum letzten offenen? Rest = offen gebliebene.</summary>
    private static (bool Ok, List<string> Open) Nesting(string html)
    {
        var open = new List<string>();
        var dangling = new List<string>();   // schließende ohne öffnendes (gehören zu einem Tag vor dem Block)
        foreach (Match m in Tag.Matches(html))
        {
            if (!m.Groups[2].Success) continue;
            var name = m.Groups[2].Value.ToLowerInvariant();
            var closing = m.Groups[1].Value == "/";
            if (!closing && (m.Groups[3].Value.TrimEnd().EndsWith('/') || VoidTags.Contains(name))) continue;
            if (!closing) { open.Add(name); continue; }
            if (open.Count == 0) { dangling.Add(name); continue; }
            if (open[^1] != name) return (false, open);
            open.RemoveAt(open.Count - 1);
        }
        return (true, dangling.Select(d => "/" + d).Concat(open).ToList());
    }
}
