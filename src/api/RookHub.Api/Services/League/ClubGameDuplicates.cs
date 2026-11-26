using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Ist eine neue Vereinspartie dieselbe wie eine schon gespeicherte — erkannt an den ZÜGEN, egal wie die Seiten heißen
/// (1.033.1, Prod 26.11.2026: Raja – Binder stand als Vereinspartie 268 mit echten Namen da und kam als 269 mit „Schwaz“
/// noch einmal herein, mit Zusammenfassung). Rein, ohne Datenbank.
/// <list type="number">
/// <item><b>Gleich</b> (bisherige Regel): derselbe <see cref="LeagueClubGame.MovesHash"/> im gleichen Jahr; unter
/// <see cref="ClubPgnText.ShortGamePlies"/> Halbzügen zusätzlich gleiche Namen.</item>
/// <item><b>Bis auf das Ende gleich</b> (neu, bewusst eng): gleiches Jahr, beide Fassungen mindestens
/// <see cref="LooseMinPlies"/> Halbzüge, alle Halbzüge bis auf den LETZTEN gleich, und
/// <list type="bullet">
/// <item>eine Fassung hat genau EINEN Halbzug mehr (Signal-Königszug der Übertragung, 1.022.0, oder ein nicht mitgeschriebener
/// letzter Zug), oder</item>
/// <item>beide sind gleich lang und der letzte Halbzug ist auf beiden Seiten ein Königszug, einer davon auf d4/e4/d5/e5
/// (<see cref="Games.SignalKingMove.Centre"/>) — am DGT-Brett ersetzt der Signalzug den echten Königszug (268: 45.Ke4 statt
/// 45.Kg4, gleich lang, darum verschiedene Hashes).</item>
/// </list>
/// Dazu: die Ergebnisse widersprechen sich nicht (gleich oder eines „*“), und keine Farbe hat in beiden Fassungen
/// verschiedene FIDE-IDs (auch die interne hinter „Schwaz“). Ein Präfix über mehr als einen Halbzug zählt NICHT — zwei Partien
/// mit derselben langen Theorie sind verschiedene Partien.</item>
/// </list>
/// Verglichen wird die SAN-Folge der Hauptvariante (ohne Kommentare, Uhren, NAGs) ohne angehängte <c>+ # ! ?</c>.
/// </summary>
internal static class ClubGameDuplicates
{
    /// <summary>So viele Halbzüge braucht die kürzere Fassung mindestens für „bis auf das Ende gleich“.</summary>
    public const int LooseMinPlies = 30;

    private static string Norm(string san) => san.Trim().TrimEnd('+', '#', '!', '?');

    /// <summary>Halbzüge, die eine gespeicherte Partie haben kann, damit sie „bis auf das Ende gleich“ sein kann
    /// (für die Abfrage der Kandidaten).</summary>
    public static IEnumerable<int> LoosePlies(int plies) =>
        plies < LooseMinPlies ? Enumerable.Empty<int>() : new[] { plies - 1, plies, plies + 1 }.Where(p => p >= LooseMinPlies);

    /// <summary>Regel 1 der Klassenbeschreibung (die bisherige).</summary>
    public static bool SameHash(LeagueClubGame stored, LeagueClubGame g) =>
        stored.MovesHash == g.MovesHash && stored.Year == g.Year
        && (g.Plies >= ClubPgnText.ShortGamePlies || (stored.White == g.White && stored.Black == g.Black));

    /// <summary>Dieselbe Partie nach Regel 1 oder 2? <paramref name="storedSans"/>/<paramref name="sans"/> = die Hauptvarianten.</summary>
    public static bool Same(LeagueClubGame stored, IReadOnlyList<string> storedSans, LeagueClubGame g, IReadOnlyList<string> sans) =>
        SameHash(stored, g) || (stored.Year == g.Year && SameMovesButEnd(storedSans, sans)
            && ResultsAgree(stored.Result, g.Result) && !FideConflict(stored, g));

    /// <summary>Hat <paramref name="g"/> die besseren Daten als die gleiche Partie <paramref name="stored"/> — mehr Halbzüge, oder ein
    /// Ergebnis, wo dort „*“ steht? (Nur das Partieformular ersetzt danach die ältere Fassung, siehe
    /// <see cref="ClubGameWriter.ArchiveOlderVersionsAsync"/>; jeder andere Weg meldet „schon vorhanden“.)</summary>
    public static bool Better(LeagueClubGame stored, LeagueClubGame g) =>
        g.Plies > stored.Plies || (stored.Result == "*" && g.Result != "*");

    /// <summary>Regel 2, nur die Züge.</summary>
    internal static bool SameMovesButEnd(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var (s, l) = a.Count <= b.Count ? (a, b) : (b, a);
        if (s.Count < LooseMinPlies || l.Count - s.Count > 1) return false;
        for (var i = 0; i < s.Count - 1; i++)
            if (Norm(s[i]) != Norm(l[i])) return false;
        var (ls, ll) = (Norm(s[^1]), Norm(l[s.Count - 1]));
        if (l.Count == s.Count + 1) return ls == ll;   // ein Halbzug mehr am Ende
        if (ls == ll) return true;                       // gleich (nur anders geschrieben)
        return ls.StartsWith('K') && ll.StartsWith('K') && (CentreKing(ls) || CentreKing(ll));
    }

    private static bool CentreKing(string san) => san.Length >= 3 && Games.SignalKingMove.Centre.Contains(san[^2..]);

    private static bool ResultsAgree(string a, string b) => a == b || a == "*" || b == "*";

    private static bool FideConflict(LeagueClubGame a, LeagueClubGame b)
    {
        static bool Differ(string? x, string? y) => !string.IsNullOrWhiteSpace(x) && !string.IsNullOrWhiteSpace(y) && x.Trim() != y.Trim();
        return Differ(a.WhiteFide ?? a.WhiteRealFide, b.WhiteFide ?? b.WhiteRealFide)
            || Differ(a.BlackFide ?? a.BlackRealFide, b.BlackFide ?? b.BlackRealFide);
    }
}
