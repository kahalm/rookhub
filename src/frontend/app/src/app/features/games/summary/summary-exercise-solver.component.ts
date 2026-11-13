import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { PuzzleBoardComponent } from '../../puzzles/puzzle-board.component';
import { PuzzleService } from '../../puzzles/puzzle.service';
import { PreferencesService } from '../../../core/preferences.service';
import { AuthService } from '../../../core/auth.service';
import { LineSolver } from '../../../shared/chess/line-solver';
import { SummaryExercise } from './club-game-summary.service';

/** Stand des Lösers: am Zug, Fehlzug (Brett nimmt ihn gleich zurück), gelöst, Lösung gezeigt. */
export type ExerciseStatus = 'play' | 'wrong' | 'solved' | 'shown';

/** Wie lange Gegnerantwort bzw. zurückgenommener Fehlzug stehen bleiben (ms). */
const REPLY_MS = 450;
const UNDO_MS = 700;

/**
 * Löser der „Empfohlenen Übungen“ (0.960.0) — klein und ohne Modi: Lichess-Aufgabe über den gemeinsamen {@link LineSolver}
 * (Vorspiel = Zug des Gegners), Brett {@link PuzzleBoardComponent}. Richtig → Gegner antwortet; falsch → der Zug steht kurz und
 * wird zurückgenommen; „Lösung zeigen“ spielt den Rest vor; „Weiter“ geht zur nächsten Aufgabe der Stelle. Beim letzten Zug zählt
 * wie bei Lichess jedes Matt. Angemeldet wird der ERSTE Ausgang je Aufgabe wie im normalen Löser verbucht
 * (`POST /api/puzzles/{id}/attempt`), anonym nur gelöst.
 */
