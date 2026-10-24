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
/// Gespeicherte Partie ↔ Analyse (Zerlegung Z13 aus <see cref="SavedGameService"/>): „Partie analysieren" (einmal
/// rechnen, wiederverwenden, mit Vereinspartien teilen), Bewertungskurve und die Verbindung zu Vereinspartien.
/// Die Aufrufe von <see cref="GameAnalysisService"/> sind unverändert.
/// </summary>
internal sealed class SavedGameAnalysisLink
{
    private readonly AppDbContext _db;
    private readonly GameAnalysisService _analyses;
    private readonly RepertoireAnalyzeService _repertoires;

    internal SavedGameAnalysisLink(AppDbContext db, GameAnalysisService analyses, RepertoireAnalyzeService repertoires)
    {
        _db = db;
        _analyses = analyses;
        _repertoires = repertoires;
    }

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
            await SavedGameCorrection.MovesChangedAsync(db, g, ct);
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
}
