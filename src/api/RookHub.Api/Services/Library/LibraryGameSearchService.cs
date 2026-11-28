using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Library;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Library;

/// <summary>
/// Meisterpartien-Suche (<c>GET /api/library-games/search</c>, 1.038.0): der Rohbestand (~130 000 kommentierte
/// Meisterpartien) nach Spieler, Turnier, Jahr, Ergebnis und ECO durchsuchen — geblättert, ohne Züge.
///
/// <para><b>Kein PGN wird durchkämmt.</b> Jeder Filter trifft eine indizierte Spalte: Namen und Turnier über den
/// VOLLTEXT-Index auf <see cref="LibraryGame.SearchText"/> (Wortanfänge), das Jahr über <see cref="LibraryGame.PlayedYear"/>,
/// ECO über <see cref="LibraryGame.Eco"/>. Ob ein Name bei Weiß oder Schwarz steht (und nicht nur beim Kommentator), prüft
/// danach ein <c>LIKE</c> auf den wenigen Zeilen, die der Volltext übrig lässt. Gemessen am echten Bestand (130 572 Zeilen,
/// 2026-11-28): Spielersuche 5–7 ms, Jahres-/ECO-/Ergebnisfilter 1–130 ms, die ungünstigste Kombination (seltener Filter,
/// nach der Note sortiert) 0,4 s.</para>
///
/// <para><b>Gezeigt wird, was die Bestandssuche heute schon zeigt</b> (<see cref="LibraryGameService.SearchAsync"/>): Kopfdaten,
/// ohne Dubletten und Aussortiertes, auch ohne Anmeldung. Die Züge holt erst „Anschauen“ (angemeldet).</para>
/// </summary>
public class LibraryGameSearchService
{
    private readonly AppDbContext _db;

    public LibraryGameSearchService(AppDbContext db) => _db = db;

    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 50;

    /// <summary>Tiefer als so viele Zeilen blättert die Suche nicht: ein großer OFFSET liest alle Zeilen davor, und wer
    /// auf Seite 200 sucht, sucht eigentlich mit einem Filter mehr.</summary>
    public const int MaxOffset = 5000;

    public async Task<LibraryGameSearchPageDto> SearchAsync(LibraryGameSearchQuery query, CancellationToken ct = default)
    {
        var input = LibraryGameSearchInput.Parse(query);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize);
        var maxPage = Math.Max(1, MaxOffset / pageSize);
        var page = Math.Clamp(query.Page, 1, maxPage);

        var rows = _db.LibraryGames.AsNoTracking()
            .Where(g => g.Status != LibraryGameStatus.Duplicate && g.Status != LibraryGameStatus.Rejected);

        var words = input.AllWords;
        if (words.Count > 0)
        {
            if (_db.Database.IsRelational())
            {
                var term = LibraryGameSearchInput.FulltextTerm(words);
                rows = rows.Where(g => EF.Functions.Match(g.SearchText!, term, MySqlMatchSearchMode.Boolean) > 0);
            }
            else
            {
                // InMemory kennt kein MATCH — Test-Krücke wie in LibraryGameService; die echte Abfrage prüft
                // LibraryGameSearchSqlTests gegen MariaDB. Wortanfang wie im Volltext.
                foreach (var w in words)
                    rows = rows.Where(g => g.SearchText != null
                        && (" " + g.SearchText).Contains(" " + w));
            }
        }
        foreach (var w in input.PlayerWords)
        {
            var like = "%" + OpeningLines.EscapeLike(w) + "%";
            rows = rows.Where(g => EF.Functions.Like(g.White!, like, OpeningLines.LikeEscape)
                                || EF.Functions.Like(g.Black!, like, OpeningLines.LikeEscape));
        }
        foreach (var w in input.EventWords)
        {
            var like = "%" + OpeningLines.EscapeLike(w) + "%";
            rows = rows.Where(g => EF.Functions.Like(g.Event!, like, OpeningLines.LikeEscape));
        }

