using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Spaced-Repetition-Scheduling (SM-2-Variante) für den Repertoire-Trainer. Persistiert nur den
/// Karten-Zustand je (User, Repertoire, Stellung); die Zug-/Baumlogik liegt im Frontend. Karten
/// werden beim ersten Review on-demand angelegt. „Fällige"/„neue" Karten ermittelt das Frontend
/// aus dem Repertoire-Baum + den hier gelieferten Zuständen.
/// </summary>
public class RepertoireTrainingService
{
    private readonly AppDbContext _db;
    public RepertoireTrainingService(AppDbContext db) => _db = db;

    private const double MinEase = 1.3;
    private const double MaxEase = 3.0;

    /// <summary>Alle Kartenzustände des Users für ein eigenes Repertoire; null wenn Repertoire
    /// nicht existiert / nicht dem User gehört.</summary>
    public async Task<List<RepertoireCardStateDto>?> GetCardsAsync(int userId, int repertoireId, CancellationToken ct = default)
    {
        if (!await OwnsRepertoireAsync(userId, repertoireId, ct)) return null;
        return await _db.RepertoireCardStates
            .Where(c => c.UserId == userId && c.RepertoireId == repertoireId)
            .Select(c => new RepertoireCardStateDto(
                c.CardKey, c.ExpectedMove, c.Reps, c.Lapses, c.IntervalDays, c.Ease, c.DueAt, c.LastReviewedAt))
            .ToListAsync(ct);
    }

    /// <summary>Wendet eine Bewertung auf eine Karte an (legt sie bei Bedarf an) und plant sie neu.
    /// Null wenn das Repertoire nicht dem User gehört.</summary>
    public async Task<RepertoireCardStateDto?> ReviewAsync(int userId, int repertoireId, ReviewCardRequest req, CancellationToken ct = default)
    {
        if (!await OwnsRepertoireAsync(userId, repertoireId, ct)) return null;

        var card = await _db.RepertoireCardStates
            .FirstOrDefaultAsync(c => c.UserId == userId && c.RepertoireId == repertoireId && c.CardKey == req.CardKey, ct);
        if (card == null)
        {
            card = new RepertoireCardState
            {
                UserId = userId,
                RepertoireId = repertoireId,
                CardKey = req.CardKey,
            };
            _db.RepertoireCardStates.Add(card);
        }

        Schedule(card, req.Grade, DateTime.UtcNow);
        card.ExpectedMove = req.ExpectedMove;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Race: zwei schnelle Reviews derselben Karte (Auto-Advance des Trainers feuert rasch) →
            // der Unique-Index (UserId, RepertoireId, CardKey) hat den parallelen Insert abgefangen.
            // Idempotent: getrackten Neu-Insert verwerfen, vorhandene Karte laden, Planung erneut anwenden.
            _db.ChangeTracker.Clear();
            card = await _db.RepertoireCardStates
                .FirstOrDefaultAsync(c => c.UserId == userId && c.RepertoireId == repertoireId && c.CardKey == req.CardKey, ct);
            if (card == null) throw;
            Schedule(card, req.Grade, DateTime.UtcNow);
            card.ExpectedMove = req.ExpectedMove;
            await _db.SaveChangesAsync(ct);
        }

        return new RepertoireCardStateDto(
            card.CardKey, card.ExpectedMove, card.Reps, card.Lapses, card.IntervalDays, card.Ease, card.DueAt, card.LastReviewedAt);
    }

    /// <summary>SM-2-Variante. Grade 0 again · 1 hard · 2 good · 3 easy.</summary>
    internal static void Schedule(RepertoireCardState card, int grade, DateTime now)
    {
        switch (grade)
        {
            case 0: // again — falsch / relearn in Kürze
                card.Reps = 0;
                card.Lapses++;
                card.Ease = Math.Max(MinEase, card.Ease - 0.2);
                card.IntervalDays = 0;
                card.DueAt = now.AddMinutes(10);
                break;

            case 1: // hard — geduldeter Alternativzug oder mühsam: kleiner Schritt, Ease runter
                card.Ease = Math.Max(MinEase, card.Ease - 0.15);
                card.IntervalDays = card.Reps == 0 ? 0.5 : Math.Max(1, card.IntervalDays * 1.2);
                card.Reps = Math.Max(1, card.Reps);
                card.DueAt = now.AddDays(card.IntervalDays);
                break;

            case 3: // easy
                card.Reps++;
                card.Ease = Math.Min(MaxEase, card.Ease + 0.15);
                card.IntervalDays = card.Reps == 1 ? 4 : Math.Round(Math.Max(card.IntervalDays, 1) * card.Ease * 1.3);
                card.DueAt = now.AddDays(card.IntervalDays);
                break;

            default: // 2 good
                card.Reps++;
                card.IntervalDays = card.Reps == 1 ? 1
                    : card.Reps == 2 ? 6
                    : Math.Round(Math.Max(card.IntervalDays, 1) * card.Ease);
                card.DueAt = now.AddDays(card.IntervalDays);
                break;
        }
        card.LastReviewedAt = now;
    }

    /// <summary>Löscht sämtliche SM-2-Kartenzustände des Users für dieses Repertoire. Gibt die Anzahl
    /// gelöschter Karten zurück, oder null wenn das Repertoire nicht dem User gehört.</summary>
    public async Task<int?> ResetAsync(int userId, int repertoireId, CancellationToken ct = default)
    {
        if (!await OwnsRepertoireAsync(userId, repertoireId, ct)) return null;
        var cards = await _db.RepertoireCardStates
            .Where(c => c.UserId == userId && c.RepertoireId == repertoireId)
            .ToListAsync(ct);
        if (cards.Count == 0) return 0;
        _db.RepertoireCardStates.RemoveRange(cards);
        await _db.SaveChangesAsync(ct);
        return cards.Count;
    }

    private Task<bool> OwnsRepertoireAsync(int userId, int repertoireId, CancellationToken ct) =>
        _db.Repertoires.AnyAsync(r => r.Id == repertoireId && r.UserId == userId, ct);
}
