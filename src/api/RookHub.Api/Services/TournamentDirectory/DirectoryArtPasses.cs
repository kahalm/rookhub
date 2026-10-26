using System.Text.Json;
using RookHub.Api.Models;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Die Turnierart-Durchgaenge der chess-results-Turniersuche (zweite Zerlegungsrunde W7b aus
/// <see cref="TournamentDirectoryService"/>): vier zusaetzliche Abfragen je Foederation beantworten, ob ein Turnier
/// Einzel oder Mannschaft und Schweizer System oder Rundenturnier ist.
/// </summary>
internal static class DirectoryArtPasses
{
    /// <summary>
    /// Die vier chess-results-Turnierarten und was sie bedeuten. Sie sind das KREUZPRODUKT aus
    /// „Mannschaft?" und „welches System?" — wer die Durchgaenge ohnehin macht, bekommt beide
    /// Angaben aus derselben Abfrage. 2 und 3 sind die MANNSCHAFTSturniere (Rundenturnier bzw.
    /// Schweizer System fuer Mannschaften).
    /// </summary>
    internal static readonly (string Art, TournamentKind Kind, TournamentSystem System)[] Passes =
    [
        ("0", TournamentKind.Individual, TournamentSystem.Swiss),
        ("1", TournamentKind.Individual, TournamentSystem.RoundRobin),
        ("2", TournamentKind.Team,       TournamentSystem.RoundRobin),
        ("3", TournamentKind.Team,       TournamentSystem.Swiss),
    ];

    /// <summary>
    /// Welche Turnierart hat jedes Turnier dieser Foederation — Einzel oder Mannschaft, Schweizer
    /// System oder Rundenturnier?
    ///
    /// <para>Die chess-results-Turniersuche kennt die Turnierart als Suchfeld, und ihre vier
    /// Werte sind genau das Kreuzprodukt beider Fragen (siehe <see cref="Passes"/>). Vier
    /// zusaetzliche Abfragen je Foederation beantworten damit aus der QUELLE, was sonst am
    /// Turniernamen geraten werden muesste: „Liga" im Namen ist ein Indiz, „SK Aachen 2 - SF
    /// Katernberg" keines.</para>
    ///
    /// <para><b>Alles oder nichts.</b> Zwei Faelle geben <c>null</c> zurueck, also „unbekannt",
    /// und lassen Art UND System unangetastet: ein FEHLER (sonst wuerde ein Netzausfall den
    /// halben Bestand umschreiben) und eine ABGESCHNITTENE Liste (chess-results kappt bei
    /// <paramref name="maxRows"/> Zeilen — der fehlende Schwanz waere sonst lauter falsch eingeordnete
    /// Turniere). Das gilt fuer JEDEN der vier Durchgaenge: faellt einer aus, ist die Zuordnung
    /// unvollstaendig, und eine halbe Wahrheit ist hier schlechter als keine.</para>
    /// </summary>
    /// <param name="fetch">Holt eine Trefferliste vom Crawler (Pfad samt Abfrage).</param>
    /// <param name="log">Logger des Aufrufers — die Warnungen erscheinen unter seiner Kategorie wie vorher.</param>
    internal static async Task<Dictionary<string, (TournamentKind Kind, TournamentSystem System)>?> FetchMapAsync(
        Func<string, CancellationToken, Task<JsonElement>> fetch, ILogger log,
        string federation, DateOnly from, DateOnly to, int maxRows, CancellationToken ct)
    {
        var map = new Dictionary<string, (TournamentKind, TournamentSystem)>(StringComparer.Ordinal);
        foreach (var (art, kind, system) in Passes)
        {
            List<DirectorySearchRows.CrawlerDirectoryRow> rows;
            try
            {
                var path = $"/api/tournament-search?fed={Uri.EscapeDataString(federation)}" +
                           $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&maxRows={maxRows}&art={art}";
                rows = DirectorySearchRows.ParseRows(await fetch(path, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.LogWarning(ex,
                    "Turnierart-Durchgang {Federation} (Art {Art}) fehlgeschlagen — Art und System bleiben unveraendert",
                    federation, art);
                return null;
            }

            if (rows.Count >= maxRows)
            {
                log.LogWarning(
                    "Turnierart-Durchgang {Federation} (Art {Art}) bei {Rows} Zeilen abgeschnitten — " +
                    "Art und System bleiben unveraendert", federation, art, rows.Count);
                return null;
            }

            // Ein Turnier steht in genau EINER Art; taucht es doch zweimal auf, gilt der erste
            // Treffer, statt die Zuordnung stillschweigend zu ueberschreiben.
            foreach (var row in rows) map.TryAdd(row.ChessResultsId, (kind, system));
        }
        return map;
    }
}
