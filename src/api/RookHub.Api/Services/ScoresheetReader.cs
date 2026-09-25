using Chess;

namespace RookHub.Api.Services;

/// <summary>
/// Die Lese-Schleife einer Formular-Einlesung — Modell fragen, auflösen, bei einer Sackgasse nachfragen — OHNE
/// Datenbank. Benutzt vom <see cref="ScoresheetScanService"/> (mit Budget-Prüfung und Verbuchung als Rückrufe)
/// und vom Testwerkzeug <c>tools/ScoresheetBench</c>, das Belege mit bekannter Partie durchmisst, ohne eine
/// zweite API gegen eine echte Datenbank zu starten.
/// </summary>
public sealed class ScoresheetReader
{
    private readonly IScoresheetVisionClient _vision;

    public ScoresheetReader(IScoresheetVisionClient vision) => _vision = vision;

    /// <summary>Lese-Durchgänge insgesamt (1 + Nachfragen).</summary>
    public const int MaxRounds = ScoresheetScanService.MaxRounds;

    /// <summary>Ergebnis des Lesens: Transkription + Auflösung, oder ein Fehlergrund.</summary>
    public sealed record ReadOutcome(ScoresheetTranscription? Transcription, ScoresheetResolution? Resolution,
        string? Json, string? Language, int Rounds, string? Error);

    /// <summary>
    /// Lesen, auflösen, bei einer Sackgasse nachfragen. Behalten wird die BESTE Lesung aller Durchgänge (die
    /// am weitesten legal aufgeht, dann die mit den wenigsten unsicheren Stellen) — eine Nachfrage kann auch
    /// schlechter ausfallen.
    /// </summary>
    /// <param name="beforeCall">Vor JEDEM Modell-Aufruf: darf er starten, und wie lang darf die Antwort werden?
    /// Vor der ersten Lesung beendet ein Nein die Einlesung, vor einer Nachfrage nur die Nachfragen. Ohne Rückruf
    /// gilt <see cref="ScoresheetBudget.MaxOutputTokens"/>.</param>
    /// <param name="afterCall">Nach jedem Aufruf: verbrauchte Tokens (auch bei Fehlern) verbuchen.</param>
    public async Task<ReadOutcome> ReadAsync(byte[] jpeg, string language, CancellationToken ct,
        Func<CancellationToken, Task<CallAllowance>>? beforeCall = null, Func<int, int, CancellationToken, Task>? afterCall = null)
    {
        ReadOutcome? best = null;
        string? previousJson = null;
        ScoresheetTranscription? previous = null;
        ScoresheetResolution? previousResolution = null;
        for (var round = 1; round <= MaxRounds; round++)
        {
            var instructions = round == 1 || previous == null || previousResolution?.StuckAt is not int stuck
                ? ScoresheetPrompt.FirstRead(language)
                : ScoresheetPrompt.Repair(language, previousJson!, previousResolution.Plies.Select(p => p.San).ToList(),
                    stuck, previous.Moves[stuck].ToScanned(), previousResolution.StuckFen!,
                    LegalMoves(previousResolution.StuckFen!));

            var allowance = beforeCall != null
                ? await beforeCall(ct)
                : new CallAllowance(ScoresheetBudget.MaxOutputTokens, null);
            if (allowance.Blocked is { } blocked)
            {
                if (best != null) return best with { Rounds = round - 1 };
                return new ReadOutcome(null, null, null, null, 0, blocked);
            }
            var answer = await _vision.ReadAsync(jpeg, instructions, allowance.MaxTokens, ct);
            if (afterCall != null && (answer.InputTokens > 0 || answer.OutputTokens > 0))
                await afterCall(answer.InputTokens, answer.OutputTokens, ct);
            if (answer.Error != null || answer.Json == null)
            {
                // Eine gescheiterte NACHFRAGE verwirft die erste Lesung nicht.
                if (best != null) return best with { Rounds = round };
                return new ReadOutcome(null, null, null, null, round, answer.Error ?? "failed");
            }

            var transcription = ScoresheetTranscription.Parse(answer.Json);
            if (transcription == null)
            {
                if (best != null) return best with { Rounds = round };
                return new ReadOutcome(null, null, answer.Json, null, round, "failed");
            }
            if (transcription.Moves.Count == 0)
            {
                if (best != null) return best with { Rounds = round };
                return new ReadOutcome(transcription, null, answer.Json, null, round, "noMoves");
            }

            var effective = EffectiveLanguage(language, transcription.NotationLanguage);
            var resolution = ScoresheetResolver.Resolve(transcription.Scanned(),
                new ScoresheetResolver.Options(ScoresheetNotation.Find(effective)));
            var outcome = new ReadOutcome(transcription, resolution, answer.Json, effective, round, null);
            if (best == null || IsBetter(resolution, best.Resolution!)) best = outcome;
            if (resolution.StuckAt == null) return best with { Rounds = round };

            previousJson = answer.Json;
            previous = transcription;
            previousResolution = resolution;
        }
        return best!;
    }

    private static bool IsBetter(ScoresheetResolution a, ScoresheetResolution b)
    {
        if (a.Plies.Count != b.Plies.Count) return a.Plies.Count > b.Plies.Count;
        return a.Plies.Count(p => p.Uncertain) < b.Plies.Count(p => p.Uncertain);
    }

    /// <summary>Gewählte Sprache gewinnt; bei „auto" die vom Modell erkannte; sonst automatisch (alle).</summary>
    public static string EffectiveLanguage(string chosen, string? detected)
        => chosen != "auto" ? chosen : ScoresheetNotation.Find(detected) != null ? detected! : "auto";

    private static List<string> LegalMoves(string fen)
    {
        try { return ChessBoard.LoadFromFen(fen).Moves(generateSan: true).Select(m => m.San ?? GamePlies.ToUci(m)).ToList(); }
        catch { return new(); }
    }

}
