using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Speichert/liest vom User auf chess.com/lichess gespeicherte Partien (RepCheck „Partie speichern").
/// Aus der SAN-Zugliste + Metadaten wird serverseitig ein PGN gebaut. Jede Partie bekommt ein
/// eindeutiges ShareToken für den öffentlichen Teilen-Link.
///
/// <para>Dazu „Partie analysieren" und die Bewertungskurve: die Partie wird ueber denselben Einwurf
/// wie auf der Punktepartie-Seite gerechnet (<see cref="GameAnalysisService.CreateForGuessAsync"/>,
/// Ursprung <see cref="GameAnalysisOrigin.SavedGame"/>), und <see cref="SavedGame.GameAnalysisId"/>
/// merkt sich, welche Analyse zur Partie gehoert.</para>
/// </summary>
public class SavedGameService
{
    private readonly AppDbContext _db;
    private readonly GameAnalysisService _analyses;
    private readonly RepertoireAnalyzeService _repertoires;

    public SavedGameService(AppDbContext db, GameAnalysisService analyses, RepertoireAnalyzeService repertoires)
    {
        _db = db;
        _analyses = analyses;
        _repertoires = repertoires;
    }

    private static readonly HashSet<string> AllowedSources = new(StringComparer.OrdinalIgnoreCase)
        { "chess.com", "lichess" };

    private static readonly HashSet<string> AllowedResults = new() { "1-0", "0-1", "1/2-1/2", "*" };

    /// <summary>Normalisiert die gemeldete Herkunft auf <c>chess.com</c>/<c>lichess</c> (sonst null).</summary>
    public static string? NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var s = source.Trim().ToLowerInvariant();
        if (s is "chesscom" or "chess.com" or "www.chess.com") return "chess.com";
        if (s is "lichess" or "lichess.org") return "lichess";
        return AllowedSources.Contains(s) ? s : null;
    }

    /// <summary>Legt eine gespeicherte Partie an (oder gibt die bereits vorhandene zurück, wenn
    /// dieselbe ExternalId für denselben User+Source schon existiert — verhindert Doppel-Klicks).
    /// Wirft bei ungültiger Eingabe.</summary>
    public async Task<SavedGameDetailDto> SaveAsync(int userId, SaveGameInputDto dto)
    {
        var source = NormalizeSource(dto.Source) ?? throw new ArgumentException("Invalid source.");
        var moves = (dto.Moves ?? new())
            .Select(m => (m ?? string.Empty).Trim())
            .Where(m => m.Length > 0)
            .ToList();
        if (moves.Count == 0) throw new ArgumentException("No moves.");
        if (moves.Count > 600) throw new ArgumentException("Too many moves (max 600 plies).");

        var externalId = string.IsNullOrWhiteSpace(dto.ExternalId) ? null : dto.ExternalId.Trim();

        var result = dto.Result?.Trim();
        if (result == null || !AllowedResults.Contains(result)) result = "*";

        // Dedup: gleicher User + Source + ExternalId → bestehende Partie. Wenn der neue
        // Save BESSER ist (mehr Züge ODER erstmals Elo), heilt er den Datensatz in-place
        // (gleiches ShareToken/Id) — so repariert ein Re-Save eine alt gespeicherte,
        // lückenhafte/Elo-lose Partie, ohne den Teilen-Link zu ändern.
        if (externalId != null)
        {
            var existing = await _db.SavedGames
                .FirstOrDefaultAsync(g => g.UserId == userId && g.Source == source && g.ExternalId == externalId);
            if (existing != null)
            {
                if (TryHeal(existing, moves, dto, result)) await _db.SaveChangesAsync();
                return MapDetail(existing);
            }
        }

        var entity = new SavedGame
        {
            UserId = userId,
            Source = source,
            ExternalId = externalId,
            White = Clip(dto.White, 120),
            Black = Clip(dto.Black, 120),
            Result = result,
            PlayedAt = dto.PlayedAt,
            SourceUrl = Clip(dto.SourceUrl, 1000),
            Pgn = BuildPgn(moves, dto, result),
            MoveCount = moves.Count,
            WhiteElo = PlausibleElo(dto.WhiteElo),
            BlackElo = PlausibleElo(dto.BlackElo),
            TimeControl = CleanTimeControl(dto.TimeControl),
            HeadersScanned = true,
            ShareToken = await GenerateUniqueTokenAsync(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.SavedGames.Add(entity);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (externalId != null && AuthService.IsUniqueViolation(ex))
        {
            // Race: ein paralleler Save derselben externen Partie hat den Unique-Index zuerst belegt.
            // Idempotent: getrackten Versuch verwerfen und die bereits gespeicherte Partie zurückgeben.
            _db.ChangeTracker.Clear();
            var existing = await _db.SavedGames.AsNoTracking()
                .FirstOrDefaultAsync(g => g.UserId == userId && g.Source == source && g.ExternalId == externalId);
            if (existing != null) return MapDetail(existing);
            throw;
        }
        return MapDetail(entity);
    }

    /// <summary>Hoechstens so viele Partie-IDs beantwortet eine Uebersichts-Abfrage.</summary>
    public const int MaxKnownLookup = 300;

    /// <summary>
    /// Welche dieser Plattform-Partien liegen schon bei RookHub? Fuer die Uebersicht auf chess.com/lichess:
    /// bekannte Partien tragen dort ein Haekchen statt des Sende-Knopfs. Nur Existenz und Analyse-Stand —
    /// keine Zuege, keine PGN.
    /// </summary>
    public async Task<List<KnownGameDto>> KnownAsync(int userId, string? source,
        IReadOnlyCollection<string>? externalIds, CancellationToken ct = default)
    {
        var src = NormalizeSource(source);
        var ids = (externalIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct()
            .Take(MaxKnownLookup)
            .ToList();
        if (src == null || ids.Count == 0) return new();

        var games = await _db.SavedGames.AsNoTracking()
            .Where(g => g.UserId == userId && g.Source == src && g.ExternalId != null && ids.Contains(g.ExternalId))
            .Select(g => new { g.Id, g.ExternalId, g.GameAnalysisId })
            .ToListAsync(ct);
        // AnalysisStatesAsync schluesselt nach GameAnalysis.Id — NICHT nach SavedGame.Id (wie ListAsync).
        var states = await AnalysisStatesAsync(
            games.Where(g => g.GameAnalysisId != null).Select(g => g.GameAnalysisId!.Value).Distinct().ToList());
        return games
            .Select(g => new KnownGameDto
            {
                ExternalId = g.ExternalId!,
                Id = g.Id,
                Analysis = g.GameAnalysisId != null && states.TryGetValue(g.GameAnalysisId.Value, out var a) ? a : null,
            })
            .ToList();
    }

    /// <summary>Gespeicherte Partien des Users, neueste zuerst (ohne PGN).</summary>
    public async Task<List<SavedGameDto>> ListAsync(int userId, int take = 200)
    {
        take = Math.Clamp(take, 1, 500);
        // Das PGN (LONGTEXT) wird NUR fuer Zeilen mitgeholt, deren Zaehler noch fehlt. Frueher
        // stand hier CountPlies(g.Pgn) in der Projektion - eine C#-Methode, die EF nicht
        // uebersetzen kann: die Spalte wanderte damit fuer JEDE der bis zu 500 Partien ueber die
        // Leitung, obwohl das Listen-DTO das PGN gar nicht traegt.
        var rows = await _db.SavedGames.AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.CreatedAt)
            .Take(take)
            .Select(g => new
            {
                g.Id, g.Source, g.White, g.Black, g.Result, g.PlayedAt,
                g.SourceUrl, g.ShareToken, g.MoveCount, g.CreatedAt, g.GameAnalysisId,
                g.WhiteElo, g.BlackElo, g.TimeControl, g.HeadersScanned,
                PgnIfUncounted = g.MoveCount == null ? g.Pgn : null,
                ScanId = _db.ScoresheetScans.Where(sc => sc.SavedGameId == g.Id).Select(sc => (int?)sc.Id).FirstOrDefault(),
            })
            .ToListAsync();

        // Wertungen des Altbestands aus dem PGN in die Spalten heben — portionsweise, weil dafuer das
        // LONGTEXT geladen werden muss. Eine EIGENE Abfrage statt eines zweiten Feldes in der Projektion
        // oben: dort gaebe es keinen Deckel, und die erste Liste eines Vielspielers zoege alle PGNs.
        var elos = await BackfillElosAsync(rows.Where(r => !r.HeadersScanned).Select(r => r.Id).ToList());

        var analyses = await AnalysisStatesAsync(
            rows.Where(r => r.GameAnalysisId != null).Select(r => r.GameAnalysisId!.Value).Distinct().ToList());

        var healed = new Dictionary<int, int>();
        foreach (var r in rows)
            if (r.MoveCount == null && r.PgnIfUncounted != null)
                healed[r.Id] = CountPlies(r.PgnIfUncounted);

        // Einmalig nachtragen, damit dieselbe Zeile ihr PGN nie wieder fuer den Zaehler hergibt.
        // Bewusst best-effort: schlaegt das Schreiben fehl, stimmt die Anzeige trotzdem.
        if (healed.Count > 0)
        {
            try
            {
                var ids = healed.Keys.ToList();
                var tracked = await _db.SavedGames.Where(g => ids.Contains(g.Id)).ToListAsync();
                foreach (var g in tracked) g.MoveCount = healed[g.Id];
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException) { /* Anzeige stimmt auch ohne den Nachtrag */ }
        }

        return rows.Select(r => new SavedGameDto
        {
            Id = r.Id,
            Source = r.Source,
            White = r.White,
            Black = r.Black,
            Result = r.Result,
            PlayedAt = r.PlayedAt,
            SourceUrl = r.SourceUrl,
            ShareToken = r.ShareToken,
            MoveCount = r.MoveCount ?? (healed.TryGetValue(r.Id, out var c) ? c : 0),
            CreatedAt = r.CreatedAt,
            WhiteElo = r.WhiteElo ?? (elos.TryGetValue(r.Id, out var e) ? e.White : null),
            BlackElo = r.BlackElo ?? (elos.TryGetValue(r.Id, out var e2) ? e2.Black : null),
            TimeControl = r.TimeControl,
            Analysis = r.GameAnalysisId is int aid && analyses.TryGetValue(aid, out var state) ? state : null,
            ScanId = r.ScanId,
        }).ToList();
    }

    /// <summary>So viele fertige Analysen ohne abgelegte Genauigkeit rechnet EIN Listenaufruf nach — der
    /// Altbestand von vor 0.515.0 ist klein, und die Liste fragt waehrend einer Rechnung alle zehn Sekunden.</summary>
    public const int AccuracyBackfillPerCall = 10;

    /// <summary>So viele Partien holt EIN Listenaufruf sich vor, um ihre Wertungen aus dem PGN
    /// nachzutragen (0.526.0). Gedeckelt, weil dafuer das PGN geladen wird.</summary>
    public const int HeaderBackfillPerCall = 50;

    /// <summary>
    /// Traegt die Wertungen aus dem PGN-Header in die Spalten nach und setzt
    /// <see cref="SavedGame.HeadersScanned"/> — auch bei einer Partie OHNE Elo-Header, sonst sieht
    /// dieselbe Zeile bei jedem Aufruf wieder „noch nicht nachgesehen" aus. Best-effort: schlaegt das
    /// Schreiben fehl, stimmt die Anzeige trotzdem (und der naechste Aufruf versucht es erneut).
    /// </summary>
    private async Task<Dictionary<int, (int? White, int? Black)>> BackfillElosAsync(List<int> ids)
    {
        var found = new Dictionary<int, (int? White, int? Black)>();
        if (ids.Count == 0) return found;
        try
        {
            var tracked = await _db.SavedGames.Where(g => ids.Contains(g.Id))
                .OrderByDescending(g => g.CreatedAt).Take(HeaderBackfillPerCall).ToListAsync();
            foreach (var g in tracked)
            {
                g.WhiteElo = ParseEloHeader(g.Pgn, "WhiteElo");
                g.BlackElo = ParseEloHeader(g.Pgn, "BlackElo");
                g.HeadersScanned = true;
                found[g.Id] = (g.WhiteElo, g.BlackElo);
            }
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException) { /* Anzeige stimmt auch ohne den Nachtrag */ }
        return found;
    }

    /// <summary>
    /// Stand der verknuepften Analysen fuer die Liste: Status, Fortschritt (EINE gruppierte Zaehlung ueber alle
    /// Ids statt einer Abfrage je Partie) und die abgelegte Genauigkeit. Eine FERTIGE Analyse ohne Genauigkeit
    /// (von vor 0.515.0) wird hier einmal nachgerechnet und geschrieben — best-effort, die Anzeige stimmt auch
    /// ohne den Nachtrag. Verweise ins Leere (Analyse geloescht) fehlen im Ergebnis, die Partie zeigt dann
    /// wieder den Knopf.
    /// </summary>
    private async Task<Dictionary<int, SavedGameAnalysisDto>> AnalysisStatesAsync(List<int> ids)
    {
        var result = new Dictionary<int, SavedGameAnalysisDto>();
        if (ids.Count == 0) return result;

        var heads = await _db.GameAnalyses.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Status, a.PlyCount, a.AccuracyWhite, a.AccuracyBlack })
            .ToListAsync();
        var analyzed = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => ids.Contains(p.GameAnalysisId) && p.CandidatesJson != null)
            .GroupBy(p => p.GameAnalysisId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var backfilled = 0;
        foreach (var h in heads)
        {
            var state = new SavedGameAnalysisDto
            {
                Status = h.Status.ToString().ToLowerInvariant(),
                Analyzed = analyzed.TryGetValue(h.Id, out var n) ? n : 0,
                Total = h.PlyCount,
                AccuracyWhite = h.AccuracyWhite,
                AccuracyBlack = h.AccuracyBlack,
            };
            if (h.Status == GameAnalysisStatus.Done && h.AccuracyWhite is null && h.AccuracyBlack is null
                && backfilled < AccuracyBackfillPerCall)
            {
                backfilled++;
                var positions = await _db.GameAnalysisPositions.AsNoTracking()
                    .Where(p => p.GameAnalysisId == h.Id).ToListAsync();
                var accuracy = GameAccuracy.FromPositions(positions, h.PlyCount);
                state.AccuracyWhite = accuracy.White;
                state.AccuracyBlack = accuracy.Black;
                try
                {
                    var tracked = await _db.GameAnalyses.FirstOrDefaultAsync(a => a.Id == h.Id);
                    if (tracked is not null)
                    {
                        tracked.AccuracyWhite = accuracy.White;
                        tracked.AccuracyBlack = accuracy.Black;
                        await _db.SaveChangesAsync();
                    }
                }
                catch (DbUpdateException) { /* Anzeige stimmt auch ohne den Nachtrag */ }
            }
            result[h.Id] = state;
        }
        return result;
    }

    /// <summary>Detail einer eigenen Partie inkl. PGN; null wenn nicht gefunden / fremd.</summary>
    public async Task<SavedGameDetailDto?> GetAsync(int userId, int id)
    {
        var g = await _db.SavedGames.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (g == null) return null;
        var dto = MapDetail(g);
        // Die eigene Partie-Seite (/games/{id}) startet aus der Sicht des Besitzers — dieselbe Regel wie
        // beim Teilen-Link, damit die beiden Seiten nicht verschieden herum aufgehen.
        var profile = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        dto.OwnerSide = DetermineOwnerSide(g, profile);
        dto.ScanId = await _db.ScoresheetScans.Where(sc => sc.SavedGameId == g.Id).Select(sc => (int?)sc.Id).FirstOrDefaultAsync();
        return dto;
    }

    /// <summary>Löscht eine eigene Partie; false wenn nicht gefunden / fremd.</summary>
    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var g = await _db.SavedGames.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (g == null) return false;
        // Das Formular-Foto geht mit (in MariaDB per Cascade — hier ausdrücklich, ohne das Foto zu laden).
        ScoresheetScanService.RemoveWithoutLoading(_db,
            await ScoresheetScanService.KeysAsync(_db.ScoresheetScans.Where(s => s.SavedGameId == id)));
        _db.SavedGames.Remove(g);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Öffentliche Sicht über das ShareToken; null wenn unbekannt.</summary>
    /// <param name="callerUserId">Angemeldeter Aufrufer (oder <c>null</c>): ist er der Besitzer, traegt die Antwort
    /// <see cref="SharedGameDto.OwnGameId"/> — die Seite wechselt dann auf seine eigene Ansicht.</param>
    public async Task<SharedGameDto?> GetSharedAsync(string token, int? callerUserId = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var g = await _db.SavedGames.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShareToken == token);
        if (g == null) return null;
        var profile = await _db.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == g.UserId);
        return new SharedGameDto
        {
            Source = g.Source,
            White = g.White,
            Black = g.Black,
            Result = g.Result,
            PlayedAt = g.PlayedAt,
            SourceUrl = g.SourceUrl,
            Pgn = g.Pgn,
            CreatedAt = g.CreatedAt,
            WhiteElo = ParseEloHeader(g.Pgn, "WhiteElo"),
            BlackElo = ParseEloHeader(g.Pgn, "BlackElo"),
            OwnerSide = DetermineOwnerSide(g, profile),
            OwnGameId = callerUserId is int caller && caller == g.UserId ? g.Id : null,
        };
    }

    // ── Analyse + Bewertungskurve ─────────────────────────────────────

    /// <summary>„Partie analysieren" an einer EIGENEN Partie; <c>null</c>, wenn es sie nicht gibt oder
    /// sie jemand anderem gehoert.</summary>
    public async Task<GameAnalyzeResultDto?> AnalyzeAsync(int userId, int savedGameId, CancellationToken ct = default)
    {
        var game = await _db.SavedGames.FirstOrDefaultAsync(g => g.Id == savedGameId && g.UserId == userId, ct);
        return game is null ? null : await AnalyzeCoreAsync(userId, game, ct);
    }

    /// <summary>„Partie analysieren" auf der geteilten Partie (<c>/g/{token}</c>) — das darf JEDER
    /// Angemeldete, genau dafuer steht der Knopf dort; <c>null</c> bei unbekanntem Token.</summary>
    public async Task<GameAnalyzeResultDto?> AnalyzeSharedAsync(int userId, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var game = await _db.SavedGames.FirstOrDefaultAsync(g => g.ShareToken == token, ct);
        return game is null ? null : await AnalyzeCoreAsync(userId, game, ct);
    }

    /// <summary>
    /// Mehrfach klicken = einmal rechnen. Die Reihenfolge ist Absicht:
    /// <list type="number">
    /// <item>Die VERKNUEPFTE Analyse, solange sie nicht gescheitert ist — gleich, wer klickt. Ein Gast
    /// bekommt damit die Rechnung des Besitzers zurueck, statt dieselben Stellungen ein zweites Mal
    /// durch die Engine zu schicken; die Kurve steht ja schon auf der Seite.</item>
    /// <item>Eine EIGENE Analyse des Aufrufers mit demselben PGN (etwa ueber die Punktepartie-Seite
    /// eingeworfen) — exakter Textvergleich, die Seiten schicken genau diesen Text. Ist er der
    /// Besitzer, wird sie verknuepft.</item>
    /// <item>Erst dann neu einwerfen.</item>
    /// </list>
    /// <para>Verknuepft wird NUR beim Besitzer: die oeffentliche Kurve ist die des Teilenden — er hat
    /// die Partie geteilt, nicht der Gast. Der Gast findet seine Analyse trotzdem, solange er
    /// angemeldet ist (<see cref="GetSharedEvalsAsync"/> faellt auf die eigene zurueck).</para>
    /// <para>Zwei Klicks binnen Millisekunden fangen diese Schritte nicht (beide sehen noch nichts);
    /// die sperrt der Knopf, solange sein Aufruf laeuft.</para>
    /// </summary>
    private async Task<GameAnalyzeResultDto> AnalyzeCoreAsync(int userId, SavedGame game, CancellationToken ct)
    {
        var isOwner = game.UserId == userId;

        if (game.GameAnalysisId is int linkedId)
        {
            var linked = await _analyses.GetHeadUncheckedAsync(linkedId, ct);
            // Eine gescheiterte ist kein Ergebnis: wer erneut klickt, will neu rechnen.
            if (linked is not null && linked.Status != "failed")
                return new GameAnalyzeResultDto { Analysis = linked, Reused = true };
        }

        var ownId = await _db.GameAnalyses.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status != GameAnalysisStatus.Failed && a.Pgn == game.Pgn)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (ownId is int id)
        {
            if (isOwner && game.GameAnalysisId != id)
            {
                game.GameAnalysisId = id;
                await _db.SaveChangesAsync(ct);
            }
            return new GameAnalyzeResultDto { Analysis = await _analyses.GetHeadUncheckedAsync(id, ct), Reused = true };
        }

        var created = await _analyses.CreateForGuessAsync(userId,
            new CreateGuessGameRequest { Pgn = game.Pgn, Title = AnalysisTitleOf(game) },
            ct, origin: GameAnalysisOrigin.SavedGame);
        if (created.Analysis is null) return new GameAnalyzeResultDto { Reason = created.Reason };

        if (isOwner)
        {
            game.GameAnalysisId = created.Analysis.Id;
            await _db.SaveChangesAsync(ct);
        }
        // Die Stellungen braucht die Antwort nicht — die Seite holt sich gleich die Bewertungen.
        created.Analysis.Positions = null;
        return new GameAnalyzeResultDto { Analysis = created.Analysis };
    }

    /// <summary>„Weiß – Schwarz" wie der Titel, den die Seiten frueher selbst mitschickten; ohne beide
    /// Namen keiner (dann baut die Analyse ihn aus den PGN-Kopfdaten).</summary>
    private static string? AnalysisTitleOf(SavedGame g)
        => string.IsNullOrWhiteSpace(g.White) || string.IsNullOrWhiteSpace(g.Black)
            ? null
            : $"{g.White.Trim()} – {g.Black.Trim()}";

    /// <summary>Bewertungen einer EIGENEN Partie; <c>null</c>, wenn es sie nicht gibt oder sie fremd ist.</summary>
    public async Task<GameEvalsDto?> GetEvalsAsync(int userId, int savedGameId, CancellationToken ct = default)
    {
        var head = await _db.SavedGames.AsNoTracking()
            .Where(g => g.Id == savedGameId && g.UserId == userId)
            .Select(g => new { g.Id, g.GameAnalysisId })
            .FirstOrDefaultAsync(ct);
        return head is null ? null : await EvalsAsync(head.Id, head.GameAnalysisId, userId, ct);
    }

    /// <summary>Bewertungen der geteilten Partie; <c>null</c> bei unbekanntem Token.
    /// <paramref name="callerUserId"/> = <c>null</c> (anonym): NUR die verknuepfte Analyse.</summary>
    public async Task<GameEvalsDto?> GetSharedEvalsAsync(string token, int? callerUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var head = await _db.SavedGames.AsNoTracking()
            .Where(g => g.ShareToken == token)
            .Select(g => new { g.Id, g.GameAnalysisId })
            .FirstOrDefaultAsync(ct);
        return head is null ? null : await EvalsAsync(head.Id, head.GameAnalysisId, callerUserId, ct);
    }

    private sealed record AnalysisHead(int Id, GameAnalysisStatus Status, int PlyCount, int TargetDepth,
        int? RefineDepth, DateTime? RefinedAt);

    /// <summary>
    /// Welche Analyse: (a) die verknuepfte, solange es sie noch gibt — der Verweis hat keinen
    /// Fremdschluessel und kann ins Leere zeigen; (b) sonst, NUR mit Aufrufer, dessen eigene mit
    /// demselben PGN (die neueste nicht gescheiterte). Anonym gibt es (b) nicht: dort zeigt die Seite
    /// die Kurve des Teilenden oder keine.
    ///
    /// <para>Weder die Partie noch die Analyse bringen dabei ihr PGN mit (beide LONGTEXT; die Seite
    /// fragt waehrend der Rechnung alle zehn Sekunden). Der Textvergleich fuer (b) laeuft als
    /// Unterabfrage in SQL.</para>
    /// </summary>
    private async Task<GameEvalsDto> EvalsAsync(int savedGameId, int? linkedId, int? callerUserId, CancellationToken ct)
    {
        AnalysisHead? analysis = null;
        if (linkedId is int id)
            analysis = await _db.GameAnalyses.AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => new AnalysisHead(a.Id, a.Status, a.PlyCount, a.TargetDepth, a.RefineDepth, a.RefinedAt))
                .FirstOrDefaultAsync(ct);
        if (analysis is null && callerUserId is int caller)
        {
            var pgn = _db.SavedGames.Where(g => g.Id == savedGameId).Select(g => g.Pgn);
            analysis = await _db.GameAnalyses.AsNoTracking()
                .Where(a => a.UserId == caller && a.Status != GameAnalysisStatus.Failed && pgn.Contains(a.Pgn))
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .Select(a => new AnalysisHead(a.Id, a.Status, a.PlyCount, a.TargetDepth, a.RefineDepth, a.RefinedAt))
                .FirstOrDefaultAsync(ct);
        }
        if (analysis is null) return new GameEvalsDto();

        var rows = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == analysis.Id && p.CandidatesJson != null)
            .OrderBy(p => p.Ply)
            .Select(p => new { p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson, p.Depth, p.AnalyzedAt, p.Refined })
            .ToListAsync(ct);
        var plies = rows
            .Select(r => GameEvals.PlyOf(r.Ply, r.Fen, r.GameMoveUci, r.CandidatesJson, r.Depth))
            .OfType<GameEvalPlyDto>()
            .ToList();
        var running = analysis.Status is GameAnalysisStatus.Pending or GameAnalysisStatus.Running;

        // Buchzüge nur für einen angemeldeten Aufrufer und aus SEINEN Repertoires: anonym gibt es keine, und die des
        // Teilenden bekäme ein Gast nie zu sehen — sonst verriete ein Teilen-Link, was jemand vorbereitet hat.
        var bookPlies = new List<int>();
        if (callerUserId is int viewer)
        {
            var fens = await _db.GameAnalysisPositions.AsNoTracking()
                .Where(p => p.GameAnalysisId == analysis.Id)
                .OrderBy(p => p.Ply)
                .Select(p => p.Fen)
                .ToListAsync(ct);
            // Zeile p+1 ist die Stellung NACH Halbzug p; für den letzten Halbzug gibt es keine (Buch endet vorher).
            bookPlies = await _repertoires.BookPliesAsync(viewer, fens.Skip(1).ToList());
        }

        return new GameEvalsDto
        {
            Status = analysis.Status.ToString().ToLowerInvariant(),
            Analyzed = rows.Count,
            Total = analysis.PlyCount,
            TargetDepth = analysis.TargetDepth,
            AnalysisId = analysis.Id,
            Plies = plies,
            Final = GameEvals.FinalOf(plies.LastOrDefault(), analysis.PlyCount),
            BookPlies = bookPlies,
            Refining = analysis.Status == GameAnalysisStatus.Done && analysis.RefineDepth != null && analysis.RefinedAt == null,
            Refined = analysis.RefineDepth != null ? rows.Count(r => r.Refined) : 0,
            EtaMinutes = running
                ? GameEvals.EtaMinutes(
                    rows.Where(r => r.AnalyzedAt != null).Select(r => r.AnalyzedAt!.Value),
                    analysis.PlyCount - rows.Count, DateTime.UtcNow)
                : null,
        };
    }

    /// <summary>Welche Seite spielte der Besitzer? Vergleich der Spielernamen mit seinem
    /// Plattform-Username (lichess/chess.com je nach Quelle, case-insensitiv) — dieselbe
    /// Logik wie das clientseitige Flippen der eigenen Nachspiel-Ansicht (games-list.isFlipped).</summary>
    private static string? DetermineOwnerSide(SavedGame g, UserProfile? profile)
    {
        var myName = g.Source == "lichess" ? profile?.LichessUsername : profile?.ChessComUsername;
        if (string.IsNullOrWhiteSpace(myName)) return null;
        if (string.Equals(g.Black?.Trim(), myName.Trim(), StringComparison.OrdinalIgnoreCase)) return "black";
        if (string.Equals(g.White?.Trim(), myName.Trim(), StringComparison.OrdinalIgnoreCase)) return "white";
        return null;
    }

    // ── Vom Server angelegt + korrigiert (Partieformular, 0.529.0) ─────

    /// <summary>Quelle einer aus einem Formular-Foto eingelesenen Partie.</summary>
    public const string ScoresheetSource = "scoresheet";

    /// <summary>Die sieben Pflicht-Header in ihrer PGN-Reihenfolge — beim Neuschreiben kommen sie zuerst.</summary>
    private static readonly string[] SevenTags = { "Event", "Site", "Date", "Round", "White", "Black", "Result" };

    /// <summary>
    /// Legt eine Partie an, die der SERVER gebaut hat (Formular-Einlesung) — anders als
    /// <see cref="SaveAsync"/> ohne Dedup und mit freier Quelle. Die Züge müssen legal sein; Kommentare hängen
    /// am Halbzug-Index wie bei <see cref="PgnWriter.MoveText"/>.
    /// </summary>
    public async Task<SavedGame> CreateGeneratedAsync(int userId, string source, IReadOnlyList<string> sans,
        IReadOnlyDictionary<int, string>? comments, GameHeaderInput header)
    {
        var result = header.Result is { } r && AllowedResults.Contains(r) ? r : "*";
        var entity = new SavedGame
        {
            UserId = userId,
            Source = source,
            White = Clip(header.White, 120),
            Black = Clip(header.Black, 120),
            Result = result,
            PlayedAt = ParseDate(header.Date),
            Pgn = BuildHeaderedPgn(new Dictionary<string, string>(), header, result, sans, null, comments),
            MoveCount = sans.Count,
            HeadersScanned = true,
            ShareToken = await GenerateUniqueTokenAsync(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.SavedGames.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    /// <summary>
    /// Korrigiert eine eigene Partie: Züge (mit Kommentaren) und Kopfdaten. Die Züge werden ab der
    /// Ausgangsstellung nachgespielt — ist einer nicht legal, gibt es eine <see cref="ArgumentException"/>
    /// und nichts wird geschrieben. Header, die hier nicht bearbeitet werden (Elo, Bedenkzeit, FEN), bleiben.
    ///
    /// <para>Ändern sich die ZÜGE, gehört die verknüpfte Analyse nicht mehr zur Partie (Kurve, Fehler und
    /// Genauigkeit rechneten eine andere) — der Verweis fällt, ebenso der Stand des Fehler-Trainings.</para>
    /// </summary>
    /// <returns><c>null</c>, wenn die Partie nicht existiert oder fremd ist.</returns>
    public async Task<SavedGameDetailDto?> UpdateAsync(int userId, int id, GameUpdateDto dto)
    {
        var g = await _db.SavedGames.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (g == null) return null;

        var old = PgnParser.SplitGames(g.Pgn).FirstOrDefault();
        var headers = old.Headers ?? new Dictionary<string, string>();
        headers.TryGetValue("FEN", out var fenHeader);
        var startFen = string.IsNullOrWhiteSpace(fenHeader) ? null : fenHeader.Trim();

        var moves = (dto.Moves ?? new()).Where(m => !string.IsNullOrWhiteSpace(m.San)).ToList();
        if (moves.Count > 600) throw new ArgumentException("Too many moves (max 600 plies).");
        var sans = LegalSans(moves.Select(m => m.San!.Trim()).ToList(), startFen);
        var comments = new Dictionary<int, string>();
        for (var i = 0; i < moves.Count; i++)
            if (!string.IsNullOrWhiteSpace(moves[i].Comment)) comments[i] = moves[i].Comment!.Trim();

        var result = dto.Result is { } r && AllowedResults.Contains(r.Trim()) ? r.Trim() : "*";
        var header = new GameHeaderInput(dto.Event, dto.Site, dto.Date, dto.Round, dto.White, dto.Black, result);
        var oldSans = GamePlies.Parse(g.Pgn, 600)?.Plies.Select(p => p.San).ToList() ?? new List<string>();
        var movesChanged = !oldSans.SequenceEqual(sans);

        g.Pgn = BuildHeaderedPgn(headers, header, result, sans, startFen, comments);
        g.White = Clip(dto.White, 120);
        g.Black = Clip(dto.Black, 120);
        g.Result = result;
        g.PlayedAt = ParseDate(dto.Date) ?? (string.IsNullOrWhiteSpace(dto.Date) ? null : g.PlayedAt);
        g.MoveCount = sans.Count;
        if (movesChanged)
        {
            g.GameAnalysisId = null;
            var progress = await _db.GameMistakeProgresses.Where(p => p.SavedGameId == g.Id).ToListAsync();
            _db.GameMistakeProgresses.RemoveRange(progress);
        }
        await _db.SaveChangesAsync();
        return MapDetail(g);
    }

    /// <summary>Spielt die Züge nach und gibt sie in der Schreibweise des Bretts zurück; wirft beim ersten
    /// illegalen Zug (mit seiner Nummer).</summary>
    public static List<string> LegalSans(IReadOnlyList<string> sans, string? startFen = null)
    {
        var board = string.IsNullOrWhiteSpace(startFen) ? new Chess.ChessBoard() : Chess.ChessBoard.LoadFromFen(startFen);
        var result = new List<string>(sans.Count);
        for (var i = 0; i < sans.Count; i++)
        {
            var legal = board.Moves(generateSan: true);
            var key = ScoresheetNotation.Key(sans[i]);
            var move = legal.FirstOrDefault(m => ScoresheetNotation.Key(m.San ?? string.Empty) == key);
            if (move == null || !board.Move(move))
                throw new ArgumentException($"Illegal move {sans[i]} at ply {i + 1}.");
            result.Add(string.IsNullOrEmpty(move.San) ? sans[i] : move.San);
        }
        return result;
    }

    /// <summary>PGN aus vorhandenen Headern (bleiben, soweit hier nicht bearbeitet) + neuen Kopfdaten + Zügen.</summary>
    private static string BuildHeaderedPgn(Dictionary<string, string> existing, GameHeaderInput header, string result,
        IReadOnlyList<string> sans, string? startFen, IReadOnlyDictionary<int, string>? comments)
    {
        var tags = new Dictionary<string, string>(existing, StringComparer.Ordinal)
        {
            ["Event"] = Header(header.Event),
            ["Site"] = Header(header.Site),
            ["Date"] = PgnDate(header.Date),
            ["Round"] = Header(header.Round),
            ["White"] = Header(header.White),
            ["Black"] = Header(header.Black),
            ["Result"] = result,
        };
        var sb = new StringBuilder();
        foreach (var t in SevenTags) sb.Append('[').Append(t).Append(" \"").Append(tags[t]).Append("\"]\n");
        foreach (var (k, v) in tags.Where(kv => !SevenTags.Contains(kv.Key)))
            sb.Append('[').Append(k).Append(" \"").Append(Header(v)).Append("\"]\n");
        sb.Append('\n');
        sb.Append(PgnWriter.MoveText(sans, startFen, comments, result));
        return sb.ToString();
    }

    /// <summary>Datum aus dem Formular: <c>2026-06-05</c>, <c>2026.06.05</c> oder <c>5.6.2026</c> → PGN
    /// <c>2026.06.05</c>; sonst <c>????.??.??</c>.</summary>
    private static string PgnDate(string? date)
        => ParseDate(date) is { } d ? d.ToString("yyyy.MM.dd") : "????.??.??";

    private static DateTime? ParseDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var formats = new[] { "yyyy-MM-dd", "yyyy.MM.dd", "d.M.yyyy", "dd.MM.yyyy", "d.M.yy", "dd.MM.yy", "yyyy/MM/dd" };
        return DateTime.TryParseExact(date.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc)
            : null;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string? Clip(string? s, int max)
        => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    /// <summary>Baut ein PGN aus SAN-Zugliste + Headern (Seven-Tag-Roster, Best-Effort).</summary>
    public static string BuildPgn(List<string> moves, SaveGameInputDto dto, string result)
    {
        var sb = new StringBuilder();
        sb.Append("[Event \"RepCheck saved game\"]\n");
        sb.Append("[Site \"").Append(Header(dto.SourceUrl)).Append("\"]\n");
        sb.Append("[Date \"").Append(dto.PlayedAt?.ToString("yyyy.MM.dd") ?? "????.??.??").Append("\"]\n");
        sb.Append("[White \"").Append(Header(dto.White)).Append("\"]\n");
        sb.Append("[Black \"").Append(Header(dto.Black)).Append("\"]\n");
        sb.Append("[Result \"").Append(result).Append("\"]\n");
        // Elo/Rating nur ausgeben, wenn plausibel (100–4000) — sonst weglassen.
        if (IsPlausibleElo(dto.WhiteElo)) sb.Append("[WhiteElo \"").Append(dto.WhiteElo).Append("\"]\n");
        if (IsPlausibleElo(dto.BlackElo)) sb.Append("[BlackElo \"").Append(dto.BlackElo).Append("\"]\n");
        if (CleanTimeControl(dto.TimeControl) is string tc) sb.Append("[TimeControl \"").Append(tc).Append("\"]\n");
        sb.Append('\n');
        sb.Append(PgnWriter.MoveText(moves, result: result));
        return sb.ToString();
    }

    /// <summary>Plausibilitäts-Check für ein Elo/Rating (verhindert Müll-Header).</summary>
    private static bool IsPlausibleElo(int? elo) => elo is >= 100 and <= 4000;

    /// <summary>Das Elo, wenn es plausibel ist — sonst <c>null</c> (für die Spalten).</summary>
    private static int? PlausibleElo(int? elo) => IsPlausibleElo(elo) ? elo : null;

    /// <summary>
    /// Bedenkzeit in der Schreibweise, die das PGN kennt: <c>600</c>, <c>180+2</c>, <c>1/86400</c>
    /// (Fernschach) oder <c>-</c>. Alles andere wird verworfen statt gespeichert — der Wert geht
    /// ungeprüft in einen PGN-Header, und die Liste rechnet daraus „3 + 2".
    /// </summary>
    public static string? CleanTimeControl(string? raw)
    {
        var tc = raw?.Trim();
        if (string.IsNullOrEmpty(tc) || tc.Length > 32) return null;
        return Regex.IsMatch(tc, @"^(-|\d{1,6}(\+\d{1,4})?|\d{1,3}/\d{1,7})$") ? tc : null;
    }

    /// <summary>Aktualisiert eine bereits gespeicherte Partie beim Re-Save, wenn die neue
    /// Version mehr Züge hat ODER erstmals ein Elo mitbringt. Gibt <c>true</c> zurück, wenn
    /// etwas geändert wurde. Kürzt NIE (weniger Züge → keine Änderung).</summary>
    private static bool TryHeal(SavedGame existing, List<string> moves, SaveGameInputDto dto, string result)
    {
        var newHasElo = IsPlausibleElo(dto.WhiteElo) || IsPlausibleElo(dto.BlackElo);
        var oldHasElo = ParseEloHeader(existing.Pgn, "WhiteElo") != null || ParseEloHeader(existing.Pgn, "BlackElo") != null;
        var moreMoves = moves.Count > CountPlies(existing.Pgn);
        if (!moreMoves && !(newHasElo && !oldHasElo)) return false;

        existing.Pgn = BuildPgn(moves, dto, result);
        existing.MoveCount = moves.Count;   // MUSS mit: das PGN wird hier ersetzt
        // Die Spalten kommen aus derselben Quelle wie das PGN — aber nur, wenn der neue Save etwas
        // mitbringt: ein Re-Save ohne Wertung darf eine vorhandene nicht loeschen.
        existing.WhiteElo = PlausibleElo(dto.WhiteElo) ?? existing.WhiteElo;
        existing.BlackElo = PlausibleElo(dto.BlackElo) ?? existing.BlackElo;
        existing.TimeControl = CleanTimeControl(dto.TimeControl) ?? existing.TimeControl;
        existing.HeadersScanned = true;
        if (!string.IsNullOrWhiteSpace(dto.White)) existing.White = Clip(dto.White, 120);
        if (!string.IsNullOrWhiteSpace(dto.Black)) existing.Black = Clip(dto.Black, 120);
        existing.Result = result;
        if (dto.PlayedAt.HasValue) existing.PlayedAt = dto.PlayedAt;
        if (!string.IsNullOrWhiteSpace(dto.SourceUrl)) existing.SourceUrl = Clip(dto.SourceUrl, 1000);
        return true;
    }

    /// <summary>Liest ein Elo aus einem PGN-Header (z. B. <c>[WhiteElo "1832"]</c>); null wenn fehlt/unplausibel.</summary>
    public static int? ParseEloHeader(string pgn, string tag)
    {
        if (string.IsNullOrEmpty(pgn)) return null;
        var m = Regex.Match(pgn, $"\\[{Regex.Escape(tag)}\\s+\"(\\d{{1,4}})\"\\]");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var elo) && IsPlausibleElo(elo)) return elo;
        return null;
    }

    /// <summary>Header-Wert säubern: leere → "?", Anführungszeichen/Zeilenumbrüche entfernen.
    /// <para>Bewusst NICHT <see cref="PgnWriter.Escape"/>: hier wird das Anführungszeichen durch ein
    /// Apostroph ERSETZT, nicht maskiert — eine andere Entscheidung, und die gespeicherten Partien
    /// tragen sie bereits.</para></summary>
    private static string Header(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "?";
        return value.Replace("\"", "'").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    /// <summary>Zählt die Halbzüge eines gebauten PGN (Movetext nach der Leerzeile).</summary>
    private static int CountPlies(string pgn)
    {
        var idx = pgn.IndexOf("\n\n", StringComparison.Ordinal);
        var movetext = idx >= 0 ? pgn[(idx + 2)..] : pgn;
        var count = 0;
        foreach (var token in movetext.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 0) continue;
            if (char.IsDigit(token[0]) && token.Contains('.')) continue;   // Zugnummer "12."
            if (AllowedResults.Contains(token)) continue;                  // Ergebnis-Token
            count++;
        }
        return count;
    }

    private async Task<string> GenerateUniqueTokenAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var token = NewToken();
            if (!await _db.SavedGames.AnyAsync(g => g.ShareToken == token)) return token;
        }
        return NewToken();   // extrem unwahrscheinlicher Kollisions-Fallback
    }

    /// <summary>URL-sicheres Zufallstoken (~22 Zeichen aus 16 Bytes).</summary>
    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static SavedGameDetailDto MapDetail(SavedGame g) => new()
    {
        Id = g.Id,
        Source = g.Source,
        White = g.White,
        Black = g.Black,
        Result = g.Result,
        PlayedAt = g.PlayedAt,
        SourceUrl = g.SourceUrl,
        ShareToken = g.ShareToken,
        MoveCount = CountPlies(g.Pgn),
        CreatedAt = g.CreatedAt,
        Pgn = g.Pgn,
        // Aus dem PGN und nicht aus den Spalten: hier LIEGT das PGN, und beim Altbestand steht die
        // Wertung nur dort (der Nachtrag laeuft ueber die Liste).
        WhiteElo = ParseEloHeader(g.Pgn, "WhiteElo"),
        BlackElo = ParseEloHeader(g.Pgn, "BlackElo"),
        TimeControl = g.TimeControl,
    };
}
