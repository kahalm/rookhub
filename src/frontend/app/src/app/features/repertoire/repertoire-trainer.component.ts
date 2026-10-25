import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, HostListener, Optional } from '@angular/core';
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
import { Subscription, forkJoin } from 'rxjs';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';

import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { applyUci, tryFreeMove, tryLoadFen } from '../puzzles/puzzle-move.util';
import { StockfishService } from '../puzzles/stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { RepertoireTrainingService, LineStateDto, LineReviewRequest, SrLevel } from './repertoire-training.service';
import { buildRepertoireGraph, normFen, RepertoireGraph } from './repertoire-tree.util';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { chapterColorsOf, TrainColor } from './repertoire-color.util';
import { SrConfigDialogComponent } from './sr-config-dialog.component';
import { ParsedGame, exceedsSyncLimits, parsePgnTextWithSource } from '../../shared/pgn-viewer/pgn-parser';
import { parsePgnTextParallel } from '../../shared/pgn-viewer/pgn-parse-pool';
import { isInfoLineGame } from './repertoire-info-line.util';
import { isStateDue, isStateLearnable, earliestDueIso, relDueLabel, shuffle, applySrReview, applyPromote, DEFAULT_SR_LEVELS } from './repertoire-sr.util';
import { getRepertoireOffline, refreshRepertoireOffline, updateRepertoireOfflineStates } from './repertoire-offline.util';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { EvalCompare } from './repertoire-trainer-eval-compare';
import { ExpectedMove, judgeMove, resolveExpectedUci } from '../../shared/chess/line-solver';
import { ExplorerAnalysisResult, RepertoireExplorerService, formatPercent } from './repertoire-explorer.service';
import { TrainerFrequencyLoader } from './repertoire-trainer-frequency-loader';
import { destsAt, linesInChapter, userMoveCount } from './repertoire-trainer.util';
import { LineMoveToken, commentParagraphs, lineChapterTitle, lineLabel, lineMoveTokens, plyComment, plyPrettyLabel } from './repertoire-trainer-line-view.util';
import { localStore, readRaw, writeRaw } from '../../core/local-json-store';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { TRAINER_PASS_THROUGH, TRAINING_LINES_ALL, TrainingLines, trainingLinesUrl } from '../../shared/player-card/training-lines';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { whiteToMove } from '../../shared/chess/fen-side';

/** „Häufigste zuerst" merkt sich das Gerät — wie die übrigen Anzeige-Vorlieben des Trainers. */
const FREQ_ORDER_KEY = 'rookhub_rep_train_freq_order';

function readFreqOrder(): boolean {
  return readRaw(localStore(), FREQ_ORDER_KEY) === '1';
}

function saveFreqOrder(on: boolean): void {
  writeRaw(localStore(), FREQ_ORDER_KEY, on ? '1' : '0');   // gesperrt/voll → nur diese Sitzung
}

type Phase = 'LOADING' | 'EMPTY' | 'PLAYING' | 'FEEDBACK' | 'DONE' | 'LINE_DONE' | 'LEARN_SHOW' | 'COMMENT';
type Outcome = 'correct' | 'tolerated' | 'wrong';
type Mode = 'quiz' | 'learn';

// Wie lange das „Correct/Tolerated"-Feedback nach einem Zug stehen bleibt, bevor automatisch
// weitergerückt wird (der User kann per Klick/Leertaste/Enter überspringen). correct: 1,5 s → 600 →
// 300 ms (der grüne Haken blitzt kurz, dann kommt zügig der Gegnerzug). Geduldet bleibt länger
// stehen (der Alternativzug soll wirken).
const ADVANCE_MS: Record<Outcome, number> = { correct: 300, tolerated: 1500, wrong: 0 };
const OPP_MOVE_DELAY_MS = 250;   // kurze Pause vor jedem automatischen Gegnerzug
const LEARN_SHOW_MS = 1000;      // Zug im Learn-Modus ohne Kommentar so lange zeigen, dann zurücknehmen
const LEARN_GAP_MS = 400;        // Learn: Pause zwischen Gegnerzug und dem Zeigen des eigenen Zugs (fühlte sich beim Wiederholen zu lang an, halbiert von 800)
// Learn-Modus: wie oft eine Linie durchgespielt werden muss, bevor sie in den Übungspool wandert
// (1× geführt lernen + 2× „durchklicken").
const LEARN_REPEATS = 3;

