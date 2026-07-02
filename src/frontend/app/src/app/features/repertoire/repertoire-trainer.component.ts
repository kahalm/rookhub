import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';

import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { calcDests } from '../puzzles/puzzle-move.util';
import { StockfishService } from '../puzzles/stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { RepertoireTrainingService, RepertoireCardStateDto, ReviewCardRequest } from './repertoire-training.service';
import { buildRepertoireGraph, normSan, normFen, sideToMove, RepertoireGraph } from './repertoire-tree.util';
import { ParsedGame, parsePgnText } from '../../shared/pgn-viewer/pgn-parser';

type Phase = 'LOADING' | 'EMPTY' | 'PLAYING' | 'FEEDBACK' | 'DONE' | 'LINE_DONE';
type Outcome = 'correct' | 'tolerated' | 'wrong';

const COLOR_KEY = (id: number) => `rookhub_rep_train_color_${id}`;
const ADVANCE_MS: Record<Outcome, number> = { correct: 600, tolerated: 1500, wrong: 0 };
const OPP_MOVE_DELAY_MS = 400;   // kurze Pause vor jedem automatischen Gegnerzug
const WRONG_HOLD_MS = 1000;

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
    MatIconModule, MatProgressBarModule, MatTooltipModule, TranslateModule, PuzzleBoardComponent,
  ],
  template: `
<div class="trainer">
  <div class="bar">
    <a mat-button [routerLink]="['/repertoires', repertoireId]"><mat-icon>arrow_back</mat-icon> {{ 'common.back' | translate }}</a>
    <span class="title">
      {{ 'repertoireTrainer.title' | translate }}
      @if (chapterFilter) {
        <span class="chapter-chip"><mat-icon>bookmark</mat-icon>{{ chapterFilter }}</span>
      }
    </span>
    <mat-button-toggle-group [value]="color" (change)="setColor($event.value)" hideSingleSelectionIndicator="true" aria-label="Color">
      <mat-button-toggle value="w">{{ 'repertoireTrainer.white' | translate }}</mat-button-toggle>
      <mat-button-toggle value="b">{{ 'repertoireTrainer.black' | translate }}</mat-button-toggle>
    </mat-button-toggle-group>
    <button mat-icon-button (click)="resetProgress()" [disabled]="resetting"
            [matTooltip]="'repertoireTrainer.resetTooltip' | translate"
            [attr.aria-label]="'repertoireTrainer.resetTooltip' | translate">
      <mat-icon>restart_alt</mat-icon>
    </button>
  </div>

  <ng-container [ngSwitch]="phase">
    <div *ngSwitchCase="'LOADING'" class="center">{{ 'common.loading' | translate }}</div>

    <mat-card *ngSwitchCase="'EMPTY'" class="msg">
      <mat-icon>school</mat-icon>
      <p>{{ 'repertoireTrainer.noCards' | translate }}</p>
    </mat-card>

    <mat-card *ngSwitchCase="'DONE'" class="msg">
      <mat-icon>celebration</mat-icon>
      <p>{{ 'repertoireTrainer.done' | translate: { correct: correct, total: sessionUserMoves } }}</p>
      <button mat-raised-button color="primary" (click)="restart()">{{ 'repertoireTrainer.again' | translate }}</button>
    </mat-card>

    <div *ngSwitchDefault class="play" (click)="onPlayClick()">
      <app-puzzle-board
        [fen]="fen" [orientation]="color === 'w' ? 'white' : 'black'"
        [turnColor]="color === 'w' ? 'white' : 'black'"
        [dests]="dests" [lastMove]="lastMove" [viewOnly]="!isPlayable"
        [boardTheme]="prefs.boardTheme" [pieceSet]="prefs.pieceSet"
        (moveMade)="onMove($event)">
      </app-puzzle-board>

      <div class="side">
        <mat-progress-bar mode="determinate" [value]="progressPct"></mat-progress-bar>
        <div class="counts">
          <span>{{ 'repertoireTrainer.lineProgress' | translate: { done: qIndex + 1, total: queue.length } }}</span>
          <span class="ok">✓ {{ correct }}</span>
          <span class="bad">✗ {{ wrong }}</span>
        </div>
        @if (currentLineLabel) {
          <p class="line-label" [matTooltip]="currentLineChapter || ''">{{ currentLineLabel }}</p>
        }
        <p class="prompt">{{ (color === 'w' ? 'repertoireTrainer.whiteToMove' : 'repertoireTrainer.blackToMove') | translate }}</p>

        <div *ngIf="phase === 'FEEDBACK'" class="feedback" [ngClass]="outcome">
          <p *ngIf="outcome === 'correct'"><mat-icon>check_circle</mat-icon> {{ 'repertoireTrainer.correct' | translate }}</p>
          <p *ngIf="outcome === 'tolerated'"><mat-icon>info</mat-icon> {{ 'repertoireTrainer.toleratedPlayable' | translate }}</p>
          <ng-container *ngIf="outcome === 'wrong'">
            <p *ngIf="!wrongRevealed"><mat-icon>cancel</mat-icon> {{ 'repertoireTrainer.wrongNoHint' | translate }}</p>
            <p *ngIf="wrongRevealed"><mat-icon>cancel</mat-icon>
              {{ 'repertoireTrainer.wrong' | translate: { move: expectedDisplay } }}</p>
            <p class="eval-info" *ngIf="evalLoading"><mat-icon>hourglass_top</mat-icon> {{ 'repertoireTrainer.evalLoading' | translate }}</p>
            <p class="eval-info" *ngIf="!evalLoading && evalMateNote === 'missed'"><mat-icon>flash_on</mat-icon> {{ 'repertoireTrainer.evalMateMissed' | translate }}</p>
            <p class="eval-info" *ngIf="!evalLoading && evalMateNote === 'allowed'"><mat-icon>flash_on</mat-icon> {{ 'repertoireTrainer.evalMateAllowed' | translate }}</p>
            <p class="eval-info" *ngIf="!evalLoading && evalMateNote === null && evalDeltaPawns !== null && evalDeltaPawns < -0.05">
              <mat-icon>trending_down</mat-icon>
              {{ 'repertoireTrainer.evalWorse' | translate: { delta: evalDeltaAbsDisplay } }}
            </p>
            <p class="eval-info" *ngIf="!evalLoading && evalMateNote === null && evalDeltaPawns !== null && evalDeltaPawns >= -0.05 && evalDeltaPawns <= 0.05">
              <mat-icon>drag_handle</mat-icon> {{ 'repertoireTrainer.evalEqual' | translate }}
            </p>
            <div class="wrong-actions" *ngIf="!wrongRevealed">
              <button mat-raised-button color="primary" (click)="showSolution(); $event.stopPropagation()">
                <mat-icon>visibility</mat-icon> {{ 'repertoireTrainer.showSolution' | translate }}
              </button>
            </div>
            <button *ngIf="wrongRevealed" mat-raised-button color="primary" (click)="continueAfterWrong(); $event.stopPropagation()">{{ 'repertoireTrainer.continue' | translate }}</button>
          </ng-container>
          <p *ngIf="outcome !== 'wrong'" class="tap-hint">{{ 'repertoireTrainer.tapToContinue' | translate }}</p>
        </div>
        <p *ngIf="phase === 'PLAYING'" class="hint">{{ 'repertoireTrainer.playYourMove' | translate }}</p>
        <p *ngIf="phase === 'LINE_DONE'" class="hint"><mat-icon>done_all</mat-icon> {{ 'repertoireTrainer.lineDone' | translate }}</p>
      </div>
    </div>
  </ng-container>
</div>
  `,
  styles: [`
    .trainer { max-width: 920px; margin: 0 auto; padding: 8px; }
    .bar { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; flex-wrap: wrap; }
    .bar .title { font-weight: 600; flex: 1; display: flex; align-items: center; gap: 8px; }
    .chapter-chip { display: inline-flex; align-items: center; gap: 4px; padding: 2px 8px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 12%, transparent); font-weight: 500; font-size: .85rem; }
    .chapter-chip mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .center, .msg { text-align: center; padding: 32px; }
    .msg { display: flex; flex-direction: column; align-items: center; gap: 12px; }
    .msg mat-icon { font-size: 40px; height: 40px; width: 40px; }
    .play { display: flex; gap: 20px; flex-wrap: wrap; align-items: flex-start; }
    app-puzzle-board { flex: 1 1 360px; max-width: 480px; }
    .side { flex: 1 1 240px; min-width: 220px; }
    .counts { display: flex; gap: 14px; margin: 10px 0; }
    .counts .ok { color: #2e7d32; } .counts .bad { color: #c62828; }
    .line-label { font-size: 13px; opacity: .85; margin: 4px 0 6px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .prompt { font-weight: 600; }
    .feedback { padding: 10px; border-radius: 8px; }
    .feedback p { display: flex; align-items: center; gap: 8px; margin: 0 0 12px; }
    .feedback.correct { background: rgba(46,125,50,.12); }
    .feedback.tolerated { background: rgba(255,160,0,.15); }
    .feedback.wrong { background: rgba(198,40,40,.12); }
    .tap-hint { font-size: 12px; opacity: .7; margin: 0; }
    .hint { color: var(--mdc-theme-text-secondary-on-background, #666); display: flex; align-items: center; gap: 6px; }
    .wrong-actions { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 8px; }
    .eval-info { font-size: 13px; opacity: .85; margin: 0 0 8px; }
  `],
})
export class RepertoireTrainerComponent implements OnInit, OnDestroy {
  repertoireId = 0;
  phase: Phase = 'LOADING';
  color: 'w' | 'b' = 'w';
  /** Kapitel-Filter aus ?chapter=…. Null = alle Kapitel. */
  chapterFilter: string | null = null;

