import {
  ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { PuzzleBoardComponent } from '@rh/features/puzzles/puzzle-board.component';
import { KidsMove, KidsSolver, KidsTask } from '../core/kids-solver';

/** Was die Eule gerade sagt. */
export type KidsPuzzleStatus = 'watch' | 'yourTurn' | 'good' | 'wrong' | 'alternative' | 'solved';

/** Pausen, damit das Kind sieht, was passiert (Stellungszug, Antwort des Gegners). */
export const SETUP_DELAY_MS = 700;
export const REPLY_DELAY_MS = 550;

const SOLVED_KEYS = ['kids.feedback.solved1', 'kids.feedback.solved2', 'kids.feedback.solved3', 'kids.feedback.solved4'];

/**
 * Eine Aufgabe auf der Kinderseite: grosses Brett, eine Eule, die sagt, was los ist, ein Tipp-Knopf.
 * Falsche Zuege kosten nichts ausser einem Fehlerpunkt (fuer die Sterne) — das Brett springt zurueck
 * und das Kind probiert weiter. Aller Anzeige-Zustand steckt in Signalen: die Antworten des Gegners
 * kommen per Timer, und Angular 22 zeichnet nach einem Timer eine unmarkierte Ansicht nicht neu.
 */
@Component({
  selector: 'kid-puzzle',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PuzzleBoardComponent, TranslatePipe],
  template: `
    <div class="puzzle">
      <div class="board">
        <app-puzzle-board
          [fen]="fen()"
          [orientation]="orientation()"
          [turnColor]="turnColor()"
          [dests]="dests()"
          [lastMove]="lastMove()"
          [check]="check()"
          [viewOnly]="!interactive()"
          [reviewShapes]="shapes()"
          [allowFullscreen]="false"
          [autoQueen]="true"
          (moveMade)="onMove($event)" />
      </div>

      <div class="side">
        <div class="bubble" [class]="'bubble status-' + status()" role="status" aria-live="polite">
          <span class="owl" aria-hidden="true">🦉</span>
          <p>
            @switch (status()) {
              @case ('watch') { {{ 'kids.feedback.watch' | translate }} }
              @case ('yourTurn') {
                {{ (orientation() === 'white' ? 'kids.feedback.yourTurnWhite' : 'kids.feedback.yourTurnBlack') | translate }}
              }
              @case ('good') { {{ 'kids.feedback.good' | translate }} }
              @case ('wrong') { {{ 'kids.feedback.wrong' | translate }} }
              @case ('alternative') { {{ 'kids.feedback.alternative' | translate }} }
              @case ('solved') { {{ solvedKey() | translate }} }
            }
          </p>
        </div>

        @if (status() === 'solved' && finalComment(); as text) {
          <p class="comment">{{ text }}</p>
        }

        <div class="actions">
          @if (status() === 'solved') {
            <button type="button" class="big next" (click)="next.emit()">{{ 'kids.next' | translate }} ▶</button>
          } @else {
            <button type="button" class="big hint" (click)="showHint()" [disabled]="!interactive()">
              💡 {{ 'kids.hint' | translate }}
            </button>
          }
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .puzzle { display: flex; gap: 20px; align-items: flex-start; justify-content: center; }
    .board { width: min(92vw, 70vh, 640px); flex: 0 0 auto; }
    .side { flex: 1 1 260px; max-width: 360px; display: flex; flex-direction: column; gap: 14px; }
    .bubble {
      display: flex; gap: 12px; align-items: center; padding: 14px 16px; border-radius: 22px;
      background: var(--kid-card); box-shadow: 0 4px 0 var(--kid-shadow); font-size: 1.25rem; font-weight: 700;
      transition: background-color .2s;
    }
    .bubble p { margin: 0; }
    .owl { font-size: 2.4rem; line-height: 1; }
    .status-good, .status-solved { background: var(--kid-good-bg); color: var(--kid-good-fg); }
    .status-wrong { background: var(--kid-bad-bg); color: var(--kid-bad-fg); animation: wiggle .35s; }
    .status-alternative { background: var(--kid-info-bg); }
    .status-solved .owl { animation: hop .6s; }
    .comment { margin: 0; padding: 12px 16px; border-radius: 16px; background: var(--kid-card); font-size: 1.05rem; line-height: 1.4; }
    .actions { display: flex; gap: 12px; }
    .big {
      flex: 1; font: inherit; font-size: 1.3rem; font-weight: 800; padding: 14px 18px; border: 0;
      border-radius: 18px; cursor: pointer; box-shadow: 0 5px 0 var(--kid-shadow); color: #fff;
    }
    .big:active { transform: translateY(3px); box-shadow: 0 2px 0 var(--kid-shadow); }
    .big:disabled { opacity: .5; cursor: default; }
    .hint { background: var(--kid-yellow); color: #3d2c00; }
    .next { background: var(--kid-green); }
    @keyframes wiggle { 25% { transform: translateX(-6px); } 75% { transform: translateX(6px); } }
    @keyframes hop { 40% { transform: translateY(-10px) rotate(-8deg); } }
    @media (max-width: 760px) {
      .puzzle { flex-direction: column; align-items: center; gap: 12px; }
      .side { max-width: min(92vw, 640px); width: 100%; }
      .bubble { font-size: 1.1rem; }
    }
  `],
})
export class KidsPuzzleComponent {
  readonly task = input.required<KidsTask>();
  /** Kurs-Kommentare je Halbzug; der nach dem letzten Zug erscheint, wenn die Aufgabe geloest ist. */
  readonly moveComments = input<Record<number, string> | null>(null);

