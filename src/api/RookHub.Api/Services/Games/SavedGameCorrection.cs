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
/// Korrigieren einer gespeicherten Partie (Zerlegung Z13 aus <see cref="SavedGameService"/>): Züge und Kopfdaten neu
/// schreiben, Deckel prüfen, und was an der alten Zugfolge hing (Analyse-Verweis, Fehler-Training, Nacherzählung) lösen.
/// </summary>
internal sealed class SavedGameCorrection
{
    private readonly AppDbContext _db;
    private readonly SavedGameService _owner;

    internal SavedGameCorrection(AppDbContext db, SavedGameService owner)
    {
        _db = db;
        _owner = owner;
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
            && (await _owner.UsageAsync(userId)).Chars - g.Pgn.Length + pgn.Length > _owner.PgnCharsPerUserCap)
            throw new SavedGameQuotaException($"Saved game storage limit reached (max {_owner.PgnCharsPerUserCap} characters per account).");
        // Ergebnis nachgetragen/geändert (Wunsch 2026-10-21): die Nacherzählung nennt es („…ended with an open result") —
        // sie wird gelöscht und beim nächsten Öffnen neu geschrieben (GET …/recap stößt sie bei fertiger Analyse an).
        var resultChanged = !string.Equals(g.Result ?? "*", result, StringComparison.Ordinal);
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
        else if (resultChanged)
            _db.GameRecaps.RemoveRange(await _db.GameRecaps.Where(r => r.SavedGameId == g.Id).ToListAsync());
        await _db.SaveChangesAsync();
        var dtoOut = SavedGameService.MapDetail(g);
        var profile = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        dtoOut.OwnerSide = SavedGameSharing.DetermineOwnerSide(g, profile);
        return dtoOut;
    }

    /// <summary>
    /// Die Züge einer Partie haben sich geändert (Korrigieren in <see cref="UpdateAsync"/> oder ein längerer Re-Save in
    /// <see cref="SaveAsync"/>): die verknüpfte Analyse (Kurve, Fehler, Genauigkeit), der Stand des Fehler-Trainings und
    /// die Nacherzählung (auch in der Link-Vorschau von <c>/g/{token}</c>) gehören zur alten Zugfolge. Der Verweis fällt,
    /// Training und Nacherzählung werden gelöscht — „Analysieren" rechnet danach die neue Fassung, und nach der Analyse
    /// entsteht die Nacherzählung neu. Speichert nicht selbst.
    /// </summary>
    internal Task OnMovesChangedAsync(SavedGame g) => MovesChangedAsync(_db, g, default);

    internal static async Task MovesChangedAsync(AppDbContext db, SavedGame g, CancellationToken ct)
    {
        g.GameAnalysisId = null;
        db.GameMistakeProgresses.RemoveRange(await db.GameMistakeProgresses.Where(p => p.SavedGameId == g.Id).ToListAsync(ct));
        db.GameRecaps.RemoveRange(await db.GameRecaps.Where(r => r.SavedGameId == g.Id).ToListAsync(ct));
    }
}