/**
 * Line-basiertes Repertoire-Training: eine ganze PGN-Linie wird vom Startzug an durchgespielt,
 * Gegnerzüge automatisch, an jedem eigenen Zug pausiert der Trainer und wertet den User-Zug gegen
 * die Linie (erwarteter Zug + [%alt]-tolerierte Alternativen). Nach dem letzten Zug: nächste Linie.
 * Ein `?chapter=Name`-Query beschränkt die Sitzung auf ein Kapitel (Black-Header). SM-2-Reviews
 * werden je Position (normFen als Card-Key) weiterhin ans Backend gesendet.
 *
 * <p><b>Das Urteil „ist das der erwartete Zug?" kommt aus shared/chess/line-solver</b> — denselben
 * reinen Funktionen wie im Puzzle-Löser, nicht der Klasse `LineSolver`: dieser Trainer führt sein
 * Brett (`this.chess`) selbst und WEIST es neu zu (jede Linie, `showSolution`, der Offline-Pfad).
 * Verglichen werden damit FELDER statt Zug-TEXT; `normSan` braucht es hier nicht mehr. Bei der
 * ANZEIGE (`expectedDisplay`, `movesInLine`, `currentMovePrettyLabel`) und beim Eval-Vergleich
 * bleibt es bei der SAN — dort ist sie Text für Menschen, kein Urteil.</p>
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
  phase: Phase = 'LOADING';
  /** Trainingsfarbe der AKTUELL laufenden Linie (Brett-Orientierung / „X am Zug" / Eval-Vorzeichen).
   *  Wird pro Linie aus `chapterColors` gesetzt — es gibt keinen globalen Farb-Umschalter mehr, weil
   *  ein Repertoire Kapitel beider Farben mischen kann. Siehe repertoire-color.util. */
  color: TrainColor = 'w';
  /** Effektive Trainingsfarbe je Kapitel (Auto-Erkennung + manuelle Overrides). */
  private chapterColors = new Map<string, TrainColor>();
  /** Kapitel-Filter aus ?chapter=…. Null = alle Kapitel. */
  chapterFilter: string | null = null;
  /** 'quiz' = fällige Pool-Linien abfragen; 'learn' = neue Linien durchspielen → in Pool (?mode=learn). */
  mode: Mode = 'quiz';
  /** Optional nur EINE Linie (?line=<lineKey>) — für „Diese Linie lernen/üben". */
  private singleLineKey: string | null = null;
  /** Learn-Modus: Kommentar des gerade gezeigten Zugs (hält die Anzeige, bis der User weitertippt). */
  learnComment = '';
  /** COMMENT-Phase: Kommentar eines gerade gespielten Gegnerzugs, hält bis der User „Weiter" bestätigt. */
  holdComment = '';
  private learnTimer: ReturnType<typeof setTimeout> | null = null;
  /** Learn-Modus: wie oft die AKTUELLE Linie schon komplett durchgespielt wurde (0-basiert bis
   *  <see cref="LEARN_REPEATS"/>); erst danach wandert sie in den Übungspool. */
  private learnPass = 0;
  /** LINE_DONE: true = beim „Weiter" wird DIESELBE Linie erneut gestartet (Learn-Wiederholung),
   *  false = es geht zur nächsten Linie. Steuert Button-Label + Verhalten. */
  pendingRepeat = false;

  fen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  dests = new Map<Key, Key[]>();
  lastMove?: [Key, Key];
  resetting = false;
  poolBusy = false;

  // Session state
  private allLines: ParsedGame[] = [];
  queue: ParsedGame[] = [];
  qIndex = 0;
  private chess = new Chess();
  private currentPly = 0;
  correct = 0;
  wrong = 0;
  currentStreak = 0;   // fortlaufende ✓/geduldet-Zug-Serie; echter Fehler setzt zurück (Mausrutscher nicht).
  bestStreak = 0;      // Session-Rekord.
  sessionUserMoves = 0;   // Zähler für Progress-Balken (grob geschätzt)

  outcome: Outcome = 'correct';
  expectedDisplay = '';
  wrongRevealed = false;
  /** Bewertungsvergleich nach einem Fehlzug (Stockfish, Spielerzug ↔ Repertoirezug). */
  private readonly evalCmp = new EvalCompare(
    { getEval: (fen, depth) => this.stockfish.getEval(fen, depth) }, () => this.cdr.markForCheck());
  get evalLoading(): boolean { return this.evalCmp.loading; }
  get evalDeltaPawns(): number | null { return this.evalCmp.deltaPawns; }
  get evalMateNote(): 'missed' | 'allowed' | null { return this.evalCmp.mateNote; }

  private statesByKey = new Map<string, LineStateDto>();   // key = lineKey
  /** Ergebnis der AKTUELLEN Linie: true, sobald ein NICHT als Mausrutscher verziehener Fehler
   * passiert ist (geduldet zählt neutral). */
  private lineHadWrong = false;
  /** Ein offener Fehler an der aktuellen Stellung, der noch als Mausrutscher verziehen werden kann
   * (zählt erst als echter Fehler, wenn der User ohne „Mausrutscher" weitermacht/„Lösung zeigen"). */
  private pendingWrong = false;
  /** Für die EMPTY-Ansicht: wann die nächste Pool-Linie fällig wird (ISO) — null = nichts im Pool. */
  nextDueAt: string | null = null;
  /** LINE_DONE (Abfragen): wann die gerade beendete Linie erneut fällig wird (ISO, aus der
   *  SR-Bewertung) — null solange die Antwort aussteht bzw. im Lern-Modus. */
  nextRepeatAt: string | null = null;
  /** true, während der Gegnerzug automatisch gespielt wird — erlaubt in dieser Phase einen
   *  Premove (chessground). */
  oppMoving = false;
  private graph: RepertoireGraph | null = null;
  private advanceTimer: ReturnType<typeof setTimeout> | null = null;
  private oppTimer: ReturnType<typeof setTimeout> | null = null;
  private startFen = '';
  /** true = Session läuft aus der Offline-Kopie (Server unerreichbar); Bewertungen werden lokal
   *  berechnet und via Offline-Queue nachgereicht, Pool-/Config-Verwaltung ist ausgeblendet. */
  offlineSession = false;
  /** Name des Repertoires für den Kopf (d3-rk-trainer-leer-zwei-aktionen); null = noch nicht da → „Repertoire-Trainer“. */
  repertoireName: string | null = null;
  /** Effektive SR-Intervalle aus der Offline-Kopie (nur fürs lokale Fälligkeits-Rechnen). */
  private srLevels: SrLevel[] | null = null;

  /** Modus „Häufigste zuerst": Linien, die man in echten Partien öfter erreicht, kommen zuerst
   *  (Lichess-Explorer, gerechnet am Server wie der Lochfinder). Aus = bisherige Reihenfolge. */
  freqOrder = readFreqOrder();
  /** Häufigkeiten über den Explorer (Fortschritt, Hinweis, Sortierung). */
  private readonly freq = new TrainerFrequencyLoader(() => this.explorer, () => this.cdr.markForCheck(), () => this.buildQueue());
  get freqLoading(): ExplorerAnalysisResult | null { return this.freq.progress; }
  get freqRunning(): boolean { return this.freq.running; }
  /** Hinweis unter der Leiste, wenn die Häufigkeiten fehlen oder unvollständig sind (i18n-Key). */
  get freqNotice(): string | null { return this.freq.notice; }

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
  private opponentSub: Subscription | null = null;
  /** Erstes Laden (PGN + Zustände); wird beim Verlassen abbestellt (Codereview F3-025). */
  private loadSub: Subscription | null = null;
  /** Abbruch-Token: nach ngOnDestroy baut keine verspätete Antwort mehr eine Sitzung (und startet keinen Explorer-Lauf). */
  private destroyed = false;

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

  ngOnInit(): void {
    this.repertoireId = Number(this.route.snapshot.paramMap.get('id')) || 0;
    this.chapterFilter = this.route.snapshot.queryParamMap.get('chapter');
    this.mode = this.route.snapshot.queryParamMap.get('mode') === 'learn' ? 'learn' : 'quiz';
    this.singleLineKey = this.route.snapshot.queryParamMap.get('line');
    this.opponent = trainingLinesUrl(this.route.snapshot.queryParamMap.get('opponent')) ? this.route.snapshot.queryParamMap.get('opponent') : null;
    const oc = this.route.snapshot.queryParamMap.get('color');
    this.opponentColor = oc === 'w' || oc === 'b' ? oc : null;
    this.stockfish.init().catch(() => {});
    // Name nur fürs Kopf-Label: ein Fehler ändert nichts (Titel bleibt „Repertoire-Trainer“). Getrennt vom forkJoin,
    // damit ein Ausfall hier nicht in die Offline-Kopie fällt. `?.`: Test-Doubles ohne die Methode.
    this.training.getRepertoireName?.(this.repertoireId)?.subscribe({
      next: name => { if (name && !this.repertoireName) { this.repertoireName = name; this.cdr.markForCheck(); } },
      error: () => {},
    });

    this.loadSub = forkJoin({
      pgn: this.training.getPgn(this.repertoireId),
      states: this.training.getLineStates(this.repertoireId),
    }).subscribe({
      next: ({ pgn, states }) => {
        // Eine vorhandene Offline-Kopie beim Online-Öffnen aktuell halten (No-op ohne Kopie).
        refreshRepertoireOffline(this.repertoireId, pgn, states);
        void this.initSession(pgn, states);
      },
      error: () => {
        // Server unerreichbar → heruntergeladene Kopie verwenden (Offline-Training); ohne Kopie
        // bleibt es bei der leeren Ansicht.
        const cached = getRepertoireOffline(this.repertoireId);
        if (cached) {
          this.offlineSession = true;
          this.repertoireName ??= cached.meta?.name || null;
          this.srLevels = cached.config;
          void this.initSession(cached.pgn, cached.states);
        } else {
          this.phase = 'EMPTY';
          this.cdr.markForCheck();
        }
      },
    });
  }

  /** Session aus PGN + SR-Zuständen aufbauen (frisch vom Server oder aus der Offline-Kopie). */
  private async initSession(pgn: string, states: LineStateDto[]): Promise<void> {
    this.graph = buildRepertoireGraph(pgn);
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
    this.statesByKey = new Map(states.map(s => [s.lineKey, s]));
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

  ngOnDestroy(): void {
    this.destroyed = true;
    this.loadSub?.unsubscribe();
    this.clearLineTimers(); this.freq.dispose(); this.opponentSub?.unsubscribe();
  }

  /** Die Trainingslinien gegen den Gegner holen (alle, nicht nur die ersten 50). <paramref name="wait"/>: bis dahin „Lädt";
   *  sonst läuft schon die Einstiegslinie, und die Reihung hängt die weiteren Linien an. */
  private loadOpponentOrder(wait: boolean): void {
    const url = trainingLinesUrl(this.opponent);
    if (!url || !this.http) { this.opponent = null; if (wait) this.buildQueue(); return; }
    if (wait) this.phase = 'LOADING';
    let params = new HttpParams().set('repertoire', this.repertoireId).set('take', TRAINING_LINES_ALL)
      .set('chapterColors', JSON.stringify(Object.fromEntries(this.chapterColors)));
    if (this.opponentColor) params = params.set('color', this.opponentColor);
    for (const k of TRAINER_PASS_THROUGH) {
      const v = this.route.snapshot.queryParamMap.get(k);
      if (v) params = params.set(k, v);
    }
    this.opponentSub = this.http.get<TrainingLines>(url, { params }).subscribe({
      next: r => {
        this.opponentRank = new Map(r.lines.map((l, i) => [l.key, i]));
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

  /** In der Reihenfolge des Gegners; Linien, die er nicht kennt (Schlüssel nicht in der Antwort), dahinter wie gehabt. */
  private byOpponent(lines: ParsedGame[]): ParsedGame[] {
    const rank = this.opponentRank!;
    const at = (l: ParsedGame) => rank.get(this.lineKeyOf(l)) ?? Number.MAX_SAFE_INTEGER;
    return [...lines].sort((a, b) => at(a) - at(b));
  }

  /** Die Linien der Gegner-Sitzung: alle der Farbe (Kapitel-Filter gilt), in seiner Reihenfolge — OHNE Fälligkeit und ohne
   *  Modus-Wechsel; mit Einstieg nur die Einstiegslinie und die nach ihr. */
  private opponentLines(): ParsedGame[] {
    let lines = linesInChapter(this.allLines, this.chapterFilter).filter(l => this.hasUserMove(l));
    if (this.opponentColor) lines = lines.filter(l => this.colorOf(l) === this.opponentColor);
    const start = this.opponentStart;
    if (!this.opponentRank) return start ? lines.filter(l => this.lineKeyOf(l) === start) : lines;
    const ordered = this.byOpponent(lines);
    if (!start) return ordered;
    const at = ordered.findIndex(l => this.lineKeyOf(l) === start);
    return at < 0 ? ordered : ordered.slice(at);
  }

  /** Die Reihung kam, während die Einstiegslinie läuft: die weiteren Linien hinten anhängen, die laufende bleibt. */
  private extendOpponentQueue(): void {
    const current = this.queue[this.qIndex];
    const lines = this.opponentLines();
    const at = current ? lines.findIndex(l => l === current) : -1;
    this.queue = [...this.queue.slice(0, this.qIndex + 1), ...(at < 0 ? lines.filter(l => l !== current) : lines.slice(at + 1))];
    this.opponentTotal = this.queue.length;
    this.sessionUserMoves = this.queue.reduce((sum, l) => sum + this.countUserMoves(l), 0);
    this.cdr.markForCheck();
  }

  /** Reihenfolge der Warteschlange: nach dem Gegner, sonst „Häufigste zuerst" (wenn an). */
  private ordered(lines: ParsedGame[]): ParsedGame[] {
    return this.opponentRank ? this.byOpponent(lines) : this.freq.order(lines, this.freqOrder);
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
    this.clearLineTimers();
    this.phase = 'LOADING';
    this.freq.load(this.repertoireId, Object.fromEntries(this.chapterColors));
  }

  /** Abruf beendet ODER übersprungen: mit dem, was da ist, weiter. */
  finishFrequencies(): void { this.freq.finish(); }

  /** Häufigkeit der aktuellen Linie für die Anzeige, `null` = aus/unbekannt. */
  get currentLineFrequency(): number | null {
    if (!this.freqOrder || !this.freq.lineFreq) return null;
    const line = this.queue[this.qIndex];
    const f = line ? this.freq.freqOf(line) : -1;
    return f >= 0 ? f : null;
  }

  formatFrequency(f: number): string { return formatPercent(f); }

  /** Effektive Trainingsfarbe einer Linie (aus ihrem Kapitel). */
  private colorOf(line: ParsedGame): TrainColor {
    return this.chapterColors.get((line.headers['Black'] || '').trim()) ?? 'w';
  }

  setMode(m: Mode): void {
    if (m === this.mode) return;
    this.clearLineTimers();
    this.mode = m;
    this.singleLineKey = null;   // Moduswechsel hebt die Einzellinien-Beschränkung auf
    this.buildQueue();
  }

  /** Stabiler Linien-Schlüssel (identisch zur Linienliste) aus der SAN-Zugfolge. */
  lineKeyOf(line: ParsedGame): string {
    return lineKeyFromSans(line.moves.map(m => m.san));
  }

  /** Fällig = im Pool, nicht pausiert und DueAt ≤ jetzt. Noch nicht gelernte Linien (kein Zustand)
   * sind NICHT im Pool und werden nicht abgefragt. */
  private isDue(line: ParsedGame, now: number): boolean {
    return isStateDue(this.statesByKey.get(this.lineKeyOf(line)), now);
  }

  /** Learn-Kandidat = noch NICHT im Pool und nicht pausiert. */
  private isLearnable(line: ParsedGame): boolean {
    return isStateLearnable(this.statesByKey.get(this.lineKeyOf(line)));
  }

  /** Baut die Session-Warteschlange: quiz = fällige Pool-Linien (gemischt), learn = ungelernte
   * Linien der Reihe nach. Chapter-/Einzellinien-Filter greifen in beiden Modi. */
  private buildQueue(): void {
    this.clearLineTimers();
    const now = Date.now();
    let filtered = linesInChapter(this.allLines, this.chapterFilter);
    if (this.singleLineKey) filtered = filtered.filter(l => this.lineKeyOf(l) === this.singleLineKey);
    const usable = filtered.filter(l => this.hasUserMove(l));
    const againstOpponent = !!this.opponent && !this.offlineSession;
    this.queue = againstOpponent
      ? this.opponentLines()                                       // der Reihe nach, unabhängig von Fälligkeit und Modus
      : this.mode === 'learn'
        ? this.ordered(usable.filter(l => this.isLearnable(l)))   // sonst PGN-Reihenfolge
        : this.ordered(shuffle(usable.filter(l => this.isDue(l, now))));
    this.opponentTotal = againstOpponent ? this.queue.length : 0;
    this.qIndex = 0;
    this.learnPass = 0;
    this.correct = 0;
    this.wrong = 0;
    this.currentStreak = 0;
    this.bestStreak = 0;
    this.sessionUserMoves = this.queue.reduce((sum, l) => sum + this.countUserMoves(l), 0);
    if (this.queue.length === 0) {
      this.nextDueAt = this.mode === 'quiz' ? this.computeNextDue(usable) : null;
      this.phase = 'EMPTY';
      this.cdr.markForCheck();
      return;
    }
    this.startCurrentLine();
  }

  /** Früheste künftige Fälligkeit einer Pool-Linie (für die EMPTY-Ansicht); null = nichts im Pool. */
  private computeNextDue(lines: ParsedGame[]): string | null {
    return earliestDueIso(lines.map(l => this.statesByKey.get(this.lineKeyOf(l))));
  }

  restart(): void { this.opponentStart = null; this.buildQueue(); }

  /** Zurück zur Spielerkarte, von der die Gegner-Sitzung kam (Spielervorbereitung oder LeagueHub). */
  backToCard(): void {
    history.back();
  }

  /** Alle (Chapter-gefilterten) übbaren Linien mit ≥1 eigenen Zug — deren stabile Schlüssel. */
  private usableLineKeys(): string[] {
    return linesInChapter(this.allLines, this.chapterFilter)
      .filter(l => this.hasUserMove(l)).map(l => this.lineKeyOf(l));
  }

  private reloadStatesAndRebuild(): void {
    this.training.getLineStates(this.repertoireId).subscribe({
      next: states => {
        this.statesByKey = new Map(states.map(s => [s.lineKey, s]));
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
          this.statesByKey.clear();
          this.resetting = false;
          this.buildQueue();
        },
        // OnPush: ohne markForCheck bliebe der Knopf grau stehen — und ohne Meldung hielte man den
        // Reset für erfolgt.
        error: () => { this.resetting = false; this.cdr.markForCheck(); this.warn('repertoireTrainer.resetFailed'); },
      });
    });
  }

  /** Hat die Linie mindestens einen Zug der Kapitelfarbe? Sonst gibt es an ihr nichts zu üben. */
  private hasUserMove(line: ParsedGame): boolean {
    return userMoveCount(line, this.colorOf(line)) > 0;
  }

  private countUserMoves(line: ParsedGame): number {
    return userMoveCount(line, this.colorOf(line));
  }

  private startCurrentLine(): void {
    const line = this.queue[this.qIndex];
    if (!line) { this.phase = 'DONE'; this.cdr.markForCheck(); return; }
    // Timer der VORHERIGEN Linie unbedingt wegräumen — sonst kann ein spät feuernder
    // advance/opp/learn-Timer die neue Linie desynchronisieren (currentPly-Overshoot).
    this.clearAdvance();
    this.clearOppTimer();
    this.clearLearn();
    // Brett-Orientierung / „am Zug"-Text / Eval-Vorzeichen richten sich nach der Farbe DIESER Linie.
    this.color = this.colorOf(line);
    this.chess = new Chess(line.fens[0]);
    this.currentPly = 0;
    this.lineHadWrong = false;
    this.pendingWrong = false;
    this.oppMoving = false;
    this.nextRepeatAt = null;
    this.holdComment = '';
    this.fen = this.chess.fen();
    this.lastMove = undefined;
    this.advanceToUserMove();
  }

  /** Spielt Gegnerzüge nacheinander, bis der User dran ist — oder die Linie zu Ende ist. */
  private advanceToUserMove(): void {
    const line = this.queue[this.qIndex];
    if (!line) { this.phase = 'DONE'; this.cdr.markForCheck(); return; }
    if (this.currentPly >= line.moves.length) { this.finishLine(); return; }

    const nextSide = this.chess.turn();
    if (nextSide === this.color) {
      // User ist am Zug.
      this.oppMoving = false;
      this.startFen = this.fen;
      if (this.mode === 'learn') {
        // Nur der ERSTE Durchlauf (learnPass 0) zeigt den erwarteten Zug vor (geführtes Lernen);
        // die Wiederholungs-Durchläufe (Pass 2/3) verlangen ihn aus dem Gedächtnis. Ein falscher
        // Zug blendet ihn dann via onLearnMove → enterLearnShow als Erinnerung ein.
        if (this.learnPass === 0) { this.enterLearnShow(); }
        else { this.enterLearnPlay(); }
        return;
      }
      this.dests = destsAt(this.fen);
      this.phase = 'PLAYING';
      this.cdr.markForCheck();
      return;
    }

    // Gegnerzug automatisch spielen (mit kleiner Verzögerung, damit man ihn wahrnimmt).
    // oppMoving=true erlaubt dem User in diesem Fenster einen Premove.
    this.oppMoving = true;
    this.phase = 'PLAYING';   // Zwischenzustand: view-only, kein Feedback-Kasten
    this.dests = new Map();
    this.cdr.markForCheck();
    this.oppTimer = setTimeout(() => {
      this.oppTimer = null;
      const m = line.moves[this.currentPly];
      try {
        const mv = this.chess.move(m.san);
        if (!mv) { this.finishLine(); return; }
        this.fen = this.chess.fen();
        this.lastMove = [mv.from as Key, mv.to as Key];
      } catch { this.finishLine(); return; }
      this.currentPly++;
      this.cdr.markForCheck();
      // Learn: hat der gerade gespielte Gegnerzug einen Kommentar, halten wir an und zeigen ihn
      // dauerhaft (bis der User „Weiter" bestätigt) — sonst würde er nur für die kurze
      // LEARN_GAP-Spanne aufblitzen. Ohne Kommentar nur die kurze Pause bis zum nächsten Zug.
      if (this.mode === 'learn') {
        const playedComment = (line.comments?.[this.currentPly - 1] || '').trim();
        if (playedComment) { this.enterCommentHold(playedComment); return; }
        this.oppTimer = setTimeout(() => { this.oppTimer = null; this.advanceToUserMove(); }, LEARN_GAP_MS);
      } else {
        this.advanceToUserMove();
      }
    }, OPP_MOVE_DELAY_MS);
  }

  private finishLine(): void {
    const line = this.queue[this.qIndex];
    this.oppMoving = false;
    this.nextRepeatAt = null;

    // Learn-Modus: eine Linie muss LEARN_REPEATS-mal durchgespielt werden (1× geführt + 2× „durch-
    // klicken"), bevor sie in den Pool wandert.
    if (this.mode === 'learn' && line) {
      this.learnPass++;
      if (this.learnPass < LEARN_REPEATS) {
        // Noch nicht oft genug → beim „Weiter" dieselbe Linie erneut (noch nicht in den Pool).
        this.pendingRepeat = true;
        this.phase = 'LINE_DONE';
        this.cdr.markForCheck();
        return;
      }
      // Oft genug gelernt → in den Pool aufnehmen (sofort fällig für die 1. Abfrage). Den Zustand
      // IMMER auch lokal nachziehen (Moduswechsel zu „Abfragen" sieht die Linie sofort als fällig);
      // offline wird der Request via Offline-Queue nachgereicht.
      const key = this.lineKeyOf(line);
      this.statesByKey.set(key, applyPromote(this.statesByKey.get(key), key, Date.now()));
      this.persistOfflineStates();
      const promoteUrl = `/api/repertoires/${this.repertoireId}/training/promote`;
      const promoteBody = { lineKeys: [key] };
      if (this.isNetOffline()) {
        this.offlineQueue.enqueue('POST', promoteUrl, promoteBody);
      } else {
        this.training.promote(this.repertoireId, [key])
          .subscribe({ next: () => {}, error: () => this.offlineQueue.enqueue('POST', promoteUrl, promoteBody) });
      }
    } else if (line) {
      // Quiz: SR-Bewertung PRO LINIE: fehlerfrei → +1 Stufe, sonst zurück auf Stufe 1.
      // Erst in den „Linie fertig"-Zustand wechseln, DANN bewerten — die Offline-Bewertung (und
      // eine synchron eintreffende Antwort) darf nextRepeatAt sofort in den offenen Kasten schreiben.
      this.pendingRepeat = false;
      this.phase = 'LINE_DONE';
      const label = (line.headers['White'] || '').trim().slice(0, 120);
      const req: LineReviewRequest = { lineKey: this.lineKeyOf(line), label, correct: !this.lineHadWrong };
      if (this.isNetOffline()) {
        this.applyLocalReview(req);
      } else {
        this.training.reviewLine(this.repertoireId, req)
          .subscribe({
            next: st => {
              this.statesByKey.set(st.lineKey, st);
              this.persistOfflineStates();
              // Nächste Fälligkeit im „Linie fertig"-Kasten anzeigen (nur solange diese Linie noch offen ist).
              if (this.phase === 'LINE_DONE') { this.nextRepeatAt = st.dueAt; this.cdr.markForCheck(); }
            },
            error: () => this.applyLocalReview(req),
          });
      }
    }
    // Nicht mehr automatisch weiter — der User rückt per „Weiter"-Knopf (bzw. Klick/Leertaste) vor.
    // Timer werden BEWUSST hier NICHT geräumt: `startCurrentLine` cleart sie beim Übergang zur
    // nächsten Linie. In manchen Test-/Race-Pfaden hilft ein spät feuernder Timer, phase aus
    // einem Fehl-Zustand zurück zu PLAYING zu setzen — den ziehen wir nicht vorzeitig weg.
    this.pendingRepeat = false;
    this.phase = 'LINE_DONE';
    this.cdr.markForCheck();
  }

  private isNetOffline(): boolean {
    return typeof navigator !== 'undefined' && !navigator.onLine;
  }

  /** Offline-/Fehlerpfad der SR-Bewertung: den Stufen-Übergang lokal berechnen (Spiegel des
   * Backends), Session + Offline-Kopie nachziehen und den Request für den Reconnect vormerken —
   * so bleibt die Fälligkeits-Logik der laufenden Offline-Session konsistent. */
  private applyLocalReview(req: LineReviewRequest): void {
    const st = applySrReview(this.statesByKey.get(req.lineKey), req.lineKey, req.correct, this.effectiveSrLevels(), Date.now());
    this.statesByKey.set(st.lineKey, st);
    this.persistOfflineStates();
    this.offlineQueue.enqueue('POST', `/api/repertoires/${this.repertoireId}/training/line-review`, req);
    if (this.phase === 'LINE_DONE') { this.nextRepeatAt = st.dueAt; this.cdr.markForCheck(); }
  }

  /** Intervalle fürs lokale Fälligkeits-Rechnen: Offline-Kopie → Client-Defaults. */
  private effectiveSrLevels(): SrLevel[] {
    if (this.srLevels?.length) return this.srLevels;
    const cached = getRepertoireOffline(this.repertoireId);
    return cached?.config?.length ? cached.config : DEFAULT_SR_LEVELS;
  }

  /** SR-Zustände in die Offline-Kopie spiegeln (No-op, wenn nicht heruntergeladen). */
  private persistOfflineStates(): void {
    updateRepertoireOfflineStates(this.repertoireId, [...this.statesByKey.values()]);
  }

  /** „Weiter" nach einer fertig gespielten Linie: entweder dieselbe Linie erneut (Learn-Wiederholung)
   *  oder die nächste Linie. Wird vom Knopf, Klick aufs Brett und Leertaste/Enter ausgelöst. */
  continueLine(): void {
    if (this.phase !== 'LINE_DONE') return;
    this.clearOppTimer();
    if (this.pendingRepeat) {
      this.pendingRepeat = false;
      this.startCurrentLine();          // dieselbe Linie (qIndex unverändert), learnPass bleibt
    } else {
      this.qIndex++;
      this.learnPass = 0;
      this.startCurrentLine();
    }
  }

  get isPlayable(): boolean {
    if (this.phase === 'PLAYING') return true;
    // Nach falschem Zug UND vor „Lösung zeigen": weiter spielbar (sofortiger Retry).
    return this.phase === 'FEEDBACK' && this.outcome === 'wrong' && !this.wrongRevealed;
  }

  get progressPct(): number {
    if (this.sessionUserMoves === 0) return 0;
    return Math.round(((this.correct + this.wrong) / this.sessionUserMoves) * 100);
  }

  get currentLineLabel(): string { return lineLabel(this.queue[this.qIndex]); }

  get currentLineChapter(): string { return lineChapterTitle(this.queue[this.qIndex]); }

  /** Züge der aktuellen Linie als flache Tokens fürs Template (`lineMoveTokens`). Im Abfragen-Modus, solange der
   * User seinen Zug noch SUCHT (PLAYING), erscheint der aktuelle Halbzug NICHT — sonst stünde die Lösung in der
   * Zugliste; im Lern-Modus und nach dem Zug (FEEDBACK/…) wird er gezeigt. */
  get movesInLine(): LineMoveToken[] {
    const revealCurrent = this.mode === 'learn' || this.phase !== 'PLAYING';
    return lineMoveTokens(this.queue[this.qIndex], this.currentPly, revealCurrent);
  }

  private bumpStreak(): void {
    this.currentStreak++;
    if (this.currentStreak > this.bestStreak) this.bestStreak = this.currentStreak;
  }

  /** PGN-Kommentar zum AKTUELL zu spielenden Halbzug (nur relevant für Lern-Modus, wo wir dem
   * User Kontext geben — im Review-Modus bewusst nicht, weil er den Zug SELBST finden soll). */
  get currentComment(): string { return plyComment(this.queue[this.qIndex], this.currentPly); }

  /** Kommentar in Absätze gesplittet (leere Zeile trennt) — für die Prosa-Anzeige im Lern-Modus. */
  get currentCommentParagraphs(): string[] { return commentParagraphs(this.currentComment); }

  /** Menschenlesbares Label des aktuellen Halbzugs, z. B. „3. exd5" (Weiß) oder „2… d6" (Schwarz). */
  get currentMovePrettyLabel(): string { return plyPrettyLabel(this.queue[this.qIndex], this.currentPly); }

  /** Kompakte Restzeit bis zur nächsten Fälligkeit, z. B. „4 h", „3 d", „2 w". */
  get nextDueLabel(): string { return relDueLabel(this.nextDueAt); }

  /** LINE_DONE: „in X" bis die gerade beendete Linie erneut abgefragt wird (Abfragen-Modus). */
  get nextRepeatLabel(): string { return relDueLabel(this.nextRepeatAt); }

  /** Farbe (chessground-Notation), die der User in dieser Linie spielt. */
  get userColorName(): 'white' | 'black' { return this.color === 'w' ? 'white' : 'black'; }

  /** Seite am Zug laut der aktuell angezeigten Stellung. */
  get boardTurn(): 'white' | 'black' { return whiteToMove(this.fen) ? 'white' : 'black'; }

  /** Premove erlauben, solange der GEGNER am Zug ist — also während des automatischen Gegnerzugs
   *  und im kurzen Feedback nach einem richtigen/geduldeten Zug. Nur im Abfragen-Modus. */
  get premovable(): boolean {
    if (this.mode !== 'quiz') return false;
    if (this.boardTurn === this.userColorName) return false;
    if (this.oppMoving) return true;
    return this.phase === 'FEEDBACK' && this.outcome !== 'wrong';
  }

  get evalDeltaAbsDisplay(): string { return this.evalCmp.deltaAbsDisplay; }

  /** Die geduldeten Alternativen ([%alt]) DIESER Stellung in der Form des gemeinsamen Kerns.
   *
   *  <p>Sie stehen im Repertoire-Graph als SAN und werden dort schon über das Brett kanonisiert;
   *  aufgelöst wird trotzdem hier erneut — gegen die Stellung, in der der Zug fallen soll.</p>
   *
   *  <p>Der Hauptzug wird bewusst NICHT ausgesiebt: die alte Zeile `accepted.delete(expectedSan)`
   *  steckt im Kern. {@link judgeMove} prüft den ERWARTETEN Zug zuerst und antwortet dann
   *  `correct` — als `alternative` kann er gar nicht mehr herauskommen.</p> */
  private altsAt(cardKey: string): ExpectedMove[] {
    const list = this.graph?.moves.get(cardKey);
    if (!list) return [];
    const out: ExpectedMove[] = [];
    const seen = new Set<string>();
    for (const m of list) {
      for (const a of m.alts) {
        if (seen.has(a)) continue;
        seen.add(a);
        out.push({ san: a });
      }
    }
    return out;
  }

  onMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    const line = this.queue[this.qIndex];
    if (!line || this.currentPly >= line.moves.length) return;
    if (this.mode === 'learn') { this.onLearnMove(ev); return; }
    const wrongRetry = this.phase === 'FEEDBACK' && this.outcome === 'wrong' && !this.wrongRevealed;
    if (this.phase !== 'PLAYING' && !wrongRetry) return;
    if (wrongRetry) {
      this.evalCmp.reset();
    }

    // Geurteilt wird gegen GENAU diese Stellung — `this.fen`, nicht `this.chess`: im Wiederhol-Fall
    // nach einem Fehlzug ist `this.fen` bereits auf die Ausgangsstellung des Halbzugs zurückgesetzt.
    this.startFen = this.fen;
    const board = tryLoadFen(this.fen);
    if (!board) return;

    const expectedMove = line.moves[this.currentPly];
    const expected: ExpectedMove = { san: expectedMove.san };
    const cardKey = normFen(this.fen);

    // EIN Urteil aus dem gemeinsamen Kern (shared/chess/line-solver): erwarteter Zug UND geduldete
    // Alternativen in einem Schritt, verglichen werden FELDER statt Zug-TEXT. `illegal` und
    // `not-your-turn` gehen dorthin, wo früher der catch-Zweig hinführte: kein Feedback, nichts
    // passiert (über das Brett-Component kommen ohnehin nur legale Züge herein).
    const verdict = judgeMove(board, expected, this.altsAt(cardKey), ev.orig, ev.dest, ev.promotion);
    if (verdict === 'illegal' || verdict === 'not-your-turn') return;
    const expectedUci = resolveExpectedUci(board, expected);

    // Den Nutzerzug auf der KOPIE anwenden: seine Folgestellung braucht der Eval-Vergleich, und ein
    // geduldeter Zug bleibt kurz auf dem Brett stehen.
    if (!tryFreeMove(board, ev.orig, ev.dest, ev.promotion)) return;
    const fenAfterPlayer = board.fen();
    this.lastMove = [ev.orig, ev.dest];

    // SR wird PRO LINIE bewertet (finishLine); hier nur Feedback + Merken, ob die Linie schon
    // einen Fehler hatte. Geduldete Züge zählen neutral.
    if (verdict === 'correct') {
      this.outcome = 'correct'; this.correct++;
      // Ein an dieser Stellung offener Fehler, den der User NICHT als Mausrutscher deklariert hat,
      // zählt jetzt (er macht ohne „Mausrutscher" weiter). Streak wird durch den echten Fehler
      // unterbrochen — dieser Zug startet eine neue Serie bei 1.
      if (this.pendingWrong) {
        this.lineHadWrong = true; this.wrong++; this.pendingWrong = false;
        this.currentStreak = 0;
      }
      this.bumpStreak();
      // Nachgeführt wird der ERWARTETE Zug, nicht der rohe Nutzerzug: nennt der Nutzer bei einer
      // Umwandlung keine Figur, gehört die der LINIE aufs Brett (dieselbe Regel wie im
      // Puzzle-Löser). Die Felder sind ohnehin dieselben — sonst wäre das Urteil nicht `correct`.
      // Ohne das Nachführen hängt eine Linie mit mehreren eigenen Zügen (advanceToUserMove spielt
      // die Gegnerzüge aus `this.chess`).
      if (expectedUci) { try { applyUci(this.chess, expectedUci); } catch { /* Brett steht woanders */ } }
      this.fen = this.chess.fen();
    } else if (verdict === 'alternative') {
      this.outcome = 'tolerated';
      if (this.pendingWrong) {
        this.lineHadWrong = true; this.wrong++; this.pendingWrong = false;
        this.currentStreak = 0;
      }
      this.bumpStreak();
      this.fen = fenAfterPlayer;
    } else {
      // Falscher Zug: NOCH nicht als Fehler zählen — der User kann „Mausrutscher" sagen. Erst beim
      // Weitermachen ohne Mausrutscher bzw. „Lösung zeigen" wird daraus ein echter Fehler.
      this.outcome = 'wrong'; this.pendingWrong = true;
      this.fen = this.startFen;
      this.lastMove = [ev.orig, ev.dest];
    }

    this.expectedDisplay = expectedMove.san;
    this.wrongRevealed = false;
    this.phase = 'FEEDBACK';

    if (this.outcome === 'wrong') {
      this.evalCmp.start(this.startFen, fenAfterPlayer, expectedMove.san, this.color);
      this.dests = destsAt(this.startFen);
    } else {
      this.scheduleAdvance(ADVANCE_MS[this.outcome]);
    }

    this.cdr.markForCheck();
  }

  onPlayClick(): void {
    if (this.phase === 'LINE_DONE') { this.continueLine(); return; }
    if (this.phase === 'COMMENT') { this.continueFromComment(); return; }
    if (this.phase === 'LEARN_SHOW' && this.learnComment) { this.learnRetract(); return; }
    if (this.phase === 'FEEDBACK' && this.outcome !== 'wrong' && this.advanceTimer !== null) this.runAdvance();
  }

  /** Leertaste/Enter = „Weiter" (wie Klick/Tippen): Kommentar im Learn-Show wegtippen, Feedback
   *  überspringen oder nach einer fertigen Linie fortfahren. */
  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (e.key !== ' ' && e.key !== 'Spacebar' && e.key !== 'Enter') return;
    if (!isBoardHotkey(e)) return;
    let acted = true;
    if (this.phase === 'LINE_DONE') this.continueLine();
    else if (this.phase === 'COMMENT') this.continueFromComment();
    else if (this.phase === 'LEARN_SHOW' && this.learnComment) this.learnRetract();
    else if (this.phase === 'FEEDBACK' && this.outcome !== 'wrong' && this.advanceTimer !== null) this.runAdvance();
    else acted = false;
    if (acted) e.preventDefault();
  }

  /** „Lösung zeigen": zählt als falsch (Server-Review), spielt den erwarteten Zug + weiter mit der Linie. */
  showSolution(): void {
    if (this.wrongRevealed) return;
    this.wrongRevealed = true;
    this.wrong++;
    this.currentStreak = 0;   // „Lösung zeigen" bricht die Serie (Mausrutscher tut das NICHT).
    this.lineHadWrong = true;   // Lösung zeigen = echter Fehler für die Linie
    this.pendingWrong = false;
    const line = this.queue[this.qIndex];
    const expected = line?.moves[this.currentPly];
    const board = expected ? tryLoadFen(this.startFen) : null;
    // Der erwartete Zug wird über das BRETT auf Felder aufgelöst (gemeinsamer Kern) statt als Text
    // gespielt. `null` = in dieser Stellung nicht auflösbar (mehrdeutige SAN, kaputte Linie) — dann
    // bleibt es beim Text-Reveal, genau wie vorher.
    const uci = board && expected ? resolveExpectedUci(board, { san: expected.san }) : null;
    if (uci) {
      try {
        this.chess.load(this.startFen);
        const mv = applyUci(this.chess, uci);
        this.fen = this.chess.fen();
        this.lastMove = [mv.from as Key, mv.to as Key];
        this.dests = new Map();
      } catch { /* Stellung nicht ladbar → nur Text-Reveal */ }
    }
    this.cdr.markForCheck();
  }

  /** „Mausrutscher": den offenen Fehler NICHT zählen und dieselbe Stellung wieder spielbar machen —
   * beliebig oft wiederholbar. */
  mouseslip(): void {
    if (this.phase !== 'FEEDBACK' || this.outcome !== 'wrong' || this.wrongRevealed) return;
    this.pendingWrong = false;
    this.outcome = 'correct';   // Feedback-Kasten schließen
    this.wrongRevealed = false;
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.dests = destsAt(this.startFen);
    this.phase = 'PLAYING';
    this.cdr.markForCheck();
  }

  /** Nach „Lösung zeigen": erwarteten Zug ist gespielt → mit der Linie fortfahren. */
  continueAfterWrong(): void {
    this.currentPly++;
    this.wrongRevealed = false;
    this.advanceToUserMove();
  }

  private scheduleAdvance(ms: number): void {
    this.clearAdvance();
    this.advanceTimer = setTimeout(() => { this.advanceTimer = null; this.runAdvance(); }, ms);
  }

  /** Nach richtigem/geduldetem Zug → im PGN weiterrücken und Gegnerzug spielen.
   * WICHTIG: Timer räumen wir zuerst weg, damit der scheduleAdvance-Timer nicht später NOCHMAL
   * runAdvance triggert, wenn der User schon per Klick/Leertaste manuell weitergeschaltet hat —
   * das würde `currentPly` doppelt hochzählen (der Gegnerzug wird nicht gespielt, weil der
   * Timer nur einen currentPly++ macht) und verschiebt „user turn" auf einen Opp-Zug. */
  private runAdvance(): void {
    this.clearAdvance();
    if (this.outcome === 'tolerated') {
      // Geduldeten Zug NICHT für den User zu Ende spielen: zurücknehmen und dieselbe Stellung
      // wieder spielbar machen, damit der User den erwarteten Hauptzug SELBST zieht (nicht
      // automatisch für ihn). currentPly bleibt stehen.
      this.retryCurrentPly();
      return;
    }
    this.currentPly++;
    this.advanceToUserMove();
  }

  // ===== Learn-Modus =====

  /** Kommentar eines gerade gespielten Gegnerzugs dauerhaft anzeigen (COMMENT-Phase), bis der User
   * „Weiter" bestätigt (Tap/Leertaste/Enter). Brett bleibt in der aktuellen Stellung, view-only. */
  private enterCommentHold(comment: string): void {
    this.clearLearn();
    this.clearOppTimer();
    this.holdComment = comment;
    this.dests = new Map();
    this.phase = 'COMMENT';
    this.cdr.markForCheck();
  }

  /** „Weiter" aus der COMMENT-Phase: Kommentar wegräumen und mit der Linie fortfahren. */
  continueFromComment(): void {
    if (this.phase !== 'COMMENT') return;
    this.holdComment = '';
    this.advanceToUserMove();
  }

  /** Wiederholungs-Durchläufe (learnPass ≥ 1): den erwarteten Zug NICHT vorzeigen — der User spielt
   * ihn aus dem Gedächtnis. Ein falscher Zug blendet ihn via onLearnMove → enterLearnShow als
   * Erinnerung ein. Brett bleibt in der Startstellung des Halbzugs und ist sofort spielbar. */
  private enterLearnPlay(): void {
    this.clearLearn();
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.dests = destsAt(this.startFen);
    this.learnComment = '';
    this.phase = 'PLAYING';
    this.cdr.markForCheck();
  }

  /** Zeigt den erwarteten Zug auf dem Brett (view-only). Ohne Kommentar nach 2s automatisch
   * zurücknehmen; MIT Kommentar stehen lassen, bis der User weitertippt (zum Lesen). */
  private enterLearnShow(): void {
    this.clearLearn();
    const line = this.queue[this.qIndex];
    const expected = line?.moves[this.currentPly];
    if (!expected) { this.finishLine(); return; }
    let fenAfter = this.startFen;
    try {
      const c = new Chess(this.startFen);
      const mv = c.move(expected.san);
      if (mv) { fenAfter = c.fen(); this.lastMove = [mv.from as Key, mv.to as Key]; }
    } catch { /* nicht spielbar → nur Text */ }
    this.fen = fenAfter;
    this.dests = new Map();
    this.expectedDisplay = expected.san;
    // Kommentar nur beim ERSTEN Durchlauf zeigen (hält bis zum Weitertippen); die Wiederholungs-
    // Durchläufe („durchklicken") laufen ohne Kommentar-Halt schnell durch.
    this.learnComment = this.learnPass === 0 ? (line.comments?.[this.currentPly] || '').trim() : '';
    this.phase = 'LEARN_SHOW';
    this.cdr.markForCheck();
    if (!this.learnComment) {
      this.learnTimer = setTimeout(() => { this.learnTimer = null; this.learnRetract(); }, LEARN_SHOW_MS);
    }
  }

  /** Gezeigten Zug zurücknehmen → der User spielt ihn selbst. */
  private learnRetract(): void {
    this.clearLearn();
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.dests = destsAt(this.startFen);
    this.learnComment = '';
    this.phase = 'PLAYING';
    this.cdr.markForCheck();
  }

  private clearLearn(): void {
    if (this.learnTimer !== null) { clearTimeout(this.learnTimer); this.learnTimer = null; }
  }

  /** Learn-Zug: nur der gezeigte (erwartete) Zug führt weiter; falsch → Zug erneut zeigen.
   *
   *  <p>Der Lern-Modus duldet ausdrücklich KEINE Alternativen — der Kern bekommt deshalb eine leere
   *  Alternativen-Liste, während der Abfragen-Modus {@link altsAt} mitgibt.</p> */
  private onLearnMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    const line = this.queue[this.qIndex];
    if (!line || this.phase !== 'PLAYING' || this.currentPly >= line.moves.length) return;
    const board = tryLoadFen(this.startFen);
    if (!board) return;
    const expected: ExpectedMove = { san: line.moves[this.currentPly].san };
    const verdict = judgeMove(board, expected, [], ev.orig, ev.dest, ev.promotion);
    if (verdict === 'illegal' || verdict === 'not-your-turn') return;
    if (verdict !== 'correct') { this.enterLearnShow(); return; }   // nicht der Zug → nochmal vormachen
    const uci = resolveExpectedUci(board, expected);
    if (uci) { try { applyUci(this.chess, uci); } catch { /* Brett steht woanders */ } }
    this.fen = this.chess.fen();
    this.lastMove = [ev.orig, ev.dest];
    this.currentPly++;
    this.advanceToUserMove();
  }

  /** Geduldeten Zug zurücknehmen und die aktuelle Stellung erneut spielbar machen. */
  private retryCurrentPly(): void {
    // this.chess steht noch auf startFen (der geduldete Zug wurde nur auf einer Kopie geprüft).
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.dests = destsAt(this.startFen);
    this.phase = 'PLAYING';
    this.cdr.markForCheck();
  }

  /** Alle Timer der laufenden Linie wegräumen (Weiter, Gegnerzug, Lern-Anzeige). */
  private clearLineTimers(): void {
    this.clearAdvance(); this.clearOppTimer(); this.clearLearn();
  }

  private clearAdvance(): void {
    if (this.advanceTimer !== null) { clearTimeout(this.advanceTimer); this.advanceTimer = null; }
  }

  private clearOppTimer(): void {
    if (this.oppTimer !== null) { clearTimeout(this.oppTimer); this.oppTimer = null; }
  }
}
