import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, HostListener, Optional, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, Subscription, catchError, forkJoin, map, of } from 'rxjs';
import { Key } from 'chessground/types';

import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { StockfishService } from '../puzzles/stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { RepertoireTrainingService, LineStateDto, RepertoireMeta, RepertoireOpponent } from './repertoire-training.service';
import { TrainerSrSync } from './repertoire-trainer-sr-sync';
import { buildRepertoireGraph } from './repertoire-tree.util';
import { chapterColorsOf, TrainColor } from '../../shared/repertoire/repertoire-color.util';
import { SrConfigDialogComponent } from './sr-config-dialog.component';
import { ParsedGame, exceedsSyncLimits, parsePgnTextWithSource } from '../../shared/pgn-viewer/pgn-parser';
import { parsePgnTextParallel } from '../../shared/pgn-viewer/pgn-parse-pool';
import { isInfoLineGame } from './repertoire-info-line.util';
import { isStateDue, isStateLearnable, earliestDueIso, relDueLabel, shuffle } from './repertoire-sr.util';
import { getRepertoireOffline, refreshRepertoireOffline } from './repertoire-offline.util';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { EvalCompare } from './repertoire-trainer-eval-compare';
import { ExplorerAnalysisResult, RepertoireExplorerService, formatPercent } from './repertoire-explorer.service';
import { TrainerFrequencyLoader } from './repertoire-trainer-frequency-loader';
import { TrainerLineSet, byRank, extendQueue, lineKeyOf, opponentLines, playableLines } from './repertoire-trainer-queue.util';
import { BoardMove, PlayedComment, TrainerMode, TrainerOutcome, TrainerPhase, TrainerSession } from './repertoire-trainer-session';
import { LineMoveToken } from './repertoire-trainer-line-view.util';
import { localStore, readRaw, writeRaw } from '../../core/local-json-store';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { TRAINER_PASS_THROUGH, TRAINING_LINES_ALL, TrainingLines, trainingLinesUrl } from '../../shared/player-card/training-lines';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import type { DrawShape } from 'chessground/draw';
import { AnalysisEngineService } from '../analysis/analysis-engine.service';
import { OpponentPositions } from '../../shared/player-card/opponent-positions';
import { OpponentNoteView, opponentNoteView } from './opponent-note.util';
import { cardLang } from '../../shared/player-card/card-lang';
import { TrainerReviewEngine, readReviewEngineOn, saveReviewEngineOn } from './repertoire-trainer-review-engine';
import { ChessableMisses, MissInfo, chessableOid, chessableUrl, missAt } from './repertoire-trainer-misses.util';

const NO_COMMENTS: PlayedComment[] = [];

/** „Häufigste zuerst" merkt sich das Gerät — wie die übrigen Anzeige-Vorlieben des Trainers. */
const FREQ_ORDER_KEY = 'rookhub_rep_train_freq_order';

function readFreqOrder(): boolean {
  return readRaw(localStore(), FREQ_ORDER_KEY) === '1';
}

function saveFreqOrder(on: boolean): void {
  writeRaw(localStore(), FREQ_ORDER_KEY, on ? '1' : '0');   // gesperrt/voll → nur diese Sitzung
}

/** „Kommentare zeigen" (0.893.0) merkt sich ebenfalls das Gerät; Vorgabe aus. */
export const SHOW_COMMENTS_KEY = 'rookhub_rep_train_show_comments';

function readShowComments(): boolean {
  return readRaw(localStore(), SHOW_COMMENTS_KEY) === '1';
}

function saveShowComments(on: boolean): void {
  writeRaw(localStore(), SHOW_COMMENTS_KEY, on ? '1' : '0');   // gesperrt/voll → nur diese Sitzung
}

/**
 * Line-basiertes Repertoire-Training: eine ganze PGN-Linie wird vom Startzug an durchgespielt,
 * Gegnerzüge automatisch, an jedem eigenen Zug pausiert der Trainer und wertet den User-Zug gegen
 * die Linie (erwarteter Zug + [%alt]-tolerierte Alternativen). Nach dem letzten Zug: nächste Linie.
 * Ein `?chapter=Name`-Query beschränkt die Sitzung auf ein Kapitel (Black-Header). SM-2-Reviews
 * werden je Linie ans Backend gesendet.
 *
 * <p>Aufbau (Zerlegung Z21): die Komponente lädt (PGN, Zustände, Gegner-Reihung), baut die Warteschlange
 * (`repertoire-trainer-queue.util`) und verdrahtet; das Abspielen samt Phasen, Timern und Zugurteil ist
 * {@link TrainerSession}, der SR-Abgleich {@link TrainerSrSync}, „Häufigste zuerst" {@link TrainerFrequencyLoader},
 * der Stockfish-Vergleich nach einem Fehlzug {@link EvalCompare}. Die Felder fürs Template reicht sie durch.</p>
 */
