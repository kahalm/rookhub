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
        Entries = history.Results.Select(PlayerHistoryEntryDto.From).ToList(),
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
    /// Wurde die Spielerkarte schon abgerufen? Trennt die zwei Faelle hinter einem fehlenden
    /// Ergebnis: „wird noch geholt" (false) und „chess-results fuehrt hier keines" (true, etwa
    /// wenn die Karte des Turniers ueber diese Startnummer keinen Player-info-Block hat). Ohne
    /// das stand in beiden Faellen „noch kein Ergebnis" — und im zweiten wartete man vergebens.
    /// </summary>
    public bool CardFetched { get; set; }

    public static PlayerHistoryEntryDto From(PlayerTournamentResult r) => new()
    {
        ChessResultsId = r.ChessResultsId,
        Name = r.TournamentName,
        EndDate = r.EndDate,
        Rank = r.Rank,
        PlayerCount = r.PlayerCount,
        Rounds = r.Rounds,
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
}
