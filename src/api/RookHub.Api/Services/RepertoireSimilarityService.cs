using System.Text.RegularExpressions;
using RookHub.Api.DTOs;
using static RookHub.Api.Services.RepertoireLineSource;

namespace RookHub.Api.Services;

/// <summary>
/// „Ähnliche Stellungen in meinen Repertoires" — das Gegenstück zur exakten Stellungssuche
/// (<see cref="RepertoirePositionLookupService"/>): dort „kommt GENAU diese Stellung vor?", hier
/// „wo habe ich schon mal so etwas gespielt?".
///
/// Die Linien kommen aus derselben Quelle (<see cref="RepertoireLineSource"/>) wie die exakte Suche
/// — ein Parser, ein Cache, dieselbe gameIndex-Zählung, derselbe <c>[FEN]</c>-Header-Respekt
/// (Chessable-Linien beginnen mitten in der Partie).
///
/// Die eigentliche Metrik liegt in <see cref="PositionSimilarity"/> (rein, testbar). Hier steckt nur
/// der Durchlauf: Materialschranke → Vergleich (normal + farbvertauscht) → optionaler Zug-Treffer →
/// je Linie der beste Treffer → sortieren → deckeln.
///
/// <b>Materialschranke und Farbtausch.</b> Die Schranke wird JE VERGLEICH geprüft, also einmal gegen
/// die Anfrage und einmal gegen die GESPIEGELTE Anfrage. Sie ist farbweise definiert; prüfte man den
/// gespiegelten Vergleich gegen die ungespiegelten Merkmale, fiele jede Stellung mit
/// Materialungleichgewicht aus der Spiegelsuche — auch ihr eigenes, perfektes Spiegelbild.
///
/// <b>Zug-Treffer.</b> Die Anfrage darf einen erwogenen Zug mitbringen („ich denke hier über 12.Sd5
/// nach"). Ein Treffer heißt: an dieser Stelle geht das Repertoire tatsächlich mit diesem Zug weiter
/// (Hauptzug ODER eine dort abzweigende Variante) — Legalität allein genügt nicht. Verglichen wird
/// from→to (+ Umwandlungsfigur), NICHT die SAN-Zeichenkette (<c>Nbd2</c> = <c>Nd2</c>). Verrechnet
/// wird als Lücken-Schluss <c>score' = score + bonus · (100 − score)</c> (0,5 exakt / 0,25 gleiche
/// Figurenart aufs gleiche Zielfeld) — das ordnet richtig, läuft nie über 100, und Stellungswert wie
/// Endwert bleiben beide sichtbar.
/// </summary>
public class RepertoireSimilarityService
{
    private readonly RepertoireLineSource _lines;

    public RepertoireSimilarityService(RepertoireLineSource lines) => _lines = lines;

    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;

    /// <summary>
    /// Voreingestellter Mindest-Score der Voreinstellung „struktur".
    /// <para>Siehe <see cref="DefaultMinScoreFor"/> für die Messtabelle und die Herleitung.</para>
    /// </summary>
    public const int DefaultMinScoreStruktur = 67;

    /// <summary>Voreingestellter Mindest-Score der Voreinstellung „ausgewogen" (siehe
    /// <see cref="DefaultMinScoreFor"/>).</summary>
    public const int DefaultMinScoreAusgewogen = 75;

    /// <summary>Voreingestellter Mindest-Score der Voreinstellung „stellungsbild" (siehe
    /// <see cref="DefaultMinScoreFor"/>).</summary>
    public const int DefaultMinScoreStellungsbild = 79;