@Component({
  selector: 'app-repertoire-trainer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconLabelDirective,
    CommonModule, RouterLink, MatCardModule, MatButtonModule, MatButtonToggleModule,
    MatIconModule, MatProgressBarModule, MatTooltipModule, MatDialogModule, MatMenuModule, TranslatePipe, PuzzleBoardComponent,
  ],
  templateUrl: './repertoire-trainer.component.html',
  styleUrls: ['./repertoire-trainer.component.scss'],
})
export class RepertoireTrainerComponent implements OnInit, OnDestroy {
  repertoireId = 0;
  /** Effektive Trainingsfarbe je Kapitel (Auto-Erkennung + manuelle Overrides, siehe repertoire-color.util). */
  private chapterColors = new Map<string, TrainColor>();
  /** Kapitel-Filter aus ?chapter=…. Null = alle Kapitel. */
  chapterFilter: string | null = null;
  /** Optional nur EINE Linie (?line=<lineKey>) — für „Diese Linie lernen/üben". */
  private singleLineKey: string | null = null;
  resetting = false;
  poolBusy = false;
  private allLines: ParsedGame[] = [];
  /** Für die EMPTY-Ansicht: wann die nächste Pool-Linie fällig wird (ISO) — null = nichts im Pool. */
  nextDueAt: string | null = null;
  /** true = Session läuft aus der Offline-Kopie (Server unerreichbar); Bewertungen werden lokal
   *  berechnet und via Offline-Queue nachgereicht, Pool-/Config-Verwaltung ist ausgeblendet. */
  offlineSession = false;
  /** Name des Repertoires für den Kopf (d3-rk-trainer-leer-zwei-aktionen); null = noch nicht da → „Repertoire-Trainer“. */
  repertoireName: string | null = null;

  /** Bewertungsvergleich nach einem Fehlzug (Stockfish, Spielerzug ↔ Repertoirezug). */
  private readonly evalCmp = new EvalCompare(
    { getEval: (fen, depth) => this.stockfish.getEval(fen, depth) }, () => this.cdr.markForCheck());
  /** SR-Zustände je Linie + Abgleich mit dem Server (Offline-Kopie, Offline-Queue). */
  private readonly sr = new TrainerSrSync({
    repertoireId: () => this.repertoireId, training: () => this.training, queue: () => this.offlineQueue,
  });
  /** Das Abspielen der Linien (Phasen, Timer, Zugurteil). */
  readonly session = new TrainerSession({
    changed: () => { this.syncReviewEngine(); this.cdr.markForCheck(); },
    colorOf: line => this.colorOf(line),
    lineLearned: key => this.sr.promoteLearned(key),
    lineReviewed: req => this.sr.review(req, dueAt => this.session.showNextRepeat(dueAt)),
    wrongMove: (startFen, fenAfterPlayer, san, color) => this.evalCmp.start(startFen, fenAfterPlayer, san, color),
    retryAfterWrong: () => this.evalCmp.reset(),
  });
  private get s(): TrainerSession { return this.session; }

  /** Modus „Häufigste zuerst": Linien, die man in echten Partien öfter erreicht, kommen zuerst
   *  (Lichess-Explorer, gerechnet am Server wie der Lochfinder). Aus = bisherige Reihenfolge. */
  freqOrder = readFreqOrder();
  /** „Kommentare zeigen" (0.893.0): Kommentare der zuletzt gespielten Züge während des Spiels in allen Modi, ohne Halt.
   *  Aus = wie bisher (Abfragen ohne Kommentare; Lern-Kontextkarte und COMMENT-Halt bleiben). Formen kommen nie
   *  während des Spiels, nur am Linienende. */
  showComments = readShowComments();
  /** Häufigkeiten über den Explorer (Fortschritt, Hinweis, Sortierung). */
  private readonly freq = new TrainerFrequencyLoader(() => this.explorer, () => this.cdr.markForCheck(), () => this.buildQueue());

  /** Reihenfolge nach einem Gegner (`?opponent=prep:<id>|league:<fide>`, „Alle in dieser Reihenfolge trainieren" der
   *  Spielerkarte, 2026-10-07): dieselben Trainingslinien wie dort, wahrscheinlichste zuerst; nur die Kapitel der Farbe
   *  (`?color=`). Ohne den Parameter bleibt der Trainer, wie er war. */
  opponent: string | null = null;
  private opponentColor: TrainColor | null = null;
  /** Linien-Schlüssel → Platz in der Reihenfolge des Gegners; null = (noch) keine. */
  private opponentRank: Map<string, number> | null = null;
  /** Hinweis zur Gegner-Reihenfolge (i18n-Key). */
  opponentNotice: string | null = null;
  /** Einstieg der Gegner-Sitzung (`?line=` aus der Karte): diese Linie zuerst, danach die nach ihr in der Reihung. */
  private opponentStart: string | null = null;
  /** So viele Linien umfasst die Gegner-Sitzung (für „Alle N Linien … durch"). */
  opponentTotal = 0;
  /** Seine Partien je Stellung (0.892.0, `positions=true`) — für „in seinen Partien n× erreicht / ab hier keine mehr". */
  private opponentPositions: OpponentPositions | null = null;
  /** Wertungsband der Lichess-Schätzung („2000–2300"), `null` = keine Schätzung. */
  private opponentBand: string | null = null;
  /** Name des Gegners für die Anzeige (`?name=` von der Karte). */
  private opponentName: string | null = null;
  /** Filter für die Trainingslinien (`TRAINER_PASS_THROUGH`): aus der Adresse oder — ohne `?opponent=` — vom gespeicherten Gegner. */
  private opponentParams: Record<string, string> = {};
  private opponentSub: Subscription | null = null;
  /** Erstes Laden (PGN + Zustände); wird beim Verlassen abbestellt (Codereview F3-025). */
  private loadSub: Subscription | null = null;
  /** Abbruch-Token: nach ngOnDestroy baut keine verspätete Antwort mehr eine Sitzung (und startet keinen Explorer-Lauf). */
  private destroyed = false;