        if (input.YearFrom is { } from) rows = rows.Where(g => g.PlayedYear >= from);
        if (input.YearTo is { } to) rows = rows.Where(g => g.PlayedYear <= to);
        if (input.Result is { } result) rows = rows.Where(g => g.Result == result);
        if (input.EcoPrefix is { } prefix) rows = rows.Where(g => g.Eco != null && g.Eco.StartsWith(prefix));
        if (input.EcoFrom is { } ecoFrom && input.EcoTo is { } ecoTo)
            rows = rows.Where(g => string.Compare(g.Eco, ecoFrom) >= 0 && string.Compare(g.Eco, ecoTo) <= 0);

        var total = await rows.CountAsync(ct);

        // Sortiert wird nur über Spalten mit eigenem Index (+ Id, die InnoDB an jeden Index hängt): Note bzw. Jahr. Ein
        // zweites Merkmal ohne Index ließe MariaDB den ganzen Bestand von Hand sortieren (LibraryGameService: 13,6 s).
        var ordered = input.Sort switch
        {
            LibraryGameSearchInput.SortNewest => rows.OrderByDescending(g => g.PlayedYear).ThenByDescending(g => g.Id),
            LibraryGameSearchInput.SortOldest => rows.OrderBy(g => g.PlayedYear).ThenBy(g => g.Id),
            _ => rows.OrderByDescending(g => g.Score).ThenByDescending(g => g.Id),
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new LibraryGameDto
            {
                Id = g.Id, White = g.White, Black = g.Black, WhiteElo = g.WhiteElo, BlackElo = g.BlackElo,
                Result = g.Result, Event = g.Event, PlayedOn = g.PlayedOn, PlayedYear = g.PlayedYear, Eco = g.Eco,
                PlyCount = g.PlyCount, Annotator = g.Annotator, CommentedPlies = g.CommentedPlies,
                CommentChars = g.CommentChars, Languages = g.Languages, Score = g.Score, SourceTitle = g.SourceTitle,
            })
            .ToListAsync(ct);

        return new LibraryGameSearchPageDto
        {
            Items = items, Total = total, Page = page, PageSize = pageSize, MaxPage = maxPage, Ignored = input.Ignored,
        };
    }
}

/// <summary>
/// Die Eingaben der Meisterpartien-Suche, geprüft und gedeckelt — getrennt von der Abfrage, damit sich die Regeln ohne
/// Datenbank prüfen lassen.
/// </summary>
internal sealed class LibraryGameSearchInput
{
    /// <summary>Längere Eingaben je Feld werden abgeschnitten; die Oberfläche begrenzt die Felder genauso.</summary>
    internal const int MaxFieldLength = 100;

    /// <summary>Höchstens so viele Wörter über alle drei Textfelder — jedes ist eine Pflicht-Bedingung im Volltext.</summary>
    internal const int MaxTerms = 8;

    internal const string SortBest = "best", SortNewest = "newest", SortOldest = "oldest";

    public List<string> FreeWords { get; } = new();
    public List<string> PlayerWords { get; } = new();
    public List<string> EventWords { get; } = new();
    public short? YearFrom { get; private set; }
    public short? YearTo { get; private set; }
    /// <summary>In PGN-Form (<c>1-0</c>, <c>0-1</c>, <c>1/2-1/2</c>).</summary>
    public string? Result { get; private set; }
    public string? EcoPrefix { get; private set; }
    public string? EcoFrom { get; private set; }
    public string? EcoTo { get; private set; }
    public string Sort { get; private set; } = SortBest;
    public List<string> Ignored { get; } = new();

    /// <summary>Alle Wörter der drei Textfelder ohne Doppel — sie gehen gemeinsam in EINEN Volltext-Ausdruck.</summary>
    public List<string> AllWords => FreeWords.Concat(PlayerWords).Concat(EventWords).Distinct().ToList();

    private int _terms;