  /** Die Aufgabe ist geloest — mit der Zahl der Fehler (Tipps zaehlen mit). */
  readonly solved = output<{ mistakes: number }>();
  /** Das Kind will weiter. */
  readonly next = output<void>();

  readonly fen = signal('8/8/8/8/8/8/8/8 w - - 0 1');
  readonly orientation = signal<'white' | 'black'>('white');
  readonly turnColor = signal<'white' | 'black'>('white');
  readonly dests = signal<Map<Key, Key[]>>(new Map());
  readonly lastMove = signal<[Key, Key] | undefined>(undefined);
  readonly check = signal(false);
  readonly shapes = signal<DrawShape[]>([]);
  readonly status = signal<KidsPuzzleStatus>('watch');
  readonly solvedKey = signal(SOLVED_KEYS[0]);
  readonly finalComment = signal<string | null>(null);
  readonly interactive = computed(() => this.status() === 'yourTurn' || this.status() === 'good'
    || this.status() === 'wrong' || this.status() === 'alternative');

  private solver: KidsSolver | null = null;
  private mistakes = 0;
  private hintLevel = 0;
  private timers: ReturnType<typeof setTimeout>[] = [];

  constructor() {
    effect(() => {
      const task = this.task();
      untracked(() => this.start(task));
    });
    inject(DestroyRef).onDestroy(() => this.clearTimers());
  }

  private start(task: KidsTask): void {
    this.clearTimers();
    this.solver = new KidsSolver(task);
    this.mistakes = 0;
    this.hintLevel = 0;
    this.finalComment.set(null);
    this.shapes.set([]);
    this.lastMove.set(undefined);
    this.orientation.set(this.solver.solverColor);
    this.status.set(this.solver.hasPendingSetup() ? 'watch' : 'yourTurn');
    this.sync();
    if (this.solver.hasPendingSetup()) {
      this.later(SETUP_DELAY_MS, () => {
        const move = this.solver?.playSetup();
        if (move) this.lastMove.set(toKeys(move));
        this.status.set('yourTurn');
        this.sync();
      });
    }
  }

  onMove(event: { orig: Key; dest: Key; promotion?: string }): void {
    const solver = this.solver;
    if (!solver || !this.interactive()) return;
    this.shapes.set([]);
    const result = solver.tryMove(event.orig, event.dest, event.promotion);
    switch (result) {
      case 'wrong':
        this.mistakes++;
        this.status.set('wrong');
        this.sync();
        return;
      case 'alternative':
        this.status.set('alternative');
        this.sync();
        return;
      case 'illegal':
        this.sync();
        return;
      case 'solved':
        this.lastMove.set([event.orig, event.dest]);
        this.finish();
        return;
      case 'continue':
        this.lastMove.set([event.orig, event.dest]);
        this.hintLevel = 0;
        this.status.set('good');
        this.sync();
        this.later(REPLY_DELAY_MS, () => {
          const reply = this.solver?.playReply();
          if (reply) this.lastMove.set(toKeys(reply));
          if (this.solver?.isFinished()) {
            this.finish();
          } else {
            this.sync();
          }
        });
        return;
    }
  }

  /** Erster Druck: die Figur leuchtet. Zweiter Druck: der Pfeil zeigt den Zug. Jeder Tipp zaehlt als Fehler. */
  showHint(): void {
    const hint = this.solver?.hint();
    if (!hint) return;
    this.mistakes++;
    this.hintLevel = Math.min(this.hintLevel + 1, 2);
    this.shapes.set(this.hintLevel === 1
      ? [{ orig: hint.from as Key, brush: 'yellow' }]
      : [{ orig: hint.from as Key, dest: hint.to as Key, brush: 'green' }]);
  }

  private finish(): void {
    const solver = this.solver;
    this.solvedKey.set(SOLVED_KEYS[Math.floor(Math.random() * SOLVED_KEYS.length)]);
    const comments = this.moveComments();
    this.finalComment.set(solver && comments ? (comments[solver.lastPly()] ?? null) : null);
    this.status.set('solved');
    this.sync();
    this.solved.emit({ mistakes: this.mistakes });
  }

  /** Brett auf den Stand des Solvers bringen. Ein NEUES dests-Objekt auch bei gleicher Stellung:
   *  erst dann setzt das Brett einen falschen Zug optisch zurueck (es reagiert auf Eingabe-Aenderungen). */
  private sync(): void {
    const solver = this.solver;
    if (!solver) return;
    this.fen.set(solver.fen());
    this.turnColor.set(solver.turnColor());
    this.check.set(solver.inCheck());
    this.dests.set(solver.dests() as Map<Key, Key[]>);
  }

  private later(ms: number, fn: () => void): void {
    this.timers.push(setTimeout(fn, ms));
  }

  private clearTimers(): void {
    this.timers.forEach(t => clearTimeout(t));
    this.timers = [];
  }
}

function toKeys(move: KidsMove): [Key, Key] {
  return [move.from as Key, move.to as Key];
}
