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
import { TranslateModule, TranslateService } from '@ngx-translate/core';
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

type Phase = 'LOADING' | 'EMPTY' | 'PLAYING' | 'FEEDBACK' | 'DONE' | 'LINE_DONE' | 'LEARN_SHOW';
type Outcome = 'correct' | 'tolerated' | 'wrong';
type Mode = 'quiz' | 'learn';

// Wie lange das „Correct/Tolerated"-Feedback nach einem Zug stehen bleibt, bevor automatisch
// weitergerückt wird (der User kann per Klick/Leertaste/Enter überspringen). correct war in
// 0.250.0 auf 3 s hochgezogen (vorher „sehr kurz"), das fühlte sich beim Durchspielen einer
// Linie zu langsam an — jetzt 1,5 s als Kompromiss.
const ADVANCE_MS: Record<Outcome, number> = { correct: 1500, tolerated: 1500, wrong: 0 };
const OPP_MOVE_DELAY_MS = 400;   // kurze Pause vor jedem automatischen Gegnerzug
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
    MatIconModule, MatProgressBarModule, MatTooltipModule, MatDialogModule, TranslateModule, PuzzleBoardComponent,
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
    <mat-button-toggle-group [value]="mode" (change)="setMode($event.value)" hideSingleSelectionIndicator="true" aria-label="Mode">
      <mat-button-toggle value="quiz">{{ 'repertoireTrainer.modeQuiz' | translate }}</mat-button-toggle>
      <mat-button-toggle value="learn">{{ 'repertoireTrainer.modeLearn' | translate }}</mat-button-toggle>
    </mat-button-toggle-group>
    <button mat-icon-button (click)="openConfig()"
            [matTooltip]="'srConfig.title' | translate"
            [attr.aria-label]="'srConfig.title' | translate">
      <mat-icon>tune</mat-icon>
    </button>
    <button mat-icon-button (click)="resetProgress()" [disabled]="resetting"
            [matTooltip]="'repertoireTrainer.resetTooltip' | translate"
            [attr.aria-label]="'repertoireTrainer.resetTooltip' | translate">
      <mat-icon>restart_alt</mat-icon>
    </button>
  </div>

  <ng-container [ngSwitch]="phase">
    <div *ngSwitchCase="'LOADING'" class="center">{{ 'common.loading' | translate }}</div>

    <mat-card *ngSwitchCase="'EMPTY'" class="msg">
      @if (mode === 'learn') {
        <mat-icon>school</mat-icon>
        <p>{{ 'repertoireTrainer.nothingToLearn' | translate }}</p>
        <button mat-flat-button color="primary" (click)="setMode('quiz')">
          <mat-icon>fitness_center</mat-icon> {{ 'repertoireTrainer.switchToQuiz' | translate }}
        </button>
        <a mat-stroked-button [routerLink]="['/repertoires', repertoireId]">
          <mat-icon>list</mat-icon> {{ 'repertoireTrainer.manageLines' | translate }}
        </a>
      } @else {
      <mat-icon>{{ nextDueAt ? 'schedule' : 'school' }}</mat-icon>
      @if (nextDueAt) {
        <p>{{ 'repertoireTrainer.nextDue' | translate: { when: nextDueLabel } }}</p>
        <button mat-flat-button color="primary" (click)="makeAllDue()" [disabled]="poolBusy">
          <mat-icon>bolt</mat-icon> {{ 'repertoireTrainer.makeAllDue' | translate }}
        </button>
      } @else {
        <p>{{ 'repertoireTrainer.nothingInPool' | translate }}</p>
        <button mat-flat-button color="primary" (click)="promoteAllToPool()" [disabled]="poolBusy">
          <mat-icon>playlist_add</mat-icon> {{ 'repertoireTrainer.addAllToPool' | translate }}
        </button>
      }
      <a mat-stroked-button [routerLink]="['/repertoires', repertoireId]">
        <mat-icon>list</mat-icon> {{ 'repertoireTrainer.manageLines' | translate }}
      </a>
      }
    </mat-card>

    <mat-card *ngSwitchCase="'DONE'" class="msg">
      <mat-icon>celebration</mat-icon>
      @if (mode === 'learn') {
        <p>{{ 'repertoireTrainer.learnDone' | translate }}</p>
        <button mat-raised-button color="primary" (click)="setMode('quiz')">
          <mat-icon>fitness_center</mat-icon> {{ 'repertoireTrainer.switchToQuiz' | translate }}
        </button>
      } @else {
        <p>{{ 'repertoireTrainer.done' | translate: { correct: correct, total: sessionUserMoves } }}</p>
        <button mat-raised-button color="primary" (click)="restart()">{{ 'repertoireTrainer.again' | translate }}</button>
      }
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
          @if (currentStreak >= 2) {
            <span class="streak" [matTooltip]="'repertoireTrainer.streakTooltip' | translate: { best: bestStreak }">
              <mat-icon>local_fire_department</mat-icon>{{ currentStreak }}
            </span>
          }
        </div>
        @if (currentLineLabel) {
          <p class="line-label" [matTooltip]="currentLineChapter || ''">{{ currentLineLabel }}</p>
        }
        @if (movesInLine.length > 0) {
          <div class="line-moves" [attr.aria-label]="'repertoireTrainer.movesInLine' | translate">
            @for (m of movesInLine; track m.ply) {
              @if (m.num !== null) { <span class="mv-num">{{ m.num }}.</span> }
              <span class="mv" [ngClass]="m.state">{{ m.san }}</span>
            }
          </div>
          @if (mode === 'learn' && currentComment) {
            <div class="ctx-comment" [attr.aria-label]="'repertoireTrainer.moveComment' | translate">
              <div class="ctx-head">
                <mat-icon>chat_bubble</mat-icon>
                <span>{{ currentMovePrettyLabel }}</span>
              </div>
              @for (p of currentCommentParagraphs; track $index) {
                <p>{{ p }}</p>
              }
            </div>
          }
        }
        <p class="prompt">{{ (color === 'w' ? 'repertoireTrainer.whiteToMove' : 'repertoireTrainer.blackToMove') | translate }}</p>

        @if (phase === 'LEARN_SHOW') {
          <div class="feedback learn">
            <p><mat-icon>visibility</mat-icon> {{ 'repertoireTrainer.learnShow' | translate: { move: expectedDisplay } }}</p>
          </div>
          @if (learnComment) {
            <div class="comment-box">
              <mat-icon>chat_bubble</mat-icon>
              <div class="comment-text">{{ learnComment }}</div>
            </div>
            <p class="tap-hint">{{ 'repertoireTrainer.pressToContinue' | translate }}</p>
          }
        }

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
              <button mat-stroked-button (click)="mouseslip(); $event.stopPropagation()">
                <mat-icon>back_hand</mat-icon> {{ 'repertoireTrainer.mouseslip' | translate }}
              </button>
              <button mat-raised-button color="primary" (click)="showSolution(); $event.stopPropagation()">
                <mat-icon>visibility</mat-icon> {{ 'repertoireTrainer.showSolution' | translate }}
              </button>
            </div>
            <button *ngIf="wrongRevealed" mat-raised-button color="primary" (click)="continueAfterWrong(); $event.stopPropagation()">{{ 'repertoireTrainer.continue' | translate }}</button>
          </ng-container>
          <p *ngIf="outcome !== 'wrong'" class="tap-hint">{{ 'repertoireTrainer.tapToContinue' | translate }}</p>
        </div>
        <p *ngIf="phase === 'PLAYING'" class="hint">{{ (mode === 'learn' ? 'repertoireTrainer.learnPlay' : 'repertoireTrainer.playYourMove') | translate }}</p>
        @if (phase === 'LINE_DONE') {
          <div class="line-done">
            <p class="hint"><mat-icon>done_all</mat-icon>
              {{ (pendingRepeat ? 'repertoireTrainer.linePassDone' : 'repertoireTrainer.lineDone') | translate }}</p>
            <button mat-raised-button color="primary" (click)="continueLine(); $event.stopPropagation()">
              {{ (pendingRepeat ? 'repertoireTrainer.repeatLine' : 'repertoireTrainer.nextLine') | translate }}
            </button>
            <p class="tap-hint">{{ 'repertoireTrainer.pressToContinue' | translate }}</p>
          </div>
        }
      </div>
    </div>
  </ng-container>
