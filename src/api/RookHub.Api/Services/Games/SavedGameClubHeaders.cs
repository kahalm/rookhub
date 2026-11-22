using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Kopfdaten einer korrigierten Vereinspartie (<c>PUT /api/league/club/games/{id}</c>, <c>ClubGameWriter.UpdateAsync</c>) in die
/// verbundenen Kopien in „Meine Partien" tragen (1.002.1, Befund 2026-11-22: Vereinspartie 259 stand nach der Korrektur auf 0-1,
/// die Kopie 243 noch auf <c>*</c>). Gegenstück zu <see cref="SavedGameAnalysisLink.ApplyClubMovesAsync"/> (Züge) — wie dort
/// ist die Vereinspartie die Quelle, aber das PGN der Kopie wird hier NICHT neu gebaut, sondern nur an den betroffenen
/// Kopfzeilen und am Ende der Zugliste geändert: Kommentare, Varianten und eigene Kopfzeilen der Kopie bleiben.
///
/// <para>Was geht in die Kopie (nur, was die Korrektur an der Vereinspartie wirklich geändert hat):</para>
/// <list type="bullet">
/// <item><b>Ergebnis</b>: immer — Spalte, <c>[Result]</c> und das Ergebnis am Ende der Zugliste.</item>
/// <item><b>Name einer Seite</b> (samt Elo, und <c>[WhiteFideId]</c>, falls die Kopie die Zeile trägt): der neue ÖFFENTLICHE Name
/// der Vereinspartie. Die Kopie trägt oft den echten Namen statt „Schwaz" (aus dem Formular abgelegt); den echten Namen hinter
/// „Schwaz" (<see cref="LeagueClubGame.WhiteRealName"/>) schreibt dieser Weg NIE in eine Kopie (Anonymisierung, Klassenkommentar
/// dort). Wird eine Seite zu „Schwaz", bekommt die Kopie „Schwaz" nur, wenn sie bisher denselben Namen trug wie die
/// Vereinspartie — ein eigener Name des Besitzers bleibt.</item>
/// <item><b>Veranstaltung, Jahr (<c>[Date]</c>), Liga/Jahrgang</b>: nur, wenn die Kopie bisher den Wert der Vereinspartie trug
/// (beim Datum: nur das Jahr oder unbekannt); ein eigenes, volles Datum der Kopie bleibt.</item>
/// </list>
/// </summary>
public static class SavedGameClubHeaders
{
    /// <summary>Die Kopfdaten einer Vereinspartie VOR der Korrektur.</summary>
    public sealed record Head(string White, string Black, string? WhiteFide, string? BlackFide, int? WhiteElo, int? BlackElo,
        string Result, string? Event, int? Year, string? Classifier1, string? Classifier2)
    {
        public static Head Of(LeagueClubGame g) => new(g.White, g.Black, g.WhiteFide, g.BlackFide, g.WhiteElo, g.BlackElo, g.Result,
            g.Event, g.Year, g.Classifier1, g.Classifier2);
    }

    public static readonly IReadOnlySet<string> Results = new HashSet<string> { "1-0", "0-1", "1/2-1/2", "*" };

    /// <summary>Alle Kopien der Vereinspartie <paramref name="after"/> nachziehen. <paramref name="anonName"/> = „Schwaz" des Vereins.
    /// Speichert selbst. → Zahl der geänderten Kopien.</summary>
    public static async Task<int> ApplyAsync(AppDbContext db, Head before, LeagueClubGame after, string? anonName, CancellationToken ct = default)
    {
        if (Head.Of(after) == before) return 0;
        var copies = await db.SavedGames.Where(s => s.LeagueClubGameId == after.Id).ToListAsync(ct);
        var changed = copies.Count(c => Apply(c, before, after, anonName));
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>Eine Kopie nachziehen (ohne Speichern). → ob sich etwas geändert hat.</summary>
    internal static bool Apply(SavedGame copy, Head before, LeagueClubGame after, string? anonName)
    {
        var pgn = copy.Pgn;
        var was = (copy.Pgn, copy.Result, copy.White, copy.Black, copy.WhiteElo, copy.BlackElo, copy.Classifier1, copy.Classifier2);
        var tags = TagsOf(pgn);
        string? T(string k) => tags.TryGetValue(k, out var v) ? v : null;
        static bool Same(string? a, string? b) =>
            string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);

        if (after.Result != before.Result && Results.Contains(after.Result))
        {
            copy.Result = after.Result;
            pgn = SetTag(pgn, "Result", after.Result);
            pgn = SetTermination(pgn, after.Result);
        }

        void Side(string color, string oldName, string newName, string? oldFide, string? newFide, int? newElo, Func<SavedGame, string?> get,
            Action<SavedGame, string?> set, Action<SavedGame, int?> setElo)
        {
            if (oldName == newName && oldFide == newFide) return;
            var mirrored = Same(T(color) ?? get(copy), oldName);
            var anon = anonName != null && newName.Trim() == anonName;
            if (!mirrored && anon) return;              // eigener (oft echter) Name des Besitzers bleibt — nie „Schwaz" darüber
            if (oldName == newName && !mirrored) return;   // nur die FIDE-ID kam dazu: eigene Schreibweise bleibt
            set(copy, Clip(newName, 120));
            pgn = SetTag(pgn, color, newName);
            setElo(copy, newElo);
            pgn = SetTag(pgn, color + "Elo", newElo?.ToString(), removeIfNull: true);
            if (tags.ContainsKey(color + "FideId")) pgn = SetTag(pgn, color + "FideId", newFide, removeIfNull: true);
        }
        Side("White", before.White, after.White, before.WhiteFide, after.WhiteFide, after.WhiteElo, c => c.White, (c, v) => c.White = v, (c, v) => c.WhiteElo = v);
        Side("Black", before.Black, after.Black, before.BlackFide, after.BlackFide, after.BlackElo, c => c.Black, (c, v) => c.Black = v, (c, v) => c.BlackElo = v);

        if (after.Event != before.Event && before.Event != null && Same(T("Event"), before.Event))
            pgn = SetTag(pgn, "Event", after.Event ?? "?");
        if (after.Year != before.Year && after.Year is { } year && T("Date") is var date
            && (date is null || date == "????.??.??" || (before.Year is { } oy && date == $"{oy}.??.??")))
            pgn = SetTag(pgn, "Date", $"{year}.??.??");
        if (after.Classifier1 != before.Classifier1 && (copy.Classifier1 == null || copy.Classifier1 == before.Classifier1))
            copy.Classifier1 = after.Classifier1;
        if (after.Classifier2 != before.Classifier2 && (copy.Classifier2 == null || copy.Classifier2 == before.Classifier2))
            copy.Classifier2 = after.Classifier2;

        copy.Pgn = pgn;
        return was != (copy.Pgn, copy.Result, copy.White, copy.Black, copy.WhiteElo, copy.BlackElo, copy.Classifier1, copy.Classifier2);
    }

