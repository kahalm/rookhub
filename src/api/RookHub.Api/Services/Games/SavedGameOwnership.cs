using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Games;

/// <summary>
/// „Nur meine Partien“ (Wunsch 10.11.2026): unter den gespeicherten Partien stehen viele Vereinspartien der eigenen Mannschaft,
/// die der Kontoinhaber nur hochgeladen hat. Eine Partie gilt als EIGENE, wenn eine dieser Regeln trifft — der Reihe nach,
/// im Zweifel NICHT:
/// <list type="number">
/// <item><see cref="SavedGame.OwnerSide"/> ist <c>white</c>/<c>black</c> (selbst festgelegt bzw. beim Einlesen bestimmt).</item>
/// <item>Die verbundene Vereinspartie trägt die FIDE-ID des Profils auf einer Seite (<c>WhiteFide</c>/<c>WhiteRealFide</c>/
/// <c>BlackFide</c>/<c>BlackRealFide</c>).</item>
/// <item>Ein Spielername ist ein Online-Konto des Profils (chess.com oder lichess, je Quelle egal): genau gleich oder als ganzes
/// Wort im Namen („Rapid Chess • kahalm“), ohne Groß-/Kleinschreibung. Fremde Online-Partien (zugeschaut, gespeichert) fallen
/// damit heraus.</item>
/// <item>Ein Spielername enthält Vor- UND Nachname des Profils als Wörter, in beliebiger Reihenfolge, mit oder ohne Komma
/// („Oberschmid, Patrik“, „Patrik Oberschmid“), Umlaute gleichgesetzt (ü = ue = u). Ein Tippfehler im Nachnamen
/// (eine Vertauschung/Ersetzung/ein Zeichen mehr oder weniger, Nachname ab 6 Zeichen) zählt nur, wenn der Vorname genau
/// stimmt. Nur der Nachname oder nur der Vorname reicht NICHT.</item>
/// </list>
/// Gerechnet wird in C# über die schlanken Spalten aller Partien des Kontos (kein PGN); die Liste filtert danach per
/// <c>Id IN (…)</c> in der Datenbank — Suche, Lasche, Deckel und Zählung sehen so dieselbe Menge.
/// </summary>
internal static class SavedGameOwnership
{
    /// <summary>Was die Regel über den Kontoinhaber weiß.</summary>
    internal sealed record Me(string? FideId, IReadOnlyList<string> Accounts, string? FirstName, string? LastName)
    {
        public static readonly Me Nobody = new(null, [], null, null);
    }

    /// <summary>Was die Regel über eine Partie wissen muss.</summary>
    internal sealed record Row(string? OwnerSide, string? White, string? Black,
        string? WhiteFide = null, string? WhiteRealFide = null, string? BlackFide = null, string? BlackRealFide = null);