    /// <summary>
    /// Der voreingestellte Mindest-Score JE VOREINSTELLUNG. Eine globale Schwelle kann es nicht
    /// geben: die drei Gewichtungen erzeugen VERSCHIEDENE Wertebereiche. „struktur" spreizt nach
    /// unten (die Bauernkomponente wird selten voll erfüllt), „stellungsbild" staucht nach oben
    /// (Figuren stehen in verwandten Systemen fast immer nah beieinander). Die frühere globale 70
    /// lag in „stellungsbild" MITTEN im unverwandten Feld.
    ///
    /// <para>Gemessen am 2026-08-08 mit der Metrik dieses Stands (Referenzpaare aus
    /// <c>PositionSimilarityTests</c>; „—" = Materialschranke greift, gar kein Vergleich):</para>
    /// <code>
    /// Paar                                    struktur  ausgewogen  stellungsbild
    /// U Karlsbad(DG) ~ Mar del Plata              52,6        62,4           67,8
    /// U Grünfeld ~ Stonewall                       —           —              —
    /// U Grünfeld ~ KIA                             —           —              —
    /// U Mar del Plata ~ Grünfeld                   —           —              —
    /// U Najdorf ~ Karlsbad(DG)                    55,8        64,3           67,9
    /// U Italienisch ~ Stonewall                   44,1        60,2           69,9
    /// U Französisch ~ KIA                         52,5        59,6           60,2
    /// U Französisch ~ Karlsbad(DG)                57,7        65,3           64,8
    /// U Slawisch ~ Mar del Plata                  50,5        64,7           73,5
    /// U Stonewall ~ KIA                           57,9        70,3           78,1
    /// U Stonewall ~ Katalanisch                   61,4        72,8           80,5
    /// U Italienisch ~ KIA                         53,2        67,3           76,7
    /// U Najdorf ~ Stonewall                       45,9        61,4           71,0
    /// U Katalanisch ~ Mar del Plata               45,6        60,8           69,5
    /// U Katalanisch ~ Slawisch                    52,9        65,8           72,6
    /// V Karlsbad(DG) ~ Karlsbad(Nimzo)            95,9        91,8           86,4
    /// V Karlsbad(DG) ~ Karlsbad(Zentrum weg)      88,7        91,6           93,3
    /// V Karlsbad(Nimzo) ~ Karlsbad(Zentrum weg)   84,7        83,7           80,5
    /// V Slawisch ~ Karlsbad(DG)                   76,2        77,4           74,8
    /// V Najdorf ~ Drache                          72,9        81,1           86,5
    /// ──────────────────────────────────────────────────────────────────────────
    /// höchstes unverwandtes Paar                  61,4        72,8           80,5
    /// niedrigstes verwandtes Paar                 72,9        77,4           74,8
    /// Lücke                                      +11,5        +4,6           −5,7
    /// gewählte Schwelle                             67          75             79
    /// </code>
    ///
    /// <para><b>struktur 67</b> und <b>ausgewogen 75</b> liegen jeweils mittig in einer echten Lücke
    /// (61,4…72,9 bzw. 72,8…77,4) — dort trennt die Schwelle sauber.</para>
    ///
    /// <para><b>stellungsbild 79</b> ist ein DOKUMENTIERTER KOMPROMISS, keine saubere Trennung: hier
    /// überlappen die beiden Gruppen (höchstes unverwandtes 80,5 über niedrigstem verwandtem 74,8),
    /// ein verwandtes Paar liegt sogar EXAKT auf einem unverwandten (Karlsbad(Nimzo)~Zentrum weg
    /// 80,5 = Stonewall~Katalanisch 80,5) — eine trennende Schwelle kann es also nicht geben.
    /// Ursache ist kein Fehler, sondern die Gewichtung selbst: bei 45 % Figurenplatzierung schlägt
    /// „anderes Bauernbild, aber dasselbe Aufbauschema" (Stonewall/Katalanisch/KIA: d4, g3, Lg2,
    /// Sf3, Dc2, kurz rochiert) das „gleiche Bauerngerüst mit anderen Figuren" (Slawisch/Karlsbad).
    /// Genau danach ist „stellungsbild" gefragt.
    /// Fehlklassifikationen über alle Schwellen gezählt, liegt das Minimum bei zwei — erreicht bei
    /// 79 (ein Durchrutscher: Stonewall~Katalanisch 80,5; ein Ausfall: Slawisch~Karlsbad 74,8) und
    /// ebenso ab 81 (kein Durchrutscher, dafür zwei Ausfälle: zusätzlich Karlsbad(Nimzo)~Zentrum weg
    /// 80,5). Gewählt ist 79, weil ein zu viel angezeigter schwacher Treffer sichtbar und billig
    /// ist, ein fehlender dagegen unsichtbar. Von den drei Paaren, welche die Gegenlesung in dieser
    /// Voreinstellung als Fehltreffer benannt hat, hält 79 zwei heraus (Stonewall~KIA 78,1;
    /// Mar del Plata~Grünfeld scheitert schon an der Materialschranke) — Stonewall~Katalanisch
    /// kommt weiterhin durch und ist oben als bewusst hingenommen vermerkt.</para>
    ///
    /// <para>Ein ausdrücklich mitgegebener <c>minScore</c> schlägt diesen Default weiterhin.</para>
    /// </summary>
    public static int DefaultMinScoreFor(string? preset) => SimilarityWeights.NormalizePreset(preset) switch
    {
        "struktur" => DefaultMinScoreStruktur,
        "stellungsbild" => DefaultMinScoreStellungsbild,
        _ => DefaultMinScoreAusgewogen,
    };