    /// <summary>Nur das Ergebnis einer Kopie setzen (Spalte, <c>[Result]</c>, Ende der Zugliste) — für den Start-Abgleich.</summary>
    internal static void SetResult(SavedGame copy, string result)
    {
        copy.Result = result;
        copy.Pgn = SetTermination(SetTag(copy.Pgn, "Result", result), result);
    }

    // ── PGN-Text: nur die betroffenen Stellen ändern ────────────────

    private static readonly Regex HeaderEnd = new(@"\r?\n[ \t]*\r?\n", RegexOptions.Compiled);
    private static readonly Regex TagLine = new(@"^\[(\w+)\s+""((?:[^""\\]|\\.)*)""\]\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Termination = new(@"(^|\s)(1-0|0-1|1/2-1/2|\*)\s*$", RegexOptions.Compiled);

    private static (string Head, string Body) Split(string pgn)
    {
        var text = pgn.TrimStart();
        if (!text.StartsWith('[')) return (string.Empty, pgn);
        var m = HeaderEnd.Match(pgn);
        return m.Success ? (pgn[..m.Index], pgn[m.Index..]) : (pgn, string.Empty);
    }

    internal static Dictionary<string, string> TagsOf(string pgn)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in TagLine.Matches(Split(pgn).Head))
            result.TryAdd(m.Groups[1].Value, SavedGamePgn.UnescapeTag(m.Groups[2].Value) ?? string.Empty);
        return result;
    }

    /// <summary>Kopfzeile <paramref name="key"/> setzen (vorhanden → ersetzt, sonst ans Ende des Kopfs); <c>null</c> mit
    /// <paramref name="removeIfNull"/> entfernt sie.</summary>
    internal static string SetTag(string pgn, string key, string? value, bool removeIfNull = false)
    {
        var (head, rest) = Split(pgn);
        var line = new Regex(@"^\[" + Regex.Escape(key) + @"\s+""(?:[^""\\]|\\.)*""\][ \t]*\r?\n?", RegexOptions.Multiline);
        if (value is null && removeIfNull) return line.Replace(head, string.Empty, 1).TrimEnd('\r', '\n') + rest;
        var tag = PgnWriter.Tag(key, Clean(value) ?? "?").TrimEnd('\n');
        if (line.IsMatch(head))
            return line.Replace(head, m => tag + (m.Value.EndsWith('\n') ? (m.Value.EndsWith("\r\n") ? "\r\n" : "\n") : string.Empty), 1) + rest;
        if (head.Length == 0) return tag + "\n\n" + pgn.TrimStart();
        return head + "\n" + tag + rest;
    }

    /// <summary>Das Ergebnis am Ende der Zugliste ersetzen bzw. anhängen.</summary>
    internal static string SetTermination(string pgn, string result)
    {
        var (head, rest) = Split(pgn);
        var trailing = rest.Length - rest.TrimEnd().Length;
        var tail = rest[(rest.Length - trailing)..];
        var body = rest[..(rest.Length - trailing)];
        if (body.Trim().Length == 0) return head + (head.Length > 0 ? "\n\n" : string.Empty) + result + (tail.Length > 0 ? tail : "\n");
        var m = Termination.Match(body);
        body = m.Success ? body[..m.Groups[2].Index] + result : body + " " + result;
        return head + body + tail;
    }

    private static string? Clean(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Replace("\n", " ").Replace("\r", " ").Trim();

    private static string? Clip(string? s, int max) => Clean(s) is { } c ? (c.Length > max ? c[..max] : c) : null;
}
