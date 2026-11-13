using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Puzzles;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// Buchbeispiele der Empfohlenen Übungen (0.962.0, Wunsch 2026-11-13: „wenn du in büchern das motiv findest dann sind beispiele aus
/// dem kapitel &gt; allgemeine probleme“, „erzwinge keine motive/aufgaben“). Dieselben Signaturen wie bei den Lichess-Aufgaben
/// (<see cref="MatePattern"/>, <see cref="TacticMotifs"/>), gerechnet aus Diagrammstellung + Lösung (<see cref="SignaturesOf"/>,
/// Tabelle <see cref="EbookDiagramSignature"/>, Lauf <see cref="RunBatchAsync"/> im <see cref="PuzzleSignatureWorker"/>).
///
/// <para><b>Lösbar</b> nur mit Buchlösung, die Stockfish bestätigt hat (<see cref="IsSolvable"/>); eine reine Engine-Lösung gibt
/// höchstens ein „Beispiel im Buch“ (Brett + Sprung in den Leser, höchstens <see cref="MaxExamples"/> je Stelle). Diagramme ohne
/// Lösung haben keine Linie, also kein Bild/Motiv, und kommen nicht vor.</para>
///
/// <para><b>Rechte</b>: nur Bücher, die der Betrachter in der Bibliothek sehen darf (<see cref="EbookAccess.Visible"/>) — schon in
/// der Abfrage gefiltert, Titel und Stellung anderer Bücher werden gar nicht gelesen.</para>
///
/// <para><b>Qualität</b>: lesbare Diagramme (<see cref="EbookDiagramVisibility"/>), Stellung glaubwürdig (<see cref="Trusted"/>),
/// regelgerechte Stellung, Lösung nachspielbar; ein gemeldetes Diagramm (<see cref="EbookDiagramReport"/>) zählt erst wieder nach
/// einer späteren Korrektur (<see cref="EbookDiagramCorrection"/>); geänderte Stellung/Lösung seit der Berechnung (<see cref="EbookDiagramSignature.LineHash"/>)
/// → übersprungen, bis der nächste Lauf neu rechnet.</para>
/// </summary>
public sealed class BookExercises(AppDbContext db)
{
    public const string CursorKey = "PuzzleSignatures.BookCursor";
    /// <summary>Wann der laufende Durchgang über alle Diagramme begann (ISO-UTC) — nach <see cref="RepassAfter"/> beginnt ein neuer,
    /// der korrigierte Stellungen und neu geprüfte Lösungen nachzieht.</summary>
    public const string PassStartedKey = "PuzzleSignatures.BookPassStartedAt";
    public static readonly TimeSpan RepassAfter = TimeSpan.FromDays(7);
    public const int MaxExamples = 2;
    /// <summary>Höchstens so viele Beispiele aus einem Buch je Stelle (dann aus demselben Kapitel bzw. ±<see cref="SamePages"/> Seiten).</summary>
    public const int PerBook = 2;
    public const int SamePages = 10;
    public const float MinConfidence = 0.9f;
    private const int Pool = 120;

    public sealed record Row(PuzzleSignatureKind Kind, short Version, long Strict, long Family, byte MateIn);

    /// <summary>Regelstand beider Erkenner — ändert sich einer, rechnet der Lauf alles neu.</summary>
    public static string VersionTag => $"{MatePattern.Version}.{TacticMotifs.Version}";

    /// <summary>Buchlösung, von Stockfish bestätigt (erster Zug bester/gleichwertig bzw. ersetzt durch eine vertretbare Buchvariante).</summary>
    public static bool IsSolvable(EbookSolutionCheck check, string? source) =>
        source == "book" && check is EbookSolutionCheck.Confirmed or EbookSolutionCheck.Replaced;

    /// <summary>Linie taugt für eine Signatur: bestätigte Buchlösung oder Engine-Lösung (nicht „fraglich“, nicht ungeprüft).</summary>
    public static bool IsUsable(EbookSolutionCheck check, string? source) =>
        IsSolvable(check, source) || check == EbookSolutionCheck.Engine || (check == EbookSolutionCheck.Confirmed && source == "engine");

