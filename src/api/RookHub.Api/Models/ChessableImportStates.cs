namespace RookHub.Api.Models;

/// <summary>Grobzustand eines Chessable-Imports.</summary>
public enum ChessableImportStatus
{
    /// <summary>Wartet in der Queue oder ist in Arbeit — die Feinstufe steht in <see cref="ChessableImportPhase"/>.</summary>
    Running,
    /// <summary>Angehalten (Tageslimit, toter Bearer, Nutzer-Pause). Wird fortgesetzt, kein Endzustand.</summary>
    Paused,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Feinstufe innerhalb von <see cref="ChessableImportStatus.Running"/>.</summary>
public enum ChessableImportPhase
{
    /// <summary>In der Queue, kein Worker hat ihn übernommen.</summary>
    Queued,
    /// <summary>Von einem Worker beansprucht — ab hier gilt der Job als „inflight".</summary>
    Claimed,
    Fetching,
    Importing,
    Done,
    /// <summary>Angehalten, weil der Chessable-Bearer tot ist (Circuit-Breaker). Kein Endzustand.</summary>
    BearerBlocked,
    /// <summary>Angehalten wegen Tages-Zeilenlimit. Kein Endzustand.</summary>
    RateLimited,
}

/// <summary>
/// Übersetzung zwischen den Zuständen und ihren Zeichenketten.
///
/// <para><b>Die Zeichenketten sind ein VERTRAG, kein Implementierungsdetail.</b> Sie stehen so in
/// der Datenbank (Werte-Konverter in <c>AppDbContext</c>, deshalb braucht die Umstellung keine
/// Migration) UND gehen so über die API hinaus: das Frontend und die RepCheck-Erweiterung
/// vergleichen sie wörtlich (<c>imp.status === 'completed'</c>). Die Enum-NAMEN darf man
/// umbenennen, diese Zeichenketten nicht.</para>
///
/// <para>Grund für die Umstellung: Die Werte lagen als Zeichenketten über 8 Dateien verstreut.
/// Ein Tippfehler war dort kein Compilerfehler, sondern ein stiller Fehler — ein Job in
/// <c>"runnning"</c> wäre von keiner Abfrage je wieder gefunden worden. Nebenbei kam heraus, dass
/// die Doku am Feld nur drei der fünf tatsächlich benutzten Status-Werte nannte.</para>
/// </summary>
public static class ChessableImportStates
{
    public static string ToWire(this ChessableImportStatus s) => s switch
    {
        ChessableImportStatus.Running => "running",
        ChessableImportStatus.Paused => "paused",
        ChessableImportStatus.Completed => "completed",
        ChessableImportStatus.Failed => "failed",
        ChessableImportStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unbekannter Import-Status"),
    };

    public static string ToWire(this ChessableImportPhase p) => p switch
    {
        ChessableImportPhase.Queued => "queued",
        ChessableImportPhase.Claimed => "claimed",
        ChessableImportPhase.Fetching => "fetching",
        ChessableImportPhase.Importing => "importing",
        ChessableImportPhase.Done => "done",
        ChessableImportPhase.BearerBlocked => "bearer-blocked",
        ChessableImportPhase.RateLimited => "rate-limited",
        _ => throw new ArgumentOutOfRangeException(nameof(p), p, "Unbekannte Import-Phase"),
    };

    /// <summary>Aus der Datenbank gelesen. Unbekannte Altwerte NICHT verschlucken: ein stumm auf
    /// „running" gemappter Fremdwert liefe als Geisterjob weiter mit.</summary>
    public static ChessableImportStatus ParseStatus(string raw) => raw switch
    {
        "running" => ChessableImportStatus.Running,
        "paused" => ChessableImportStatus.Paused,
        "completed" => ChessableImportStatus.Completed,
        "failed" => ChessableImportStatus.Failed,
        "cancelled" => ChessableImportStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unbekannter Import-Status in der Datenbank: '{raw}'"),
    };

    public static ChessableImportPhase ParsePhase(string raw) => raw switch
    {
        "queued" => ChessableImportPhase.Queued,
        "claimed" => ChessableImportPhase.Claimed,
        "fetching" => ChessableImportPhase.Fetching,
        "importing" => ChessableImportPhase.Importing,
        "done" => ChessableImportPhase.Done,
        "bearer-blocked" => ChessableImportPhase.BearerBlocked,
        "rate-limited" => ChessableImportPhase.RateLimited,
        _ => throw new InvalidOperationException($"Unbekannte Import-Phase in der Datenbank: '{raw}'"),
    };

    /// <summary>Phasen, in denen ein Job als aktiv gilt (ein Worker hat ihn übernommen).</summary>
    public static readonly ChessableImportPhase[] Inflight =
        { ChessableImportPhase.Claimed, ChessableImportPhase.Fetching, ChessableImportPhase.Importing };
}