    internal static async Task<Me> MeAsync(AppDbContext db, int userId, CancellationToken ct = default)
    {
        var p = await db.UserProfiles.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.FideId, x.ChessComUsername, x.LichessUsername, x.FirstName, x.LastName }).FirstOrDefaultAsync(ct);
        if (p is null) return Me.Nobody;
        var accounts = new[] { p.ChessComUsername, p.LichessUsername }
            .Where(a => !string.IsNullOrWhiteSpace(a) && a.Trim().Length >= 3).Select(a => a!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new Me(string.IsNullOrWhiteSpace(p.FideId) ? null : p.FideId.Trim(), accounts, p.FirstName, p.LastName);
    }

    /// <summary>Die Regel (Reihenfolge siehe Klasse).</summary>
    internal static bool IsOwn(Row g, Me me)
    {
        if (g.OwnerSide is "white" or "black") return true;
        if (me.FideId is { } fide && new[] { g.WhiteFide, g.WhiteRealFide, g.BlackFide, g.BlackRealFide }.Any(f => f?.Trim() == fide))
            return true;
        return IsMyName(g.White, me) || IsMyName(g.Black, me);
    }

    /// <summary>Spielername ↔ Online-Konto oder Vor- + Nachname des Profils.</summary>
    internal static bool IsMyName(string? name, Me me)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var raw = name.Trim();
        foreach (var a in me.Accounts)
        {
            if (string.Equals(raw, a, StringComparison.OrdinalIgnoreCase)) return true;
            if (RawWords(raw).Any(w => string.Equals(w, a, StringComparison.OrdinalIgnoreCase))) return true;
        }
        var first = Fold(me.FirstName);
        var last = Fold(me.LastName);
        if (first.Length < 2 || last.Length < 2) return false;
        var words = NameWords(raw);
        if (!words.Contains(first)) return false;
        return words.Any(w => w != first && (w == last || (last.Length >= 6 && OneEditApart(w, last))));
    }

    /// <summary>Wörter eines Namens für den Konto-Abgleich: getrennt an allem außer Buchstaben, Ziffern, <c>_</c> und <c>-</c>.</summary>
    private static IEnumerable<string> RawWords(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-') { sb.Append(c); continue; }
            if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>Wörter eines Klarnamens, gefaltet (<see cref="Fold"/>); Initialen („M.“) und Einzelbuchstaben fallen weg.</summary>
    private static List<string> NameWords(string name) =>
        name.Split([' ', ',', ';', '/', '(', ')', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.EndsWith('.'))
            .Select(Fold).Where(w => w.Length >= 2).ToList();

    /// <summary>Klein, Umlaute als Grundbuchstabe (ä/ae → a, ö/oe → o, ü/ue → u, ß → ss), übrige Akzente weg, nur Buchstaben.</summary>
    internal static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = value.Trim().ToLowerInvariant().Replace("ß", "ss").Replace("ae", "a").Replace("oe", "o").Replace("ue", "u");
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetter(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Höchstens eine Bearbeitung (ersetzen, einfügen, löschen oder zwei Nachbarn vertauschen) auseinander.</summary>
    internal static bool OneEditApart(string a, string b)
    {
        if (a == b) return true;
        if (Math.Abs(a.Length - b.Length) > 1) return false;
        if (a.Length == b.Length)
        {
            var diff = Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToList();
            if (diff.Count == 1) return true;
            return diff.Count == 2 && diff[1] == diff[0] + 1 && a[diff[0]] == b[diff[1]] && a[diff[1]] == b[diff[0]];
        }
        var (s, l) = a.Length < b.Length ? (a, b) : (b, a);
        for (var i = 0; i < l.Length; i++)
            if (l.Remove(i, 1) == s) return true;
        return false;
    }

    /// <summary>Die schlanken Spalten der Regel samt FIDE-IDs der verbundenen Vereinspartie (eine Abfrage, kein PGN).</summary>
    internal static IQueryable<(int Id, Row Row)> RowsQuery(AppDbContext db, IQueryable<SavedGame> games) =>
        from g in games
        join c in db.LeagueClubGames on g.LeagueClubGameId equals c.Id into cs
        from c in cs.DefaultIfEmpty()
        select new ValueTuple<int, Row>(g.Id, new Row(g.OwnerSide, g.White, g.Black,
            c == null ? null : c.WhiteFide, c == null ? null : c.WhiteRealFide,
            c == null ? null : c.BlackFide, c == null ? null : c.BlackRealFide));

    /// <summary>Schränkt die (schon auf den Besitzer gefilterten) Partien auf die eigenen ein.</summary>
    internal static async Task<IQueryable<SavedGame>> ApplyAsync(AppDbContext db, IQueryable<SavedGame> games, int userId,
        CancellationToken ct = default)
    {
        var me = await MeAsync(db, userId, ct);
        var rows = await RowsQuery(db, games).ToListAsync(ct);
        var ids = rows.Where(r => IsOwn(r.Row, me)).Select(r => r.Id).ToList();
        return games.Where(g => ids.Contains(g.Id));
    }
}
