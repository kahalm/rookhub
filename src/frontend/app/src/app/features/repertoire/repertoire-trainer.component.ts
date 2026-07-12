import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';

import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { calcDests } from '../puzzles/puzzle-move.util';
import { StockfishService } from '../puzzles/stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { RepertoireTrainingService, LineStateDto } from './repertoire-training.service';
import { buildRepertoireGraph, normSan, normFen, RepertoireGraph } from './repertoire-tree.util';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { autoChapterColors, resolveChapterColors, rootSideOf, sideOfLastMove, TrainColor } from './repertoire-color.util';
import { SrConfigDialogComponent } from './sr-config-dialog.component';
import { ParsedGame, parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { isStateDue, isStateLearnable, earliestDueIso, relDueLabel, shuffle } from './repertoire-sr.util';
import { startNumbering, prettyMoveLabel } from './repertoire-move-format.util';
import { parseWhiteEval } from './repertoire-eval.util';

type Phase = 'LOADING' | 'EMPTY' | 'PLAYING' | 'FEEDBACK' | 'DONE' | 'LINE_DONE' | 'LEARN_SHOW' | 'COMMENT';
type Outcome = 'correct' | 'tolerated' | 'wrong';
type Mode = 'quiz' | 'learn';

// Wie lange das „Correct/Tolerated"-Feedback nach einem Zug stehen bleibt, bevor automatisch
// weitergerückt wird (der User kann per Klick/Leertaste/Enter überspringen). correct: 1,5 s → 600 →
// 300 ms (der grüne Haken blitzt kurz, dann kommt zügig der Gegnerzug). Geduldet bleibt länger
// stehen (der Alternativzug soll wirken).
const ADVANCE_MS: Record<Outcome, number> = { correct: 300, tolerated: 1500, wrong: 0 };
const OPP_MOVE_DELAY_MS = 250;   // kurze Pause vor jedem automatischen Gegnerzug
const WRONG_HOLD_MS = 1000;
const LEARN_SHOW_MS = 1000;      // Zug im Learn-Modus ohne Kommentar so lange zeigen, dann zurücknehmen
const LEARN_GAP_MS = 400;        // Learn: Pause zwischen Gegnerzug und dem Zeigen des eigenen Zugs (fühlte sich beim Wiederholen zu lang an, halbiert von 800)
// Learn-Modus: wie oft eine Linie durchgespielt werden muss, bevor sie in den Übungspool wandert
// (1× geführt lernen + 2× „durchklicken").
const LEARN_REPEATS = 3;

/**
 * Line-basiertes Repertoire-Training: eine ganze PGN-Linie wird vom Startzug an durchgespielt,
 * Gegnerzüge automatisch, an jedem eigenen Zug pausiert der Trainer und wertet den User-Zug gegen
 * die Linie (SAN + [%alt]-tolerierte Alternativen). Nach dem letzten Zug: nächste Linie. Ein
 * `?chapter=Name`-Query beschränkt die Sitzung auf ein Kapitel (Black-Header). SM-2-Reviews werden
 * je Position (normFen als Card-Key) weiterhin ans Backend gesendet.
 */
@Component({
  selector: 'app-repertoire-trainer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, RouterLink, MatCardModule, MatButtonModule, MatButtonToggleModule,
    MatIconModule, MatProgressBarModule, MatTooltipModule, MatDialogModule, TranslatePipe, PuzzleBoardComponent,
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
  evalLoading = false;
  evalDeltaPawns: number | null = null;
  evalMateNote: 'missed' | 'allowed' | null = null;
  private evalEpoch = 0;

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
  private wrongRevertTimer: ReturnType<typeof setTimeout> | null = null;
  private oppTimer: ReturnType<typeof setTimeout> | null = null;
  private startFen = '';

  constructor(
    private route: ActivatedRoute,
    private training: RepertoireTrainingService,
    public prefs: PreferencesService,
    private translate: TranslateService,
    private cdr: ChangeDetectorRef,
    private stockfish: StockfishService,
    private dialog: MatDialog,
  ) {}

  ngOnInit(): void {
    this.repertoireId = Number(this.route.snapshot.paramMap.get('id')) || 0;
    this.chapterFilter = this.route.snapshot.queryParamMap.get('chapter');
    this.mode = this.route.snapshot.queryParamMap.get('mode') === 'learn' ? 'learn' : 'quiz';
    this.singleLineKey = this.route.snapshot.queryParamMap.get('line');
    this.stockfish.init().catch(() => {});

    forkJoin({
      pgn: this.training.getPgn(this.repertoireId),
      states: this.training.getLineStates(this.repertoireId),
    }).subscribe({
      next: ({ pgn, states }) => {
        this.graph = buildRepertoireGraph(pgn);
        this.allLines = parsePgnText(pgn);
        // Trainingsfarbe je Kapitel automatisch erkennen + manuelle Overrides drüberlegen. Dadurch
        // wird jede Linie aus der RICHTIGEN Seite abgefragt, auch wenn das Repertoire Kapitel beider
        // Farben mischt.
        const auto = autoChapterColors(this.allLines.map(l => ({
          chapter: (l.headers['Black'] || '').trim(),
          side: sideOfLastMove(l.fens[0], l.moves.length),
          rootSide: rootSideOf(l.fens[0]),
        })));
        this.chapterColors = resolveChapterColors(this.repertoireId, auto);
        this.statesByKey = new Map(states.map(s => [s.lineKey, s]));
        this.buildQueue();
      },
      error: () => { this.phase = 'EMPTY'; this.cdr.markForCheck(); },
    });
  }

  ngOnDestroy(): void { this.clearAdvance(); this.clearOppTimer(); this.clearLearn(); }

  /** Effektive Trainingsfarbe einer Linie (aus ihrem Kapitel). */
  private colorOf(line: ParsedGame): TrainColor {
    return this.chapterColors.get((line.headers['Black'] || '').trim()) ?? 'w';
  }

  setMode(m: Mode): void {
    if (m === this.mode) return;
    this.clearAdvance(); this.clearOppTimer(); this.clearLearn();
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
    this.clearAdvance(); this.clearOppTimer(); this.clearLearn();
    const now = Date.now();
    let filtered = this.chapterFilter
      ? this.allLines.filter(l => (l.headers['Black'] || '').trim() === this.chapterFilter!.trim())
      : this.allLines;
    if (this.singleLineKey) filtered = filtered.filter(l => this.lineKeyOf(l) === this.singleLineKey);
    const usable = filtered.filter(l => this.hasUserMove(l));
    this.queue = this.mode === 'learn'
      ? usable.filter(l => this.isLearnable(l))                 // Reihenfolge = PGN-Reihenfolge
      : shuffle(usable.filter(l => this.isDue(l, now)));
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

  restart(): void { this.buildQueue(); }

  /** Alle (Chapter-gefilterten) übbaren Linien mit ≥1 eigenen Zug — deren stabile Schlüssel. */
  private usableLineKeys(): string[] {
    const filtered = this.chapterFilter
      ? this.allLines.filter(l => (l.headers['Black'] || '').trim() === this.chapterFilter!.trim())
      : this.allLines;
    return filtered.filter(l => this.hasUserMove(l)).map(l => this.lineKeyOf(l));
  }

  private reloadStatesAndRebuild(): void {
    this.training.getLineStates(this.repertoireId).subscribe({
      next: states => {
        this.statesByKey = new Map(states.map(s => [s.lineKey, s]));
        this.poolBusy = false;
        this.buildQueue();
      },
      error: () => { this.poolBusy = false; this.cdr.markForCheck(); },
    });
  }

  /** „Alle in den Pool aufnehmen" (Kurs bzw. gefiltertes Kapitel) → sofort fällig. */
  promoteAllToPool(): void {
    const keys = this.usableLineKeys();
    if (keys.length === 0) return;
    this.poolBusy = true; this.cdr.markForCheck();
    this.training.promote(this.repertoireId, keys).subscribe({
      next: () => this.reloadStatesAndRebuild(),
      error: () => { this.poolBusy = false; this.cdr.markForCheck(); },
    });
  }

  /** „Alle jetzt fällig machen" (nur Pool-Linien; Kurs bzw. gefiltertes Kapitel). */
  makeAllDue(): void {
    const keys = this.chapterFilter ? this.usableLineKeys() : [];   // leer = ganzer Kurs
    this.poolBusy = true; this.cdr.markForCheck();
    this.training.makeDue(this.repertoireId, keys).subscribe({
      next: () => this.reloadStatesAndRebuild(),
      error: () => { this.poolBusy = false; this.cdr.markForCheck(); },
    });
  }

  /** Intervall-Zyklen bearbeiten (global + pro-Repertoire-Override). */
  openConfig(): void {
    this.dialog.open(SrConfigDialogComponent, { data: { repertoireId: this.repertoireId }, width: '420px' });
  }

  /** Alle SM-2-Zustände dieses Repertoires löschen (Bestätigungs-Dialog vorher). */
  resetProgress(): void {
    if (!confirm(this.translate.instant('repertoireTrainer.resetConfirm'))) return;
    this.resetting = true;
    this.training.reset(this.repertoireId).subscribe({
      next: () => {
        this.statesByKey.clear();
        this.resetting = false;
        this.buildQueue();
      },
      error: () => { this.resetting = false; },
    });
  }

  private hasUserMove(line: ParsedGame): boolean {
    // FEN[0] enthält die Startseite; wir suchen den ersten Ply, an dem der User (= Kapitelfarbe) zieht.
    if (line.moves.length === 0) return false;
    const color = this.colorOf(line);
    const start = new Chess(line.fens[0]);
    let side: 'w' | 'b' = start.turn();
    for (let i = 0; i < line.moves.length; i++) {
      if (side === color) return true;
      side = side === 'w' ? 'b' : 'w';
    }
    return false;
  }

  private countUserMoves(line: ParsedGame): number {
    const color = this.colorOf(line);
    const start = new Chess(line.fens[0]);
    let side: 'w' | 'b' = start.turn();
    let n = 0;
    for (let i = 0; i < line.moves.length; i++) {
      if (side === color) n++;
      side = side === 'w' ? 'b' : 'w';
    }
    return n;
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
      try { this.dests = calcDests(new Chess(this.fen)); } catch { this.dests = new Map(); }
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
      // Oft genug gelernt → in den Pool aufnehmen (sofort fällig für die 1. Abfrage).
      this.training.promote(this.repertoireId, [this.lineKeyOf(line)])
        .subscribe({ next: () => {}, error: () => {} });
    } else if (line) {
      // Quiz: SR-Bewertung PRO LINIE: fehlerfrei → +1 Stufe, sonst zurück auf Stufe 1.
      const label = (line.headers['White'] || '').trim().slice(0, 120);
      this.training.reviewLine(this.repertoireId, { lineKey: this.lineKeyOf(line), label, correct: !this.lineHadWrong })
        .subscribe({
          next: st => {
            this.statesByKey.set(st.lineKey, st);
            // Nächste Fälligkeit im „Linie fertig"-Kasten anzeigen (nur solange diese Linie noch offen ist).
            if (this.phase === 'LINE_DONE') { this.nextRepeatAt = st.dueAt; this.cdr.markForCheck(); }
          },
          error: () => {},
        });
    }
    // Nicht mehr automatisch weiter — der User rückt per „Weiter"-Knopf (bzw. Klick/Leertaste) vor.
    // Timer werden BEWUSST hier NICHT geräumt: `startCurrentLine` cleart sie beim Übergang zur
    // nächsten Linie. In manchen Test-/Race-Pfaden hilft ein spät feuernder Timer, phase aus
    // einem Fehl-Zustand zurück zu PLAYING zu setzen — den ziehen wir nicht vorzeitig weg.
    this.pendingRepeat = false;
    this.phase = 'LINE_DONE';
    this.cdr.markForCheck();
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

  get currentLineLabel(): string {
    const line = this.queue[this.qIndex];
    if (!line) return '';
    return (line.headers['White'] || '').trim();
  }

  get currentLineChapter(): string {
    const line = this.queue[this.qIndex];
    if (!line) return '';
    return (line.headers['Black'] || '').trim();
  }

  /** Züge der aktuellen Linie als flache Tokens fürs Template. Nur bereits gespielte + aktueller
   * Halbzug — die Zukunft wird bewusst NICHT vorweggenommen (Chessable-artig, spart Spoiler).
   * `state` = `past` (vor currentPly) bzw. `current` (aktueller Halbzug). `num` ist gesetzt für
   * den ersten Halbzug eines Zugpaares. `comment` ist der PGN-Kommentar zum Halbzug (falls einer
   * hinterlegt ist) — im Template im Lern-Modus angezeigt. */
  get movesInLine(): { num: number | null; san: string; ply: number; state: 'past' | 'current'; comment: string }[] {
    const line = this.queue[this.qIndex];
    if (!line || line.moves.length === 0) return [];
    // Zug-Nummerierung stabil aus dem Start-FEN ableiten (fullmove counter + turn).
    const { side, fullMove } = startNumbering(line.fens[0]);
    const out: { num: number | null; san: string; ply: number; state: 'past' | 'current'; comment: string }[] = [];
    let curSide = side;
    let curMove = fullMove;
    // Im Abfragen-Modus, solange der User seinen Zug noch SUCHT (PLAYING), darf der aktuelle
    // (noch nicht gespielte) Halbzug NICHT erscheinen — sonst steht die Lösung in der Zug-Liste.
    // Im Lern-Modus (führt den Zug ohnehin vor) und nach dem Zug (FEEDBACK/…) wird er gezeigt.
    const revealCurrent = this.mode === 'learn' || this.phase !== 'PLAYING';
    const upto = revealCurrent ? Math.min(this.currentPly, line.moves.length - 1) : this.currentPly - 1;
    for (let ply = 0; ply <= upto; ply++) {
      const isFirstBlackHalfmove = ply === 0 && curSide === 'b';
      const num = curSide === 'w' || isFirstBlackHalfmove ? curMove : null;
      const state: 'past' | 'current' = ply < this.currentPly ? 'past' : 'current';
      const comment = (line.comments?.[ply] || '').trim();
      out.push({ num, san: line.moves[ply].san, ply, state, comment });
      if (curSide === 'b') curMove++;
      curSide = curSide === 'w' ? 'b' : 'w';
    }
    return out;
  }

  private bumpStreak(): void {
    this.currentStreak++;
    if (this.currentStreak > this.bestStreak) this.bestStreak = this.currentStreak;
  }

  /** PGN-Kommentar zum AKTUELL zu spielenden Halbzug (nur relevant für Lern-Modus, wo wir dem
   * User Kontext geben — im Review-Modus bewusst nicht, weil er den Zug SELBST finden soll). */
  get currentComment(): string {
    const line = this.queue[this.qIndex];
    if (!line) return '';
    return (line.comments?.[this.currentPly] || '').trim();
  }

  /** Kommentar in Absätze gesplittet (leere Zeile trennt) — für die Prosa-Anzeige im Lern-Modus. */
  get currentCommentParagraphs(): string[] {
    const c = this.currentComment;
    if (!c) return [];
    return c.split(/\n\s*\n/).map(p => p.trim()).filter(Boolean);
  }

  /** Menschenlesbares Label des aktuellen Halbzugs, z. B. „3. exd5" (Weiß) oder „2… d6" (Schwarz). */
  get currentMovePrettyLabel(): string {
    const line = this.queue[this.qIndex];
    if (!line || this.currentPly < 0 || this.currentPly >= line.moves.length) return '';
    return prettyMoveLabel(line.fens[0], line.moves[this.currentPly].san, this.currentPly);
  }

  /** Kompakte Restzeit bis zur nächsten Fälligkeit, z. B. „4 h", „3 d", „2 w". */
  get nextDueLabel(): string { return relDueLabel(this.nextDueAt); }

  /** LINE_DONE: „in X" bis die gerade beendete Linie erneut abgefragt wird (Abfragen-Modus). */
  get nextRepeatLabel(): string { return relDueLabel(this.nextRepeatAt); }

  /** Farbe (chessground-Notation), die der User in dieser Linie spielt. */
  get userColorName(): 'white' | 'black' { return this.color === 'w' ? 'white' : 'black'; }

  /** Seite am Zug laut der aktuell angezeigten Stellung. */
  get boardTurn(): 'white' | 'black' { return this.fen.split(/\s+/)[1] === 'b' ? 'black' : 'white'; }

  /** Premove erlauben, solange der GEGNER am Zug ist — also während des automatischen Gegnerzugs
   *  und im kurzen Feedback nach einem richtigen/geduldeten Zug. Nur im Abfragen-Modus. */
  get premovable(): boolean {
    if (this.mode !== 'quiz') return false;
    if (this.boardTurn === this.userColorName) return false;
    if (this.oppMoving) return true;
    return this.phase === 'FEEDBACK' && this.outcome !== 'wrong';
  }

  get evalDeltaAbsDisplay(): string {
    if (this.evalDeltaPawns === null) return '';
    return Math.abs(this.evalDeltaPawns).toFixed(2).replace(/\.?0+$/, '');
  }

  onMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    const line = this.queue[this.qIndex];
    if (!line || this.currentPly >= line.moves.length) return;
    if (this.mode === 'learn') { this.onLearnMove(ev); return; }
    const wrongRetry = this.phase === 'FEEDBACK' && this.outcome === 'wrong' && !this.wrongRevealed;
    if (this.phase !== 'PLAYING' && !wrongRetry) return;
    if (wrongRetry) {
      this.clearWrongRevert();
      this.evalLoading = false; this.evalDeltaPawns = null; this.evalMateNote = null; this.evalEpoch++;
    }

    this.startFen = this.fen;
    let userSan = '';
    let fenAfterPlayer = '';
    try {
      const c = new Chess(this.fen);
      const mv = c.move({ from: ev.orig, to: ev.dest, promotion: (ev.promotion as any) || 'q' });
      userSan = normSan(mv.san);
      fenAfterPlayer = c.fen();
      this.lastMove = [ev.orig, ev.dest];
    } catch { return; }

    const expectedMove = line.moves[this.currentPly];
    const expectedSan = normSan(expectedMove.san);
    const cardKey = normFen(this.fen);
    // Tolerierte Alternativen kommen aus dem Repertoire-Graph ([%alt]-Kommentare).
    const graphList = this.graph?.moves.get(cardKey);
    const accepted = new Set<string>();
    if (graphList) {
      for (const m of graphList) {
        for (const a of m.alts) accepted.add(normSan(a));
      }
    }
    accepted.delete(expectedSan);

    // SR wird PRO LINIE bewertet (finishLine); hier nur Feedback + Merken, ob die Linie schon
    // einen Fehler hatte. Geduldete Züge zählen neutral.
    if (userSan === expectedSan) {
      this.outcome = 'correct'; this.correct++;
      // Ein an dieser Stellung offener Fehler, den der User NICHT als Mausrutscher deklariert hat,
      // zählt jetzt (er macht ohne „Mausrutscher" weiter). Streak wird durch den echten Fehler
      // unterbrochen — dieser Zug startet eine neue Serie bei 1.
      if (this.pendingWrong) {
        this.lineHadWrong = true; this.wrong++; this.pendingWrong = false;
        this.currentStreak = 0;
      }
      this.bumpStreak();
      // Korrekten Zug in der maßgeblichen Partie nachführen, damit advanceToUserMove die
      // Gegnerzüge aus der richtigen Stellung spielt (sonst hängt eine Linie mit mehreren
      // eigenen Zügen).
      try { this.chess.move({ from: ev.orig, to: ev.dest, promotion: (ev.promotion as any) || 'q' }); } catch {}
      this.fen = this.chess.fen();
    } else if (accepted.has(userSan)) {
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
      this.kickOffEvalCompare(fenAfterPlayer, cardKey, expectedMove.san);
      try { this.dests = calcDests(new Chess(this.startFen)); } catch { this.dests = new Map(); }
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
    const t = e.target as HTMLElement | null;
    if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
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
    this.clearWrongRevert();
    this.wrongRevealed = true;
    this.wrong++;
    this.currentStreak = 0;   // „Lösung zeigen" bricht die Serie (Mausrutscher tut das NICHT).
    this.lineHadWrong = true;   // Lösung zeigen = echter Fehler für die Linie
    this.pendingWrong = false;
    const line = this.queue[this.qIndex];
    const expected = line?.moves[this.currentPly];
    if (expected) {
      try {
        const c = new Chess(this.startFen);
        const mv = c.move(expected.san);
        if (mv) {
          this.chess.load(this.startFen);
          this.chess.move(expected.san);
          this.fen = this.chess.fen();
          this.lastMove = [mv.from as Key, mv.to as Key];
          this.dests = new Map();
        }
      } catch { /* SAN nicht spielbar → nur Text-Reveal */ }
    }
    this.cdr.markForCheck();
  }

  /** „Mausrutscher": den offenen Fehler NICHT zählen und dieselbe Stellung wieder spielbar machen —
   * beliebig oft wiederholbar. */
  mouseslip(): void {
    if (this.phase !== 'FEEDBACK' || this.outcome !== 'wrong' || this.wrongRevealed) return;
    this.clearWrongRevert();
    this.pendingWrong = false;
    this.outcome = 'correct';   // Feedback-Kasten schließen
    this.wrongRevealed = false;
    this.fen = this.startFen;
    this.lastMove = undefined;
    try { this.dests = calcDests(new Chess(this.startFen)); } catch { this.dests = new Map(); }
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
    try { this.dests = calcDests(new Chess(this.startFen)); } catch { this.dests = new Map(); }
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
    try { this.dests = calcDests(new Chess(this.startFen)); } catch { this.dests = new Map(); }
    this.learnComment = '';
    this.phase = 'PLAYING';
    this.cdr.markForCheck();
  }

  private clearLearn(): void {
    if (this.learnTimer !== null) { clearTimeout(this.learnTimer); this.learnTimer = null; }
  }

  /** Learn-Zug: nur der gezeigte (erwartete) Zug führt weiter; falsch → Zug erneut zeigen. */
  private onLearnMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    const line = this.queue[this.qIndex];
    if (!line || this.phase !== 'PLAYING' || this.currentPly >= line.moves.length) return;
    let userSan = '';
    try {
      const c = new Chess(this.startFen);
      userSan = normSan(c.move({ from: ev.orig, to: ev.dest, promotion: (ev.promotion as any) || 'q' }).san);
    } catch { return; }
    if (userSan === normSan(line.moves[this.currentPly].san)) {
      try { this.chess.move({ from: ev.orig, to: ev.dest, promotion: (ev.promotion as any) || 'q' }); } catch {}
      this.fen = this.chess.fen();
      this.lastMove = [ev.orig, ev.dest];
      this.currentPly++;
      this.advanceToUserMove();
    } else {
      this.enterLearnShow();   // nicht der Zug → nochmal vormachen
    }
  }

  /** Geduldeten Zug zurücknehmen und die aktuelle Stellung erneut spielbar machen. */
  private retryCurrentPly(): void {
    // this.chess steht noch auf startFen (der geduldete Zug wurde nur auf einer Kopie geprüft).
    this.fen = this.startFen;
    this.lastMove = undefined;
    try { this.dests = calcDests(new Chess(this.startFen)); } catch { this.dests = new Map(); }
    this.phase = 'PLAYING';
    this.cdr.markForCheck();
  }

  private clearAdvance(): void {
    if (this.advanceTimer !== null) { clearTimeout(this.advanceTimer); this.advanceTimer = null; }
    this.clearWrongRevert();
  }

  private clearOppTimer(): void {
    if (this.oppTimer !== null) { clearTimeout(this.oppTimer); this.oppTimer = null; }
  }

  private scheduleWrongRevert(): void {
    this.clearWrongRevert();
    this.wrongRevertTimer = setTimeout(() => { this.wrongRevertTimer = null; }, WRONG_HOLD_MS);
  }

  private clearWrongRevert(): void {
    if (this.wrongRevertTimer !== null) { clearTimeout(this.wrongRevertTimer); this.wrongRevertTimer = null; }
  }

  private kickOffEvalCompare(fenAfterPlayer: string, cardKey: string, expectedSan: string): void {
    const epoch = ++this.evalEpoch;
    this.evalLoading = true;
    this.evalDeltaPawns = null;
    this.evalMateNote = null;

    let fenAfterRep = '';
    try {
      const c = new Chess(this.startFen);
      c.move(expectedSan);
      fenAfterRep = c.fen();
    } catch { this.evalLoading = false; this.cdr.markForCheck(); return; }

    Promise.all([
      this.stockfish.getEval(fenAfterPlayer, 14).catch(() => ''),
      this.stockfish.getEval(fenAfterRep, 14).catch(() => ''),
    ]).then(([sPlayer, sRep]) => {
      if (epoch !== this.evalEpoch) return;
      this.evalLoading = false;
      if (!sPlayer || !sRep) { this.cdr.markForCheck(); return; }
      const ep = parseWhiteEval(sPlayer);
      const er = parseWhiteEval(sRep);
      const sign = this.color === 'w' ? 1 : -1;
      const cpPlayer = ep.cp * sign;
      const cpRep = er.cp * sign;
      const playerWasInWin = er.mateFor && (er.mateFor === this.color);
      const playerNowLosing = ep.mateFor && (ep.mateFor !== this.color);
      if (playerNowLosing) this.evalMateNote = 'allowed';
      else if (playerWasInWin && !ep.mateFor) this.evalMateNote = 'missed';
      this.evalDeltaPawns = (cpPlayer - cpRep) / 100;
      this.cdr.markForCheck();
    }).catch(() => {
      if (epoch !== this.evalEpoch) return;
      this.evalLoading = false;
      this.cdr.markForCheck();
    });
  }
}
