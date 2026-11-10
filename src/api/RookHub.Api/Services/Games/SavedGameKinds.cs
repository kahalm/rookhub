using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Laschen der Partienliste (<c>GET /api/games?kind=</c>, Wunsch 10.11.2026: „allgemeine saved games, online games, otb
/// games“). Alle Bedingungen laufen in der Datenbank, damit Suche, Deckel und Zählung dieselbe Menge sehen.
/// <list type="bullet">
/// <item><c>all</c> — alles (Vorgabe, auch für unbekannte Werte).</item>
/// <item><c>online</c> — Quelle chess.com oder lichess. Die Quelle entscheidet: eine mit einer Vereinspartie verbundene
/// Online-Partie bleibt Online und zählt NICHT als Brettpartie.</item>
/// <item><c>otb</c> — keine Online-Quelle UND (mit einer Vereinspartie verbunden ODER aus einem Partieformular ODER ein
/// PGN-Import, dessen Kopf auf eine Brettpartie deutet, <see cref="IsOtbHeadPattern"/>). Im Zweifel nicht.</item>
/// </list>
/// Eine Partie der Quelle <c>rookhub</c> (Analysebrett) ohne Verbindung steht nur unter „Alle“, ebenso ein PGN-Import
/// ohne Brett-Hinweis (auch einer mit lichess-Adresse im Kopf: die Lasche „Online“ hängt bewusst nur an der Quelle).
/// </summary>
internal static class SavedGameKinds
{
    public const string All = "all";
    public const string Online = "online";
    public const string Otb = "otb";

    /// <summary>Die Quellen der Lasche „Online“.</summary>
    internal static readonly string[] OnlineSources = ["chess.com", "lichess"];

    /// <summary>Kleinbuchstaben, getrimmt; alles Unbekannte (auch leer) = <see cref="All"/>.</summary>
    public static string Normalize(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        Online => Online,
        Otb => Otb,
        _ => All,
    };

    /// <summary>Kopfzeilen bis zur ersten Zugzeile (wie <see cref="SavedGameSearch.HeaderPattern"/>): auch ein langes
    /// kommentiertes PGN kostet nur seinen Kopf. Läuft in .NET (InMemory) und in MariaDB (PCRE2).</summary>
    private const string Head = @"(?i)\A\s*(?:\[[^\n]*\n)*?";

    /// <summary>
    /// Positiver Hinweis: <c>[Event]</c> oder <c>[Site]</c> trägt einen echten Wert (mindestens ein Buchstabe oder eine
    /// Ziffer, also nicht „?“, „-“ oder leer). Ein PGN ohne beides (ChessBase-Rohexport „?“) bleibt draußen.
    /// </summary>
    internal const string NamedHeadPattern = Head + @"\[(?:Event|Site) ""[^""\n]*[0-9a-z]";

    /// <summary>Werte, die nie als Online-Hinweis zählen: Adressen von Turnier-/Wertungsseiten (chess-results.com samt
    /// Unterseiten s1/s2…, fide.com samt ratings.fide.com; Fix 0.956.1 — 143 Brettpartien bekamen chess-results als Site).
    /// Als Vorausschau am Wertanfang, damit die ganze Kopfzeile übersprungen wird.</summary>
    private const string TournamentSiteValue = @"\s*(?:https?://)?(?:[a-z0-9-]+\.)*(?:chess-results\.com|fide\.com)(?:[/:?#""]|$)";

    /// <summary>Wert einer lichess-STUDIE/Übertragung (oder bloß „lichess.org“) — keine Partie-Adresse
    /// (lichess.org/&lt;8 Zeichen&gt;); zählt nur mit Turnier-Event als Brettpartie (<see cref="TournamentEventPattern"/>).</summary>
    private const string LichessStudyValue = @"\s*(?:https?://)?(?:www\.)?lichess\.org(?:/(?:study|broadcast)/[^""\n]*)?/?\s*""";

    private const string OnlineMarkers = @"(?:https?:|www\.|chess\.com|lichess|chess24|playchess|chesstempo|playok"
        + @"|online|offline|internet|rated|casual|live chess)";

    /// <summary>
    /// Ausschluss: <c>[Event]</c>, <c>[Site]</c> oder <c>[Link]</c> nennt eine Plattform, eine Adresse oder eine
    /// Online-/App-Partie („rated bullet game“, „Live Chess“, „iChessOne offline game“). Adressen von Turnierseiten
    /// (<see cref="TournamentSiteValue"/>) zählen nicht.
    /// </summary>
    internal const string OnlineHeadPattern = Head
        + @"\[(?:Event|Site|Link) ""(?!" + TournamentSiteValue + @")[^""\n]*" + OnlineMarkers;

    /// <summary>Wie <see cref="OnlineHeadPattern"/>, überspringt aber zusätzlich Site/Link einer lichess-Studie
    /// (<see cref="LichessStudyValue"/>). Eine Partie-Adresse lichess.org/&lt;id&gt; trifft weiter.</summary>
    internal const string OnlineHeadIgnoringStudyPattern = Head
        + @"\[(?:Event|Site|Link) ""(?!" + TournamentSiteValue + @")(?!" + LichessStudyValue + @")[^""\n]*" + OnlineMarkers;

    /// <summary>Event trägt einen echten Turniernamen (ganzes Wort Open, Turnier, Meisterschaft …). Nur für die
    /// lichess-Studie; ein Event mit Online-Wort („Rated …“, „Lichess Open“) fängt schon
    /// <see cref="OnlineHeadIgnoringStudyPattern"/>. Arena/Swiss/Titled zählen bewusst nicht.</summary>
    internal const string TournamentEventPattern = Head
        + @"\[Event ""[^""\n]*(?:\bopen\b|turnier|tournament|meisterschaft|championship|\bliga\b|league|\bcup\b"
        + @"|pokal|festival|memorial|olympia|olympiad|masters|mannschaft|landesliga|bundesliga|stadtmst|staatsmeister)";

    /// <summary>Für die Doku/Tests: ein PGN-Kopf gilt als Brettpartie, wenn <see cref="NamedHeadPattern"/> trifft und
    /// <see cref="OnlineHeadPattern"/> nicht — oder es eine lichess-Studie mit Turnier-Event ist (Turnier-Event UND
    /// <see cref="OnlineHeadIgnoringStudyPattern"/> trifft nicht). Die Bedenkzeit prüft <see cref="IsOtbTimeControl"/>.</summary>
    internal static bool IsOtbHeadPattern(string pgn)
        => Regex.IsMatch(pgn, NamedHeadPattern)
           && (!Regex.IsMatch(pgn, OnlineHeadPattern)
               || (Regex.IsMatch(pgn, TournamentEventPattern) && !Regex.IsMatch(pgn, OnlineHeadIgnoringStudyPattern)));

    /// <summary>
    /// Bedenkzeit einer PGN-Partie: fehlend oder unverständlich (<c>-</c>, <c>40/7200:3600</c>) sagt nichts; eine
    /// lesbare muss klassisch sein (<see cref="GameClassifier.Mode"/> = Classical, Grundzeit + 40 × Inkrement ≥ 25 min).
    /// Schnellschach, Blitz, Bullet und Fernschach („Daily“) zählen nicht als Brettpartie — im Zweifel nicht.
    /// </summary>
    internal static bool IsOtbTimeControl(string? timeControl)
    {
        var mode = GameClassifier.Mode(timeControl);
        return mode == null || mode == "Classical";
    }

    /// <summary>Schränkt die (schon auf den Besitzer gefilterten) Partien auf die Lasche ein.</summary>
    internal static async Task<IQueryable<SavedGame>> ApplyAsync(IQueryable<SavedGame> games, string? kind,
        CancellationToken ct = default)
    {
        switch (Normalize(kind))
        {
            case Online:
                return games.Where(g => OnlineSources.Contains(g.Source));
            case Otb:
                var notOtbTimeControls = await NotOtbTimeControlsAsync(games, ct);
                return games.Where(OtbPredicate(notOtbTimeControls));
            default:
                return games;
        }
    }

    /// <summary>Die Bedenkzeiten der PGN-Importe, die eine Brettpartie ausschließen. Wenige verschiedene Werte je Konto —
    /// geprüft in C# (<see cref="GameClassifier.Mode"/>), gefiltert per <c>IN</c>, statt die Rechnung in SQL nachzubauen.</summary>
    private static async Task<List<string>> NotOtbTimeControlsAsync(IQueryable<SavedGame> games, CancellationToken ct)
    {
        var values = await games.Where(g => g.Source == SavedGameService.ImportSource && g.TimeControl != null)
            .Select(g => g.TimeControl!).Distinct().ToListAsync(ct);
        return values.Where(tc => !IsOtbTimeControl(tc)).ToList();
    }

    private static System.Linq.Expressions.Expression<Func<SavedGame, bool>> OtbPredicate(List<string> notOtbTimeControls)
        => g => !OnlineSources.Contains(g.Source)
            && (g.LeagueClubGameId != null
                || g.Source == SavedGameService.ScoresheetSource
                || (g.Source == SavedGameService.ImportSource
                    && (g.TimeControl == null || !notOtbTimeControls.Contains(g.TimeControl))
                    && Regex.IsMatch(g.Pgn, NamedHeadPattern)
                    && (!Regex.IsMatch(g.Pgn, OnlineHeadPattern)
                        || (Regex.IsMatch(g.Pgn, TournamentEventPattern)
                            && !Regex.IsMatch(g.Pgn, OnlineHeadIgnoringStudyPattern)))));

    /// <summary>Zählung je Lasche über alle Partien des Kontos (ohne Suche) — drei <c>COUNT</c>.</summary>
    internal static async Task<(int All, int Online, int Otb)> CountAsync(IQueryable<SavedGame> games, CancellationToken ct = default)
    {
        var all = await games.CountAsync(ct);
        var online = await (await ApplyAsync(games, Online, ct)).CountAsync(ct);
        var otb = await (await ApplyAsync(games, Otb, ct)).CountAsync(ct);
        return (all, online, otb);
    }
}