  // ===== Durchreicher fürs Template (Zustand liegt in Session, Häufigkeiten, Bewertungsvergleich) =====
  get phase(): TrainerPhase { return this.s.phase; }
  get mode(): TrainerMode { return this.s.mode; }
  get color(): TrainColor { return this.s.color; }
  get queue(): ParsedGame[] { return this.s.queue; }
  get qIndex(): number { return this.s.qIndex; }
  /** Brett: beim Durchklicken der fertigen Linie deren Stellung (Nachbetrachtung, 0.890.0). */
  get fen(): string { return this.s.viewFen; }
  get dests(): Map<Key, Key[]> { return this.s.dests; }
  get lastMove(): [Key, Key] | undefined { return this.s.viewLastMove; }
  get correct(): number { return this.s.correct; }
  get wrong(): number { return this.s.wrong; }
  get currentStreak(): number { return this.s.currentStreak; }
  get bestStreak(): number { return this.s.bestStreak; }
  get sessionUserMoves(): number { return this.s.sessionUserMoves; }
  get outcome(): TrainerOutcome { return this.s.outcome; }
  get expectedDisplay(): string { return this.s.expectedDisplay; }
  get wrongRevealed(): boolean { return this.s.wrongRevealed; }
  get learnComment(): string { return this.s.learnComment; }
  get holdComment(): string { return this.s.holdComment; }
  get pendingRepeat(): boolean { return this.s.pendingRepeat; }
  get nextRepeatAt(): string | null { return this.s.nextRepeatAt; }
  get oppMoving(): boolean { return this.s.oppMoving; }
  get isPlayable(): boolean { return this.s.isPlayable; }
  get premovable(): boolean { return this.s.premovable; }
  get progressPct(): number { return this.s.progressPct; }
  get currentLineLabel(): string { return this.s.currentLineLabel; }
  get currentLineChapter(): string { return this.s.currentLineChapter; }
  get movesInLine(): LineMoveToken[] { return this.s.movesInLine; }
  get currentComment(): string { return this.s.currentComment; }
  get currentCommentParagraphs(): string[] { return this.s.currentCommentParagraphs; }
  get currentMovePrettyLabel(): string { return this.s.currentMovePrettyLabel; }
  get userColorName(): 'white' | 'black' { return this.s.userColorName; }
  get boardTurn(): 'white' | 'black' { return this.s.boardTurn; }
  /** Kompakte Restzeit bis zur nächsten Fälligkeit, z. B. „4 h", „3 d", „2 w". */
  get nextDueLabel(): string { return relDueLabel(this.nextDueAt); }
  /** LINE_DONE: „in X" bis die gerade beendete Linie erneut abgefragt wird (Abfragen-Modus). */
  get nextRepeatLabel(): string { return relDueLabel(this.s.nextRepeatAt); }
  get freqLoading(): ExplorerAnalysisResult | null { return this.freq.progress; }
  get freqRunning(): boolean { return this.freq.running; }
  /** Hinweis unter der Leiste, wenn die Häufigkeiten fehlen oder unvollständig sind (i18n-Key). */
  get freqNotice(): string | null { return this.freq.notice; }
  get evalLoading(): boolean { return this.evalCmp.loading; }
  get evalDeltaPawns(): number | null { return this.evalCmp.deltaPawns; }
  get evalMateNote(): 'missed' | 'allowed' | null { return this.evalCmp.mateNote; }
  get evalDeltaAbsDisplay(): string { return this.evalCmp.deltaAbsDisplay; }
  get evalFailed(): boolean { return this.evalCmp.failed; }
  get wrongShown(): boolean { return this.s.wrongShown; }
  get hints(): number { return this.s.hints; }
  get hintLevel(): number { return this.s.hintLevel; }
  get canHint(): boolean { return this.s.canHint; }
  get canSkip(): boolean { return this.s.canSkip; }
  get boardShapes(): DrawShape[] { return this.s.boardShapes; }
  /** Der Schalter „Kommentare zeigen" steht, solange eine Linie läuft (nicht im Lade-/Leer-/Fertig-Zustand). */
  get canToggleComments(): boolean { return !['LOADING', 'EMPTY', 'DONE'].includes(this.s.phase); }
  /** Kommentare der zuletzt gespielten Züge — nur mit „Kommentare zeigen". */
  get playedComments(): PlayedComment[] { return this.showComments ? this.s.playedComments : NO_COMMENTS; }

  /** „Kommentare zeigen" an/aus (Taste K); gemerkt je Gerät. Hält das Spiel nicht an. */
  toggleShowComments(): void {
    this.showComments = !this.showComments;
    saveShowComments(this.showComments);
    this.cdr.markForCheck();
  }

  /** Chessable-Fehler je Linie (0.886.0) — einmal je Repertoire geladen; `null` = (noch) keine. */
  private chessableMisses: ChessableMisses | null = null;

  /** Nach einem Fehlzug: wie oft schon falsch (Chessable an diesem Zug, RookHub die Linie); `null` = nichts anzeigen. */
  get currentMiss(): MissInfo | null {
    if (this.s.phase !== 'FEEDBACK' || this.s.outcome !== 'wrong') return null;
    const line = this.s.currentLine;
    const lapses = line ? this.sr.stateOf(this.lineKeyOf(line))?.lapses ?? 0 : 0;
    return missAt(line, this.s.currentPly, this.s.color, this.chessableMisses, lapses);
  }

  /** Link der aktuellen Linie auf Chessable (nur mit `[ChessableOid]`). */
  get currentChessableUrl(): string | null {
    const oid = chessableOid(this.s.currentLine);
    return oid ? chessableUrl(oid) : null;
  }

