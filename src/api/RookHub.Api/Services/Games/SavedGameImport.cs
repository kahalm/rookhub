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
/// Partien hereinholen (Zerlegung Z13 aus <see cref="SavedGameService"/>): „Partie speichern" aus der Erweiterung (mit
/// Dubletten-Schutz und Heilen), vom Server angelegte Partien (Partieformular) und der PGN-Upload.
/// </summary>
internal sealed class SavedGameImport
{
    private readonly AppDbContext _db;
    private readonly SavedGameService _owner;
    private readonly SavedGameAnalysisLink _link;
    private readonly SavedGameCorrection _correction;
    private readonly ILogger<SavedGameService> _logger;

    internal SavedGameImport(AppDbContext db, SavedGameService owner, SavedGameAnalysisLink link, SavedGameCorrection correction,
        ILogger<SavedGameService> logger)
    {
        _db = db;
        _owner = owner;
        _link = link;
        _correction = correction;
        _logger = logger;
    }

    /// <summary>Legt eine gespeicherte Partie an (oder gibt die bereits vorhandene zurück, wenn
    /// dieselbe ExternalId für denselben User+Source schon existiert — verhindert Doppel-Klicks).
    /// Wirft bei ungültiger Eingabe.</summary>
    public async Task<SavedGameDetailDto> SaveAsync(int userId, SaveGameInputDto dto)
    {
        var source = SavedGameService.NormalizeSource(dto.Source) ?? throw new ArgumentException("Invalid source.");
        // Vorab entschaerfen: der Link geht in die Spalte, in den [Site]-Header und beim Heilen in den Bestand.
        dto.SourceUrl = SafeSourceUrl(dto.SourceUrl);
        var moves = (dto.Moves ?? new())
            .Select(m => (m ?? string.Empty).Trim())
            .Where(m => m.Length > 0)
            .ToList();
        if (moves.Count == 0) throw new ArgumentException("No moves.");
        if (moves.Count > 600) throw new ArgumentException("Too many moves (max 600 plies).");
        if (moves.Any(m => m.Length > SavedGameService.MaxSanLength)) throw new ArgumentException("Invalid move.");

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
                        if (incoming.Count > stored.Count) await _correction.OnMovesChangedAsync(existing);
                        await _db.SaveChangesAsync();
                    }
                    return SavedGameService.MapDetail(existing);
                }
                // Kürzere Fassung derselben Partie (Doppelklick, Zugliste noch nicht ganz geladen):
                // die gespeicherte bleibt — kürzt nie.
                if (IsPrefixOf(incoming, stored)) return SavedGameService.MapDetail(existing);

                divergedFrom = existing;
                externalId = null;
            }
        }

        if (await _db.SavedGames.CountAsync(g => g.UserId == userId) >= _owner.GamesPerUserCap)
            throw new SavedGameQuotaException($"Saved game limit reached (max {_owner.GamesPerUserCap} per account).");

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
            if (existing != null) return SavedGameService.MapDetail(existing);
            throw;
        }
        if (divergedFrom != null)
            _logger.LogWarning(
                "Partie speichern: {Source}-Partie {ExternalId} von User {UserId} setzt die gespeicherte Partie {ExistingGameId} nicht fort — als neue Partie {SavedGameId} ohne ExternalId angelegt",
                source, divergedFrom.ExternalId, userId, divergedFrom.Id, entity.Id);
        return SavedGameService.MapDetail(entity);
    }

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
        if (games.Count > SavedGameService.MaxImportGames) { result.Truncated = true; games = games.Take(SavedGameService.MaxImportGames).ToList(); }

        // Deckel je Konto (A6-007): Belegung EINMAL lesen und selbst fortschreiben. Zwei parallele Uploads desselben
        // Kontos können ihn um höchstens einen Upload überschreiten — ein weicher Deckel, der das Füllen verhindert.
        var (ownGames, ownChars) = await _owner.UsageAsync(userId, ct);

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
                .Where(g => g.UserId == userId && g.Source == SavedGameService.ImportSource && g.ExternalId == externalId)
                .Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
            if (existing is int id) { result.Duplicates++; result.Ids.Add(id); continue; }

            var kept = new Dictionary<string, string>(headers.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Trim()), StringComparer.Ordinal);
            var gamePgn = BuildHeaderedPgn(kept, header, gameResult, sans, startFen, comments);
            if (ownGames >= _owner.GamesPerUserCap || ownChars + gamePgn.Length > _owner.PgnCharsPerUserCap)
            {
                result.Failed.Add(Fail("quota"));
                continue;
            }
            var clubGameId = startFen is null || IsStandardStart(startFen) ? await _link.ClubGameForMovesAsync(sans, userId, ct) : null;
            var (clubClass1, clubClass2) = await _link.ClubClassifiersAsync(clubGameId, ct);
            var entity = new SavedGame
            {
                UserId = userId,
                Source = SavedGameService.ImportSource,
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
                GameAnalysisId = startFen is null || IsStandardStart(startFen) ? await _link.ClubAnalysisForMovesAsync(sans, userId, ct) : null,
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

    /// <summary>Aktualisiert eine bereits gespeicherte Partie beim Re-Save, wenn die neue
    /// Version mehr Züge hat ODER erstmals ein Elo mitbringt. Gibt <c>true</c> zurück, wenn
    /// etwas geändert wurde. Kürzt NIE (weniger Züge → keine Änderung).</summary>
    internal static bool TryHeal(SavedGame existing, List<string> moves, SaveGameInputDto dto, string result)
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

    private Task<string> GenerateUniqueTokenAsync()
        => ShareTokens.NewUniqueAsync(t => _db.SavedGames.AnyAsync(g => g.ShareToken == t));
}
