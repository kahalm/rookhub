using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using static RookHub.Api.Services.Engines.MoveComparisonPrompts;
using static RookHub.Api.Services.Engines.MoveComparisonSteps;

namespace RookHub.Api.Services.Engines;

/// <summary>Begründungen eines Zugvergleichs schreiben lassen (aus <see cref="MoveComparisonService"/> herausgezogen,
/// 2026-11-09): je schwächerem Kandidaten zwei bis fünf Sätze, Vereinspartien ab dem Stichtag über Fable, sonst lokal.
/// Protokolliert unter der Kategorie des Dienstes.</summary>
/// <param name="fable">Begründungen der Vereinspartien ab dem Stichtag über Claude Fable (0.911.0); <c>null</c> = nur lokal.</param>
internal sealed class MoveComparisonExplainer(IClaudeJsonClient llm, ClubFableExplanations? fable, ILogger<MoveComparisonService> logger)
{
    /// <summary>Begründungen nur mit einem Modell auf eigener Hardware (über Claude kostete jeder Vergleich Geld).</summary>
    public bool LocalAvailable => llm.IsConfigured && llm.IsLocal;

    /// <summary>Je schwächerem Kandidaten eine Begründung schreiben lassen, dann fertig — auch wenn das Modell keine
    /// brauchbare liefert (sonst stünde der Vergleich für immer auf „wird erklärt").</summary>
    public async Task ExplainAsync(AppDbContext db, MoveComparison c, CancellationToken ct)
    {
        var id = c.Id;
        var system = SystemPrompt(c.Language);
        var pairs = WeakerCandidates(c).Select(w => (Line: w, Facts: PairFactsOf(c, w))).Where(p => p.Facts is not null).ToList();
        // Vereinspartie ab dem Stichtag: erst Fable (eigener Kontext — der Abfrage-Kontext läuft nicht parallel), Rückfall lokal.
        var useFable = fable != null && await fable.AppliesAsync(db, c, ct);
        var fableTexts = new string?[pairs.Count];
        if (useFable)
            for (var i = 0; i < pairs.Count; i++) fableTexts[i] = await fable!.ExplainAsync(db, pairs[i].Facts!, ct);
        using var gate = new SemaphoreSlim(MoveComparisonService.Parallel);
        var texts = await Task.WhenAll(pairs.Select(async (p, i) =>
        {
            if (fableTexts[i] is { } done) return done;
            // Neu angefordert (MoveComparisonService.ReexplainAsync) und Fable liefert nichts: die alte Begründung bleibt —
            // sie durch einen Text des lokalen Modells zu ersetzen, wäre ein Rückschritt.
            if (useFable && p.Line.Explanation != null) return null;
            if (!LocalAvailable) return null;
            await gate.WaitAsync(ct);
            try { return await ExplainPairAsync(p.Facts!, system, ct); }
            finally { gate.Release(); }
        }));
        // Eine vorhandene Begründung geht nur gegen eine neue weg (beim ersten Lauf steht ohnehin keine da).
        for (var i = 0; i < pairs.Count; i++) pairs[i].Line.Explanation = texts[i] ?? pairs[i].Line.Explanation;
        c.Status = MoveComparisonStatus.Done;
        if (c.Model is null || texts.Any(t => t != null))
        {
            var model = fableTexts.Any(t => t != null) ? fable!.Model : llm.TranslationModel;
            c.Model = model is { Length: > 80 } m ? m[..80] : model;
        }
        c.UpdatedAt = DateTime.UtcNow;
        c.FinishedAt = c.UpdatedAt;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Zugvergleich {Id}: {Saved} von {Total} Begruendungen", id, texts.Count(t => t != null), pairs.Count);
    }

    private async Task<string?> ExplainPairAsync(PairFacts facts, string system, CancellationToken ct)
    {
        var prompt = UserPrompt(facts);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var json = await llm.CompleteJsonAsync("move-comparison", system,
                attempt == 0 ? prompt : prompt + "\n\nIMPORTANT: your previous answer mentioned a move that is not in the lines above. Mention ONLY moves that appear in the lines above.",
                Schema, 1500, ct);
            var text = TextOf(json);
            if (text != null && GameMoveExplanationService.MentionsOnly(text, facts.AllowedMoves())) return text;
            if (text != null) logger.LogInformation("Zugvergleich: Begruendung verworfen (nennt einen fremden Zug): {Text}", text);
        }
        return null;
    }
}