  constructor(
    private route: ActivatedRoute,
    private training: RepertoireTrainingService,
    public prefs: PreferencesService,
    private translate: TranslateService,
    private cdr: ChangeDetectorRef,
    private stockfish: StockfishService,
    private dialog: MatDialog,
    private offlineQueue: OfflineQueueService,
    private explorer: RepertoireExplorerService,
    private snackbar: SnackbarService,
    private confirm: ConfirmService,
    /** Nur für die Reihenfolge nach einem Gegner (`?opponent=`); optional, damit die Specs ohne HTTP bauen. */
    @Optional() private http: HttpClient | null = null,
  ) {}

  /** Engine der Nachbetrachtung (dieselbe wie im Analysebrett). Optional: Specs bauen die Komponente ohne Injektor und setzen sie
   *  selbst. */
  private analysisEngine: AnalysisEngineService | null = injectOptional(AnalysisEngineService);

  // ===== Nachbetrachtung (0.890.0): fertige Linie durchklicken, Engine zuschalten =====

  /** Schalter „Engine" der Nachbetrachtung (gemerkt je Gerät, Vorgabe aus). */
  reviewEngineOn = readReviewEngineOn();
  private readonly reviewEngine = new TrainerReviewEngine(() => this.analysisEngine, () => this.cdr.markForCheck());

  get canReview(): boolean { return this.s.canReview; }
  get reviewPly(): number { return this.s.reviewPly; }
  /** Halbzüge der fertigen Linie (Schluss der Nachbetrachtung). */
  get reviewMax(): number { return this.s.currentPly; }
  get reviewComment(): string { return this.s.reviewComment; }
  /** Offline (und ohne Engine-Dienst) gibt es keine Engine — Durchklicken geht trotzdem. */
  get reviewEngineAvailable(): boolean { return !this.offlineSession && !!this.analysisEngine; }
  get reviewEngineRunning(): boolean { return this.reviewEngine.active; }
  get reviewEvalText(): string { return this.reviewEngine.evalText; }
  get reviewWhiteHeight(): number { return this.reviewEngine.whiteHeight; }
  get reviewBestLine(): string { return this.reviewEngine.bestLine; }
  get reviewDepth(): number { return this.reviewEngine.depth; }

  reviewTo(ply: number): void { this.s.reviewTo(ply); }
  reviewStep(delta: number): void { this.s.reviewStep(delta); }

  toggleReviewEngine(): void {
    if (!this.reviewEngineAvailable) return;
    this.reviewEngineOn = !this.reviewEngineOn;
    saveReviewEngineOn(this.reviewEngineOn);
    this.syncReviewEngine();
    this.cdr.markForCheck();
  }

  /** Engine nur im „Linie fertig"-Kasten mit eingeschaltetem Schalter; sonst aus (bei jedem Zustandswechsel abgeglichen). */
  private syncReviewEngine(): void {
    const want = this.s.canReview && this.reviewEngineOn && this.reviewEngineAvailable && !this.destroyed;
    this.reviewEngine.sync(want ? this.s.viewFen : null);
  }

  /** Die gespielte Linie im Analysebrett (neuer Tab): Ausgangsstellung + Züge als UCI, Brett aus Sicht der Trainingsfarbe. */
  get analysisLink(): string | null {
    const line = this.s.currentLine;
    if (!line) return null;
    const moves = line.moves.slice(0, this.s.currentPly).map(m => m.from + m.to + (m.promotion ?? ''));
    const q = new URLSearchParams({ fen: line.fens[0], moves: moves.join(' '), orientation: this.userColorName });
    return '/analysis?' + q.toString();
  }

  ngOnInit(): void {
    this.repertoireId = Number(this.route.snapshot.paramMap.get('id')) || 0;
    this.chapterFilter = this.route.snapshot.queryParamMap.get('chapter');
    this.s.mode = this.route.snapshot.queryParamMap.get('mode') === 'learn' ? 'learn' : 'quiz';
    this.singleLineKey = this.route.snapshot.queryParamMap.get('line');
    this.opponent = trainingLinesUrl(this.route.snapshot.queryParamMap.get('opponent')) ? this.route.snapshot.queryParamMap.get('opponent') : null;
    this.opponentName = this.route.snapshot.queryParamMap.get('name');
    const oc = this.route.snapshot.queryParamMap.get('color');
    this.opponentColor = oc === 'w' || oc === 'b' ? oc : null;
    if (this.opponent) {
      for (const k of TRAINER_PASS_THROUGH) {
        const v = this.route.snapshot.queryParamMap.get(k);
        if (v) this.opponentParams[k] = v;
      }
    }
    this.stockfish.init().catch(() => {});

    this.loadSub = forkJoin({
      pgn: this.training.getPgn(this.repertoireId),
      states: this.training.getLineStates(this.repertoireId),
      meta: this.loadMeta(),
    }).subscribe({
      next: ({ pgn, states, meta }) => {
        if (meta?.name && !this.repertoireName) this.repertoireName = meta.name;
        this.useStoredOpponent(meta?.opponent ?? null);
        // Eine vorhandene Offline-Kopie beim Online-Öffnen aktuell halten (No-op ohne Kopie).
        refreshRepertoireOffline(this.repertoireId, pgn, states);
        void this.initSession(pgn, states);
        this.loadChessableMisses(pgn);
      },
      error: () => {
        // Server unerreichbar → heruntergeladene Kopie verwenden (Offline-Training); ohne Kopie
        // bleibt es bei der leeren Ansicht.
        const cached = getRepertoireOffline(this.repertoireId);
        if (cached) {
          this.offlineSession = true;
          this.repertoireName ??= cached.meta?.name || null;
          this.sr.setSrLevels(cached.config);
          void this.initSession(cached.pgn, cached.states);
        } else {
          this.s.phase = 'EMPTY';
          this.cdr.markForCheck();
        }
      },
    });
  }

