using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<SavedGameService> _logger;

    public SavedGameService(AppDbContext db, GameAnalysisService analyses, RepertoireAnalyzeService repertoires,
        ILogger<SavedGameService>? logger = null)
    {
        _db = db;
        _analyses = analyses;
        _repertoires = repertoires;
        _logger = logger ?? NullLogger<SavedGameService>.Instance;
    }

    private static readonly HashSet<string> AllowedSources = new(StringComparer.OrdinalIgnoreCase)
        { "chess.com", "lichess" };

    private static readonly HashSet<string> AllowedResults = new() { "1-0", "0-1", "1/2-1/2", "*" };

    // ── Deckel je Konto (A6-007) ─────
    // Ohne Deckel füllte ein Skript über POST /api/games/import (200 Partien je Aufruf, wechselnder Round-Header =
    // nie Dublette, Kommentare und Kopfdaten ungekürzt) die Datenbank um mehrere GB je Stunde — die Liste zeigt
    // ohnehin nur die neuesten 500, gelöscht wird nur von Hand.

    /// <summary>So viele gespeicherte Partien hat ein Konto höchstens (alle Quellen zusammen). Darüber legen der
    /// Import (<c>quota</c>) und „Partie speichern" (<see cref="SavedGameQuotaException"/>) nichts Neues mehr an;
    /// Vorhandenes bleibt, Dubletten und Heilen gehen weiter.</summary>
    public const int MaxGamesPerUser = 5_000;

    /// <summary>So viele Zeichen PGN hat ein Konto höchstens gespeichert (Summe über alle Partien) — der Zähldeckel
    /// allein ließe 5 000 × 5 Mio. Zeichen zu. 100 Mio. = 5 000 Partien à 20 000 Zeichen (stark kommentiert; ohne
    /// Kommentare hat eine Partie rund 1 000). Geprüft beim Import und beim Korrigieren, wenn das PGN wächst;
    /// „Partie speichern" braucht die Summe nicht, dort ist jede Partie klein (höchstens 600 Züge à
    /// <see cref="MaxSanLength"/> Zeichen, gekürzte Kopfdaten).</summary>
    public const long MaxPgnCharsPerUser = 100_000_000;

    /// <summary>Längster angenommener Zug bei „Partie speichern" — ein SAN hat höchstens rund 8 Zeichen
    /// (<c>exd8=Q+!</c>); mehr ginge ungeprüft ins PGN.</summary>
    public const int MaxSanLength = 16;

    /// <summary>Wirksamer Zähldeckel (<see cref="MaxGamesPerUser"/>); nur Tests setzen ihn klein.</summary>
    internal int GamesPerUserCap { get; set; } = MaxGamesPerUser;

    /// <summary>Wirksamer Zeichendeckel (<see cref="MaxPgnCharsPerUser"/>); nur Tests setzen ihn klein.</summary>
    internal long PgnCharsPerUserCap { get; set; } = MaxPgnCharsPerUser;

    /// <summary>Was ein Konto belegt: Zahl der Partien und Summe der PGN-Zeichen, in EINER Abfrage
    /// (<c>COUNT(*)</c>, <c>SUM(CHAR_LENGTH(Pgn))</c>). Ohne Partien keine Zeile.</summary>
    internal static IQueryable<SavedGameUsage> UsageQuery(AppDbContext db, int userId)
        => db.SavedGames.AsNoTracking().Where(g => g.UserId == userId).GroupBy(g => g.UserId)
            .Select(grp => new SavedGameUsage(grp.Count(), grp.Sum(g => (long)g.Pgn.Length)));

    private async Task<SavedGameUsage> UsageAsync(int userId, CancellationToken ct = default)
        => await UsageQuery(_db, userId).FirstOrDefaultAsync(ct) ?? new SavedGameUsage(0, 0);

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
        // Vorab entschaerfen: der Link geht in die Spalte, in den [Site]-Header und beim Heilen in den Bestand.
        dto.SourceUrl = SafeSourceUrl(dto.SourceUrl);
        var moves = (dto.Moves ?? new())
            .Select(m => (m ?? string.Empty).Trim())
            .Where(m => m.Length > 0)
            .ToList();
        if (moves.Count == 0) throw new ArgumentException("No moves.");
        if (moves.Count > 600) throw new ArgumentException("Too many moves (max 600 plies).");
        if (moves.Any(m => m.Length > MaxSanLength)) throw new ArgumentException("Invalid move.");

        var externalId = string.IsNullOrWhiteSpace(dto.ExternalId) ? null : dto.ExternalId.Trim();

        var result = dto.Result?.Trim();
        if (result == null || !AllowedResults.Contains(result)) result = "*";

        // Dedup: gleicher User + Source + ExternalId → bestehende Partie. Wenn der neue
        // Save BESSER ist (mehr Züge, erstmals Elo ODER fehlende Spieler/Bedenkzeit/Datum), heilt er den Datensatz in-place
        // (gleiches ShareToken/Id) — so repariert ein Re-Save eine alt gespeicherte,
        // lückenhafte/Elo-lose Partie, ohne den Teilen-Link zu ändern.
        // Das gilt nur, solange es DIESELBE Partie ist: die neuen Züge setzen die gespeicherten fort
        // (oder sind deren Anfang). Sonst ist die ExternalId keine Partie-Kennung — RepCheck meldete auf
        // dem lichess-Analysebrett jede Partie als „analysis" (N8-001) — und der Save wird eine eigene
        // Partie ohne ExternalId, statt die alte hinter ihrem Teilen-Link zu überschreiben.
        SavedGame? divergedFrom = null;
        if (externalId != null)
        {
            var existing = await _db.SavedGames
                .FirstOrDefaultAsync(g => g.UserId == userId && g.Source == source && g.ExternalId == externalId);
            if (existing != null)
            {
                var stored = SanKeys(StoredSans(existing.Pgn));
                var incoming = SanKeys(moves);
                if (IsPrefixOf(stored, incoming))
                {
                    if (TryHeal(existing, moves, dto, result))
                    {
                        // Neue Züge (nicht nur Elo): wie beim Korrigieren gehört, was an der alten Zugfolge hing, nicht
                        // mehr zur Partie — sonst gab „Analysieren" die alte Kurve als „Reused" zurück (N8-002).
                        if (incoming.Count > stored.Count) await OnMovesChangedAsync(existing);
                        await _db.SaveChangesAsync();
                    }
                    return MapDetail(existing);
                }
                // Kürzere Fassung derselben Partie (Doppelklick, Zugliste noch nicht ganz geladen):
                // die gespeicherte bleibt — kürzt nie.
                if (IsPrefixOf(incoming, stored)) return MapDetail(existing);

                divergedFrom = existing;
                externalId = null;
            }
        }

        if (await _db.SavedGames.CountAsync(g => g.UserId == userId) >= GamesPerUserCap)
            throw new SavedGameQuotaException($"Saved game limit reached (max {GamesPerUserCap} per account).");

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
        if (divergedFrom != null)
            _logger.LogWarning(
                "Partie speichern: {Source}-Partie {ExternalId} von User {UserId} setzt die gespeicherte Partie {ExistingGameId} nicht fort — als neue Partie {SavedGameId} ohne ExternalId angelegt",
                source, divergedFrom.ExternalId, userId, divergedFrom.Id, entity.Id);
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
                g.WhiteElo, g.BlackElo, g.TimeControl, g.HeadersScanned, g.Classifier1, g.Classifier2, g.Tags,
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
            SourceUrl = SafeSourceUrl(r.SourceUrl),
            ShareToken = r.ShareToken,
            MoveCount = r.MoveCount ?? (healed.TryGetValue(r.Id, out var c) ? c : 0),
            CreatedAt = r.CreatedAt,
            WhiteElo = r.WhiteElo ?? (elos.TryGetValue(r.Id, out var e) ? e.White : null),
            BlackElo = r.BlackElo ?? (elos.TryGetValue(r.Id, out var e2) ? e2.Black : null),
            TimeControl = r.TimeControl,
            Analysis = r.GameAnalysisId is int aid && analyses.TryGetValue(aid, out var state) ? state : null,
            ScanId = r.ScanId,
            Classifier1 = GameClassifier.Effective(r.Source, r.TimeControl, r.Classifier1, r.Classifier2).First,
            Classifier2 = GameClassifier.Effective(r.Source, r.TimeControl, r.Classifier1, r.Classifier2).Second,
            Classifier1Set = GameClassifier.Clean(r.Classifier1),
            Classifier2Set = GameClassifier.Clean(r.Classifier2),
            Tags = GameTags.Parse(r.Tags),
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
    private Task<Dictionary<int, SavedGameAnalysisDto>> AnalysisStatesAsync(List<int> ids) =>
        GameEvalsStore.StatesAsync(_db, ids, AccuracyBackfillPerCall);

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
        dto.ClubGameId = g.LeagueClubGameId;
        if (dto.ScanId == null && g.LeagueClubGameId is { } clubId)
        {
            var now = DateTime.UtcNow;
            dto.ClubSheet = await _db.LeagueClubGames.AnyAsync(c => c.Id == clubId && c.UploadedByUserId == g.UserId)
                && await _db.ScoresheetScanArchives.AnyAsync(a => a.LeagueClubGameId == clubId && a.Page == 1 && a.ExpiresAt > now);
        }
        return dto;
    }

    /// <summary>Löscht eine eigene Partie; false wenn nicht gefunden / fremd.</summary>
    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var g = await _db.SavedGames.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (g == null) return false;
        // Das Formular-Foto geht mit (alle Seiten); die Einlesung selbst bleibt ohne Foto fürs Kontingent stehen (ohne es
        // zu laden).
        var scanKeys = await ScoresheetScanService.KeysAsync(_db.ScoresheetScans.Where(s => s.SavedGameId == id));
        ScoresheetScanService.DetachWithoutLoading(_db, scanKeys);
        ScoresheetScanService.RemovePagesWithoutLoading(_db,
            await ScoresheetScanService.PageKeysAsync(_db, scanKeys.Select(k => k.Id).ToList()));
        ScoresheetScanService.RemoveViewsWithoutLoading(_db,
            await ScoresheetScanService.ViewKeysAsync(_db, scanKeys.Select(k => k.Id).ToList()));
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
            SourceUrl = SafeSourceUrl(g.SourceUrl),
            Pgn = g.Pgn,
            CreatedAt = g.CreatedAt,
            WhiteElo = ParseEloHeader(g.Pgn, "WhiteElo"),
            BlackElo = ParseEloHeader(g.Pgn, "BlackElo"),
            OwnerSide = DetermineOwnerSide(g, profile),
            OwnGameId = callerUserId is int caller && caller == g.UserId ? g.Id : null,
            Recap = (await GameRecapService.CurrentAsync(_db, g.Id, g.ReviewLanguage))?.Text,
        };
    }

    // ── Analyse + Bewertungskurve ─────────────────────────────────────

    /// <summary>„Partie analysieren" an einer EIGENEN Partie; <c>null</c>, wenn es sie nicht gibt oder
    /// sie jemand anderem gehoert.</summary>
    /// <param name="lang">Sprache der Seite (0.540.0) — darin entstehen nach der Analyse die Texte zur Partie
    /// (<see cref="SavedGame.ReviewLanguage"/>); die Erweiterung schickt keine.</param>
    public async Task<GameAnalyzeResultDto?> AnalyzeAsync(int userId, int savedGameId, CancellationToken ct = default,
        string? lang = null)
    {
        var game = await _db.SavedGames.FirstOrDefaultAsync(g => g.Id == savedGameId && g.UserId == userId, ct);
        return game is null ? null : await AnalyzeCoreAsync(userId, game, lang, ct);
    }

    /// <summary>„Partie analysieren" auf der geteilten Partie (<c>/g/{token}</c>) — das darf JEDER
    /// Angemeldete, genau dafuer steht der Knopf dort; <c>null</c> bei unbekanntem Token.</summary>
    public async Task<GameAnalyzeResultDto?> AnalyzeSharedAsync(int userId, string token, CancellationToken ct = default,
        string? lang = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var game = await _db.SavedGames.FirstOrDefaultAsync(g => g.ShareToken == token, ct);
        return game is null ? null : await AnalyzeCoreAsync(userId, game, lang, ct);
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
    /// <item>Die Analyse einer VEREINSPARTIE mit genau denselben Zügen (0.653.0, Wunsch 2026-10-04: „wenn jemand das Game
    /// lokal kopiert, soll es nur einmal analysiert werden") — sie rechnet der Hintergrund ohnehin
    /// (<see cref="ClubAnalysisForMovesAsync"/>).</item>
    /// <item>Erst dann neu einwerfen.</item>
    /// </list>
    /// <para>Verknuepft wird NUR beim Besitzer: die oeffentliche Kurve ist die des Teilenden — er hat
    /// die Partie geteilt, nicht der Gast. Der Gast findet seine Analyse trotzdem, solange er
    /// angemeldet ist (<see cref="GetSharedEvalsAsync"/> faellt auf die eigene zurueck).</para>
    /// <para>Zwei Klicks binnen Millisekunden fangen diese Schritte nicht (beide sehen noch nichts);
    /// die sperrt der Knopf, solange sein Aufruf laeuft.</para>
    /// </summary>
    private async Task<GameAnalyzeResultDto> AnalyzeCoreAsync(int userId, SavedGame game, string? lang, CancellationToken ct)
    {
        var isOwner = game.UserId == userId;
        // Nur der Besitzer bestimmt, in welcher Sprache die Texte zu SEINER Partie entstehen.
        if (isOwner && !string.IsNullOrWhiteSpace(lang))
        {
            var language = GameMoveExplanationService.NormalizeLanguage(lang);
            if (game.ReviewLanguage != language)
            {
                game.ReviewLanguage = language;
                await _db.SaveChangesAsync(ct);
            }
        }

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

        if (GamePlies.Parse(game.Pgn, maxPlies: 600) is { } parsed && IsStandardStart(parsed.Header.StartFen)
            && await ClubAnalysisForMovesAsync(parsed.Plies.Select(p => p.San).ToList(), userId, ct) is int clubId)
        {
            if (isOwner && game.GameAnalysisId != clubId)
            {
                game.GameAnalysisId = clubId;
                await _db.SaveChangesAsync(ct);
            }
            return new GameAnalyzeResultDto { Analysis = await _analyses.GetHeadUncheckedAsync(clubId, ct), Reused = true };
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

    /// <summary>
    /// Die Analyse einer Vereinspartie (LeagueHub) mit GENAU diesen Zügen ab der Grundstellung — die nicht gescheiterte,
    /// fertige zuerst, sonst die jüngste. Eine aus der Vereins-Datenbank kopierte Partie (Knopf „Zu meinen Partien", PGN
    /// heruntergeladen und wieder hochgeladen) wird daran gehängt, statt ein zweites Mal durch die Engine zu laufen. Wer die
    /// Züge hat, erfährt aus ihren Bewertungen nichts über den Verein; Namen und Kopfdaten der Vereinspartie liest der
    /// Bewertungsweg nicht. Vergleich über <see cref="League.LeagueClubService.HashOf"/> (dieselben SAN wie beim Upload).
    /// <para>Nur Vereinspartien der Vereine von <paramref name="userId"/> (Mandanten-Schritt 2026-10-07): die Analyse trägt
    /// die Namen der Vereinspartie, und ein Verein sieht nie die Partien eines anderen. Liga-Partien des Stapels sind öffentlich.</para>
    /// </summary>
    internal async Task<int?> ClubAnalysisForMovesAsync(IReadOnlyList<string> sans, int userId, CancellationToken ct)
    {
        if (sans.Count == 0) return null;
        var hash = League.LeagueClubService.HashOf(sans);
        var mine = (await League.LeagueClubResolver.ClubIdsOfAsync(_db, userId, ct)).ToList();
        var clubIds = _db.LeagueClubGames.Where(g => g.MovesHash == hash && mine.Contains(g.ClubId)).Select(g => (int?)g.Id);
        // dazu die Liga-Partien des Stapels (0.665.0) mit denselben Zügen
        return await _db.GameAnalyses.AsNoTracking()
            .Where(a => a.Status != GameAnalysisStatus.Failed
                && ((a.Origin == GameAnalysisOrigin.Club && a.EngineId == null && clubIds.Contains(a.LeagueClubGameId))
                    || (a.Origin == GameAnalysisOrigin.League && a.MovesHash == hash)))
            .OrderByDescending(a => a.Status == GameAnalysisStatus.Done).ThenByDescending(a => a.Id)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Liga + Jahrgang der Vereinspartie für die Kopie (0.666.0); beide <c>null</c>, wenn es keine Verbindung gibt oder
    /// sie nichts hergibt.</summary>
    internal async Task<(string? First, string? Second)> ClubClassifiersAsync(int? clubGameId, CancellationToken ct)
    {
        if (clubGameId is not int id) return (null, null);
        var c = await _db.LeagueClubGames.AsNoTracking().Where(g => g.Id == id)
            .Select(g => new { g.Classifier1, g.Classifier2 }).FirstOrDefaultAsync(ct);
        return (GameClassifier.Clean(c?.Classifier1), GameClassifier.Clean(c?.Classifier2));
    }

    /// <summary>Die Vereinspartie mit GENAU diesen Zügen (jüngste zuerst) — die Quelle einer Kopie (0.660.0); nur aus den
    /// Vereinen von <paramref name="userId"/> (Mandanten-Schritt 2026-10-07).</summary>
    internal async Task<int?> ClubGameForMovesAsync(IReadOnlyList<string> sans, int userId, CancellationToken ct)
    {
        if (sans.Count == 0) return null;
        var hash = League.LeagueClubService.HashOf(sans);
        var mine = (await League.LeagueClubResolver.ClubIdsOfAsync(_db, userId, ct)).ToList();
        return await _db.LeagueClubGames.AsNoTracking().Where(g => g.MovesHash == hash && mine.Contains(g.ClubId)).OrderByDescending(g => g.Id)
            .Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Eine korrigierte Vereinspartie in alle verbundenen Kopien tragen (0.660.0): je Kopie neue Züge (Kopfdaten der Kopie
    /// bleiben, Kommentare zu Zügen gehen — sie gehörten zur alten Zugfolge), und wie bei jeder Zugänderung fallen Analyse,
    /// Fehler-Training und Nacherzählung. <paramref name="except"/> = die Kopie, von der die Korrektur kam. → Zahl der Kopien.
    /// </summary>
    public static async Task<int> ApplyClubMovesAsync(AppDbContext db, int clubGameId, IReadOnlyList<string> sans, int? except,
        CancellationToken ct = default)
    {
        var copies = await db.SavedGames.Where(g => g.LeagueClubGameId == clubGameId && g.Id != except).ToListAsync(ct);
        foreach (var g in copies)
        {
            var old = PgnParser.SplitGames(g.Pgn).FirstOrDefault();
            var tags = old.Headers ?? new Dictionary<string, string>();
            string? T(string k) => tags.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) && !v.StartsWith('?') ? v : null;
            var result = T("Result") ?? g.Result ?? "*";
            var pgn = BuildHeaderedPgn(tags, new GameHeaderInput(T("Event"), T("Site"), null, T("Round"),
                T("White") ?? g.White, T("Black") ?? g.Black, result), result, sans, null, null);
            // Das Datum bleibt, wie es war (auch nur das Jahr, „2026.??.??") — der Kopfdaten-Weg kennt nur ganze Daten.
            if (tags.TryGetValue("Date", out var date) && !string.IsNullOrWhiteSpace(date))
                pgn = pgn.Replace("[Date \"????.??.??\"]", $"[Date \"{date.Replace("\"", "")}\"]");
            g.Pgn = pgn;
            g.MoveCount = sans.Count;
            await MovesChangedAsync(db, g, ct);
        }
        await db.SaveChangesAsync(ct);
        return copies.Count;
    }

    /// <summary>
    /// Kopien von Vereinspartien von vor 0.660.0 nachträglich verbinden: gleiche Züge ab der Grundstellung (über den
    /// Dubletten-Schlüssel der Vereins-Datenbank). Höchstens <paramref name="take"/> Kopien je Lauf. → Zahl der verbundenen.
    /// </summary>
    public async Task<int> LinkClubCopiesAsync(int take = 500, CancellationToken ct = default)
    {
        // je Vereinspartie ihr Verein — eine Kopie wird nur mit einer Partie aus einem Verein ihres Besitzers verbunden
        // (Mandanten-Schritt 2026-10-07)
        var hashes = (await _db.LeagueClubGames.AsNoTracking().Select(g => new { g.Id, g.MovesHash, g.ClubId }).ToListAsync(ct))
            .GroupBy(g => g.MovesHash).ToDictionary(x => x.Key, x => x.OrderByDescending(g => g.Id).Select(g => (g.Id, g.ClubId)).ToList());
        if (hashes.Count == 0) return 0;
        var memberships = new Dictionary<int, HashSet<int>>();
        var candidates = await _db.SavedGames.Where(g => g.LeagueClubGameId == null && g.Source == "pgn")
            .OrderBy(g => g.Id).Take(take).ToListAsync(ct);
        var linked = 0;
        foreach (var g in candidates)
        {
            var parsed = PgnParser.SplitGames(g.Pgn).FirstOrDefault();
            if (parsed.Headers is { } h && h.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen) && !IsStandardStart(fen)) continue;
            var sans = PgnParser.ExtractMainlineSans(parsed.MoveText ?? string.Empty);
            if (sans.Count == 0 || !hashes.TryGetValue(League.LeagueClubService.HashOf(sans), out var sources)) continue;
            if (!memberships.TryGetValue(g.UserId, out var mine))
                memberships[g.UserId] = mine = await League.LeagueClubResolver.ClubIdsOfAsync(_db, g.UserId, ct);
            var (clubId, _) = sources.FirstOrDefault(x => mine.Contains(x.ClubId));
            if (clubId == 0) continue;
            g.LeagueClubGameId = clubId;
            // Liga + Jahrgang nachtragen, soweit der Nutzer nichts eingetragen hat (0.666.0).
            var (c1, c2) = await ClubClassifiersAsync(clubId, ct);
            g.Classifier1 ??= c1;
            g.Classifier2 ??= c2;
            linked++;
        }
        if (linked > 0) await _db.SaveChangesAsync(ct);
        return linked;
    }

    private static bool IsStandardStart(string fen)
        => string.Join(' ', fen.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4))
           == "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -";

    /// <summary>„Weiß – Schwarz" wie der Titel, den die Seiten frueher selbst mitschickten; ohne beide
    /// Namen keiner (dann baut die Analyse ihn aus den PGN-Kopfdaten).</summary>
    private static string? AnalysisTitleOf(SavedGame g)
        => string.IsNullOrWhiteSpace(g.White) || string.IsNullOrWhiteSpace(g.Black)
            ? null
            : $"{g.White.Trim()} – {g.Black.Trim()}";

    /// <summary>Bewertungen einer EIGENEN Partie; <c>null</c>, wenn es sie nicht gibt oder sie fremd ist.</summary>
    public async Task<GameEvalsDto?> GetEvalsAsync(int userId, int savedGameId, CancellationToken ct = default, bool withBook = true)
    {
        var head = await _db.SavedGames.AsNoTracking()
            .Where(g => g.Id == savedGameId && g.UserId == userId)
            .Select(g => new { g.Id, g.GameAnalysisId })
            .FirstOrDefaultAsync(ct);
        return head is null ? null : await EvalsAsync(head.Id, head.GameAnalysisId, userId, ct, withBook);
    }

    /// <summary>Bewertungen der geteilten Partie; <c>null</c> bei unbekanntem Token.
    /// <paramref name="callerUserId"/> = <c>null</c> (anonym): NUR die verknuepfte Analyse.</summary>
    public async Task<GameEvalsDto?> GetSharedEvalsAsync(string token, int? callerUserId, CancellationToken ct = default, bool withBook = true)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var head = await _db.SavedGames.AsNoTracking()
            .Where(g => g.ShareToken == token)
            .Select(g => new { g.Id, g.GameAnalysisId })
            .FirstOrDefaultAsync(ct);
        return head is null ? null : await EvalsAsync(head.Id, head.GameAnalysisId, callerUserId, ct, withBook);
    }


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
    private async Task<GameEvalsDto> EvalsAsync(int savedGameId, int? linkedId, int? callerUserId, CancellationToken ct, bool withBook = true)
    {
        GameEvalsStore.Head? analysis = null;
        if (linkedId is int id)
            analysis = await GameEvalsStore.Heads(_db.GameAnalyses.AsNoTracking().Where(a => a.Id == id))
                .FirstOrDefaultAsync(ct);
        if (analysis is null && callerUserId is int caller)
        {
            var pgn = _db.SavedGames.Where(g => g.Id == savedGameId).Select(g => g.Pgn);
            analysis = await GameEvalsStore.Heads(_db.GameAnalyses.AsNoTracking()
                    .Where(a => a.UserId == caller && a.Status != GameAnalysisStatus.Failed && pgn.Contains(a.Pgn))
                    .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id))
                .FirstOrDefaultAsync(ct);
        }
        if (analysis is null) return new GameEvalsDto();

        // Buchzüge nur für einen angemeldeten Aufrufer und aus SEINEN Repertoires: anonym gibt es keine, und die des
        // Teilenden bekäme ein Gast nie zu sehen — sonst verriete ein Teilen-Link, was jemand vorbereitet hat.
        // withBook=false (0.664.0): das Mengen-Set der Repertoires wird nach fünf Minuten Leerlauf neu aufgebaut (2–4 s bei
        // 13 MB PGN) und hielt die ganze Antwort auf — die Seite holt Kurve und Buchzüge jetzt getrennt.
        Func<List<string>, Task<List<int>>>? book = withBook && callerUserId is int viewer
            ? fens => _repertoires.BookPliesAsync(viewer, fens)
            : null;
        return await GameEvalsStore.ReadAsync(_db, analysis, book, ct);
    }

    /// <summary>Welche Seite spielte der Besitzer? Vergleich der Spielernamen mit seinem
    /// Plattform-Username (lichess/chess.com je nach Quelle, case-insensitiv) — dieselbe
    /// Logik wie das clientseitige Flippen der eigenen Nachspiel-Ansicht (games-list.isFlipped).</summary>
    internal static string? DetermineOwnerSide(SavedGame g, UserProfile? profile)
    {
        // Selbst festgelegt schlägt jede Vermutung (0.531.0).
        if (g.OwnerSide is "white" or "black") return g.OwnerSide;
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
        IReadOnlyDictionary<int, string>? comments, GameHeaderInput header, string? ownerSide = null)
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
            OwnerSide = ownerSide is "white" or "black" ? ownerSide : null,
            ShareToken = await GenerateUniqueTokenAsync(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.SavedGames.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    /// <summary>So viele Partien nimmt ein PGN-Upload höchstens auf; der Rest bleibt liegen (<c>Truncated</c>).</summary>
    public const int MaxImportGames = 200;

    /// <summary>Höchstens so viele Zeichen PGN je Upload (≈ 200 kommentierte Partien).</summary>
    public const int MaxImportChars = 5_000_000;

    /// <summary>
    /// PGN-Upload (Datei oder eingefügt): jede Partie des Textes wird eine eigene Partie mit Quelle <c>pgn</c>.
    /// Übernommen werden die HAUPTVARIANTE mit ihren Kommentaren (Varianten fallen weg — Analyse, Kurve und
    /// Fehler-Training arbeiten auf einer Zugfolge) und alle Kopfdaten (Elo, Bedenkzeit, FEN …). Eine Partie, deren
    /// Hauptvariante nicht bis zum Ende legal ist, wird NICHT gekürzt angelegt, sondern gemeldet — eine halbe Partie
    /// sähe vollständig aus.
    ///
    /// <para><b>Zweimal hochgeladen = einmal da:</b> die Kennung ist ein Hash über Kopfdaten und Züge
    /// (<c>ExternalId</c>, eindeutig je Nutzer und Quelle) — wer dieselbe Datei nach einer Ergänzung noch einmal
    /// hochlädt, bekommt nur die neuen Partien.</para>
    ///
    /// <para><paramref name="ownerSide"/> (<c>white</c>/<c>black</c>, sonst ignoriert) wird als festgelegte Seite an
    /// jede NEU angelegte Partie geschrieben — eine schon vorhandene (Dublette) bleibt, wie sie ist.</para>
    /// </summary>
    public async Task<PgnImportResultDto> ImportPgnAsync(int userId, string pgn, string? ownerSide = null, CancellationToken ct = default)
    {
        var side = ownerSide is "white" or "black" ? ownerSide : null;
        var result = new PgnImportResultDto();
        var games = PgnParser.SplitGames(pgn).Where(g => !string.IsNullOrWhiteSpace(g.MoveText)).ToList();
        if (games.Count > MaxImportGames) { result.Truncated = true; games = games.Take(MaxImportGames).ToList(); }

        // Deckel je Konto (A6-007): Belegung EINMAL lesen und selbst fortschreiben. Zwei parallele Uploads desselben
        // Kontos können ihn um höchstens einen Upload überschreiten — ein weicher Deckel, der das Füllen verhindert.
        var (ownGames, ownChars) = await UsageAsync(userId, ct);

        var index = 0;
        foreach (var (rawHeaders, moveText) in games)
        {
            index++;
            var headers = rawHeaders ?? new Dictionary<string, string>();
            string? H(string key) => headers.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && v.Trim() != "?" ? v.Trim() : null;
            PgnImportFailureDto Fail(string reason) => new() { Index = index, White = Clip(H("White"), 120), Black = Clip(H("Black"), 120), Reason = reason };

            var startFen = H("FEN");
            List<string> sans;
            try
            {
                var fen = startFen ?? new Chess.ChessBoard().ToFen();
                // Nur ein Ergebnis („1-0", „*") ist eine Partie ohne Züge, kein illegaler Zug — TryExtractUciMainline meldet beides null.
                if (PgnParser.ExtractMainlineSans(moveText).Count == 0) { result.Failed.Add(Fail("noMoves")); continue; }
                var uci = PgnParser.TryExtractUciMainline(fen, moveText);
                if (uci == null) { result.Failed.Add(Fail(startFen != null && !IsLoadableFen(startFen) ? "badFen" : "illegal")); continue; }
                if (uci.Count == 0) { result.Failed.Add(Fail("noMoves")); continue; }
                if (uci.Count > 600) { result.Failed.Add(Fail("tooLong")); continue; }
                sans = SansOf(fen, uci);
                if (sans.Count != uci.Count) { result.Failed.Add(Fail("illegal")); continue; }
            }
            catch (Exception) { result.Failed.Add(Fail("illegal")); continue; }

            var comments = PgnParser.ExtractMoveComments(moveText) ?? new Dictionary<int, string>();
            var gameResult = H("Result") is { } r && AllowedResults.Contains(r) ? r : "*";
            var header = new GameHeaderInput(H("Event"), H("Site"), H("Date"), H("Round"), H("White"), H("Black"), gameResult);
            var externalId = "pgn:" + ImportKey(headers, sans);

            var existing = await _db.SavedGames.AsNoTracking()
                .Where(g => g.UserId == userId && g.Source == ImportSource && g.ExternalId == externalId)
                .Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
            if (existing is int id) { result.Duplicates++; result.Ids.Add(id); continue; }

            var kept = new Dictionary<string, string>(headers.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Trim()), StringComparer.Ordinal);
            var gamePgn = BuildHeaderedPgn(kept, header, gameResult, sans, startFen, comments);
            if (ownGames >= GamesPerUserCap || ownChars + gamePgn.Length > PgnCharsPerUserCap)
            {
                result.Failed.Add(Fail("quota"));
                continue;
            }
            var clubGameId = startFen is null || IsStandardStart(startFen) ? await ClubGameForMovesAsync(sans, userId, ct) : null;
            var (clubClass1, clubClass2) = await ClubClassifiersAsync(clubGameId, ct);
            var entity = new SavedGame
            {
                UserId = userId,
                Source = ImportSource,
                ExternalId = externalId,
                White = Clip(H("White"), 120),
                Black = Clip(H("Black"), 120),
                Result = gameResult,
                PlayedAt = ParseDate(H("Date")),
                Pgn = gamePgn,
                MoveCount = sans.Count,
                WhiteElo = PlausibleElo(int.TryParse(H("WhiteElo"), out var we) ? we : null),
                BlackElo = PlausibleElo(int.TryParse(H("BlackElo"), out var be) ? be : null),
                TimeControl = CleanTimeControl(H("TimeControl")),
                HeadersScanned = true,
                OwnerSide = side,
                ShareToken = await GenerateUniqueTokenAsync(),
                CreatedAt = DateTime.UtcNow,
                // Aus der Vereins-Datenbank kopiert: deren Analyse gleich mitnehmen (0.653.0) — und seit 0.660.0 mit ihr
                // verbunden bleiben, damit eine Korrektur der Vereinspartie hier ankommt.
                GameAnalysisId = startFen is null || IsStandardStart(startFen) ? await ClubAnalysisForMovesAsync(sans, userId, ct) : null,
                LeagueClubGameId = clubGameId,
                Classifier1 = clubClass1,   // Liga + Jahrgang aus der Vereinspartie (0.666.0)
                Classifier2 = clubClass2,
            };
            _db.SavedGames.Add(entity);
            await _db.SaveChangesAsync(ct);
            ownGames++;
            ownChars += gamePgn.Length;
            result.Imported++;
            result.Ids.Add(entity.Id);
        }
        return result;
    }

    /// <summary>Quelle der hochgeladenen Partien.</summary>
    public const string ImportSource = "pgn";

    internal static bool IsLoadableFen(string fen)
    {
        try { Chess.ChessBoard.LoadFromFen(fen); return true; } catch { return false; }
    }

    /// <summary>Die UCI-Hauptvariante als SAN in der Schreibweise des Bretts; bricht am ersten nicht spielbaren Zug ab.</summary>
    internal static List<string> SansOf(string fen, IReadOnlyList<string> uci)
    {
        var board = Chess.ChessBoard.LoadFromFen(fen);
        var sans = new List<string>(uci.Count);
        foreach (var u in uci)
        {
            var move = Array.Find(board.Moves(generateSan: true), m => GamePlies.ToUci(m) == u);
            if (move == null || !board.Move(move)) break;
            sans.Add(string.IsNullOrEmpty(move.San) ? u : move.San);
        }
        return sans;
    }

    /// <summary>Kennung einer hochgeladenen Partie: Hash über die Seven-Tag-Kopfdaten, die FEN und die Züge.</summary>
    internal static string ImportKey(IReadOnlyDictionary<string, string> headers, IReadOnlyList<string> sans)
    {
        string V(string k) => headers.TryGetValue(k, out var v) ? v.Trim() : "";
        var text = string.Join("|", SevenTags.Select(V).Append(V("FEN"))) + "|" + string.Join(' ', sans);
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash)[..40].ToLowerInvariant();
    }

    /// <summary>
    /// Korrigiert eine eigene Partie: Züge (mit Kommentaren) und Kopfdaten. Die Züge werden ab der
    /// Ausgangsstellung nachgespielt — ist einer nicht legal, gibt es eine <see cref="ArgumentException"/>
    /// und nichts wird geschrieben. Header, die hier nicht bearbeitet werden (Elo, Bedenkzeit, FEN), bleiben.
    ///
    /// <para>Ändern sich die ZÜGE, gehört die verknüpfte Analyse nicht mehr zur Partie (Kurve, Fehler und
    /// Genauigkeit rechneten eine andere) — der Verweis fällt, ebenso der Stand des Fehler-Trainings und die
    /// Nacherzählung (<see cref="OnMovesChangedAsync"/>).</para>
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

        var pgn = BuildHeaderedPgn(headers, header, result, sans, startFen, comments);
        // Deckel je Konto (A6-007): sonst füllte man ihn über viele kleine Partien und blähte sie hier einzeln auf
        // (Kommentare und Kopfdaten sind ungekürzt). Nur ein WACHSENDES PGN zählt nach — Kürzen geht immer.
        if (pgn.Length > g.Pgn.Length
            && (await UsageAsync(userId)).Chars - g.Pgn.Length + pgn.Length > PgnCharsPerUserCap)
            throw new SavedGameQuotaException($"Saved game storage limit reached (max {PgnCharsPerUserCap} characters per account).");
        g.Pgn = pgn;
        g.White = Clip(dto.White, 120);
        g.Black = Clip(dto.Black, 120);
        g.Result = result;
        g.PlayedAt = ParseDate(dto.Date) ?? (string.IsNullOrWhiteSpace(dto.Date) ? null : g.PlayedAt);
        g.MoveCount = sans.Count;
        // null = unverändert; "" oder etwas anderes = Festlegung zurücknehmen.
        if (dto.OwnerSide != null) g.OwnerSide = dto.OwnerSide is "white" or "black" ? dto.OwnerSide : null;
        // null = unverändert, leer = zurücknehmen (Online-Partien fallen auf den abgeleiteten Wert zurück).
        if (dto.Classifier1 != null) g.Classifier1 = GameClassifier.Clean(dto.Classifier1);
        if (dto.Classifier2 != null) g.Classifier2 = GameClassifier.Clean(dto.Classifier2);
        if (dto.Tags != null) g.Tags = GameTags.Join(dto.Tags);
        if (movesChanged) await OnMovesChangedAsync(g);
        await _db.SaveChangesAsync();
        var dtoOut = MapDetail(g);
        var profile = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        dtoOut.OwnerSide = DetermineOwnerSide(g, profile);
        return dtoOut;
    }

    /// <summary>
    /// Die Züge einer Partie haben sich geändert (Korrigieren in <see cref="UpdateAsync"/> oder ein längerer Re-Save in
    /// <see cref="SaveAsync"/>): die verknüpfte Analyse (Kurve, Fehler, Genauigkeit), der Stand des Fehler-Trainings und
    /// die Nacherzählung (auch in der Link-Vorschau von <c>/g/{token}</c>) gehören zur alten Zugfolge. Der Verweis fällt,
    /// Training und Nacherzählung werden gelöscht — „Analysieren" rechnet danach die neue Fassung, und nach der Analyse
    /// entsteht die Nacherzählung neu. Speichert nicht selbst.
    /// </summary>
    private Task OnMovesChangedAsync(SavedGame g) => MovesChangedAsync(_db, g, default);

    private static async Task MovesChangedAsync(AppDbContext db, SavedGame g, CancellationToken ct)
    {
        g.GameAnalysisId = null;
        db.GameMistakeProgresses.RemoveRange(await db.GameMistakeProgresses.Where(p => p.SavedGameId == g.Id).ToListAsync(ct));
        db.GameRecaps.RemoveRange(await db.GameRecaps.Where(r => r.SavedGameId == g.Id).ToListAsync(ct));
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

    /// <summary>Hosts der beiden Quellen (<see cref="AllowedSources"/>), jeweils samt Subdomains (www., m.).</summary>
    private static readonly string[] SourceUrlHosts = ["chess.com", "lichess.org"];

    /// <summary>
    /// Der Herkunfts-Link einer Partie, wie ihn die Oberflaechen als „Original oeffnen" rendern — auch auf der
    /// ANONYMEN Teilen-Seite <c>/g/{token}</c> (Codereview F4-014). Nur absolute http(s)-Adressen der beiden
    /// Quellen; alles andere wird <c>null</c>, statt unter dem Etikett der App auf eine beliebige Seite zu
    /// fuehren (javascript: neutralisiert Angular ohnehin, eine fremde https-Seite nicht). Gilt beim Speichern
    /// UND beim Ausliefern — so ist auch ein frueher gespeicherter Wert entschaerft.
    /// </summary>
    public static string? SafeSourceUrl(string? raw)
    {
        var s = Clip(raw, 1000);
        if (s is null || !Uri.TryCreate(s, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
        var host = uri.Host;
        return SourceUrlHosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)) ? s : null;
    }

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
        if (!moreMoves && !(newHasElo && !oldHasElo) && !FillsMissing(existing, dto)) return false;

        // Das PGN wird aus dem NEUEN Save gebaut — was ihm fehlt, kommt aus der gespeicherten Partie, sonst stünde im Kopf
        // „?" bzw. keine Wertung, während die Spalten den alten Wert behalten.
        if (string.IsNullOrWhiteSpace(dto.White)) dto.White = existing.White;
        if (string.IsNullOrWhiteSpace(dto.Black)) dto.Black = existing.Black;
        dto.WhiteElo ??= existing.WhiteElo;
        dto.BlackElo ??= existing.BlackElo;
        if (CleanTimeControl(dto.TimeControl) == null) dto.TimeControl = existing.TimeControl;
        dto.PlayedAt ??= existing.PlayedAt;
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

    /// <summary>
    /// Bringt der neue Save Kopfdaten, die der gespeicherten Partie FEHLEN (Spieler, Bedenkzeit, Datum)? Dann heilt er sie
    /// auch ohne neue Züge oder Wertung (0.725.x, Prod-Partie 63 am 08.10.: RepCheck schickte direkt nach Partieende gar keine
    /// Metadaten, „?" gegen „?" — ein zweiter Klick auf „Partie speichern" reparierte das nicht, weil nur Züge/Elo heilten).
    /// </summary>
    internal static bool FillsMissing(SavedGame existing, SaveGameInputDto dto)
        => (string.IsNullOrWhiteSpace(existing.White) && !string.IsNullOrWhiteSpace(dto.White))
           || (string.IsNullOrWhiteSpace(existing.Black) && !string.IsNullOrWhiteSpace(dto.Black))
           || (existing.TimeControl == null && CleanTimeControl(dto.TimeControl) != null)
           || (existing.PlayedAt == null && dto.PlayedAt.HasValue);

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

    /// <summary>Die Halbzüge eines gespeicherten PGN, rein lexikalisch (ohne Brett — auch alt gespeicherte,
    /// lückenhafte Partien lassen sich so lesen): Kommentare, NAGs, Zugnummern und das Ergebnis fallen weg.</summary>
    private static List<string> StoredSans(string pgn)
    {
        var moveText = PgnParser.SplitGames(pgn ?? string.Empty).FirstOrDefault().MoveText ?? string.Empty;
        moveText = Regex.Replace(moveText, @"\{[^}]*\}", " ");
        var sans = new List<string>();
        foreach (var token in moveText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (char.IsDigit(token[0]) && (token.Contains('.') || token.Contains('…'))) continue;   // Zugnummer
            if (token[0] == '$' || AllowedResults.Contains(token)) continue;                        // NAG, Ergebnis
            sans.Add(token);
        }
        return sans;
    }

    /// <summary>Vergleichsform einer Zugliste: ohne die „…"-Platzhalter der alten DOM-Auslese, Schach-/
    /// Matt-Zeichen und Bewertungen am Zugende, Rochade mit O statt 0.</summary>
    private static List<string> SanKeys(IEnumerable<string> sans)
        => sans.Select(s => s.Trim())
            .Where(s => s.Length > 0 && s.Any(c => c != '.' && c != '…'))
            .Select(s => s.TrimEnd('+', '#', '!', '?').Replace('0', 'O'))
            .ToList();

    /// <summary>Ist <paramref name="head"/> der Anfang von <paramref name="line"/> (gleich lang zählt mit)?</summary>
    private static bool IsPrefixOf(IReadOnlyList<string> head, IReadOnlyList<string> line)
    {
        if (head.Count > line.Count) return false;
        for (var i = 0; i < head.Count; i++)
            if (!string.Equals(head[i], line[i], StringComparison.Ordinal)) return false;
        return true;
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

    private Task<string> GenerateUniqueTokenAsync()
        => ShareTokens.NewUniqueAsync(t => _db.SavedGames.AnyAsync(g => g.ShareToken == t));

    private static SavedGameDetailDto MapDetail(SavedGame g) => new()
    {
        Id = g.Id,
        Source = g.Source,
        White = g.White,
        Black = g.Black,
        Result = g.Result,
        PlayedAt = g.PlayedAt,
        SourceUrl = SafeSourceUrl(g.SourceUrl),
        ShareToken = g.ShareToken,
        MoveCount = CountPlies(g.Pgn),
        CreatedAt = g.CreatedAt,
        Pgn = g.Pgn,
        // Aus dem PGN und nicht aus den Spalten: hier LIEGT das PGN, und beim Altbestand steht die
        // Wertung nur dort (der Nachtrag laeuft ueber die Liste).
        WhiteElo = ParseEloHeader(g.Pgn, "WhiteElo"),
        BlackElo = ParseEloHeader(g.Pgn, "BlackElo"),
        TimeControl = g.TimeControl,
        Classifier1 = GameClassifier.Effective(g.Source, g.TimeControl, g.Classifier1, g.Classifier2).First,
        Classifier2 = GameClassifier.Effective(g.Source, g.TimeControl, g.Classifier1, g.Classifier2).Second,
        Classifier1Set = GameClassifier.Clean(g.Classifier1),
        Classifier2Set = GameClassifier.Clean(g.Classifier2),
        Tags = GameTags.Parse(g.Tags),
    };
}

/// <summary>Belegung eines Kontos an gespeicherten Partien (<see cref="SavedGameService.UsageQuery"/>).</summary>
internal sealed record SavedGameUsage(int Games, long Chars);

/// <summary>Das Konto ist am Deckel für gespeicherte Partien (<see cref="SavedGameService.MaxGamesPerUser"/>,
/// <see cref="SavedGameService.MaxPgnCharsPerUser"/>, A6-007). Eine <see cref="ArgumentException"/>, damit die
/// bestehenden 400-Zweige (<c>{ message }</c>) greifen; wer den Grund nennen will, fängt sie davor.</summary>
public sealed class SavedGameQuotaException(string message) : ArgumentException(message);
