using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Turnier einer gespeicherten Partie für die Liste (Wunsch 10.11.2026: „in der Lasche Am Brett das Turnier anzeigen“), z. B.
/// „TMM Landesliga 2025/2026 · R5“. Quelle in dieser Reihenfolge:
/// <list type="number">
/// <item>verbundene Vereinspartie mit zugeordneter Liga: Name der Liga (<c>LeagueTournaments.Name</c> über <c>LeagueTnr</c>)
/// und deren Runde;</item>
/// <item>sonst das <c>Event</c> der Vereinspartie;</item>
/// <item>sonst der PGN-Kopf <c>[Event]</c> (+ <c>[Round]</c>).</item>
/// </list>
/// Ein Wert zählt nur, wenn er echt ist (nicht leer, „?“, „-“). Gelesen wird bei jedem Aufruf — korrigierte Kopfdaten oder
/// eine nachträgliche Liga-Zuordnung erscheinen also ohne weiteres Zutun. Online-Partien (chess.com/lichess) tragen keins:
/// dort steht nur „Rated blitz game“ o. Ä. im Kopf.
/// </summary>
internal static class SavedGameEventLabel
{
    /// <summary>So viel vom PGN-Anfang wird gelesen — der Kopf steht vorne, ein langes kommentiertes PGN kostet nichts.</summary>
    internal const int HeadChars = 2000;

    internal static string? Of(string? leagueName, int? leagueRound, string? clubEvent, string? pgnHead)
    {
        if (Real(leagueName) is { } league) return WithRound(league, leagueRound?.ToString());
        if (Real(clubEvent) is { } club) return club;
        if (string.IsNullOrEmpty(pgnHead)) return null;
        return Real(Header(pgnHead, "Event")) is { } ev ? WithRound(ev, Real(Header(pgnHead, "Round"))) : null;
    }

    private static string WithRound(string name, string? round) => round is null ? name : $"{name} · R{round}";

    /// <summary>Echter Wert: getrimmt, nicht leer und nicht nur „?“/„-“/„.“.</summary>
    internal static string? Real(string? value)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) || v.All(c => c is '?' or '-' or '.' or ' ') ? null : v;
    }

    private static string? Header(string pgn, string tag)
    {
        var m = Regex.Match(pgn, $"\\[{tag}\\s+\"([^\"\\n]*)\"\\]");
        return m.Success ? m.Groups[1].Value : null;
    }
}