  /** Name + gespeicherter Gegner (`GET /api/repertoires/{id}`). Ein Ausfall ändert nichts (Titel bleibt „Repertoire-Trainer“, kein
   *  Gegner) und fällt NICHT in die Offline-Kopie. `?.`: Test-Doubles ohne die Methode(n). */
  private loadMeta(): Observable<RepertoireMeta | null> {
    const meta: Observable<RepertoireMeta | null> = this.training.getRepertoireMeta?.(this.repertoireId)
      ?? this.training.getRepertoireName?.(this.repertoireId)?.pipe(map((name): RepertoireMeta => ({ name, opponent: null })))
      ?? of(null);
    return meta.pipe(catchError(() => of(null)));
  }

  /** Ohne `?opponent=` in der Adresse gilt der Gegner, den ein „Prep: …"-Repertoire beim Anlegen behalten hat (0.894.0) —
   *  egal ob der Trainer von der Detailseite, der Liste oder dem Dashboard kommt. Die Adresse geht immer vor. */
  private useStoredOpponent(o: RepertoireOpponent | null): void {
    if (this.opponent || !o || !trainingLinesUrl(o.opponent)) return;
    this.opponent = o.opponent;
    this.opponentName ??= o.name || null;
    if (!this.opponentColor && (o.color === 'w' || o.color === 'b')) this.opponentColor = o.color;
    this.opponentParams = {};
    for (const k of TRAINER_PASS_THROUGH) {
      const v = o.params?.[k];
      if (typeof v === 'string' && v) this.opponentParams[k] = v;
    }
  }

  /** Session aus PGN + SR-Zuständen aufbauen (frisch vom Server oder aus der Offline-Kopie). */
  private async initSession(pgn: string, states: LineStateDto[]): Promise<void> {
    this.s.graph = buildRepertoireGraph(pgn);
    // Info-Linien (Erklärungen, siehe isInfoLineGame) werden nicht abgefragt — weder im Quiz noch im
    // Lern-Modus, und „Alle in den Pool" nimmt sie nicht auf. Ansehen kann man sie in der Linienliste.
    // Das GANZE Repertoire, in Portionen (0.712.0) — vorher fragte der Trainer nur die ersten 2 MB/500 Partien ab.
    const parsed = exceedsSyncLimits(pgn) ? await parsePgnTextParallel(pgn) : parsePgnTextWithSource(pgn);
    if (this.destroyed) return;   // während des Zerlegens verlassen (F3-025)
    this.allLines = parsed.filter(p => !isInfoLineGame(p.raw)).map(p => p.game);
    // Trainingsfarbe je Kapitel automatisch erkennen + manuelle Overrides drüberlegen. Dadurch
    // wird jede Linie aus der RICHTIGEN Seite abgefragt, auch wenn das Repertoire Kapitel beider
    // Farben mischt.
    this.chapterColors = chapterColorsOf(this.repertoireId, this.allLines);
    this.sr.setStates(states);
    if (this.opponent && !this.offlineSession) {
      // Gegen einen Gegner (2026-10-08): ?line= ist der EINSTIEG — die Linie sofort zeigen, die Reihung im Hintergrund nachholen.
      this.opponentStart = this.singleLineKey;
      this.singleLineKey = null;
      if (this.opponentStart) this.buildQueue();
      this.loadOpponentOrder(!this.opponentStart);
    }
    else if (this.freqOrder && !this.offlineSession) this.loadFrequencies();
    else this.buildQueue();
  }

