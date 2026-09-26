using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Meisterkommentare zur Stellung eines Fehlers (0.542.0) — der Kontext, mit dem „Warum war das ein Fehler?"
/// (<see cref="GameMoveExplanationService"/>) angereichert wird: kommentierte Partien des Rohbestands
/// (<see cref="LibraryGame"/>), die GENAU dieselbe Zugfolge bis zur Stellung vor dem Fehler gespielt haben, und das, was
/// ihr Kommentator an dieser Stelle schrieb.
///
/// <para><b>Nur dieselbe Stellung, keine Ähnlichkeitssuche.</b> Der Kommentar-Index („Frag die Kommentare") findet zu
/// einer Erklärung vor allem Kommentare mit denselben Zugnummern und Zügen aus ANDEREN Stellungen (gemessen 2026-09-26
/// auf Prod: zu „23...Qh4 lässt die Grundreihe ungeschützt" kam „23. Qc2: Under time pressure …"). Gibt man so etwas
/// dem Modell mit, vermischt es zwei Partien. Die Suche über die Zugfolge ist dagegen eindeutig — um den Preis, dass sie
/// nur in der Eröffnung trifft: Amateurpartien verlassen den Meisterbestand meist zwischen dem 6. und 16. Halbzug
/// (zwei Dev-Partien: letzte Treffer bei Halbzug 6 bzw. 14). Gesucht wird deshalb nur, solange
/// <see cref="LibraryGame.OpeningLine"/> reicht (<see cref="LibraryGameReader.OpeningPlies"/>), und ab dem ersten
/// Halbzug ohne Treffer gar nicht mehr (dahinter kann es keinen geben).</para>
/// <para><b>Welcher Kommentar</b> (erster Treffer gewinnt, bei Gleichstand die Partie mit der besseren Note):
/// (1) der Meister spielte DENSELBEN Zug und der Kommentator schrieb dazu etwas — das Urteil über genau diesen Fehler;
/// (2) der Kommentar zum Zug an dieser Stelle nennt den gespielten Zug (meist als Variante: „8.Nc3 (8.Nf3?! …)");
/// (3) der Meister spielte den Bestzug der Engine, mit Kommentar; (4) der Kommentar zur Stellung davor.</para>
/// </summary>
public static class MasterComments
{
    /// <summary>So viele Bibliothekspartien je Stellung werden durchgesehen — die besten zuerst.</summary>
    public const int MaxCandidates = 40;

    /// <summary>Länge des Auszugs, den das Modell (und die Seite) bekommt.</summary>
    public const int MaxTextChars = 500;

    /// <summary>Der Kommentar zur Stellung davor zählt erst ab dieser Länge — „!", „Novelty" oder eine Quellenangabe
    /// erklären nichts.</summary>
    public const int MinPositionCommentChars = 40;

    /// <summary>Ein Kommentar aus einer Meisterpartie. <paramref name="Rank"/> = welche Regel ihn gefunden hat (1–4).</summary>
    public sealed record Found(int LibraryGameId, int Rank, string Source, string Text);

    /// <summary>Je Fehler (Schlüssel = Halbzug) der beste Kommentar aus einer Meisterpartie mit derselben Zugfolge — Fehler
    /// ohne Treffer fehlen im Ergebnis. <paramref name="positions"/> = die Stellungen der Analyse (Halbzug 0 muss die
    /// Grundstellung sein, sonst gibt es keine vergleichbare Zugfolge).</summary>
    public static async Task<Dictionary<int, Found>> ForFlawsAsync(AppDbContext db, IReadOnlyList<GameAnalysisPosition> positions,
        IEnumerable<GameMistakes.Flaw> flaws, CancellationToken ct = default)
    {
        var found = new Dictionary<int, Found>();
        var byPly = positions.Where(p => p.Ply >= 0).GroupBy(p => p.Ply).ToDictionary(g => g.Key, g => g.First());
        if (!byPly.TryGetValue(0, out var first) || !PgnParser.IsStartPosition(first.Fen)) return found;

        foreach (var flaw in flaws.Where(f => f.Ply >= 2 && f.Ply < LibraryGameReader.OpeningPlies).OrderBy(f => f.Ply))
        {
            var prefix = LineBefore(byPly, flaw.Ply);
            if (prefix == null) break;
            var withSeparator = prefix + " ";
            var candidates = await db.LibraryGames.AsNoTracking()
                .Where(g => g.OpeningLine != null && (g.OpeningLine == prefix || g.OpeningLine.StartsWith(withSeparator))
                    && g.CommentedPlies > 0 && g.Status != LibraryGameStatus.Duplicate && g.Status != LibraryGameStatus.Rejected)
                .OrderByDescending(g => g.Score).ThenBy(g => g.Id)
                .Take(MaxCandidates)
                .Select(g => new Candidate(g.Id, g.OpeningLine!, g.Pgn, g.Languages, g.White, g.Black, g.Event, g.PlayedOn, g.Annotator))
                .ToListAsync(ct);
            // Dieselbe Zugfolge, nur länger: ohne Treffer hier gibt es für keinen späteren Fehler einen.
            if (candidates.Count == 0) break;
            if (Pick(candidates, flaw, byPly) is { } hit) found[flaw.Ply] = hit;
        }
        return found;
    }

