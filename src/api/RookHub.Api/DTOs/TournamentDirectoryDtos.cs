using System.ComponentModel.DataAnnotations;
using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

/// <summary>Ein Verzeichniseintrag, wie ihn Liste, Kalender und Detailansicht brauchen.</summary>
public class DirectoryEntryDto
{
    public string ChessResultsId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Federation { get; set; }
    public string? State { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Location { get; set; }
    public string? TimeControl { get; set; }
    public string Speed { get; set; } = nameof(TournamentSpeed.Unknown);
    public string? Organizer { get; set; }
    public string? Director { get; set; }
    public string? ChiefArbiter { get; set; }
    public int? Rounds { get; set; }
    public int? PlayerCount { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    /// <summary>Herkunft der Koordinaten - "Region" heisst: nur ungefaehr, Bundesland-Mittelpunkt.</summary>
    public string GeoSource { get; set; } = nameof(Models.GeoSource.None);
    public string? GeoPlaceName { get; set; }
    /// <summary>Entfernung zum Suchmittelpunkt in km; nur bei einer Umkreissuche gesetzt.</summary>
    public double? DistanceKm { get; set; }
    public bool Cancelled { get; set; }
    public bool Subscribed { get; set; }

    /// <summary>
    /// Wie viele Gruppen desselben Turniers dieser Eintrag zusammenfasst (1 = einzelnes Turnier).
    /// chess-results fuehrt „Open Braunau 2026 A/B/C" als drei Zeilen; hier ist es eine.
    /// </summary>
    public int GroupSize { get; set; } = 1;

    /// <summary>Die Gruppen mit ihrer Beschriftung — leer, wo chess-results den Zusatz abschnitt.</summary>
    public List<DirectoryGroupMemberDto> Groups { get; set; } = [];

    public static DirectoryEntryDto FromEntity(
        TournamentDirectoryEntry e, double? distanceKm = null, bool subscribed = false,
        IReadOnlyList<TournamentDirectoryEntry>? groups = null) => new()
    {
        ChessResultsId = e.ChessResultsId,
        // Bei mehreren Gruppen der Name OHNE Kuerzel — „Open Braunau 2026" statt „… A".
        Name = groups is { Count: > 1 } ? (e.BaseName ?? e.Name) : e.Name,
        Federation = e.Federation,
        State = e.State,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        Location = e.LocationText,
        TimeControl = e.TimeControlText,
        Speed = e.Speed.ToString(),
        Organizer = e.Organizer,
        Director = e.Director,
        ChiefArbiter = e.ChiefArbiter,
        Rounds = e.Rounds,
        Lat = e.Lat,
        Lon = e.Lon,
        GeoSource = e.GeoSource.ToString(),
        GeoPlaceName = e.GeoPlaceName,
        DistanceKm = distanceKm is null ? null : Math.Round(distanceKm.Value, 1),
        Cancelled = e.RemovedAt != null,
        Subscribed = subscribed,
        GroupSize = groups?.Count ?? 1,
        // Die Teilnehmerzahl der Gruppen summiert sich — sie ist die Groesse des GANZEN Turniers.
        PlayerCount = groups is { Count: > 1 } ? groups.Sum(g => g.PlayerCount ?? 0) : e.PlayerCount,
        Groups = groups is { Count: > 1 }
            ? groups.Select(g => new DirectoryGroupMemberDto
            {
                ChessResultsId = g.ChessResultsId,
                Label = Services.TournamentNameGrouping.GroupLabel(g.Name),
                PlayerCount = g.PlayerCount,
                Rounds = g.Rounds,
            }).ToList()
            : [],
    };
}

/// <summary>Eine Gruppe (A/B/C) innerhalb eines zusammengefassten Turniers.</summary>
public class DirectoryGroupMemberDto
{
    public string ChessResultsId { get; set; } = "";
    /// <summary>„A", „Gruppe 2" — leer, wenn chess-results den Zusatz im Namen abgeschnitten hat.</summary>
    public string Label { get; set; } = "";
    public int? PlayerCount { get; set; }
    public int? Rounds { get; set; }
}

public class DirectoryPageDto
{
    public List<DirectoryEntryDto> Items { get; set; } = [];
    public int Total { get; set; }
    /// <summary>true, wenn der Umkreis-Vorfilter die Obergrenze erreicht hat - Radius verkleinern.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Ein Kalendermonat: die Turniere EINMAL, die Tage nur mit ihren Nummern.
///
/// <para>Vorher stand an jedem Tag der VOLLE Eintrag. Ein mehrtaegiges Turnier steht an jedem
/// seiner Tage, und ein Monat auf dem Dev-Server hatte damit 5962 Eintraege fuer 200 verschiedene
/// Turniere — 3 MB JSON, von denen 97 % Wiederholung waren. Der Kalender ist die Startseite der
/// Turnierseite, das war also der Aufbau JEDES Aufrufs.</para>
/// </summary>
public class DirectoryCalendarDto
{
    /// <summary>Jedes Turnier des Monats genau einmal.</summary>
    public List<DirectoryEntryDto> Tournaments { get; set; } = [];
    public List<DirectoryCalendarDayDto> Days { get; set; } = [];
}

/// <summary>Ein Tag im Kalender mit den Nummern der an diesem Tag LAUFENDEN Turniere.</summary>
public class DirectoryCalendarDayDto
{
    public DateOnly Date { get; set; }
    public List<string> Ids { get; set; } = [];
}

public class DirectorySearchProfileDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? PlaceQuery { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public int RadiusKm { get; set; }
    public List<string> Federations { get; set; } = [];
    public List<string> Speeds { get; set; } = [];
    public bool WeekendOnly { get; set; }
    public int? MinPlayers { get; set; }
    public bool NotifyNew { get; set; }
    public int SortOrder { get; set; }

    public static DirectorySearchProfileDto FromEntity(TournamentSearchProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        PlaceQuery = p.PlaceQuery,
        Lat = p.Lat,
        Lon = p.Lon,
        RadiusKm = p.RadiusKm,
        Federations = Services.TournamentDirectoryService.SplitCsv(p.Federations),
        Speeds = Services.TournamentDirectoryService.SplitCsv(p.Speeds),
        WeekendOnly = p.WeekendOnly,
        MinPlayers = p.MinPlayers,
        NotifyNew = p.NotifyNew,
        SortOrder = p.SortOrder,
    };
}

public class SearchProfileInputDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(200)]
    public string? PlaceQuery { get; set; }

    [Range(-90, 90)]
    public double Lat { get; set; }

    [Range(-180, 180)]
    public double Lon { get; set; }

    [Range(1, 2000)]
    public int RadiusKm { get; set; } = 100;

    public List<string>? Federations { get; set; }
    public List<string>? Speeds { get; set; }
    public bool WeekendOnly { get; set; }

    [Range(0, 10000)]
    public int? MinPlayers { get; set; }

    public bool NotifyNew { get; set; } = true;

    [Range(0, 1000)]
    public int SortOrder { get; set; }
}

/// <summary>Ein Ortsvorschlag fuer das Suchprofil-Formular.</summary>
public class GeoPlaceSuggestionDto
{
    public string Label { get; set; } = "";
    public string Country { get; set; } = "";
    public string? PostalCode { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
}
