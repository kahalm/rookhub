using RookHub.Api.DTOs;

namespace RookHub.Api.Services.ChessBase;

/// <summary>
/// Der Upload-Weg der Vereins-Datenbank (0.598.0): hochgeladene Dateien → <see cref="ChessBaseFiles"/> → PGN. Ein PGN
/// entsteht dabei, keine Partie — die Seite legt es wie eine PGN-Datei als Entwurf ab und prüft es in der Übersicht.
/// Die Umwandlung rechnet (rund 1 ms je Partie, <see cref="MaxGames"/> gewöhnliche Partien höchstens 5 s); gleichzeitig
/// laufen höchstens <see cref="MaxParallel"/>, damit ein Teilen-Link ohne Anmeldung den Server nicht auslasten kann.
/// Die 5 s gelten nur für echte Datenbanken: eine gebaute (Kopfsätze, die alle auf denselben 16-MB-Zugsatz zeigen,
/// Ströme aus Pendelzügen) rechnete Minuten und hielte dabei einen der beiden Plätze — deshalb das
/// <see cref="Budget"/>, das auch mitten in einer Partie abbricht, und der Deckel <see cref="MainlineBoard.MaxPlies"/>.
/// </summary>
public sealed class ChessBaseImportService(ILogger<ChessBaseImportService> logger)
{
    /// <summary>Höchstens so viele Partien je Datenbank. Die Übersicht nimmt 500 auf einmal — die Seite teilt das PGN
    /// dafür in mehrere offene Listen.</summary>
    public const int MaxGames = 5000;

    /// <summary>Rumpf-Grenze: die allgemeine <c>/api/</c>-Regel des Frontend-nginx lässt 15 MB durch. Die Seite packt
    /// die Dateien vorher (gzip, eine kommentierte <c>.cbg</c> auf ein Siebtel) — ausgepackt gilt
    /// <see cref="ChessBaseFiles.MaxTotalBytes"/>.</summary>
    public const int MaxBodyBytes = 15 * 1024 * 1024;

    public const int MaxParallel = 2;

    /// <summary>So viele übersprungene Partien werden einzeln genannt; die Zahl steht trotzdem ganz da.</summary>
    public const int MaxListedSkips = 100;

    /// <summary>So lange darf eine Datenbank rechnen (ab dem freien Platz), danach <c>tooLarge</c>. Das Dreifache der
    /// 5 s, die 5000 echte Partien brauchen — Luft für einen Server, der nebenbei Analysen rechnet.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(15);

    /// <summary><see cref="DefaultBudget"/>; die Tests setzen es kürzer.</summary>
    internal TimeSpan Budget { get; init; } = DefaultBudget;

    private static readonly SemaphoreSlim Gate = new(MaxParallel, MaxParallel);

    /// <summary>→ das Ergebnis oder ein Grund-Code (<c>noFile</c>, <c>noDatabase</c>, <c>multipleDatabases</c>,
    /// <c>missingFile</c>, <c>tooLarge</c>, <c>invalidZip</c>, <c>invalidFile</c>, <c>unreadable</c>, <c>busy</c>) mit Meldung.</summary>
    public async Task<(LeagueClubChessBaseResultDto? Result, string? Reason, string? Message)> ConvertAsync(
        IReadOnlyCollection<IFormFile>? files, CancellationToken ct)
    {
        if (files == null || files.Count == 0) return (null, "noFile", "Keine Datei.");
        var uploads = new List<(string, byte[])>(files.Count);
        long total = 0;
        foreach (var f in files)
        {
            total += f.Length;
            if (total > MaxBodyBytes) return (null, "tooLarge", "Die Dateien sind zu groß.");
            using var ms = new MemoryStream((int)f.Length);
            await f.CopyToAsync(ms, ct);
            uploads.Add((f.FileName, ms.ToArray()));
        }

        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(30), ct))
            return (null, "busy", "Gerade werden andere Datenbanken gelesen — bitte gleich noch einmal.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);
        try
        {
            var db = ChessBaseFiles.FromUploads(uploads);
            var r = ChessBaseConverter.Convert(db, MaxGames, budget.Token);
            var skipped = r.Games.Where(g => g.Error != null).ToList();
            logger.LogInformation(
                "ChessBase-Import {Format} {Name}: {Games} Partien, {Converted} gelesen, {Skipped} übersprungen, {Deleted} gelöscht, gekappt {Truncated}",
                db.Format, db.Name, r.Games.Count, r.Converted, skipped.Count, r.Deleted, r.Truncated);
            return (new LeagueClubChessBaseResultDto
            {
                Format = db.Format == ChessBaseFormat.Cb2 ? "2cbh" : "cbh",
                Name = db.Name,
                Pgn = r.Pgn,
                Games = r.Games.Count,
                Converted = r.Converted,
                Deleted = r.Deleted,
                Truncated = r.Truncated,
                SkippedCount = skipped.Count,
                Skipped = skipped.Take(MaxListedSkips).Select(g => new LeagueClubChessBaseSkipDto
                {
                    Id = g.Id, White = g.White, Black = g.Black, Reason = g.Error!,
                }).ToList(),
            }, null, null);
        }
        catch (ChessBaseFormatException e)
        {
            return (null, e.Reason, e.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Das Budget, nicht der Client: so viel Arbeit steckt in keiner echten Vereins-Datenbank.
            logger.LogWarning("ChessBase-Import: Zeitbudget {Budget} überschritten ({Files})", Budget,
                string.Join(", ", uploads.Select(u => u.Item1)));
            return (null, "tooLarge", "Die Datenbank ist zu umfangreich, um sie in einem Schritt zu lesen.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Eine Datei, die anders aufgebaut ist als beschrieben (fremde Version, beschädigt): kein Serverfehler,
            // aber ins Log — nur so fällt eine ChessBase-Version auf, die wir nicht lesen.
            logger.LogWarning(e, "ChessBase-Import: Datenbank nicht lesbar ({Files})", string.Join(", ", uploads.Select(u => u.Item1)));
            return (null, "unreadable", "Die Datenbank lässt sich nicht lesen.");
        }
        finally
        {
            Gate.Release();
        }
    }
}