    /// <summary>Bonus, wenn der erwogene Zug DER Repertoirezug ist (gleiches from→to).</summary>
    public const double ExactMoveBonus = 0.5;
    /// <summary>Bonus für „gleiche Figurenart aufs gleiche Zielfeld, aber von woanders".</summary>
    public const double SameTargetMoveBonus = 0.25;

    public const string MatchExact = "exact";
    public const string MatchSameTarget = "sameTarget";

    public async Task<SimilarPositionsResultDto> FindAsync(int userId, SimilarPositionsRequestDto dto, CancellationToken ct)
    {
        var limit = Math.Clamp(dto.Limit ?? DefaultLimit, 1, MaxLimit);
        // Die Voreinstellung bestimmt die Schwelle mit — ein ausdrücklich gesetzter minScore schlägt sie.
        var minScore = Math.Clamp(dto.MinScore ?? DefaultMinScoreFor(dto.Preset), 0, 100);
        var weights = SimilarityWeights.FromPreset(dto.Preset);
        bool includeMirrored = dto.IncludeMirrored ?? true;
        bool sameSideToMove = dto.SameSideToMove ?? false;

        var result = new SimilarPositionsResultDto
        {
            Preset = SimilarityWeights.NormalizePreset(dto.Preset),
            MinScore = minScore,
            Limit = limit,
        };

        var query = PositionSimilarity.Extract(dto.Fen);
        if (query.IsEmpty) return result;                       // leeres Brett → nichts Sinnvolles zu suchen
        var mirroredQuery = includeMirrored ? PositionSimilarity.Mirror(query) : null;

        // Zug der Anfrage auflösen. Bleibt er unauflösbar (illegale FEN + mehrdeutiges SAN), wird er
        // ignoriert UND onlyWithMove wirkungslos — sonst käme still eine leere Liste zurück, deren
        // Ursache der Nutzer nirgends sehen kann. `move: null` in der Antwort zeigt genau das an.
        var moveQuery = ResolveMove(dto.Fen, dto.Move, query);
        bool onlyWithMove = moveQuery != null && (dto.OnlyWithMove ?? false);
        result.Move = moveQuery == null ? null : new SimilarMoveEchoDto
        {
            From = moveQuery.Value.From is int f ? PositionSimilarity.AlgebraicFromSquare(f) : null,
            To = PositionSimilarity.AlgebraicFromSquare(moveQuery.Value.To),
            Promotion = moveQuery.Value.Promotion?.ToString(),
            Piece = moveQuery.Value.Piece == null ? null : FenCharOf(moveQuery.Value.Piece.Value).ToString(),
        };
        result.OnlyWithMove = onlyWithMove;

        // Zugriffsprüfung: GetGamesAsync liefert ausschließlich Repertoires aus
        // RepertoireAccess.ReadableBy — nicht lesbare (oder unbekannte) Ids fallen beim Filtern
        // still heraus, statt fremde Namen über eine Fehlermeldung preiszugeben.
        var games = await _lines.GetGamesAsync(userId, ct);
        var wanted = dto.RepertoireIds is { Count: > 0 } ? new HashSet<int>(dto.RepertoireIds) : null;

        var best = new List<Candidate>();
        int compared = 0;

        foreach (var game in games)
        {
            ct.ThrowIfCancellationRequested();
            if (wanted != null && !wanted.Contains(game.RepertoireId)) continue;

            var board = BoardFor(game.StartFen);
            if (board == null) continue;                        // unbrauchbare [FEN] → Linie überspringen

            Candidate? lineBest = null;
            void Consider(string fen, int ply, IReadOnlyList<LineContinuation> continuations)
            {
                var candidate = PositionSimilarity.Extract(fen);
                // Die identische Stellung ist kein „ähnlicher" Treffer — dafür gibt es die exakte
                // Stellungssuche („kommt vor in"). Das farbvertauschte Spiegelbild bleibt drin:
                // das ist eine echte Entdeckung, keine Wiederholung der Anfrage.
                if (candidate.Placement == query.Placement) return;

                // Die Materialschranke gilt JE FARBE — also muss sie gegen GENAU DIE Merkmale laufen,
                // die danach auch verglichen werden. Der gespiegelte Vergleich nimmt die gespiegelte
                // Anfrage; prüft man ihn gegen die ungespiegelte, fällt jede Stellung mit
                // Materialungleichgewicht aus der Spiegelsuche — sogar ihr eigenes, perfektes
                // Spiegelbild (belegt an r3k2r/…/R2QK2R gegen r2qk2r/…/R3K2R: Δ je Farbe 9).
                bool normalOk = PositionSimilarity.PassesMaterialGate(query, candidate);
                bool mirroredOk = mirroredQuery != null && PositionSimilarity.PassesMaterialGate(mirroredQuery, candidate);
                if (!normalOk && !mirroredOk) return;
                compared++;

                if (normalOk && (!sameSideToMove || candidate.SideToMove == query.SideToMove))
                {
                    var normal = PositionSimilarity.Compare(query, candidate, weights);
                    lineBest = Better(lineBest, Build(game, ply, fen, normal, false, moveQuery, continuations, onlyWithMove));
                }
                if (mirroredOk && (!sameSideToMove || candidate.SideToMove == mirroredQuery!.SideToMove))
                {
                    var flipped = PositionSimilarity.Compare(mirroredQuery!, candidate, weights);
                    lineBest = Better(lineBest, Build(game, ply, fen, flipped, true, moveQuery, continuations, onlyWithMove));
                }
            }

            // Startstellung nur bei eigener [FEN] mitnehmen (echter Linien-Anfang); bei Linien aus der
            // Grundstellung wäre sie Rauschen — genau wie im exakten Index.
            if (game.StartFen != null)
                Consider(board.ToFen(), 0, moveQuery == null ? Array.Empty<LineContinuation>() : ContinuationsAt(board, game.Moves, 0));

            int seen = 0;
            try
            {
                WalkPositions(board, game.Moves, startPly: 0, visit =>
                {
                    Consider(visit.Fen, visit.Ply, visit.Continuations);
                    return ++seen < MaxPositionsPerLine;
                }, withContinuations: moveQuery != null);
            }
            catch { /* defensiv: eine kaputte Linie darf die Suche nicht kippen */ }

            if (lineBest != null && lineBest.Value.Score >= minScore) best.Add(lineBest.Value);
        }

        result.Compared = compared;
        result.Matches = best
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Ply < 0 ? int.MaxValue : c.Ply)          // Hauptlinie vor Variante, früher vor später
            .ThenBy(c => c.Game.RepertoireName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Game.GameIndex)
            .Take(limit)
            .Select(ToDto)
            .ToList();
        return result;
    }

    // ─── Kandidaten ───────────────────────────────────────────────────────

    /// <summary>Baut einen Kandidaten inkl. Zug-Treffer und Endwert; <c>null</c>, wenn
    /// <paramref name="onlyWithMove"/> gilt und der Zug hier nicht gespielt wird.</summary>
    private static Candidate? Build(RepGame game, int ply, string fen, SimilarityBreakdown score, bool mirrored,
        MoveQuery? moveQuery, IReadOnlyList<LineContinuation> continuations, bool onlyWithMove)
    {
        var (match, played) = MatchMove(moveQuery, continuations, mirrored);
        if (onlyWithMove && match == null) return null;
        // Lücken-Schluss statt fünfter gewichteter Komponente: der Bonus holt einen Anteil des
        // FEHLENDEN Rests auf, kann also nie über 100 laufen und dreht die Reihenfolge innerhalb
        // gleich guter Stellungen zugunsten des passenden Zugs.
        // Der Deckel für nicht anwendbare Komponenten (PositionSimilarity.NotApplicablePenalty)
        // sitzt bewusst im STELLUNGSWERT, nicht hier: der Zug-Treffer ist zusätzliche Evidenz und
        // darf sie schließen. Ein unvollständig gemessener Treffer kann so im gerundeten Endwert
        // wieder bei 100 landen — sortiert wird ungerundet, der vollständige Treffer bleibt vorn,
        // und `positionScore` zeigt weiterhin die 99.
        double bonus = match switch
        {
            MatchExact => ExactMoveBonus,
            MatchSameTarget => SameTargetMoveBonus,
            _ => 0.0,
        };
        double final = score.Score + bonus * (100.0 - score.Score);
        // Ohne Treffer den Hauptzug melden, damit die Liste zeigen kann, wie es dort weitergeht.
        if (played == null)
            foreach (var c in continuations)
                if (c.IsMainline) { played = c; break; }
        if (played == null && continuations.Count > 0) played = continuations[0];
        return new Candidate(game, ply, fen, score, mirrored, final, match, played);
    }

    /// <summary>Trefferstufe des Anfragezugs an dieser Stellung + die passende Fortsetzung.</summary>
    private static (string? Match, LineContinuation? Played) MatchMove(
        MoveQuery? moveQuery, IReadOnlyList<LineContinuation> continuations, bool mirrored)
    {
        if (moveQuery == null || continuations.Count == 0) return (null, null);
        var q = moveQuery.Value;
        // Beim farbvertauschten Vergleich ist die ANFRAGE gespiegelt worden — also muss auch der
        // Anfragezug an der Mittellinie gespiegelt werden, um zur Kandidatenstellung zu passen.
        int? from = q.From is int f ? (mirrored ? PositionSimilarity.MirrorSquare(f) : f) : null;
        int to = mirrored ? PositionSimilarity.MirrorSquare(q.To) : q.To;

        LineContinuation? sameTarget = null;
        foreach (var c in continuations)
        {
            if (c.To != to) continue;
            // Umwandlungsfigur nur prüfen, wenn die Anfrage eine nennt (eine Brett-Oberfläche, die
            // sie weglässt, soll den Treffer nicht verlieren).
            bool promotionOk = q.Promotion == null || c.Promotion == q.Promotion;
            if (from != null && c.From == from && promotionOk) return (MatchExact, c);
            if (sameTarget == null && q.Piece != null && c.Piece == q.Piece.Value && promotionOk) sameTarget = c;
        }
        return sameTarget == null ? (null, null) : (MatchSameTarget, sameTarget);
    }

    /// <summary>Je Linie zählt nur der beste Treffer — sonst füllt eine einzige Linie die ganze Liste.</summary>
    private static Candidate? Better(Candidate? current, Candidate? next)
    {
        if (next == null) return current;
        if (current == null) return next;
        var cur = current.Value;
        var nxt = next.Value;
        if (nxt.Score > cur.Score) return nxt;
        if (nxt.Score < cur.Score) return cur;
        // Gleichstand: Hauptlinie vor Variante, früherer Ply vor späterem, ungespiegelt vor gespiegelt.
        int curPly = cur.Ply < 0 ? int.MaxValue : cur.Ply;
        int nextPly = nxt.Ply < 0 ? int.MaxValue : nxt.Ply;
        if (nextPly != curPly) return nextPly < curPly ? nxt : cur;
        return cur.Mirrored && !nxt.Mirrored ? nxt : cur;
    }

    /// <summary><paramref name="Score"/> = Endwert (mit Zug-Bonus), <paramref name="Breakdown"/> =
    /// reiner Stellungswert.</summary>
    private readonly record struct Candidate(RepGame Game, int Ply, string Fen, SimilarityBreakdown Breakdown,
        bool Mirrored, double Score, string? MoveMatch, LineContinuation? Played);

    private static SimilarPositionMatchDto ToDto(Candidate c) => new()
    {
        RepertoireId = c.Game.RepertoireId,
        RepertoireName = c.Game.RepertoireName,
        Chapter = c.Game.Chapter,
        LineName = c.Game.LineName,
        GameIndex = c.Game.GameIndex,
        Ply = c.Ply,
        Fen = c.Fen,
        Score = Round(c.Score),
        PositionScore = Round(c.Breakdown.Score),
        Mirrored = c.Mirrored,
        MoveMatch = c.MoveMatch,
        MoveSan = c.Played?.San,
        MoveFrom = c.Played == null ? null : PositionSimilarity.AlgebraicFromSquare(c.Played.Value.From),
        MoveTo = c.Played == null ? null : PositionSimilarity.AlgebraicFromSquare(c.Played.Value.To),
        MovePromotion = c.Played?.Promotion?.ToString(),
        Breakdown = new SimilarityBreakdownDto
        {
            Pawns = Round(c.Breakdown.Pawns),
            Material = Round(c.Breakdown.Material),
            Pieces = c.Breakdown.Pieces.HasValue ? Round(c.Breakdown.Pieces.Value) : null,
            King = c.Breakdown.King.HasValue ? Round(c.Breakdown.King.Value) : null,
        },
    };

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    // ─── Anfragezug auflösen ──────────────────────────────────────────────

    /// <summary>Der erwogene Zug, auf Felder heruntergebrochen. <c>From == null</c> heißt: nur
    /// Zielfeld UND Figurenart sind bekannt (kommt nur aus dem SAN-Rückfall) — dann ist höchstens
    /// <c>sameTarget</c> erreichbar. Ohne beides gibt es gar keinen <see cref="MoveQuery"/>: ein
    /// Zug, der nie treffen kann, gilt als nicht auflösbar (siehe <see cref="ResolveMove"/>).</summary>
    public readonly record struct MoveQuery(int? From, int To, SimPieceType? Piece, char? Promotion);

    // Toleranter SAN-Rückfall für den Fall, dass das Brett die Anfrage-FEN nicht laden kann
    // (Chessable-Diagramme sind teils illegal) oder das SAN mehrdeutig ist: Figurenbuchstabe,
    // optionales Ausgangsfeld, Zielfeld, optionale Umwandlung. Deckt auch die Chessable-Schreibweise
    // ohne „=" ab (`a8Q`), die sonst still verloren ginge.
    private static readonly Regex SanFallback = new(
        @"^(?<piece>[KQRBN])?(?<ff>[a-h])?(?<fr>[1-8])?x?(?<to>[a-h][1-8])=?(?<promo>[QRBNqrbn])?$",
        RegexOptions.Compiled);

    /// <summary>Löst den Anfragezug auf: from/to direkt, sonst SAN gegen die Anfrage-FEN, sonst
    /// tolerant über <see cref="SanFallback"/>. <c>null</c> = kein (brauchbarer) Zug.</summary>
    public static MoveQuery? ResolveMove(string? fen, SimilarMoveInputDto? dto, PositionFeatures query)
    {
        if (dto == null) return null;

        var promotion = NormalizePromotion(dto.Promotion);
        var to = PositionSimilarity.SquareFromAlgebraic(dto.To);
        var from = PositionSimilarity.SquareFromAlgebraic(dto.From);
        // Feld-Angabe nur mit BEIDEN Feldern: ein bloßes Zielfeld ist nicht auflösbar. Ohne
        // Ausgangsfeld bleibt auch die Figurenart unbekannt (die kommt aus query.TypeAt(from)),
        // damit scheitert `exact` am fehlenden From und `sameTarget` an der fehlenden Figurenart —
        // der Zug könnte NIE treffen und mit onlyWithMove käme still eine leere Liste zurück.
        // Also: auf das SAN zurückfallen (das liefert Herkunft/Figurenart) und sonst `null` melden,
        // damit der Nutzer den Grund an `move: null` sieht.
        if (to != null && from != null)
            return new MoveQuery(from, to.Value, query.TypeAt(from.Value), promotion);

        var san = (dto.San ?? "").Trim().TrimEnd('!', '?', '+', '#');
        if (san.Length == 0) return null;

        // Sauberer Weg: SAN am echten Brett auflösen (kennt Rochade, Disambiguierung, en passant).
        var board = BoardFor(fen);
        if (board != null)
        {
            var resolved = ContinuationsAt(board, new List<PgnMove> { new(san, new List<List<PgnMove>>()) }, 0);
            if (resolved.Count > 0)
            {
                var m = resolved[0];
                return new MoveQuery(m.From, m.To, m.Piece, promotion ?? m.Promotion);
            }
        }

        var match = SanFallback.Match(san);
        if (!match.Success) return null;
        var toSquare = PositionSimilarity.SquareFromAlgebraic(match.Groups["to"].Value);
        if (toSquare == null) return null;
        int? fromSquare = match.Groups["ff"].Success && match.Groups["fr"].Success
            ? PositionSimilarity.SquareFromAlgebraic(match.Groups["ff"].Value + match.Groups["fr"].Value)
            : null;
        var piece = match.Groups["piece"].Success
            ? PieceTypeOf(match.Groups["piece"].Value[0])
            : SimPieceType.Pawn;                     // kein Buchstabe = Bauernzug
        return new MoveQuery(fromSquare, toSquare.Value, piece,
            promotion ?? NormalizePromotion(match.Groups["promo"].Value));
    }

    private static char? NormalizePromotion(string? text)
    {
        var c = char.ToLowerInvariant((text ?? "").Trim().FirstOrDefault());
        return c is 'q' or 'r' or 'b' or 'n' ? c : null;
    }

    private static SimPieceType? PieceTypeOf(char letter) => char.ToLowerInvariant(letter) switch
    {
        'p' => SimPieceType.Pawn,
        'n' => SimPieceType.Knight,
        'b' => SimPieceType.Bishop,
        'r' => SimPieceType.Rook,
        'q' => SimPieceType.Queen,
        'k' => SimPieceType.King,
        _ => null,
    };

    private static char FenCharOf(SimPieceType type) => type switch
    {
        SimPieceType.Pawn => 'p',
        SimPieceType.Knight => 'n',
        SimPieceType.Bishop => 'b',
        SimPieceType.Rook => 'r',
        SimPieceType.Queen => 'q',
        _ => 'k',
    };
}
