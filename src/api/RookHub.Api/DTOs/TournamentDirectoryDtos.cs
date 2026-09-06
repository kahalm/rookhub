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

    public static DirectoryEntryDto FromEntity(TournamentDirectoryEntry e, double? distanceKm = null, bool subscribed = false) => new()
    {
        ChessResultsId = e.ChessResultsId,
        Name = e.Name,
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
        PlayerCount = e.PlayerCount,
        Lat = e.Lat,
        Lon = e.Lon,
        GeoSource = e.GeoSource.ToString(),
        GeoPlaceName = e.GeoPlaceName,
        DistanceKm = distanceKm is null ? null : Math.Round(distanceKm.Value, 1),
        Cancelled = e.RemovedAt != null,
        Subscribed = subscribed,
    };
}

public class DirectoryPageDto
{
    public List<DirectoryEntryDto> Items { get; set; } = [];
    public int Total { get; set; }
    /// <summary>true, wenn der Umkreis-Vorfilter die Obergrenze erreicht hat - Radius verkleinern.</summary>
    public bool Truncated { get; set; }
}

/// <summary>Ein Tag im Kalender mit den an diesem Tag LAUFENDEN Turnieren.</summary>
public class DirectoryCalendarDayDto
{
    public DateOnly Date { get; set; }
    public List<DirectoryEntryDto> Items { get; set; } = [];
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
