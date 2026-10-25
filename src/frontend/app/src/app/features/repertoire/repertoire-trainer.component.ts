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
import { Key } from 'chessground/types';

import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { StockfishService } from '../puzzles/stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { RepertoireTrainingService, LineStateDto } from './repertoire-training.service';
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
import { BoardMove, TrainerMode, TrainerOutcome, TrainerPhase, TrainerSession } from './repertoire-trainer-session';
import { LineMoveToken } from './repertoire-trainer-line-view.util';
import { localStore, readRaw, writeRaw } from '../../core/local-json-store';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { TRAINER_PASS_THROUGH, TRAINING_LINES_ALL, TrainingLines, trainingLinesUrl } from '../../shared/player-card/training-lines';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';

/** „Häufigste zuerst" merkt sich das Gerät — wie die übrigen Anzeige-Vorlieben des Trainers. */
const FREQ_ORDER_KEY = 'rookhub_rep_train_freq_order';

function readFreqOrder(): boolean {
  return readRaw(localStore(), FREQ_ORDER_KEY) === '1';
}

function saveFreqOrder(on: boolean): void {
  writeRaw(localStore(), FREQ_ORDER_KEY, on ? '1' : '0');   // gesperrt/voll → nur diese Sitzung
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
    changed: () => this.cdr.markForCheck(),
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
  get fen(): string { return this.s.fen; }
  get dests(): Map<Key, Key[]> { return this.s.dests; }
  get lastMove(): [Key, Key] | undefined { return this.s.lastMove; }
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
    this.s.mode = this.route.snapshot.queryParamMap.get('mode') === 'learn' ? 'learn' : 'quiz';
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
          this.sr.setSrLevels(cached.config);
          void this.initSession(cached.pgn, cached.states);
        } else {
          this.s.phase = 'EMPTY';
          this.cdr.markForCheck();
        }
      },
    });
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

  ngOnDestroy(): void {
    this.destroyed = true;
    this.loadSub?.unsubscribe();
    this.s.clearTimers(); this.freq.dispose(); this.opponentSub?.unsubscribe();
  }

  /** Die Trainingslinien gegen den Gegner holen (alle, nicht nur die ersten 50). <paramref name="wait"/>: bis dahin „Lädt";
   *  sonst läuft schon die Einstiegslinie, und die Reihung hängt die weiteren Linien an. */
  private loadOpponentOrder(wait: boolean): void {
    const url = trainingLinesUrl(this.opponent);
    if (!url || !this.http) { this.opponent = null; if (wait) this.buildQueue(); return; }
    if (wait) this.s.phase = 'LOADING';
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

  /** Linien dieser Sitzung (Kapitel-Filter, Farbe je Kapitel). */
  private get lineSet(): TrainerLineSet {
    return { lines: this.allLines, chapter: this.chapterFilter, colorOf: l => this.colorOf(l) };
  }

  /** Die Linien der Gegner-Sitzung (Farbe, Reihung, Einstieg). */
  private opponentLines(): ParsedGame[] {
    return opponentLines(this.lineSet, { color: this.opponentColor, rank: this.opponentRank, start: this.opponentStart });
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

  /** Fällig = im Pool, nicht pausiert und DueAt ≤ jetzt. Noch nicht gelernte Linien (kein Zustand)
   * sind NICHT im Pool und werden nicht abgefragt. */
  private isDue(line: ParsedGame, now: number): boolean {
    return isStateDue(this.sr.stateOf(this.lineKeyOf(line)), now);
  }

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
    const queue = againstOpponent
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
  /** Klick/Tippen aufs Brett = „Weiter" (Linie fertig, Kommentar, Vorzeigen mit Kommentar, Feedback überspringen). */
  onPlayClick(): void { this.s.tap(); }

  /** Leertaste/Enter = „Weiter" (wie Klick/Tippen). */
  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (e.key !== ' ' && e.key !== 'Spacebar' && e.key !== 'Enter') return;
    if (!isBoardHotkey(e)) return;
    if (this.s.tap()) e.preventDefault();
  }
}