</div>
  `,
  styles: [`
    .trainer { max-width: min(1400px, 96vw); margin: 0 auto; padding: 8px; }
    .bar { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; flex-wrap: wrap; }
    .bar .title { font-weight: 600; flex: 1; display: flex; align-items: center; gap: 8px; }
    .chapter-chip { display: inline-flex; align-items: center; gap: 4px; padding: 2px 8px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 12%, transparent); font-weight: 500; font-size: .85rem; }
    .chapter-chip mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .center, .msg { text-align: center; padding: 32px; }
    .msg { display: flex; flex-direction: column; align-items: center; gap: 12px; }
    .msg mat-icon { font-size: 40px; height: 40px; width: 40px; }
    .play { display: flex; gap: 20px; flex-wrap: wrap; align-items: flex-start; }
    /* Brett darf jetzt bis knapp Viewport-Höhe (minus Navbar + Toolbar) hochwachsen (Chessable-artig)
     * und außerdem breit werden — begrenzt zusätzlich über min(80vh, 96vw − Seitenleiste). Der
     * innere chess-board hat aspect-ratio 1/1, daher wird Höhe automatisch = Breite. */
    app-puzzle-board { flex: 1 1 480px; max-width: min(820px, calc(100vh - 180px)); }
    .side { flex: 1 1 340px; min-width: 300px; max-width: 520px; }
    .counts { display: flex; align-items: center; gap: 14px; margin: 10px 0; flex-wrap: wrap; }
    .counts .ok { color: #2e7d32; } .counts .bad { color: #c62828; }
    .counts .streak { display: inline-flex; align-items: center; gap: 2px; padding: 2px 8px; border-radius: 999px;
      background: color-mix(in srgb, #ff6f00 22%, transparent); color: #ff6f00; font-weight: 600; font-size: .85rem; }
    .counts .streak mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .line-label { font-size: 13px; opacity: .85; margin: 4px 0 6px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    /* Züge der aktuellen Linie: bereits gespielte grau, aktueller in Primary hervorgehoben, verbleibende
     * gedimmt. Monospace + wrap, damit lange Linien lesbar bleiben. */
    .line-moves { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 6px; margin: 4px 0 10px;
      padding: 8px 10px; border-radius: 6px; font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
      font-size: 13px; line-height: 1.5;
      background: var(--mat-sys-surface-container-high, rgba(255,255,255,.04));
      max-height: 200px; overflow-y: auto; }
    .line-moves .mv-num { opacity: .6; font-size: 12px; margin-left: 4px; }
    .line-moves .mv-num:first-child { margin-left: 0; }
    .line-moves .mv { padding: 0 2px; border-radius: 3px; }
    .line-moves .mv.past { opacity: .55; }
    .line-moves .mv.current { background: color-mix(in srgb, var(--mat-sys-primary, #1565c0) 22%, transparent);
      color: var(--mat-sys-primary, #1565c0); font-weight: 700; padding: 0 6px; }
    .line-moves .mv.current::before { content: '▸ '; }
    .line-moves .mv.future { opacity: .8; }
    /* Prosa-Block für den aktuellen Zug-Kommentar im Lern-Modus (Chessable-artig): eigener
     * Kasten unter der Züge-Liste mit Move-Label als Überschrift und Absätzen. */
    .ctx-comment { margin: 4px 0 12px; padding: 12px 14px; border-radius: 8px;
      background: var(--mat-sys-surface-container-high, rgba(21,101,192,.10));
      border-left: 3px solid var(--mat-sys-primary, #1565c0); }
    .ctx-comment .ctx-head { display: flex; align-items: center; gap: 8px; margin-bottom: 8px;
      font-weight: 600; color: var(--mat-sys-primary, #1565c0); }
    .ctx-comment .ctx-head mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .ctx-comment p { margin: 0 0 10px; font-size: 14px; line-height: 1.5; white-space: pre-wrap; overflow-wrap: anywhere; }
    .ctx-comment p:last-child { margin-bottom: 0; }
    .prompt { font-weight: 600; }
    .feedback { padding: 10px; border-radius: 8px; }
    .feedback p { display: flex; align-items: center; gap: 8px; margin: 0 0 12px; }
    .feedback.correct { background: rgba(46,125,50,.12); }
    .feedback.tolerated { background: rgba(255,160,0,.15); }
    .feedback.wrong { background: rgba(198,40,40,.12); }
    .feedback.learn { background: rgba(21,101,192,.12); }
    /* Kommentar im Learn-Modus als eigene, deutlich abgesetzte Box (besser lesbar). */
    .comment-box { display: flex; gap: 8px; align-items: flex-start; margin: 10px 0 6px;
      padding: 10px 12px; border-radius: 8px; text-align: left;
      background: var(--mat-sys-surface-container-high, rgba(21,101,192,.10));
      border-left: 3px solid var(--mat-sys-primary, #1565c0); }
    .comment-box mat-icon { flex: 0 0 auto; opacity: .7; font-size: 18px; width: 18px; height: 18px; margin-top: 1px; }
    .comment-text { white-space: pre-wrap; font-size: 13px; line-height: 1.45; }
    .line-done { display: flex; flex-direction: column; align-items: center; gap: 8px; }
    .tap-hint { font-size: 12px; opacity: .7; margin: 0; }
    .hint { color: var(--mdc-theme-text-secondary-on-background, #666); display: flex; align-items: center; gap: 6px; }
    .wrong-actions { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 8px; }
    .eval-info { font-size: 13px; opacity: .85; margin: 0 0 8px; }
  `],
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
    const st = this.statesByKey.get(this.lineKeyOf(line));
    return !!st && st.inPool && !st.paused && new Date(st.dueAt).getTime() <= now;
  }

  /** Learn-Kandidat = noch NICHT im Pool und nicht pausiert. */
  private isLearnable(line: ParsedGame): boolean {
    const st = this.statesByKey.get(this.lineKeyOf(line));
    return (!st || !st.inPool) && !st?.paused;
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
    let min: number | null = null;
    for (const l of lines) {
      const st = this.statesByKey.get(this.lineKeyOf(l));
      if (!st || !st.inPool || st.paused) continue;
      const t = new Date(st.dueAt).getTime();
      if (min === null || t < min) min = t;
    }
    return min === null ? null : new Date(min).toISOString();
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
      // Learn: kurze Pause zwischen dem Gegnerzug und dem Zeigen des nächsten eigenen Zugs, damit
      // beide nicht quasi gleichzeitig erscheinen.
      if (this.mode === 'learn') {
        this.oppTimer = setTimeout(() => { this.oppTimer = null; this.advanceToUserMove(); }, LEARN_GAP_MS);
      } else {
        this.advanceToUserMove();
      }
    }, OPP_MOVE_DELAY_MS);
  }

  private finishLine(): void {
    const line = this.queue[this.qIndex];

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
        .subscribe({ next: st => this.statesByKey.set(st.lineKey, st), error: () => {} });
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
    let fullMove = 1;
    let side: 'w' | 'b' = 'w';
    try {
      const start = new Chess(line.fens[0]);
      side = start.turn();
      const parts = line.fens[0].split(/\s+/);
      const n = parseInt(parts[5] || '1', 10);
      if (Number.isFinite(n) && n >= 1) fullMove = n;
    } catch { /* Defaults reichen */ }
    const out: { num: number | null; san: string; ply: number; state: 'past' | 'current'; comment: string }[] = [];
    let curSide = side;
    let curMove = fullMove;
    const upto = Math.min(this.currentPly, line.moves.length - 1);
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
    let side: 'w' | 'b' = 'w';
    let fullMove = 1;
    try {
      const start = new Chess(line.fens[0]);
      side = start.turn();
      const parts = line.fens[0].split(/\s+/);
      const n = parseInt(parts[5] || '1', 10);
      if (Number.isFinite(n) && n >= 1) fullMove = n;
    } catch { /* Defaults */ }
    let curSide = side;
    let num = fullMove;
    for (let ply = 0; ply < this.currentPly; ply++) {
      if (curSide === 'b') num++;
      curSide = curSide === 'w' ? 'b' : 'w';
    }
    const san = line.moves[this.currentPly].san;
    return curSide === 'w' ? `${num}. ${san}` : `${num}… ${san}`;
  }

  /** Kompakte Restzeit bis zur nächsten Fälligkeit, z. B. „4 h", „3 d", „2 w". */
  get nextDueLabel(): string {
    if (!this.nextDueAt) return '';
    const ms = new Date(this.nextDueAt).getTime() - Date.now();
    const h = ms / 3_600_000;
    if (h < 1) return '< 1 h';
    if (h < 48) return `${Math.round(h)} h`;
    const d = h / 24;
    if (d < 14) return `${Math.round(d)} d`;
    const w = d / 7;
    if (w < 9) return `${Math.round(w)} w`;
    return `${Math.round(d / 30)} mo`;
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
