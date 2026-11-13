using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// Rechnet einmalig die Mattbilder der beliebten Matt-Aufgaben (<see cref="MatePattern"/>, 0.960.0) und danach die taktischen Motive der
/// übrigen beliebten Aufgaben (<see cref="TacticMotifs"/>, 0.961.0, <see cref="RunTacticBatchAsync"/>) in <see cref="PuzzleSignature"/> —
/// wiederaufnehmbar, klein portioniert, pausierbar über den Schalter „Hintergrundjobs“.
///
/// <para><b>Fortschritt</b> steht in <see cref="SystemSetting"/> unter <see cref="CursorKey"/> als <c>v{Version}:{letzte PuzzleId}</c>;
/// ein neuer <see cref="MatePattern.Version"/> beginnt wieder bei 0 und überschreibt. Je Schritt höchstens <see cref="BatchSize"/>
/// Aufgaben in Id-Reihenfolge (Bereich über den Primärschlüssel), dann <see cref="Interval"/> Pause; ist der Bestand durch, sieht der
/// Dienst nur noch alle <see cref="IdleInterval"/> nach neuen Aufgaben.</para>
///
/// <para><b>Rücksicht auf die Datenbank</b>: in den Fenstern von <c>PuzzleSignatures:QuietHours</c> (Vorgabe <see cref="DefaultQuietHours"/>,
/// Europe/Vienna — Proxmox-Sicherung ab 01:00 und Datenbank-Abzug 04:30) ruht er; Datenbankfehler lassen die Portion offen, der nächste
/// Takt versucht sie erneut. <c>PuzzleSignatures:Enabled=false</c> schaltet ihn ab.</para>
///
/// <para>Umfang Prod (13.11.2026): 1,58 Mio. Matt-Aufgaben, davon ~1,25 Mio. mit Beliebtheit ≥ <see cref="MinPopularity"/> → ~1 250
/// Schritte; mit Pause ≈ 1,5–2 h.</para>
/// </summary>
public class PuzzleSignatureWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<PuzzleSignatureWorker> logger)
    : PeriodicWorker(scopeFactory, logger)
{
    public const string CursorKey = "PuzzleSignatures.MateCursor";
    /// <summary>Fortschritt der Motiv-Signaturen (0.961.0), <c>v{TacticMotifs.Version}:{letzte PuzzleId}</c>.</summary>
    public const string TacticCursorKey = "PuzzleSignatures.TacticCursor";
    /// <summary>Motive nur für oft gespielte Aufgaben (Lichess-Wertung erst dann belastbar) — dieselbe Schwelle wie die Auswahl.</summary>
    public const int MinPlays = 200;
    public const int BatchSize = 1000;
    /// <summary>Nur beliebte Aufgaben (Lichess-Beliebtheit −100..100) — die Übungen nehmen ohnehin nur solche.</summary>
    public const int MinPopularity = 80;
    public const string DefaultQuietHours = "Mon-Sun 00:40-01:40; Mon-Sun 04:15-05:00";
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(30);

    private readonly QuietHours _quiet = new(config["PuzzleSignatures:QuietHours"] ?? DefaultQuietHours, config["PuzzleSignatures:TimeZone"]);
    private volatile bool _idle;

    protected override WorkerSchedule Schedule => WorkerSchedule.Every(_idle ? IdleInterval : Interval);
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(5));
    protected override bool Enabled => config.GetValue("PuzzleSignatures:Enabled", true);
    protected override void LogDisabled() => Logger.LogInformation("Aufgaben-Signaturen: per Konfiguration abgeschaltet");
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "Aufgaben-Signaturen: Durchlauf fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        if (_quiet.IsQuietNow()) return;
        var db = services.GetRequiredService<AppDbContext>();
        // Erst die Mattbilder, dann die Motive (0.961.0) — beide in derselben Portionsgröße, je Schritt höchstens eine volle Portion.
        var n = await RunBatchAsync(db, BatchSize, ct);
        if (n < BatchSize) n = await RunTacticBatchAsync(db, BatchSize, ct);
        var wasIdle = _idle;
        _idle = n < BatchSize;
        if (_idle && !wasIdle) Logger.LogInformation("Aufgaben-Signaturen: Bestand durchgerechnet, sehe alle {Minutes} min nach neuen Aufgaben", IdleInterval.TotalMinutes);
    }

    /// <summary>Stand des Fortschritts: letzte bearbeitete PuzzleId im aktuellen Regelstand (0 = noch nichts).</summary>
    public static Task<int> CursorAsync(AppDbContext db, CancellationToken ct = default) => CursorAsync(db, CursorKey, MatePattern.Version, ct);

    private static async Task<int> CursorAsync(AppDbContext db, string key, short version, CancellationToken ct)
    {
        var value = await db.SystemSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        var prefix = $"v{version}:";
        return value is not null && value.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(value[prefix.Length..], out var id) ? id : 0;
    }

    /// <summary>Ist der Bestand im aktuellen Regelstand durchgerechnet (für den Bericht der Übungen: „Mattbilder fertig“)?</summary>
    public static async Task<bool> DoneAsync(AppDbContext db, CancellationToken ct = default)
    {
        var cursor = await CursorAsync(db, ct);
        return cursor > 0 && !await Candidates(db, cursor).AnyAsync(ct);
    }

    private static IQueryable<Puzzle> Candidates(AppDbContext db, int after) =>
        db.Puzzles.AsNoTracking().Where(p => p.Id > after && p.Popularity >= MinPopularity && p.Themes != null && p.Themes.Contains("mate"));

    /// <summary>Eine Portion: bis zu <paramref name="max"/> Matt-Aufgaben nach dem Fortschritt → Anzahl gelesener Aufgaben (öffentlich für Tests).</summary>
    public static async Task<int> RunBatchAsync(AppDbContext db, int max, CancellationToken ct = default)
    {
        var cursor = await CursorAsync(db, ct);
        var batch = await Candidates(db, cursor).OrderBy(p => p.Id).Take(max)
            .Select(p => new { p.Id, p.Fen, p.Moves, p.Rating }).ToListAsync(ct);
        if (batch.Count == 0) return 0;
        var rows = new List<PuzzleSignature>(batch.Count);
        foreach (var p in batch)
        {
            var moves = p.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (MatePattern.Play(p.Fen, moves) is not { } final || MatePattern.Of(final) is not { } sig) continue;
            rows.Add(new PuzzleSignature
            {
                PuzzleId = p.Id, Kind = PuzzleSignatureKind.Mate, Version = MatePattern.Version,
                Strict = sig.StrictHash, Family = sig.FamilyHash, Rating = p.Rating, MateIn = (byte)Math.Min(255, (moves.Length) / 2),
            });
        }
        var ids = batch.Select(p => p.Id).ToList();
        var last = ids[^1];
        // Ältere Regelstände dieser Aufgaben weg, dann neu (bei gleicher Version gibt es hier keine — der Fortschritt läuft nur vorwärts).
        // Alles in EINEM SaveChanges (eine Transaktion): Zeilen und Fortschritt kommen zusammen oder gar nicht.
        db.PuzzleSignatures.RemoveRange(await db.PuzzleSignatures.Where(s => s.Kind == PuzzleSignatureKind.Mate && ids.Contains(s.PuzzleId)).ToListAsync(ct));
        db.PuzzleSignatures.AddRange(rows);
        await SaveCursorAsync(db, CursorKey, MatePattern.Version, last, ct);
        db.ChangeTracker.Clear();
        return batch.Count;
    }

    /// <summary>Fortschritt der Motiv-Signaturen (letzte PuzzleId, 0 = noch nichts).</summary>
    public static Task<int> TacticCursorAsync(AppDbContext db, CancellationToken ct = default) =>
        CursorAsync(db, TacticCursorKey, TacticMotifs.Version, ct);

    /// <summary>
    /// Eine Portion Motiv-Signaturen (0.961.0): beliebte, oft gespielte Aufgaben OHNE Matt (die haben ihr Mattbild) → je erkanntem
    /// Motiv (<see cref="TacticMotifs"/>) eine Zeile. Prod (13.11.2026): ~2,4 Mio. Aufgaben → ~2 400 Schritte, mit Pause ≈ 3 h.
    /// </summary>
    public static async Task<int> RunTacticBatchAsync(AppDbContext db, int max, CancellationToken ct = default)
    {
        var cursor = await TacticCursorAsync(db, ct);
        var batch = await db.Puzzles.AsNoTracking()
            .Where(p => p.Id > cursor && p.Popularity >= MinPopularity && p.NbPlays >= MinPlays && (p.Themes == null || !p.Themes.Contains("mate")))
            .OrderBy(p => p.Id).Take(max)
            .Select(p => new { p.Id, p.Fen, p.Moves, p.Rating }).ToListAsync(ct);
        if (batch.Count == 0) return 0;
        var rows = new List<PuzzleSignature>();
        foreach (var p in batch)
            foreach (var m in TacticMotifs.OfPuzzle(p.Fen, p.Moves))
                rows.Add(new PuzzleSignature
                {
                    PuzzleId = p.Id, Kind = TacticMotifs.KindOf(m.Theme), Version = TacticMotifs.Version,
                    Strict = m.StrictHash, Family = m.FamilyHash, Rating = p.Rating,
                });
        var ids = batch.Select(p => p.Id).ToList();
        db.PuzzleSignatures.RemoveRange(await db.PuzzleSignatures.Where(s => s.Kind != PuzzleSignatureKind.Mate && ids.Contains(s.PuzzleId)).ToListAsync(ct));
        db.PuzzleSignatures.AddRange(rows);
        await SaveCursorAsync(db, TacticCursorKey, TacticMotifs.Version, ids[^1], ct);
        db.ChangeTracker.Clear();
        return batch.Count;
    }

    private static async Task SaveCursorAsync(AppDbContext db, string key, short version, int last, CancellationToken ct)
    {
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) db.SystemSettings.Add(row = new SystemSetting { Key = key });
        row.Value = $"v{version}:{last}";
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
