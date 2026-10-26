using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.Scoresheet;
using static RookHub.Api.Services.Games.SavedGamePgn;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Speichert/liest vom User auf chess.com/lichess gespeicherte Partien (RepCheck „Partie speichern").
/// Aus der SAN-Zugliste + Metadaten wird serverseitig ein PGN gebaut. Jede Partie bekommt ein
/// eindeutiges ShareToken für den öffentlichen Teilen-Link.
///
/// <para>Dazu „Partie analysieren" und die Bewertungskurve: die Partie wird ueber denselben Einwurf
/// wie auf der Punktepartie-Seite gerechnet (<see cref="GameAnalysisService.CreateForGuessAsync"/>,
/// Ursprung <see cref="GameAnalysisOrigin.SavedGame"/>), und <see cref="SavedGame.GameAnalysisId"/>
/// merkt sich, welche Analyse zur Partie gehoert.</para>
///
/// <para>Fassade (Zerlegung Z13): Hereinholen liegt in <see cref="SavedGameImport"/>, Korrigieren in
/// <see cref="SavedGameCorrection"/>, Analyse und Vereinspartien in <see cref="SavedGameAnalysisLink"/>, der Teilen-Link in
/// <see cref="SavedGameSharing"/>, der PGN-Bau in <see cref="SavedGamePgn"/>.</para>
/// </summary>
public class SavedGameService
{
    private readonly AppDbContext _db;
    private readonly SavedGameSharing _sharing;
    private readonly SavedGameAnalysisLink _link;
    private readonly SavedGameCorrection _correction;
    private readonly SavedGameImport _import;

    public SavedGameService(AppDbContext db, GameAnalysisService analyses, RepertoireAnalyzeService repertoires,
        ILogger<SavedGameService>? logger = null)
    {
        _db = db;
        var log = logger ?? NullLogger<SavedGameService>.Instance;
        _sharing = new SavedGameSharing(db);
        _link = new SavedGameAnalysisLink(db, analyses, repertoires);
        _correction = new SavedGameCorrection(db, this);
        _import = new SavedGameImport(db, this, _link, _correction, log);
    }

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

    internal async Task<SavedGameUsage> UsageAsync(int userId, CancellationToken ct = default)
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
        (dto.WhiteElo, dto.BlackElo) = await _sharing.WithLeagueEloAsync(g.LeagueClubGameId, dto.WhiteElo, dto.BlackElo);
        if (dto.ScanId == null && g.LeagueClubGameId is { } clubId)
        {
            var now = DateTime.UtcNow;
            dto.ClubSheet = await _db.LeagueClubGames.AnyAsync(c => c.Id == clubId && c.UploadedByUserId == g.UserId)
                && await _db.ScoresheetScanArchives.AnyAsync(a => a.LeagueClubGameId == clubId && a.Page == 1 && a.View == 0 && a.ExpiresAt > now);
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

    /// <summary>
    /// Widerruft den öffentlichen Teilen-Link einer eigenen Partie (Codereview A2-006): die Partie bekommt ein NEUES
    /// Token, der alte <c>/g/</c>-Link (samt Roast-, Trainings- und Analyse-Links daran) liefert ab sofort 404. Das neue
    /// Token ist nirgends veröffentlicht — erst „Link kopieren" gibt es heraus. Vorher blieb nur „Partie löschen".
    /// <c>null</c>, wenn die Partie nicht existiert oder fremd ist.
    /// </summary>
    public async Task<string?> RotateShareTokenAsync(int userId, int id, CancellationToken ct = default)
    {
        var g = await _db.SavedGames.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (g == null) return null;
        g.ShareToken = await ShareTokens.NewUniqueAsync(t => _db.SavedGames.AnyAsync(x => x.ShareToken == t, ct));
        await _db.SaveChangesAsync(ct);
        return g.ShareToken;
    }

    // ── Hereinholen (SavedGameImport) ─────────────────────────────────

    // ── Vom Server angelegt + korrigiert (Partieformular, 0.529.0) ─────

    /// <summary>Quelle einer aus einem Formular-Foto eingelesenen Partie.</summary>
    public const string ScoresheetSource = "scoresheet";

    /// <summary>So viele Partien nimmt ein PGN-Upload höchstens auf; der Rest bleibt liegen (<c>Truncated</c>).</summary>
    public const int MaxImportGames = 200;

    /// <summary>Höchstens so viele Zeichen PGN je Upload (≈ 200 kommentierte Partien).</summary>
    public const int MaxImportChars = 5_000_000;

    /// <summary>Quelle der hochgeladenen Partien.</summary>
    public const string ImportSource = "pgn";

    /// <summary>Legt eine gespeicherte Partie an oder gibt die vorhandene zurück (<see cref="SavedGameImport.SaveAsync"/>).
    /// Wirft bei ungültiger Eingabe.</summary>
    public Task<SavedGameDetailDto> SaveAsync(int userId, SaveGameInputDto dto) => _import.SaveAsync(userId, dto);

    /// <summary>Legt eine Partie an, die der SERVER gebaut hat (<see cref="SavedGameImport.CreateGeneratedAsync"/>).</summary>
    public Task<SavedGame> CreateGeneratedAsync(int userId, string source, IReadOnlyList<string> sans,
        IReadOnlyDictionary<int, string>? comments, GameHeaderInput header, string? ownerSide = null) =>
        _import.CreateGeneratedAsync(userId, source, sans, comments, header, ownerSide);

    /// <summary>PGN-Upload: jede Partie des Textes eine eigene Partie (<see cref="SavedGameImport.ImportPgnAsync"/>).</summary>
    public Task<PgnImportResultDto> ImportPgnAsync(int userId, string pgn, string? ownerSide = null, CancellationToken ct = default) =>
        _import.ImportPgnAsync(userId, pgn, ownerSide, ct);

    internal static bool FillsMissing(SavedGame existing, SaveGameInputDto dto) => SavedGameImport.FillsMissing(existing, dto);

    // ── Korrigieren (SavedGameCorrection) ─────────────────────────────

    /// <summary>Korrigiert eine eigene Partie (<see cref="SavedGameCorrection.UpdateAsync"/>); <c>null</c>, wenn sie nicht
    /// existiert oder fremd ist, <see cref="ArgumentException"/> bei einem illegalen Zug.</summary>
    public Task<SavedGameDetailDto?> UpdateAsync(int userId, int id, GameUpdateDto dto) => _correction.UpdateAsync(userId, id, dto);

    // ── Analyse + Bewertungskurve (SavedGameAnalysisLink) ─────────────

    public Task<GameAnalyzeResultDto?> AnalyzeAsync(int userId, int savedGameId, CancellationToken ct = default,
        string? lang = null) => _link.AnalyzeAsync(userId, savedGameId, ct, lang);

    public Task<GameAnalyzeResultDto?> AnalyzeSharedAsync(int userId, string token, CancellationToken ct = default,
        string? lang = null) => _link.AnalyzeSharedAsync(userId, token, ct, lang);

    public Task<GameEvalsDto?> GetEvalsAsync(int userId, int savedGameId, CancellationToken ct = default, bool withBook = true) =>
        _link.GetEvalsAsync(userId, savedGameId, ct, withBook);

    public Task<GameEvalsDto?> GetSharedEvalsAsync(string token, int? callerUserId, CancellationToken ct = default, bool withBook = true) =>
        _link.GetSharedEvalsAsync(token, callerUserId, ct, withBook);

    /// <summary>Eine korrigierte Vereinspartie in alle verbundenen Kopien tragen (<see cref="SavedGameAnalysisLink.ApplyClubMovesAsync"/>).</summary>
    public static Task<int> ApplyClubMovesAsync(AppDbContext db, int clubGameId, IReadOnlyList<string> sans, int? except,
        CancellationToken ct = default) => SavedGameAnalysisLink.ApplyClubMovesAsync(db, clubGameId, sans, except, ct);

    public Task<int> LinkClubCopiesAsync(int take = 500, CancellationToken ct = default) => _link.LinkClubCopiesAsync(take, ct);

    // ── Teilen-Link (SavedGameSharing) ────────────────────────────────

    /// <summary>Öffentliche Sicht über das ShareToken; null wenn unbekannt.</summary>
    public Task<SharedGameDto?> GetSharedAsync(string token, int? callerUserId = null) => _sharing.GetSharedAsync(token, callerUserId);

    internal static string? DetermineOwnerSide(SavedGame g, UserProfile? profile) => SavedGameSharing.DetermineOwnerSide(g, profile);

    // ── PGN (SavedGamePgn) ────────────────────────────────────────────

    public static List<string> LegalSans(IReadOnlyList<string> sans, string? startFen = null) => SavedGamePgn.LegalSans(sans, startFen);

    public static string? SafeSourceUrl(string? raw) => SavedGamePgn.SafeSourceUrl(raw);

    public static string BuildPgn(List<string> moves, SaveGameInputDto dto, string result) => SavedGamePgn.BuildPgn(moves, dto, result);

    public static string? CleanTimeControl(string? raw) => SavedGamePgn.CleanTimeControl(raw);

    public static int? ParseEloHeader(string pgn, string tag) => SavedGamePgn.ParseEloHeader(pgn, tag);

    internal static bool IsLoadableFen(string fen) => SavedGamePgn.IsLoadableFen(fen);

    internal static List<string> SansOf(string fen, IReadOnlyList<string> uci) => SavedGamePgn.SansOf(fen, uci);

    internal static string ImportKey(IReadOnlyDictionary<string, string> headers, IReadOnlyList<string> sans) =>
        SavedGamePgn.ImportKey(headers, sans);

    internal static SavedGameDetailDto MapDetail(SavedGame g) => new()
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