  fen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  dests = new Map<Key, Key[]>();
  lastMove?: [Key, Key];
  resetting = false;

  // Session state
  private allLines: ParsedGame[] = [];
  queue: ParsedGame[] = [];
  qIndex = 0;
  private chess = new Chess();
  private currentPly = 0;
  correct = 0;
  wrong = 0;
  sessionUserMoves = 0;   // Zähler für Progress-Balken (grob geschätzt)

  outcome: Outcome = 'correct';
  expectedDisplay = '';
  wrongRevealed = false;
  evalLoading = false;
  evalDeltaPawns: number | null = null;
  evalMateNote: 'missed' | 'allowed' | null = null;
  private evalEpoch = 0;
  private pendingWrongReview: (() => void) | null = null;

  private statesByKey = new Map<string, RepertoireCardStateDto>();
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
  ) {}

  ngOnInit(): void {
    this.repertoireId = Number(this.route.snapshot.paramMap.get('id')) || 0;
    this.chapterFilter = this.route.snapshot.queryParamMap.get('chapter');
    const saved = localStorage.getItem(COLOR_KEY(this.repertoireId));
    if (saved === 'w' || saved === 'b') this.color = saved;
    this.stockfish.init().catch(() => {});

    forkJoin({
      pgn: this.training.getPgn(this.repertoireId),
      states: this.training.getCards(this.repertoireId),
    }).subscribe({
      next: ({ pgn, states }) => {
        this.graph = buildRepertoireGraph(pgn);
        if (!saved) this.color = this.graph.guessedColor;
        this.allLines = parsePgnText(pgn);
        this.statesByKey = new Map(states.map(s => [s.cardKey, s]));
        this.buildQueue();
      },
      error: () => { this.phase = 'EMPTY'; this.cdr.markForCheck(); },
    });
  }

  ngOnDestroy(): void { this.clearAdvance(); this.clearOppTimer(); }

  setColor(c: 'w' | 'b'): void {
    if (c === this.color) return;
    this.clearAdvance(); this.clearOppTimer();
    this.color = c;
    localStorage.setItem(COLOR_KEY(this.repertoireId), c);
    this.buildQueue();
  }

  /** Baut die Session-Warteschlange aus den Linien (Chapter-gefiltert), zufällig gemischt. */
  private buildQueue(): void {
    this.clearAdvance(); this.clearOppTimer();
    const filtered = this.chapterFilter
      ? this.allLines.filter(l => (l.headers['Black'] || '').trim() === this.chapterFilter!.trim())
      : this.allLines;
    // Nur Linien mit ≥1 eigenen Zug behalten.
    const usable = filtered.filter(l => this.hasUserMove(l));
    this.queue = shuffle(usable);
    this.qIndex = 0;
    this.correct = 0;
    this.wrong = 0;
    this.sessionUserMoves = this.queue.reduce((sum, l) => sum + this.countUserMoves(l), 0);
    if (this.queue.length === 0) { this.phase = 'EMPTY'; this.cdr.markForCheck(); return; }
    this.startCurrentLine();
  }

  restart(): void { this.buildQueue(); }

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
    // FEN[0] enthält die Startseite; wir suchen den ersten Ply, an dem der User zieht.
    if (line.moves.length === 0) return false;
    const start = new Chess(line.fens[0]);
    let side: 'w' | 'b' = start.turn();
    for (let i = 0; i < line.moves.length; i++) {
      if (side === this.color) return true;
      side = side === 'w' ? 'b' : 'w';
    }
    return false;
  }

  private countUserMoves(line: ParsedGame): number {
    const start = new Chess(line.fens[0]);
    let side: 'w' | 'b' = start.turn();
    let n = 0;
    for (let i = 0; i < line.moves.length; i++) {
      if (side === this.color) n++;
      side = side === 'w' ? 'b' : 'w';
    }
    return n;
  }

  private startCurrentLine(): void {
    const line = this.queue[this.qIndex];
    if (!line) { this.phase = 'DONE'; this.cdr.markForCheck(); return; }
    this.chess = new Chess(line.fens[0]);
    this.currentPly = 0;
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
      // User ist am Zug → warten
      this.startFen = this.fen;
      try { this.dests = calcDests(new Chess(this.fen)); } catch { this.dests = new Map(); }
      this.phase = 'PLAYING';
      this.cdr.markForCheck();
      return;
    }

    // Gegnerzug automatisch spielen (mit kleiner Verzögerung, damit man ihn wahrnimmt).
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
      this.advanceToUserMove();
    }, OPP_MOVE_DELAY_MS);
  }

  private finishLine(): void {
    this.phase = 'LINE_DONE';
    this.cdr.markForCheck();
    // Kurz die Endstellung stehen lassen, dann nächste Linie.
    this.oppTimer = setTimeout(() => {
      this.oppTimer = null;
      this.qIndex++;
      this.startCurrentLine();
    }, 900);
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

  get evalDeltaAbsDisplay(): string {
    if (this.evalDeltaPawns === null) return '';
    return Math.abs(this.evalDeltaPawns).toFixed(2).replace(/\.?0+$/, '');
  }

  onMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    const line = this.queue[this.qIndex];
    if (!line || this.currentPly >= line.moves.length) return;
    const wrongRetry = this.phase === 'FEEDBACK' && this.outcome === 'wrong' && !this.wrongRevealed;
    if (this.phase !== 'PLAYING' && !wrongRetry) return;
    if (wrongRetry) {
      this.clearWrongRevert();
      this.pendingWrongReview = null;
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

    let grade: ReviewCardRequest['grade'];
    if (userSan === expectedSan) {
      this.outcome = 'correct'; this.correct++; grade = 2;
      this.fen = fenAfterPlayer;
    } else if (accepted.has(userSan)) {
      this.outcome = 'tolerated'; grade = 1;
      this.fen = fenAfterPlayer;
    } else {
      this.outcome = 'wrong'; grade = 0;
      // Falschen Zug zurücknehmen; als lastMove markiert lassen.
      this.fen = this.startFen;
      this.lastMove = [ev.orig, ev.dest];
    }

    this.expectedDisplay = expectedMove.san;
    this.wrongRevealed = false;
    this.phase = 'FEEDBACK';

    if (this.outcome === 'wrong') {
      this.pendingWrongReview = () =>
        this.training.review(this.repertoireId, { cardKey, expectedMove: expectedMove.san, grade: 0 })
          .subscribe({ next: st => this.statesByKey.set(st.cardKey, st), error: () => {} });
      this.kickOffEvalCompare(fenAfterPlayer, cardKey, expectedMove.san);
      try { this.dests = calcDests(new Chess(this.startFen)); } catch { this.dests = new Map(); }
    } else {
      this.training.review(this.repertoireId, { cardKey, expectedMove: expectedMove.san, grade })
        .subscribe({ next: st => this.statesByKey.set(st.cardKey, st), error: () => {} });
      this.scheduleAdvance(ADVANCE_MS[this.outcome]);
    }

    this.cdr.markForCheck();
  }

  onPlayClick(): void {
    if (this.phase === 'FEEDBACK' && this.outcome !== 'wrong' && this.advanceTimer !== null) this.runAdvance();
  }

  /** „Lösung zeigen": zählt als falsch (Server-Review), spielt den erwarteten Zug + weiter mit der Linie. */
  showSolution(): void {
    if (this.wrongRevealed) return;
    this.clearWrongRevert();
    this.wrongRevealed = true;
    this.wrong++;
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
    this.pendingWrongReview?.();
    this.pendingWrongReview = null;
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

  /** Nach richtigem/geduldetem Zug → im PGN weiterrücken und Gegnerzug spielen. */
  private runAdvance(): void {
    if (this.outcome === 'tolerated') {
      // Geduldeten Zug zurücknehmen, den erwarteten Hauptzug spielen und die Linie fortsetzen —
      // der Gegner reagiert auf die kanonische Fortsetzung, nicht auf die tolerierte Abweichung.
      const line = this.queue[this.qIndex];
      const expected = line?.moves[this.currentPly];
      if (expected) {
        try {
          this.chess.load(this.startFen);
          this.chess.move(expected.san);
          this.fen = this.chess.fen();
          this.lastMove = [expected.from as Key, expected.to as Key];
        } catch { /* Fallback: einfach weiter */ }
      }
    }
    this.currentPly++;
    this.advanceToUserMove();
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

    const parseCpWhite = (s: string): { cp: number; mateFor: 'w' | 'b' | null } => {
      if (!s) return { cp: 0, mateFor: null };
      if (s.startsWith('#-')) return { cp: -100000 + parseInt(s.slice(2), 10), mateFor: 'b' };
      if (s.startsWith('#'))  return { cp:  100000 - parseInt(s.slice(1), 10), mateFor: 'w' };
      const v = parseFloat(s);
      return { cp: isNaN(v) ? 0 : Math.round(v * 100), mateFor: null };
    };

    Promise.all([
      this.stockfish.getEval(fenAfterPlayer, 14).catch(() => ''),
      this.stockfish.getEval(fenAfterRep, 14).catch(() => ''),
    ]).then(([sPlayer, sRep]) => {
      if (epoch !== this.evalEpoch) return;
      this.evalLoading = false;
      if (!sPlayer || !sRep) { this.cdr.markForCheck(); return; }
      const ep = parseCpWhite(sPlayer);
      const er = parseCpWhite(sRep);
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

/** Fisher–Yates in-place-Kopie. Reihenfolge der Trainings-Linien wird pro Session gemischt. */
function shuffle<T>(arr: readonly T[]): T[] {
  const out = arr.slice();
  for (let i = out.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [out[i], out[j]] = [out[j], out[i]];
  }
  return out;
}
