using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.DTOs;

/// <summary>Der Turnierverlauf EINES Kontos.</summary>
public class PlayerHistoryDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// „ok", „noName" (kein Nachname im Profil — die Spielersuche laesst sich nicht stellen) oder
    /// „sourceUnavailable" (chess-results nicht erreichbar; ein vorhandener Zwischenspeicher gilt
    /// weiter). Ein Grund ist besser als eine leere Tabelle: „trage deinen Namen ein" ist eine
    /// Handlungsanweisung, „keine Turniere" waere eine Falschaussage.
    /// </summary>
    public string Status { get; set; } = "ok";

    public List<PlayerHistoryEntryDto> Entries { get; set; } = [];

    /// <summary>
    /// Wie viele Ergebnisse noch geholt werden. Jedes kostet einen Seitenabruf und laeuft im
    /// Hintergrund — solange die Zahl groesser als null ist, lohnt sich ein zweiter Blick, und
    /// die Ansicht kann sich selbst nachladen statt eine halbe Tabelle als endgueltig auszugeben.
    /// </summary>
    public int Pending { get; set; }

    public static PlayerHistoryDto From(TournamentHistoryService.PlayerHistory history) => new()
    {
        UserId = history.UserId,
        DisplayName = history.DisplayName,
        Status = history.Status switch
        {
            TournamentHistoryService.HistoryStatus.NoName => "noName",
            TournamentHistoryService.HistoryStatus.SourceUnavailable => "sourceUnavailable",
            _ => "ok",
        },
        Pending = history.PendingResults,
        Entries = history.Results
            .Select(r => PlayerHistoryEntryDto.From(
                r, history.Speeds.GetValueOrDefault(r.ChessResultsId, TournamentSpeed.Unknown)))
            .ToList(),
    };
}

/// <summary>Ein Turnier im Verlauf.</summary>
public class PlayerHistoryEntryDto
{
    public string ChessResultsId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateOnly? EndDate { get; set; }
    public int? Rank { get; set; }
    public int? PlayerCount { get; set; }
    public int? Rounds { get; set; }

    /// <summary>
    /// Tatsaechlich gespielte Partien — nicht die Rundenzahl: in einer Liga wird ein Spieler an
    /// einem TEIL der Termine aufgestellt. <c>null</c> bei Karten, die vor dieser Zaehlung geholt
    /// wurden; der naechtliche Durchgang traegt sie einmalig nach.
    /// </summary>
    public int? GamesPlayed { get; set; }

    public decimal? Points { get; set; }
    /// <summary>Turnier-Leistung — die Zahl, um die es hier eigentlich geht.</summary>
    public int? PerformanceRating { get; set; }
    public decimal? RatingChange { get; set; }
    /// <summary>Wertung zu Turnierbeginn — der Bezugspunkt der Performance.</summary>
    public int? RatingBefore { get; set; }

    /// <summary>
    /// Ergebnis vorhanden? `false` heisst entweder „noch nicht gespielt" oder „wird gerade
    /// geholt" — die Unterscheidung liefert das Enddatum.
    /// </summary>
    public bool HasResult { get; set; }

    /// <summary>
    /// Bedenkzeit-Klasse: „standard" (Turnierschach), „rapid" (Schnellschach), „blitz" — oder
    /// „unknown", solange die Turnierseite dafuer noch nicht geholt wurde. Sie steht hier, weil
    /// eine Performance im Blitz und eine im Turnierschach zwei verschiedene Zahlen sind, auch
    /// wenn beide „Performance" heissen.
    /// </summary>
    public string Speed { get; set; } = "unknown";

    /// <summary>
    /// Wurde die Spielerkarte schon abgerufen? Trennt die zwei Faelle hinter einem fehlenden
    /// Ergebnis: „wird noch geholt" (false) und „chess-results fuehrt hier keines" (true, etwa
    /// wenn die Karte des Turniers ueber diese Startnummer keinen Player-info-Block hat). Ohne
    /// das stand in beiden Faellen „noch kein Ergebnis" — und im zweiten wartete man vergebens.
    /// </summary>
    public bool CardFetched { get; set; }

    public static PlayerHistoryEntryDto From(PlayerTournamentResult r, TournamentSpeed speed) => new()
    {
        Speed = speed switch
        {
            TournamentSpeed.Standard => "standard",
            TournamentSpeed.Rapid => "rapid",
            TournamentSpeed.Blitz => "blitz",
            _ => "unknown",
        },
        ChessResultsId = r.ChessResultsId,
        Name = r.TournamentName,
        EndDate = r.EndDate,
        Rank = r.Rank,
        PlayerCount = r.PlayerCount,
        Rounds = r.Rounds,
        GamesPlayed = r.GamesPlayed,
        Points = r.Points,
        PerformanceRating = r.PerformanceRating,
        RatingChange = r.RatingChange,
        RatingBefore = r.RatingInternational,
        HasResult = r.Points is not null || r.PerformanceRating is not null,
        CardFetched = r.CardFetchedAt is not null,
    };
}

/// <summary>Ein Freund, dessen Verlauf sich ansehen laesst.</summary>
public class HistoryFriendDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Traegt das Profil eine FIDE- oder chess-results-Kennung? Wenn nicht, sucht die Historie
    /// ueber den NAMEN und findet damit auch Namensgleiche. Das gehoert gesagt.
    /// </summary>
    public bool Exact { get; set; }

    /// <summary>
    /// Steht ein Nachname im Profil? Ohne ihn gibt es keine Spielersuche und damit keinen Verlauf.
    /// Solche Freunde werden trotzdem aufgefuehrt — nur nicht auswaehlbar: eine leere Auswahl
    /// nennt keinen Grund, ein ausgegrauter Eintrag schon.
    /// </summary>
    public bool HasName { get; set; }
}