  /** Chessable-Fehler nachladen — nur, wenn das Repertoire Chessable-Linien hat; ein Ausfall ändert nichts (Anzeige fehlt dann). */
  private loadChessableMisses(pgn: string): void {
    if (!pgn.includes('[ChessableOid ')) return;
    this.training.getChessableMisses?.(this.repertoireId)?.subscribe({
      next: m => { if (!this.destroyed) { this.chessableMisses = m; this.cdr.markForCheck(); } },
      error: () => {},
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.loadSub?.unsubscribe();
    this.s.clearTimers(); this.freq.dispose(); this.opponentSub?.unsubscribe();
    this.reviewEngine.dispose();
  }

  /** Die Trainingslinien gegen den Gegner holen (alle, nicht nur die ersten 50). <paramref name="wait"/>: bis dahin „Lädt";
   *  sonst läuft schon die Einstiegslinie, und die Reihung hängt die weiteren Linien an. */
  private loadOpponentOrder(wait: boolean): void {
    const url = trainingLinesUrl(this.opponent);
    if (!url || !this.http) { this.opponent = null; if (wait) this.buildQueue(); return; }
    if (wait) this.s.phase = 'LOADING';
    let params = new HttpParams().set('repertoire', this.repertoireId).set('take', TRAINING_LINES_ALL).set('positions', 'true')
      .set('chapterColors', JSON.stringify(Object.fromEntries(this.chapterColors)));
    if (this.opponentColor) params = params.set('color', this.opponentColor);
    for (const [k, v] of Object.entries(this.opponentParams)) params = params.set(k, v);
    this.opponentSub = this.http.get<TrainingLines>(url, { params }).subscribe({
      next: r => {
        this.opponentRank = new Map(r.lines.map((l, i) => [l.key, i]));
        this.opponentPositions = r.positions ?? null;
        this.opponentBand = r.lichessBand ?? null;
        if (r.color === 'w' || r.color === 'b') this.opponentColor = r.color;
        this.opponentNotice = 'repertoireTrainer.opponentOrder';
        if (wait) this.buildQueue(); else this.extendOpponentQueue();
      },
      error: () => {
        // ohne die Reihenfolge weiter — aber nicht still
        this.opponentRank = null;
        this.opponentNotice = 'repertoireTrainer.opponentFailed';
        if (wait) this.buildQueue();
      },
    });
  }

  /** Linien dieser Sitzung (Kapitel-Filter, Farbe je Kapitel). */
  private get lineSet(): TrainerLineSet {
    return { lines: this.allLines, chapter: this.chapterFilter, colorOf: l => this.colorOf(l) };
  }

  /** Die Linien der Gegner-Sitzung (Farbe, Reihung, Einstieg). */
  private opponentLines(): ParsedGame[] {
    return opponentLines(this.lineSet, {
      color: this.opponentColor, rank: this.opponentRank, start: this.opponentStart, isPaused: k => this.isPausedKey(k),
    });
  }

  /** Die Reihung kam, während die Einstiegslinie läuft: die weiteren Linien hinten anhängen, die laufende bleibt. */
  private extendOpponentQueue(): void {
    const queue = extendQueue(this.s.queue, this.s.qIndex, this.opponentLines());
    this.opponentTotal = queue.length;
    this.s.extend(queue);
  }

  /** Reihenfolge der Warteschlange: nach dem Gegner, sonst „Häufigste zuerst" (wenn an). */
  private ordered(lines: ParsedGame[]): ParsedGame[] {
    return this.opponentRank ? byRank(lines, this.opponentRank) : this.freq.order(lines, this.freqOrder);
  }

  /** „Häufigste zuerst" an/aus. Baut die Sitzung neu auf (wie ein Moduswechsel). */
  toggleFreqOrder(): void {
    if (this.offlineSession) return;
    this.freqOrder = !this.freqOrder;
    saveFreqOrder(this.freqOrder);
    if (this.freqOrder && !this.freq.lineFreq) this.loadFrequencies();
    else { this.freq.notice = null; this.buildQueue(); }
  }

  /** Häufigkeiten holen — Runde um Runde, bis alles ausgewertet ist; dann die Sitzung aufbauen. */
  private loadFrequencies(): void {
    this.s.clearTimers();
    this.s.phase = 'LOADING';
    this.freq.load(this.repertoireId, Object.fromEntries(this.chapterColors));
  }

  /** Abruf beendet ODER übersprungen: mit dem, was da ist, weiter. */
  finishFrequencies(): void { this.freq.finish(); }

  /** Häufigkeit der aktuellen Linie für die Anzeige, `null` = aus/unbekannt. */
  get currentLineFrequency(): number | null {
    if (!this.freqOrder || !this.freq.lineFreq) return null;
    const line = this.s.currentLine;
    const f = line ? this.freq.freqOf(line) : -1;
    return f >= 0 ? f : null;
  }

  formatFrequency(f: number): string { return formatPercent(f); }

  /** Effektive Trainingsfarbe einer Linie (aus ihrem Kapitel). */
  private colorOf(line: ParsedGame): TrainColor {
    return this.chapterColors.get((line.headers['Black'] || '').trim()) ?? 'w';
  }

  setMode(m: TrainerMode): void {
    if (m === this.mode) return;
    this.s.clearTimers();
    this.s.mode = m;
    this.singleLineKey = null;   // Moduswechsel hebt die Einzellinien-Beschränkung auf
    this.buildQueue();
  }

  /** Stabiler Linien-Schlüssel (identisch zur Linienliste) aus der SAN-Zugfolge. */
  lineKeyOf(line: ParsedGame): string { return lineKeyOf(line); }

  /** Die aktuelle Linie wurde noch nie trainiert — weder in RookHub (kein Durchlauf) noch auf Chessable (kein Stand im
   *  Kopf `[ChessableLastTrained]`/`[ChessableLevel]`, keine Chessable-Fehler zu ihrer oid). Anzeige „(neu)“ hinter dem
   *  Zähler (Wunsch 2026-11-07). Auf Chessable Trainiertes kommt über die Erweiterung auch als RookHub-Stand an. */
  get currentLineNew(): boolean {
    const line = this.s.currentLine;
    if (!line) return false;
    const st = this.sr.stateOf(lineKeyOf(line));
    if (st && (st.reps > 0 || st.level > 0 || !!st.lastReviewedAt)) return false;
    const h = line.headers;
    if ((h['ChessableLastTrained'] ?? '').trim() || (h['ChessableLevel'] ?? '').trim()) return false;
    const oid = chessableOid(line);
    if (oid && (this.chessableMisses?.[oid]?.length ?? 0) > 0) return false;
    return true;
  }

  /** Fällig = im Pool, nicht pausiert und DueAt ≤ jetzt. Noch nicht gelernte Linien (kein Zustand)
   * sind NICHT im Pool und werden nicht abgefragt. */
  private isDue(line: ParsedGame, now: number): boolean {
    return isStateDue(this.sr.stateOf(this.lineKeyOf(line)), now);
  }

  private isPausedKey(lineKey: string): boolean { return !!this.sr.stateOf(lineKey)?.paused; }

  /** Learn-Kandidat = noch NICHT im Pool und nicht pausiert. */
  private isLearnable(line: ParsedGame): boolean {
    return isStateLearnable(this.sr.stateOf(this.lineKeyOf(line)));
  }

  /** Baut die Session-Warteschlange: quiz = fällige Pool-Linien (gemischt), learn = ungelernte
   * Linien der Reihe nach. Chapter-/Einzellinien-Filter greifen in beiden Modi. */
  private buildQueue(): void {
    this.s.clearTimers();
    const now = Date.now();
    const usable = playableLines(this.lineSet, this.singleLineKey);
    const againstOpponent = !!this.opponent && !this.offlineSession;
    // Eine ausdrücklich gewählte (`?line=`) PAUSIERTE Linie wird trotzdem gezeigt — mit Hinweis „pausiert" (0.889.0).
    const explicitPaused = !!this.singleLineKey && usable.length === 1 && this.isPausedKey(this.singleLineKey);
    const queue = explicitPaused ? usable
      : againstOpponent
      ? this.opponentLines()                                       // der Reihe nach, unabhängig von Fälligkeit und Modus
      : this.mode === 'learn'
        ? this.ordered(usable.filter(l => this.isLearnable(l)))   // sonst PGN-Reihenfolge
        : this.ordered(shuffle(usable.filter(l => this.isDue(l, now))));
    this.opponentTotal = againstOpponent ? queue.length : 0;
    if (queue.length === 0) this.nextDueAt = this.mode === 'quiz' ? this.computeNextDue(usable) : null;
    this.s.start(queue);   // leer → EMPTY
  }

  /** Früheste künftige Fälligkeit einer Pool-Linie (für die EMPTY-Ansicht); null = nichts im Pool. */
  private computeNextDue(lines: ParsedGame[]): string | null {
    return earliestDueIso(lines.map(l => this.sr.stateOf(this.lineKeyOf(l))));
  }

  restart(): void { this.opponentStart = null; this.buildQueue(); }

  /** Zurück zur Spielerkarte, von der die Gegner-Sitzung kam (Spielervorbereitung oder LeagueHub). */
  backToCard(): void {
    history.back();
  }

  /** Alle (Chapter-gefilterten) übbaren Linien mit ≥1 eigenen Zug — deren stabile Schlüssel. */
  private usableLineKeys(): string[] {
    return playableLines(this.lineSet).map(l => this.lineKeyOf(l));
  }

  private reloadStatesAndRebuild(): void {
    this.training.getLineStates(this.repertoireId).subscribe({
      next: states => {
        this.sr.setStates(states);
        this.poolBusy = false;
        this.buildQueue();
      },
      // Der Pool wurde serverseitig schon geändert — nur die Anzeige ist veraltet: das sagen.
      error: () => { this.poolBusy = false; this.cdr.markForCheck(); this.warn('repertoireTrainer.reloadFailed'); },
    });
  }

  /** Nutzer-Aktion gescheitert → Snackbar statt Schweigen (src/frontend/CLAUDE.md „Fehlerbehandlung"). */
  private warn(key: string): void {
    this.snackbar.warn(this.translate.instant(key));
  }

  /** „Alle in den Pool aufnehmen" (Kurs bzw. gefiltertes Kapitel) → sofort fällig. */
  promoteAllToPool(): void {
    const keys = this.usableLineKeys();
    if (keys.length === 0) return;
    this.poolBusy = true; this.cdr.markForCheck();
    this.training.promote(this.repertoireId, keys).subscribe({
      next: () => this.reloadStatesAndRebuild(),
      error: () => { this.poolBusy = false; this.cdr.markForCheck(); this.warn('repertoireTrainer.poolFailed'); },
    });
  }

  /** „Alle jetzt fällig machen" (nur Pool-Linien; Kurs bzw. gefiltertes Kapitel). */
  makeAllDue(): void {
    const keys = this.chapterFilter ? this.usableLineKeys() : [];   // leer = ganzer Kurs
    this.poolBusy = true; this.cdr.markForCheck();
    this.training.makeDue(this.repertoireId, keys).subscribe({
      next: () => this.reloadStatesAndRebuild(),
      error: () => { this.poolBusy = false; this.cdr.markForCheck(); this.warn('repertoireTrainer.poolFailed'); },
    });
  }

  /** Intervall-Zyklen bearbeiten (global + pro-Repertoire-Override). */
  openConfig(): void {
    this.dialog.open(SrConfigDialogComponent, { data: { repertoireId: this.repertoireId }, width: '420px' });
  }

  /** Alle SM-2-Zustände dieses Repertoires löschen (Bestätigungs-Dialog vorher). */
  resetProgress(): void {
    this.confirm.ask('repertoireTrainer.resetConfirm').subscribe(ok => {
      if (!ok) return;
      this.resetting = true;
      this.cdr.markForCheck();   // OnPush: die Antwort des Dialogs markiert diese Ansicht nicht
      this.training.reset(this.repertoireId).subscribe({
        next: () => {
          this.sr.clear();
          this.resetting = false;
          this.buildQueue();
        },
        // OnPush: ohne markForCheck bliebe der Knopf grau stehen — und ohne Meldung hielte man den
        // Reset für erfolgt.
        error: () => { this.resetting = false; this.cdr.markForCheck(); this.warn('repertoireTrainer.resetFailed'); },
      });
    });
  }

  // ===== Bedienung (an die Session) =====
  onMove(ev: BoardMove): void { this.s.onMove(ev); }
  continueLine(): void { this.s.continueLine(); }
  continueFromComment(): void { this.s.continueFromComment(); }
  showSolution(): void { this.s.showSolution(); }
  mouseslip(): void { this.s.mouseslip(); }
  continueAfterWrong(): void { this.s.continueAfterWrong(); }
  retryWrong(): void { this.s.retryWrong(); }
  /** 💡 Tipp (Taste „h"): Figur → Pfeil → Zug ausspielen. */
  hint(): void { this.s.hint(); }
  /** Linie überspringen (Taste „s"): ohne SR-Bewertung zur nächsten (ein schon gemachter Fehler zählt). */
  skipLine(): void { this.s.skipLine(); }
  /** Anzeige zur Stellung aus Sicht seiner Partien (Gegner-Modus, 0.892.0); `null` = nichts zu sagen. */
  get opponentNote(): OpponentNoteView | null {
    const line = this.s.currentLine;
    if (!line || !this.opponentPositions) return null;
    const ply = this.s.phase === 'LINE_DONE' ? this.s.reviewPly : this.s.currentPly;
    return opponentNoteView(line.fens, line.moves.map(m => m.san), ply, this.opponentPositions, {
      who: this.opponentName, band: this.opponentBand, lang: cardLang(this.translate),
      t: (k, p) => this.translate.instant(k, p),
    });
  }

  /** Die laufende Linie ist pausiert (nur bei ausdrücklicher Wahl über `?line=` bzw. als Gegner-Einstieg) → Hinweis. */
  get currentLinePaused(): boolean {
    const line = this.s.currentLine;
    return !!line && this.isPausedKey(this.lineKeyOf(line));
  }

  /** „Linie pausieren" geht, wo „Überspringen" geht — außer die Linie ist schon pausiert. */
  get canPause(): boolean { return this.s.canSkip && !this.currentLinePaused; }

  /**
   * Linie pausieren (Taste „p", 0.889.0): am Server pausieren (offline über die Offline-Queue, wie die Bewertungen), aus der
   * Warteschlange nehmen und zur nächsten — keine SR-Bewertung, ein schon gemachter Fehler zählt. Fortsetzen in der Linienliste;
   * die Snackbar bietet „Rückgängig".
   */
  pauseLine(): void {
    if (!this.canPause) return;
    const line = this.s.pauseLine();
    if (!line) return;
    const key = this.lineKeyOf(line);
    this.sr.setPaused(key, true);
    if (this.opponentTotal > 0) this.opponentTotal--;
    this.snackbar.show(this.translate.instant('repertoireTrainer.linePaused'), { action: 'common.undo', duration: 6000 })
      .onAction().subscribe(() => this.undoPause(line));
  }

  /** „Rückgängig": Pause sofort aufheben und die Linie an der aktuellen Stelle wieder einreihen. */
  private undoPause(line: ParsedGame): void {
    if (this.destroyed) return;
    this.sr.setPaused(this.lineKeyOf(line), false);
    if (this.opponent && !this.offlineSession) this.opponentTotal++;
    this.s.reinsertLine(line);
  }

  /** Klick/Tippen aufs Brett = „Weiter" (Linie fertig, Kommentar, Vorzeigen mit Kommentar, Feedback überspringen). */
  onPlayClick(): void {
    // Linie fertig: Klick ins Brett geht nur dann zur nächsten Linie, wenn nicht analysiert wird (Wunsch 2026-11-07) —
    // also weder die Engine zugeschaltet noch in der Zugliste von der Schlussstellung weggeblättert. Sonst bleibt der
    // Klick beim Brett; weiter geht es mit dem Knopf, Leertaste oder Enter.
    if (this.s.phase === 'LINE_DONE' && this.analysingLineEnd) return;
    this.s.tap();
  }

  /** Am Linienende wird analysiert: Engine an (und verfügbar) oder von der Schlussstellung weggeblättert. */
  get analysingLineEnd(): boolean {
    return this.s.reviewing || (this.reviewEngineOn && this.reviewEngineAvailable);
  }

  /** Leertaste/Enter = „Weiter" (wie Klick/Tippen); „h" = Tipp, „s" = Linie überspringen, „p" = Linie pausieren,
   *  „k" = Kommentare zeigen an/aus;
   *  im „Linie fertig"-Kasten ←/→/Pos1/Ende = durch die Linie klicken. */
  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    if (this.s.canReview && (key === 'ArrowLeft' || key === 'ArrowRight' || key === 'Home' || key === 'End')) {
      if (e.ctrlKey || e.metaKey || e.altKey || !isBoardHotkey(e)) return;
      if (key === 'ArrowLeft') this.s.reviewStep(-1);
      else if (key === 'ArrowRight') this.s.reviewStep(1);
      else this.s.reviewTo(key === 'Home' ? 0 : this.s.currentPly);
      e.preventDefault();
      return;
    }
    if (key !== ' ' && key !== 'Spacebar' && key !== 'Enter' && key !== 'h' && key !== 's' && key !== 'p' && key !== 'k') return;
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    if (!isBoardHotkey(e)) return;
    if (key === 'h') { if (this.s.canHint) { this.s.hint(); e.preventDefault(); } return; }
    if (key === 's') { if (this.s.canSkip) { this.s.skipLine(); e.preventDefault(); } return; }
    if (key === 'p') { if (this.canPause) { this.pauseLine(); e.preventDefault(); } return; }
    if (key === 'k') { if (this.canToggleComments) { this.toggleShowComments(); e.preventDefault(); } return; }
    if (this.s.tap()) e.preventDefault();
  }
}

/** `inject(…, { optional: true })`, außerhalb eines Injektors (Specs mit `new`) `null`. */
function injectOptional<T>(token: new (...args: never[]) => T): T | null {
  try { return inject(token, { optional: true }); } catch { return null; }
}