    private sealed record Candidate(int Id, string OpeningLine, string Pgn, string? Languages, string? White, string? Black,
        string? Event, DateOnly? PlayedOn, string? Annotator);

    /// <summary>Die normalisierte Zugfolge VOR dem Halbzug <paramref name="ply"/> — dieselbe Form wie
    /// <see cref="LibraryGame.OpeningLine"/> (<see cref="LibraryGameReader.AppendOpeningSan"/>).</summary>
    private static string? LineBefore(IReadOnlyDictionary<int, GameAnalysisPosition> byPly, int ply)
    {
        var sb = new StringBuilder(ply * 6);
        for (var i = 0; i < ply; i++)
        {
            if (!byPly.TryGetValue(i, out var p) || string.IsNullOrEmpty(p.GameMoveSan)) return null;
            if (sb.Length > 0) sb.Append(' ');
            LibraryGameReader.AppendOpeningSan(sb, p.GameMoveSan);
        }
        return sb.ToString();
    }

    private static Found? Pick(IEnumerable<Candidate> candidates, GameMistakes.Flaw flaw,
        IReadOnlyDictionary<int, GameAnalysisPosition> byPly)
    {
        var ply = flaw.Ply;
        var played = Norm(flaw.PlayedSan);
        var best = flaw.BestSan is { } b ? Norm(b) : null;
        Found? winner = null;
        foreach (var c in candidates)
        {
            var parsed = PgnParser.SplitGames(c.Pgn).FirstOrDefault();
            if (parsed.MoveText is null) continue;
            var comments = PgnParser.ExtractMoveComments(parsed.MoveText, foldAllVariations: true);
            if (comments is null) continue;
            var lang = (c.Languages ?? "en").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(l => l != "und") ?? "en";
            var tokens = c.OpeningLine.Split(' ');
            var masterMove = ply < tokens.Length ? tokens[ply] : null;
            var atMove = comments.TryGetValue(ply, out var a) ? Clean(a, lang) : null;
            var before = comments.TryGetValue(ply - 1, out var p) ? Clean(p, lang) : null;

            (int Rank, int Ply, string Move, string Text)? pick = null;
            if (masterMove != null && atMove != null)
            {
                if (masterMove == played) pick = (1, ply, masterMove, atMove);
                else if (Mentions(atMove, flaw.PlayedSan, lang)) pick = (2, ply, masterMove, atMove);
                else if (masterMove == best) pick = (3, ply, masterMove, atMove);
            }
            if (pick == null && before != null && before.Length >= MinPositionCommentChars && byPly.TryGetValue(ply - 1, out var prev))
                pick = (4, ply - 1, Norm(prev.GameMoveSan), before);
            if (pick is not { } hit || (winner != null && winner.Rank <= hit.Rank)) continue;

            var label = byPly.TryGetValue(hit.Ply, out var at) ? MoveNumber(at.Fen) + hit.Move : hit.Move;
            winner = new Found(c.Id, hit.Rank, Source(c), Cut($"{label}: {hit.Text}"));
            if (winner.Rank == 1) break;
        }
        return winner;
    }

    /// <summary>„Kasparov – Karpov, Moskau 1985 (annotated by …)" — dieselbe Kopfzeile wie im Kommentar-Index.</summary>
    private static string Source(Candidate c)
    {
        var year = c.PlayedOn is DateOnly d ? $" {d.Year}" : "";
        var evt = string.IsNullOrWhiteSpace(c.Event) || c.Event == "?" ? "" : $", {c.Event}";
        return $"{c.White ?? "?"} – {c.Black ?? "?"}{evt}{year}" + (string.IsNullOrWhiteSpace(c.Annotator) ? "" : $" (annotated by {c.Annotator})");
    }