    internal static LibraryGameSearchInput Parse(LibraryGameSearchQuery q)
    {
        var input = new LibraryGameSearchInput();
        input.AddWords(q.Player, input.PlayerWords);
        input.AddWords(q.Event, input.EventWords);
        input.AddWords(q.Q, input.FreeWords);

        if (q.YearFrom is { } yf) input.YearFrom = ClampYear(yf);
        if (q.YearTo is { } yt) input.YearTo = ClampYear(yt);
        if (input.YearFrom is { } a && input.YearTo is { } b && a > b) (input.YearFrom, input.YearTo) = (b, a);

        if (!string.IsNullOrWhiteSpace(q.Result))
        {
            input.Result = NormalizeResult(q.Result);
            if (input.Result is null) input.Ignored.Add(Cut(q.Result.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(q.Eco)) input.ParseEco(q.Eco);

        input.Sort = q.Sort?.Trim().ToLowerInvariant() switch
        {
            SortNewest => SortNewest,
            SortOldest => SortOldest,
            _ => SortBest,
        };
        return input;
    }

    /// <summary>
    /// Wörter wie im Volltext-Index: getrennt an allem, was kein Buchstabe und keine Ziffer ist („Saint-Amant“ sind zwei),
    /// kleingeschrieben. Unter <see cref="LibraryGameService.MinQueryLength"/> Zeichen nimmt der Index ein Wort gar nicht
    /// auf, Stoppwörter (<see cref="Stopwords"/>) auch nicht — sie würden nie etwas finden und werden gemeldet statt still
    /// verschluckt.
    /// </summary>
    private void AddWords(string? text, List<string> into)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var word = new StringBuilder();
        foreach (var c in Cut(text) + " ")
        {
            if (char.IsLetterOrDigit(c)) { word.Append(char.ToLowerInvariant(c)); continue; }
            if (word.Length == 0) continue;
            var w = word.ToString();
            word.Clear();
            if (w.Length < LibraryGameService.MinQueryLength || Stopwords.Contains(w) || _terms >= MaxTerms) { Ignored.Add(w); continue; }
            if (into.Contains(w)) continue;
            into.Add(w);
            _terms++;
        }
    }

    /// <summary>
    /// Die Stoppwörter des InnoDB-Volltext-Index ab drei Buchstaben (<c>INFORMATION_SCHEMA.INNODB_FT_DEFAULT_STOPWORD</c>).
    /// Sie stehen NICHT im Index: ein Pflicht-Wort <c>+the</c> fände gar nichts, <c>+the*</c> nur „Theodor“. Deshalb filtern
    /// sie nicht und werden gemeldet wie zu kurze Wörter.
    /// </summary>
    internal static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "about", "are", "com", "for", "from", "how", "that", "the", "this", "was", "what", "when", "where", "who", "will",
        "with", "und", "www",
    };

    /// <summary>
    /// Der Volltext-Ausdruck: jedes Wort Pflicht und als WORTANFANG (<c>+capa*</c>). Die Operatoren der Boolean-Syntax kommen
    /// gar nicht erst an — <see cref="AddWords"/> lässt nur Buchstaben und Ziffern durch.
    /// </summary>
    internal static string FulltextTerm(IEnumerable<string> words) => string.Join(' ', words.Select(w => $"+{w}*"));

    internal static string? NormalizeResult(string value) => value.Trim().ToLowerInvariant() switch
    {
        "white" or "1-0" => "1-0",
        "black" or "0-1" => "0-1",
        "draw" or "1/2-1/2" or "½-½" => "1/2-1/2",
        _ => null,
    };

    private static readonly Regex EcoCode = new(@"^[A-E](\d\d?)?$", RegexOptions.CultureInvariant);
    private static readonly Regex EcoRange = new(@"^([A-E]\d\d)-([A-E]\d\d)$", RegexOptions.CultureInvariant);

    /// <summary>„B90“ (genau), „B9“/„B“ (alle darunter) oder „B20-B99“ (Bereich, Grenzen eingeschlossen). Etwas anderes
    /// filtert nicht und wird gemeldet.</summary>
    private void ParseEco(string raw)
    {
        var eco = Regex.Replace(Cut(raw), @"\s+", "").ToUpperInvariant().Replace('–', '-');
        if (EcoCode.IsMatch(eco)) { EcoPrefix = eco; return; }
        var range = EcoRange.Match(eco);
        if (range.Success)
        {
            var (from, to) = (range.Groups[1].Value, range.Groups[2].Value);
            if (string.CompareOrdinal(from, to) > 0) (from, to) = (to, from);
            (EcoFrom, EcoTo) = (from, to);
            return;
        }
        Ignored.Add(Cut(raw.Trim()));
    }

    private static short ClampYear(int year)
        => (short)Math.Clamp(year, LibraryGameReader.MinYear, LibraryGameReader.MaxYear);

    private static string Cut(string text) => text.Length > MaxFieldLength ? text[..MaxFieldLength] : text;
}