    /// <summary>Stellung glaubwürdig erkannt? Eine von Stockfish bestätigte Buchlösung belegt die Stellung selbst (eine falsch gelesene
    /// Stellung passt kaum zur Lösung im Buch); eine reine Engine-Lösung nur bei sicherer Bilderkennung (≥ <see cref="MinConfidence"/>
    /// bzw. Font-Diagramm ohne Sicherheitswert).</summary>
    public static bool Trusted(EbookSolutionCheck check, string? source, float? confidence) =>
        IsSolvable(check, source) || confidence is null or >= MinConfidence;

    public static long LineHash(string fen, string? solutionUci) => MatePattern.Hash($"{fen}|{solutionUci}");
    public static int ShuffleOf(int diagramId) => (int)(MatePattern.Hash($"d{diagramId}") & 0x7fffffff);

    /// <summary>
    /// Signaturen eines Diagramms (rein): endet die Lösung (Löser zuerst, ab <paramref name="fen"/>) matt → Mattbild; sonst — nur wenn
    /// die Stellung klar gewinnt (<paramref name="engineEval"/> ≥ <see cref="GameExercises.MinTacticCp"/>, wie bei den Taktikstellen der
    /// Partie) — die Motive des ersten Zugs. Unspielbare Stellung/Lösung → leer.
    /// </summary>
    public static List<Row> SignaturesOf(string fen, string? solutionUci, int? engineEval)
    {
        var moves = (solutionUci ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (moves.Length == 0 || MatePattern.Play(fen, moves) is not { } end) return [];
        if (MatePattern.IsMate(end))
            return MatePattern.Of(end) is { } sig
                ? [new Row(PuzzleSignatureKind.Mate, MatePattern.Version, sig.StrictHash, sig.FamilyHash, (byte)Math.Min(255, (moves.Length + 1) / 2))]
                : [];
        if (engineEval is not >= GameExercises.MinTacticCp) return [];
        return TacticMotifs.Detect(fen, moves)
            .Select(m => new Row(TacticMotifs.KindOf(m.Theme), TacticMotifs.Version, m.StrictHash, m.FamilyHash, 0)).ToList();
    }

    /// <summary>Stand des Laufs: letzte bearbeitete Diagramm-Id im aktuellen Regelstand (0 = noch nichts).</summary>
    public static async Task<int> CursorAsync(AppDbContext db, CancellationToken ct = default)
    {
        var value = await db.SystemSettings.AsNoTracking().Where(s => s.Key == CursorKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        var prefix = $"v{VersionTag}:";
        return value is not null && value.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(value[prefix.Length..], out var id) ? id : 0;
    }

    /// <summary>
    /// Eine Portion: bis zu <paramref name="max"/> Diagramme mit Lösung nach dem Fortschritt (Id-Reihenfolge) → ihre Signaturen; die alten
    /// Zeilen desselben Id-Bereichs gehen weg (auch die von inzwischen ungeeigneten Diagrammen). Rückgabe = gelesene Diagramme;
    /// 0 = Bestand durch — ist der Durchgang älter als <see cref="RepassAfter"/>, beginnt der nächste Aufruf von vorn.
    /// <paramref name="chapters"/> behält die Inhaltsverzeichnisse der zuletzt gesehenen Bücher (der Dienst reicht ihn durch).
    /// </summary>
    public static async Task<int> RunBatchAsync(AppDbContext db, EbookStorage? storage, int max, CancellationToken ct = default,
        Dictionary<int, EbookChapterLookup>? chapters = null, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        var cursor = await CursorAsync(db, ct);
        var batch = await db.EbookDiagrams.AsNoTracking().Where(d => d.Id > cursor && d.SolutionUci != null)
            .OrderBy(d => d.Id).Take(max)
            .Select(d => new EbookDiagram
            {
                Id = d.Id, EbookId = d.EbookId, Page = d.Page, W = d.W, EpubChapter = d.EpubChapter, Fen = d.Fen, SolutionUci = d.SolutionUci,
                SolutionSource = d.SolutionSource, SolutionCheck = d.SolutionCheck, EngineEval = d.EngineEval, FontFamily = d.FontFamily,
                Confidence = d.Confidence,
            }).ToListAsync(ct);
        if (batch.Count == 0)
        {
            var started = await db.SystemSettings.AsNoTracking().Where(s => s.Key == PassStartedKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
            var old = !DateTime.TryParse(started, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var s0) || at - s0 > RepassAfter;
            if (cursor > 0 && old)
            {
                await SaveAsync(db, CursorKey, $"v{VersionTag}:0", at, ct);
                await SaveAsync(db, PassStartedKey, at.ToString("O"), at, ct);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
            return 0;
        }
        chapters ??= [];
        var rows = new List<EbookDiagramSignature>();
        foreach (var d in batch)
        {
            if (!EbookDiagramVisibility.IsReadable(d) || !IsUsable(d.SolutionCheck, d.SolutionSource) || !Trusted(d.SolutionCheck, d.SolutionSource, d.Confidence)) continue;
            var sigs = SignaturesOf(d.Fen, d.SolutionUci, d.EngineEval);
            if (sigs.Count == 0 || !EbookSolutionChecker.IsLegal(d.Fen)) continue;
            if (!chapters.TryGetValue(d.EbookId, out var lookup))
            {
                if (chapters.Count >= 16) chapters.Clear();
                chapters[d.EbookId] = lookup = await EbookChapterLookup.ForBookAsync(db, storage, d.EbookId, ct);
            }
            var chapter = lookup.Of(d);
            if (chapter is { Length: > 200 }) chapter = chapter[..200];
            foreach (var r in sigs)
                rows.Add(new EbookDiagramSignature
                {
                    DiagramId = d.Id, Kind = r.Kind, Version = r.Version, Strict = r.Strict, Family = r.Family, EbookId = d.EbookId,
                    Solvable = IsSolvable(d.SolutionCheck, d.SolutionSource), LineHash = LineHash(d.Fen, d.SolutionUci),
                    Shuffle = ShuffleOf(d.Id), MateIn = r.MateIn, Chapter = chapter,
                });
        }
        var last = batch[^1].Id;
        // Alles in EINEM SaveChanges: Zeilen und Fortschritt kommen zusammen oder gar nicht.
        db.EbookDiagramSignatures.RemoveRange(await db.EbookDiagramSignatures.Where(s => s.DiagramId > cursor && s.DiagramId <= last).ToListAsync(ct));
        db.EbookDiagramSignatures.AddRange(rows);
        if (cursor == 0) await SaveAsync(db, PassStartedKey, at.ToString("O"), at, ct);
        await SaveAsync(db, CursorKey, $"v{VersionTag}:{last}", at, ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return batch.Count;
    }

    private static async Task SaveAsync(AppDbContext db, string key, string value, DateTime at, CancellationToken ct)
    {
        var row = db.SystemSettings.Local.FirstOrDefault(s => s.Key == key) ?? await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) db.SystemSettings.Add(row = new SystemSetting { Key = key });
        row.Value = value;
        row.UpdatedAt = at;
    }

    /// <summary>Kandidat aus der Signaturtabelle, geprüft gegen das aktuelle Diagramm.</summary>
    internal sealed record Candidate(int DiagramId, int EbookId, int Shuffle, string? Chapter, int Page, bool PdfPage, string? Label, string Fen,
        string SolutionUci, bool Solvable, string Title, string? Author, int? TargetMinElo);

    private sealed class SigRow
    {
        public int DiagramId { get; init; }
        public int EbookId { get; init; }
        public int Shuffle { get; init; }
        public long LineHash { get; init; }
        public string? Chapter { get; init; }
    }

    /// <summary>
    /// Buchbeispiele einer Stelle: erst gleiche strenge Signatur, dann gleiche Familie; nur Bücher des Betrachters. Höchstens
    /// <see cref="GameExercises.PerGroup"/>, lösbare vor Beispielen, davon höchstens <see cref="MaxExamples"/>. Fest gesät
    /// (<paramref name="seed"/>): derselbe Aufruf liefert dieselben Diagramme. <paramref name="used"/> = schon gezeigte Diagramme der Partie.
    /// </summary>
    public async Task<List<GameExerciseDto>> PickAsync(GameExercises.Occurrence o, int target, EbookViewer viewer, HashSet<int> used, int seed,
        CancellationToken ct = default)
    {
        var tiers = new List<(PuzzleSignatureKind Kind, long Hash, bool Strict)>();
        if (o.Signature is { } sig)
        {
            tiers.Add((PuzzleSignatureKind.Mate, sig.StrictHash, true));
            tiers.Add((PuzzleSignatureKind.Mate, sig.FamilyHash, false));
        }
        if (o.Motif is { } motif)
        {
            var kind = TacticMotifs.KindOf(motif.Theme);
            tiers.Add((kind, motif.StrictHash, true));
            tiers.Add((kind, motif.FamilyHash, false));
        }
        var result = new List<GameExerciseDto>();
        if (tiers.Count == 0) return result;
        var visible = EbookAccess.Visible(db, viewer.Caller, viewer.ManageAll).Select(b => b.Id);
        var examples = 0;
        foreach (var (kind, hash, strict) in tiers)
        {
            var need = GameExercises.PerGroup - result.Count;
            if (need <= 0) break;
            var source = (strict
                    ? db.EbookDiagramSignatures.AsNoTracking().Where(x => x.Kind == kind && x.Strict == hash)
                    : db.EbookDiagramSignatures.AsNoTracking().Where(x => x.Kind == kind && x.Family == hash))
                .Where(x => visible.Contains(x.EbookId));
            var rows = await source.Where(x => x.Shuffle >= seed).OrderBy(x => x.Shuffle).ThenBy(x => x.DiagramId).Take(Pool)
                .Select(x => new SigRow { DiagramId = x.DiagramId, EbookId = x.EbookId, Shuffle = x.Shuffle, LineHash = x.LineHash, Chapter = x.Chapter })
                .ToListAsync(ct);
            if (rows.Count < Pool)
                rows.AddRange(await source.Where(x => x.Shuffle < seed).OrderBy(x => x.Shuffle).ThenBy(x => x.DiagramId).Take(Pool - rows.Count)
                    .Select(x => new SigRow { DiagramId = x.DiagramId, EbookId = x.EbookId, Shuffle = x.Shuffle, LineHash = x.LineHash, Chapter = x.Chapter })
                    .ToListAsync(ct));
            rows = rows.Where(r => !used.Contains(r.DiagramId)).GroupBy(r => r.DiagramId).Select(g => g.First()).ToList();
            if (rows.Count == 0) continue;
            var candidates = await ValidateAsync(rows, ct);
            foreach (var c in Choose(candidates, target, need, MaxExamples - examples, seed))
            {
                used.Add(c.DiagramId);
                if (!c.Solvable) examples++;
                result.Add(new GameExerciseDto(c.DiagramId, c.Fen,
                    c.Solvable ? c.SolutionUci.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() : [], 0, [],
                    strict ? "strict" : "family", "book",
                    new GameExerciseBookDto(c.EbookId, c.Title, string.IsNullOrWhiteSpace(c.Author) ? null : c.Author, c.Chapter, c.Page, c.PdfPage,
                        string.IsNullOrWhiteSpace(c.Label) ? null : c.Label, c.Solvable)));
            }
        }
        // Lösbare vor Beispielen (die Reihenfolge innerhalb bleibt: streng vor Familie).
        return result.OrderBy(e => e.Book!.Solvable ? 0 : 1).ToList();
    }

    /// <summary>Gegen das aktuelle Diagramm prüfen: noch da, unverändert, lesbar, Lösung brauchbar, nicht offen gemeldet.</summary>
    private async Task<List<Candidate>> ValidateAsync(List<SigRow> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.DiagramId).ToList();
        var diagrams = await db.EbookDiagrams.AsNoTracking().Where(d => ids.Contains(d.Id))
            .Select(d => new { d.Id, d.EbookId, d.Fen, d.SolutionUci, d.SolutionCheck, d.SolutionSource, d.Page, d.W, d.Label, d.FontFamily, d.Confidence })
            .ToDictionaryAsync(d => d.Id, ct);
        var reports = (await db.EbookDiagramReports.AsNoTracking().Where(r => ids.Contains(r.DiagramId)).Select(r => new { r.DiagramId, r.CreatedAt }).ToListAsync(ct))
            .GroupBy(r => r.DiagramId).ToDictionary(g => g.Key, g => g.Max(r => r.CreatedAt));
        var fixedAt = reports.Count == 0 ? new Dictionary<int, DateTime>()
            : (await db.EbookDiagramCorrections.AsNoTracking().Where(c => ids.Contains(c.DiagramId)).Select(c => new { c.DiagramId, c.CreatedAt }).ToListAsync(ct))
                .GroupBy(c => c.DiagramId).ToDictionary(g => g.Key, g => g.Max(c => c.CreatedAt));
        var bookIds = rows.Select(r => r.EbookId).Distinct().ToList();
        var books = await db.Ebooks.AsNoTracking().Where(b => bookIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Title, b.Author, b.TargetMinElo }).ToDictionaryAsync(b => b.Id, ct);
        var list = new List<Candidate>();
        foreach (var r in rows)
        {
            if (!diagrams.TryGetValue(r.DiagramId, out var d) || d.EbookId != r.EbookId || !books.TryGetValue(d.EbookId, out var b)) continue;
            if (LineHash(d.Fen, d.SolutionUci) != r.LineHash || d.FontFamily == EbookDiagramVisibility.Unread) continue;
            if (!IsUsable(d.SolutionCheck, d.SolutionSource) || !Trusted(d.SolutionCheck, d.SolutionSource, d.Confidence) || string.IsNullOrEmpty(d.SolutionUci)) continue;
            if (reports.TryGetValue(d.Id, out var reported) && !(fixedAt.TryGetValue(d.Id, out var fixedOn) && fixedOn > reported)) continue;
            if (!EbookSolutionChecker.IsLegal(d.Fen)) continue;
            list.Add(new Candidate(d.Id, d.EbookId, r.Shuffle, r.Chapter, d.Page, d.W > 0, d.Label, d.Fen, d.SolutionUci!,
                IsSolvable(d.SolutionCheck, d.SolutionSource), b.Title, b.Author, b.TargetMinElo));
        }
        return list;
    }

    /// <summary>Passt das Buch zur Spielstärke? 0 = Zielgruppe ab höchstens 150 über bis 700 unter der Stärke, 1 = unbekannt, 2 = sonst.</summary>
    internal static int Fit(int? targetMinElo, int strength) =>
        targetMinElo is not int t ? 1 : t <= strength + 150 && t >= strength - 700 ? 0 : 2;

    /// <summary>
    /// Auswahl (rein): Bücher mit lösbarem Diagramm zuerst, dann nach Passung zur Stärke, dann fest gesät gemischt; je Buch zuerst sein bestes Diagramm (lösbar vor Beispiel),
    /// in einer zweiten Runde höchstens ein weiteres aus demselben Kapitel (ohne Kapitel: ±<see cref="SamePages"/> Seiten) — so kommen
    /// möglichst verschiedene Bücher, und was aus einem Buch zusammenbleibt, stammt aus derselben Stelle.
    /// </summary>
    internal static List<Candidate> Choose(List<Candidate> candidates, int strength, int need, int examplesLeft, int seed)
    {
        var picked = new List<Candidate>();
        if (need <= 0) return picked;
        var books = candidates.GroupBy(c => c.EbookId)
            .Select(g => (Fit: Fit(g.First().TargetMinElo, strength), Tie: MatePattern.Hash($"{seed}:{g.Key}"),
                Items: g.OrderBy(c => c.Solvable ? 0 : 1).ThenBy(c => c.Shuffle).ThenBy(c => c.DiagramId).ToList()))
            .OrderBy(b => b.Items[0].Solvable ? 0 : 1).ThenBy(b => b.Fit).ThenBy(b => b.Tie).ToList();
        bool Allowed(Candidate c) => c.Solvable || examplesLeft > 0;
        void Take(Candidate c)
        {
            picked.Add(c);
            if (!c.Solvable) examplesLeft--;
        }
        var first = new Dictionary<int, Candidate>();
        foreach (var b in books)
        {
            if (picked.Count >= need) break;
            if (b.Items.FirstOrDefault(Allowed) is not { } c) continue;
            Take(c);
            first[c.EbookId] = c;
        }
        for (var round = 1; round < PerBook; round++)
            foreach (var b in books)
            {
                if (picked.Count >= need) break;
                if (!first.TryGetValue(b.Items[0].EbookId, out var head)) continue;
                var near = b.Items.FirstOrDefault(c => !picked.Contains(c) && Allowed(c) && (head.Chapter != null
                    ? c.Chapter == head.Chapter
                    : c.Chapter == null && c.PdfPage == head.PdfPage && Math.Abs(c.Page - head.Page) <= SamePages));
                if (near is not null) Take(near);
            }
        return picked;
    }
}