    /// <summary>
    /// Markup raus (<c>[%evp …]</c>, <c>[%emt …]</c>, ChessBase-Diagramm <c>[#]</c>), Figurenschrift aufgelöst, die
    /// Bewertungssymbole der ChessBase-Schrift (sie kommen als hebräische Buchstaben an, „עe8") weg, lange Zugketten gekürzt
    /// (<see cref="ShortenLines"/>), Leerraum zusammengezogen. Ein Rest ohne Prosa ist kein Kommentar: das Modell soll
    /// IDEEN bekommen, keine Varianten — die Züge darf es ohnehin nur aus den Engine-Linien nennen.
    /// </summary>
    internal static string? Clean(string raw, string lang)
    {
        var text = Markup.Replace(Figurines.Apply(raw, lang), " ");
        text = SymbolFont.Replace(text, "");
        text = ShortenLines(Spaces.Replace(text, " ").Trim());
        return ProseChars(text) < MinProseChars ? null : text;
    }

    /// <summary>So viele Buchstaben außerhalb von Zügen braucht ein Kommentar mindestens.</summary>
    public const int MinProseChars = 25;

    /// <summary>Folgen von mehr als <see cref="MaxLineTokens"/> Zug-Tokens (Zugnummern, Züge, Bewertungszeichen) werden
    /// nach den ersten mit „…" abgekürzt — „8.Nf3?! Bg4 9.Be2 …" bleibt als Hinweis stehen, der Rest der Variante nicht.</summary>
    internal static string ShortenLines(string text)
    {
        var tokens = text.Split(' ');
        var output = new List<string>(tokens.Length);
        var run = 0;
        foreach (var token in tokens)
        {
            if (IsMoveToken(token))
            {
                run++;
                if (run <= MaxLineTokens) output.Add(token);
                else if (run == MaxLineTokens + 1) output.Add("…");
                continue;
            }
            run = 0;
            output.Add(token);
        }
        return string.Join(' ', output);
    }

    public const int MaxLineTokens = 3;

    /// <summary>Gehört das Token zu einer Zugkette? Klammern und Satzzeichen zählen nicht mit; ein Punkt am Ende nur, wenn
    /// davor ein Zug steht („… mit Nf3.") — „12." und „12..." sind Zugnummern und behalten ihre Punkte.</summary>
    internal static bool IsMoveToken(string token)
    {
        var t = token.Trim('(', ')', ',', ';', ':');
        if (t.Length == 0) return false;
        if (MoveLike.IsMatch(t)) return true;
        var bare = t.TrimEnd('.');
        return bare.Length > 0 && bare.Length < t.Length && !bare.All(char.IsDigit) && MoveLike.IsMatch(bare);
    }

    private static int ProseChars(string text)
        => text.Split(' ').Where(t => !IsMoveToken(t) && t != "…").Sum(t => t.Count(char.IsLetter));

    /// <summary>Nennt der Kommentar den Zug — in englischer Schreibweise oder mit den Figurenbuchstaben seiner Sprache
    /// („Sf6")?</summary>
    internal static bool Mentions(string text, string san, string lang)
    {
        var plain = Norm(san);
        if (plain.Length < 2) return false;
        foreach (var form in new[] { plain, Norm(PieceLetters.Convert(plain, "en", lang)) }.Distinct())
            if (Regex.IsMatch(text, @"(?<![A-Za-z0-9])" + Regex.Escape(form) + @"(?![A-Za-z0-9])")) return true;
        return false;
    }

    private static string Norm(string san) => san.TrimEnd('+', '#', '!', '?').Replace('0', 'O');

    private static string MoveNumber(string fenBefore)
        => (fenBefore.Split(' ') is { Length: >= 6 } parts ? parts[5] : "?")
           + (fenBefore.Split(' ') is { Length: >= 2 } s && s[1] == "b" ? "..." : ".");

    private static string Cut(string text) => text.Length <= MaxTextChars ? text : text[..(MaxTextChars - 1)].TrimEnd() + "…";

    private static readonly Regex Markup = new(@"\[%[^\]]*\]|\[#\]", RegexOptions.Compiled);
    private static readonly Regex SymbolFont = new(@"[\u0590-\u05FF]", RegexOptions.Compiled);
    /// <summary>Zugnummer („12." „12..."), Zug (auch „12.Nf3", „e8=Q+", „O-O-O"), Bewertungszeichen („+-", „=", „!?").</summary>
    private static readonly Regex MoveLike = new(
        @"^(\d+\.+)?([KQRBNSDTLCFAPVHGW]?[a-h]?[1-8]?x?[a-h][1-8](=[A-Z])?|O-O(-O)?|0-0(-0)?)[+#]?[!?]*$|^\d+\.+$|^[+\-=±∓⩲⩱∞]+$|^[!?]+$",
        RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
}