@Component({
  selector: 'app-summary-exercise-solver',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PuzzleBoardComponent, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="solver">
      <div class="board">
        <app-puzzle-board [fen]="fen()" [orientation]="orientation()" [turnColor]="turn()" [dests]="dests()"
                          [lastMove]="lastMove()" [check]="check()" [viewOnly]="status() !== 'play'"
                          [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet"
                          [allowFullscreen]="false" (moveMade)="onMove($event)" />
      </div>
      <p class="status" [class.ok]="status() === 'solved'" [class.bad]="status() === 'wrong'" role="status">
        @switch (status()) {
          @case ('solved') { <mat-icon>check_circle</mat-icon> {{ 'games.summary.exercises.solved' | translate }} }
          @case ('wrong') { <mat-icon>cancel</mat-icon> {{ 'games.summary.exercises.wrong' | translate }} }
          @case ('shown') { <mat-icon>visibility</mat-icon> {{ 'games.summary.exercises.shown' | translate }} }
          @default { {{ (orientation() === 'white' ? 'common.whiteToMove' : 'common.blackToMove') | translate }} }
        }
      </p>
      <p class="meta">{{ 'games.summary.exercises.counter' | translate: { i: index() + 1, n: exercises().length } }}
        · {{ 'games.summary.exercises.rating' | translate: { rating: current()?.rating } }}</p>
      <div class="actions">
        @if (status() === 'play' || status() === 'wrong') {
          <button mat-stroked-button type="button" class="show" (click)="showSolution()">{{ 'games.summary.exercises.showSolution' | translate }}</button>
        } @else {
          <button mat-stroked-button type="button" class="retry" (click)="retry()">{{ 'games.summary.exercises.retry' | translate }}</button>
        }
        @if (exercises().length > 1) {
          <button mat-flat-button color="primary" type="button" class="next" (click)="next()">{{ 'common.next' | translate }}</button>
        }
      </div>
    </div>
  `,
  styles: [`
    .solver { display: flex; flex-direction: column; gap: 6px; }
    .board { width: 100%; max-width: 420px; margin: 0 auto; }
    .status { display: flex; align-items: center; gap: 6px; margin: 4px 0 0; font-weight: 500; min-height: 24px; }
    .status.ok { color: var(--rh-success); }
    .status.bad { color: var(--rh-error); }
    .meta { margin: 0; font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; }
  `],
})
export class SummaryExerciseSolverComponent {
  readonly exercises = input.required<SummaryExercise[]>();
  readonly index = model(0);
  /** Erster Ausgang je Aufgabe (gelöst oder nicht) — für die Häkchen an den Karten. */
  readonly result = output<{ id: number; solved: boolean }>();

  readonly preferences = inject(PreferencesService);
  private readonly puzzles = inject(PuzzleService);
  private readonly auth = inject(AuthService);

  readonly fen = signal('8/8/8/8/8/8/8/8 w - - 0 1');
  readonly orientation = signal<'white' | 'black'>('white');
  readonly turn = signal<'white' | 'black'>('white');
  readonly dests = signal<Map<Key, Key[]>>(new Map());
  readonly lastMove = signal<[Key, Key] | undefined>(undefined);
  readonly check = signal(false);
  readonly status = signal<ExerciseStatus>('play');
  readonly current = signal<SummaryExercise | null>(null);

  private solver: LineSolver | null = null;
  private startedAt = 0;
  private readonly reported = new Set<number>();
  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const ex = this.exercises()[this.index()];
      untracked(() => this.setup(ex ?? null));
    });
    inject(DestroyRef).onDestroy(() => this.clearTimer());
  }

  private setup(ex: SummaryExercise | null): void {
    this.clearTimer();
    this.current.set(ex);
    this.solver = ex ? new LineSolver({ fen: ex.fen, line: ex.moves.map(uci => ({ uci })), startPly: 1 }) : null;
    this.status.set('play');
    this.startedAt = Date.now();
    this.sync();
  }

  private sync(): void {
    const s = this.solver;
    if (!s) return;
    this.fen.set(s.chess.fen());
    this.orientation.set(s.solverColor === 'w' ? 'white' : 'black');
    this.turn.set(s.chess.turn() === 'w' ? 'white' : 'black');
    this.lastMove.set(s.lastMove());
    this.check.set(s.chess.inCheck());
    this.dests.set(this.status() === 'play' && s.userToMove ? s.dests() : new Map());
  }

  onMove(m: { orig: Key; dest: Key; promotion?: string }): void {
    const s = this.solver;
    if (!s || this.status() !== 'play') return;
    const verdict = s.judge(m.orig, m.dest, m.promotion);
    if (verdict === 'correct' || verdict === 'alternative') {
      s.playExpected();
      this.afterGoodMove();
      return;
    }
    if (verdict !== 'wrong') { this.sync(); return; }
    // Wie Lichess: beim letzten Zug zählt jedes Matt.
    if (s.ply === s.line.length - 1 && this.mates(s.chess.fen(), m)) {
      s.playFree(m.orig, m.dest, m.promotion);
      this.finish(true);
      return;
    }
    s.playFree(m.orig, m.dest, m.promotion);
    this.status.set('wrong');
    this.report(false);
    this.sync();
    this.timer = setTimeout(() => { this.timer = null; s.undo(1); this.status.set('play'); this.sync(); }, UNDO_MS);
  }

  private mates(fen: string, m: { orig: Key; dest: Key; promotion?: string }): boolean {
    try {
      const probe = new Chess(fen);
      probe.move({ from: m.orig, to: m.dest, promotion: m.promotion ?? 'q' });
      return probe.isCheckmate();
    } catch { return false; }
  }

  private afterGoodMove(): void {
    const s = this.solver!;
    if (s.done) { this.finish(true); return; }
    this.sync();
    this.dests.set(new Map());
    this.timer = setTimeout(() => {
      this.timer = null;
      s.opponentReply();
      if (s.done) this.finish(true); else this.sync();
    }, REPLY_MS);
  }

  private finish(solved: boolean): void {
    this.status.set(solved ? 'solved' : 'shown');
    this.report(solved);
    this.sync();
  }

  /** Spielt den Rest der Lösung Zug für Zug vor. */
  showSolution(): void {
    const s = this.solver;
    if (!s) return;
    this.clearTimer();
    if (this.status() === 'wrong') s.undo(1);   // der Fehlzug stand noch (Rücknahme-Timer eben gestoppt)
    this.report(false);
    this.status.set('shown');
    const step = () => {
      if (s.done) { this.sync(); return; }
      s.playExpected();
      this.sync();
      this.timer = setTimeout(step, REPLY_MS);
    };
    step();
  }

  retry(): void {
    this.clearTimer();
    this.solver?.reset();
    this.status.set('play');
    this.sync();
  }

  next(): void {
    const n = this.exercises().length;
    if (n > 0) this.index.set((this.index() + 1) % n);
  }

  private report(solved: boolean): void {
    const ex = this.current();
    if (!ex || this.reported.has(ex.id)) return;
    this.reported.add(ex.id);
    this.result.emit({ id: ex.id, solved });
    if (!this.auth.isLoggedIn) return;
    const seconds = Math.max(1, Math.round((Date.now() - this.startedAt) / 1000));
    // Wie der normale Löser verbuchen; ein Fehlschlag hier ist still (Hintergrund-Meldung, die Übung bleibt lösbar).
    this.puzzles.recordAttempt(ex.id, solved, seconds).subscribe({ error: () => undefined });
  }

  private clearTimer(): void {
    if (this.timer !== null) { clearTimeout(this.timer); this.timer = null; }
  }
}
