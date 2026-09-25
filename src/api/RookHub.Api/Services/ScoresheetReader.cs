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

    /// <summary>
    /// Antwort-Deckel eines Aufrufs MIT Nachdenken. Am Testsatz brauchte die längste Lesung (92 Halbzüge) 23 142
    /// Tokens; eine Lesung, die 40 000 übersteigt, hat sich festgedacht (Prod 25.09.: 64 000 Tokens Nachdenken, kein
    /// Zeichen Antwort). Der Deckel begrenzt den Schaden auf rund 1 $ und lässt im Tagesbudget eines Nutzers
    /// (2 $) Platz für den Rückfall ohne Nachdenken.
    /// </summary>
    public const int FullCallMaxTokens = 40_000;

    /// <summary>Antwort-Deckel ohne Nachdenken: nur das JSON — bei 120 Halbzügen grob 10 000 Tokens.</summary>
    public const int TranscribeCallMaxTokens = 32_000;

    /// <summary>Deckel des gerade laufenden Aufrufs (<c>null</c> zwischen den Aufrufen) — damit ein von außen
    /// abgebrochener Aufruf mit seinem ungünstigsten Fall verbucht werden kann.</summary>
    public int? InFlightMaxTokens { get; private set; }

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
    /// <param name="maxRounds">Durchgänge höchstens — 1 für einen Leser, der den Auftrag nicht liest (dots.ocr:
    /// eine Nachfrage ergäbe dieselbe Lesung noch einmal).</param>
    /// <param name="startMode">Womit der erste Durchgang liest — <see cref="ScoresheetReadMode.Transcribe"/> für ein
    /// Modell, das OHNE Nachdenken lesen soll (Einstellung <c>Scoresheet:Thinking=false</c>, z. B. Haiku).</param>
    /// <remarks>
    /// Wird ein Aufruf MIT Nachdenken am Deckel abgeschnitten, ist der nächste Durchgang derselbe Auftrag OHNE
    /// Nachdenken (<see cref="ScoresheetReadMode.Transcribe"/>), und dabei bleibt es auch für die Nachfragen — ein
    /// weiterer Aufruf mit Nachdenken würde sich an derselben Partie wieder festdenken.
    /// </remarks>
    public async Task<ReadOutcome> ReadAsync(byte[] jpeg, string language, CancellationToken ct,
        Func<CancellationToken, Task<CallAllowance>>? beforeCall = null, Func<int, int, CancellationToken, Task>? afterCall = null,
        int maxRounds = MaxRounds, ScoresheetReadMode startMode = ScoresheetReadMode.Full)
    {
        ReadOutcome? best = null;
        var mode = startMode;
        var rounds = Math.Clamp(maxRounds, 1, MaxRounds);
        string? previousJson = null;
        ScoresheetTranscription? previous = null;
        ScoresheetResolution? previousResolution = null;
        for (var round = 1; round <= rounds; round++)
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
            var maxTokens = Math.Min(allowance.MaxTokens,
                mode == ScoresheetReadMode.Full ? FullCallMaxTokens : TranscribeCallMaxTokens);
            InFlightMaxTokens = maxTokens;
            var answer = await _vision.ReadAsync(jpeg, instructions, maxTokens, ct, mode);
            InFlightMaxTokens = null;
            if (afterCall != null && (answer.InputTokens > 0 || answer.OutputTokens > 0))
                await afterCall(answer.InputTokens, answer.OutputTokens, ct);
            if (answer.Error == "truncated" && mode == ScoresheetReadMode.Full && round < rounds)
            {
                // Festgedacht: derselbe Auftrag noch einmal, ohne Nachdenken (siehe remarks).
                mode = ScoresheetReadMode.Transcribe;
                continue;
            }
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
        return best ?? new ReadOutcome(null, null, null, null, rounds, "failed");
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
