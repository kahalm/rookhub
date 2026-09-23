namespace RookHub.Api.DTOs;

/// <summary>Anfrage an <c>POST /api/repertoires/{id}/explorer-analysis</c> (Lochfinder und Trainer-Modus
/// „Häufigste zuerst").</summary>
public class ExplorerAnalysisRequestDto
{
    /// <summary><c>"w"</c>/<c>"b"</c> = nur die Kapitel dieser Farbe; <c>null</c> = alle.</summary>
    public string? Color { get; set; }

    /// <summary>Trainingsfarbe je Kapitel (<c>[Black]</c>-Header, getrimmt) — rechnet der Client wie im
    /// Trainer aus (Auto-Erkennung + eigene Festlegung). Fehlendes Kapitel = Weiß, wie im Trainer.</summary>
    public Dictionary<string, string>? ChapterColors { get; set; }

    /// <summary><c>"online"</c> (Standard, explorer.lichess.ovh) oder <c>"local"</c> (eigener Explorer im
    /// Stack, nur wenn <c>LichessExplorer:LocalUrl</c> gesetzt ist).</summary>
    public string? Source { get; set; }

    /// <summary><c>"lichess"</c> (Standard) oder <c>"masters"</c>.</summary>
    public string? Database { get; set; }

    /// <summary>Elo-Stufen der Lichess-Datenbank (0, 1000, 1200, …, 2500).</summary>
    public List<int>? Ratings { get; set; }

    /// <summary>Bedenkzeiten der Lichess-Datenbank (bullet, blitz, rapid, classical, …).</summary>
    public List<string>? Speeds { get; set; }

    /// <summary>Ab diesem Anteil (in Prozent der Partien in der Stellung) ist ein fehlender Gegnerzug
    /// ein Loch. 0,1 bis 50.</summary>
    public double ThresholdPercent { get; set; } = 1.0;

    public bool IncludeHoles { get; set; } = true;

    public bool IncludeLineFrequencies { get; set; }
}

public class ExplorerAnalysisResultDto
{
    /// <summary>Alle nötigen Stellungen sind ausgewertet. Sonst: erneut anfragen — was schon abgefragt
    /// wurde, liegt im Speicher, und der nächste Aufruf macht dort weiter.</summary>
    public bool Complete { get; set; }
    public int PositionsAnalyzed { get; set; }
    public int PositionsPending { get; set; }

    /// <summary>Lichess hat gebremst (429) — die Leitung ruht noch <see cref="RetryAfterSeconds"/>.</summary>
    public bool RateLimited { get; set; }
    public int? RetryAfterSeconds { get; set; }

    /// <summary>Kein Token für den Explorer (weder am Server noch beim Nutzer hinterlegt).</summary>
    public bool TokenMissing { get; set; }
    /// <summary>Lichess hat den Token abgelehnt.</summary>
    public bool TokenInvalid { get; set; }
    /// <summary>Der Explorer war in diesem Aufruf wiederholt nicht erreichbar.</summary>
    public bool FetchFailed { get; set; }

    /// <summary>Nach Häufigkeit absteigend, höchstens 300.</summary>
    public List<RepertoireHoleDto> Holes { get; set; } = new();

    /// <summary>Endstellung einer Linie (die ersten drei FEN-Felder) → wie oft man sie erreicht (0…1).
    /// Nur mit <see cref="ExplorerAnalysisRequestDto.IncludeLineFrequencies"/>.</summary>
    public Dictionary<string, double>? LineFrequencies { get; set; }
}

public class RepertoireHoleDto
{
    /// <summary>Farbe der Kapitel, in denen das Loch liegt (<c>"w"</c>/<c>"b"</c>).</summary>
    public string Color { get; set; } = "w";
    /// <summary>Stellung VOR dem fehlenden Gegnerzug.</summary>
    public string Fen { get; set; } = string.Empty;
    /// <summary>Startstellung der Zugfolge, falls nicht die Grundstellung.</summary>
    public string? StartFen { get; set; }
    /// <summary>Zugfolge (SAN) von der Startstellung bis <see cref="Fen"/>.</summary>
    public List<string> Path { get; set; } = new();
    public string San { get; set; } = string.Empty;
    public string Uci { get; set; } = string.Empty;
    /// <summary>Anteil des Zugs an allen Partien in der Stellung (0…1).</summary>
    public double Share { get; set; }
    /// <summary>Partien mit diesem Zug.</summary>
    public long Games { get; set; }
    /// <summary>Partien in der Stellung.</summary>
    public long PositionGames { get; set; }
    /// <summary>Wie oft man dieses Loch mit dem Repertoire erreicht (0…1).</summary>
    public double Frequency { get; set; }
    public string? Opening { get; set; }
    public string? Eco { get; set; }
}

/// <summary>Welche Explorer-Quellen dieser Server anbietet (<c>GET /api/repertoires/explorer/sources</c>).</summary>
public class ExplorerSourcesDto
{
    public bool Online { get; set; } = true;
    public bool Local { get; set; }
    /// <summary>Elo-Stufen und Bedenkzeiten, für die der lokale Bestand Partien hat.</summary>
    public List<int> LocalRatings { get; set; } = new();
    public List<string> LocalSpeeds { get; set; } = new();
}

/// <summary>Antwort von <c>GET /api/explorer/position</c>: die Zugstatistik EINER Stellung für den
/// Eröffnungs-Explorer auf dem Analysebrett.</summary>
public class ExplorerPositionResultDto
{
    /// <summary><c>ok</c>, <c>tokenMissing</c>, <c>tokenInvalid</c>, <c>rateLimited</c> oder <c>failed</c>.</summary>
    public string Status { get; set; } = "ok";
    public int? RetryAfterSeconds { get; set; }
    public string Source { get; set; } = "online";
    public string Database { get; set; } = "lichess";
    public long Total { get; set; }
    public long White { get; set; }
    public long Draws { get; set; }
    public long Black { get; set; }
    /// <summary>Eröffnung der Stellung selbst (falls der Explorer sie kennt).</summary>
    public string? Opening { get; set; }
    public string? Eco { get; set; }
    /// <summary>Züge nach Häufigkeit, wie der Explorer sie liefert (höchstens 40).</summary>
    public List<ExplorerPositionMoveDto> Moves { get; set; } = new();
}

public class ExplorerPositionMoveDto
{
    public string Uci { get; set; } = string.Empty;
    public string San { get; set; } = string.Empty;
    public long Games { get; set; }
    public long White { get; set; }
    public long Draws { get; set; }
    public long Black { get; set; }
    public int? AverageRating { get; set; }
    /// <summary>Eröffnung NACH diesem Zug.</summary>
    public string? Opening { get; set; }
    public string? Eco { get; set; }
}
