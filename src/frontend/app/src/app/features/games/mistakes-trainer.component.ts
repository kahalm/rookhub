import { ChangeDetectionStrategy, Component, Inject, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { fenAfterUci } from '../../shared/pgn-viewer/board-moves.util';
import { sameMove } from '../../shared/chess/line-solver';
import { PreferencesService } from '../../core/preferences.service';
import { MOVE_CLASS_COLORS, MoveClass, formatEval } from './game-review.util';
import { Mistake, MistakesBySide } from './mistakes.util';

export interface MistakesTrainerData {
  bySide: MistakesBySide;
  side: 'white' | 'black';
}

/** ask = du bist dran · wrong = daneben · right = selbst gefunden · shown = Lösung gezeigt · done = durch. */
type Phase = 'ask' | 'right' | 'wrong' | 'shown' | 'done';

/**
 * „Eigene Fehler nachspielen" — der Trainer zur Partie, analog zu Lichess' „Aus deinen Fehlern lernen":
 * Stellung VOR dem eigenen Fehler, der gespielte Zug steht daneben, gesucht ist der bessere. Die Aufgaben
 * stellt `mistakes.util.ts` aus RookHubs eigener Partie-Analyse zusammen; hier wird nur noch gespielt.
 *
 * Bewusst ein Dialog und keine zweite Seite: die Partie ist schon geladen, und nach dem Schließen steht
 * man wieder genau dort, wo man war. Geurteilt wird mit `sameMove` aus dem gemeinsamen Löser-Kern —
 * das Brett wandelt ohne Rückfrage in eine Dame um, und die Regel dort lässt eine fehlende
 * Umwandlungsfigur gelten (eine Unterverwandlung als Lösung ließe sich auf diesem Brett gar nicht
 * eingeben). Als richtig zählt der Zug der Engine; ein anderer, ähnlich guter Zug gilt hier nicht —
 * dafür gibt es den Analysieren-Knopf auf der Seite.
 */
@Component({
  selector: 'app-mistakes-trainer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [MatButtonModule, MatIconModule, MatDialogModule, TranslatePipe, ChessBoardComponent],
  template: `
    <h2 class="dialog-title">
      {{ 'games.mistakes.title' | translate }}
      @if (bothSides) {
        <span class="sides">
          @for (s of SIDES; track s) {
            <button mat-button [class.active]="side() === s" (click)="chooseSide(s)">
              {{ ('games.review.' + s) | translate }} ({{ (s === 'white' ? data.bySide.white : data.bySide.black).length }})
            </button>
          }
        </span>
      }
    </h2>

    @if (list().length === 0) {
      <p class="empty">{{ 'games.mistakes.none' | translate }}</p>
      <div class="dialog-actions"><span class="spacer"></span><button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button></div>
    } @else if (phase() === 'done') {
      <p class="summary">{{ 'games.mistakes.summary' | translate: { solved: solved(), total: list().length } }}</p>
      <div class="dialog-actions">
        <button mat-stroked-button (click)="restart()"><mat-icon>replay</mat-icon> {{ 'games.mistakes.again' | translate }}</button>
        <span class="spacer"></span>
        <button mat-flat-button color="primary" mat-dialog-close>{{ 'common.close' | translate }}</button>
      </div>
    } @else if (current(); as m) {
      <div class="head">
        <span class="progress">{{ 'games.mistakes.progress' | translate: { current: index() + 1, total: list().length } }}</span>
        <span class="badge" [style.background]="color(m.cls)">{{ ('games.review.class.' + m.cls) | translate }}</span>
        <span class="evals">{{ fmt(m) }}</span>
      </div>

      <div class="board-wrap">
        <app-chess-board [fen]="boardFen()" [lastMove]="lastMove()" [flipped]="flipped()"
                         [playable]="phase() === 'ask'" (userMove)="onMove($event)"
                         [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
      </div>

      <p class="prompt" [class.right]="phase() === 'right'" [class.wrong]="phase() === 'wrong'">
        @switch (phase()) {
          @case ('ask') { {{ 'games.mistakes.prompt' | translate: { san: m.playedSan } }} }
          @case ('wrong') { {{ 'games.mistakes.wrong' | translate: { san: tried() } }} }
          @case ('right') { {{ 'games.mistakes.right' | translate: { san: m.bestSan } }} }
          @case ('shown') { {{ 'games.mistakes.shown' | translate: { san: m.bestSan } }} }
        }
      </p>
      @if (phase() === 'right' || phase() === 'shown') {
        <p class="cost">{{ 'games.mistakes.cost' | translate: { san: m.playedSan, percent: lost(m) } }}</p>
      }

      <div class="dialog-actions">
        @if (phase() === 'wrong') {
          <button mat-stroked-button (click)="retry()"><mat-icon>refresh</mat-icon> {{ 'games.mistakes.retry' | translate }}</button>
        }
        @if (phase() === 'ask' || phase() === 'wrong') {
          <button mat-button (click)="showSolution()">{{ 'games.mistakes.show' | translate }}</button>
          <span class="spacer"></span>
          <button mat-button (click)="next()">{{ 'games.mistakes.skip' | translate }}</button>
        } @else {
          <span class="spacer"></span>
          <button mat-flat-button color="primary" (click)="next()">
            {{ (last() ? 'games.mistakes.finish' : 'games.mistakes.next') | translate }}
          </button>
        }
      </div>
    }
  `,
  styles: [`
    :host { display: block; padding: 1rem 1.25rem 0.75rem; max-width: min(460px, 92vw); }
    .dialog-title { margin: 0 0 0.5rem; font-size: 1.15rem; display: flex; flex-wrap: wrap; align-items: center; gap: 4px 10px; }
    .sides { display: flex; gap: 2px; margin-left: auto; }
    .sides button { min-width: 0; padding: 0 8px; font-size: 0.8rem; opacity: 0.6; }
    .sides button.active { opacity: 1; font-weight: 600; }
    .head { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; font-size: 0.85rem; margin-bottom: 6px; }
    .badge { padding: 1px 8px; border-radius: 10px; color: #fff; font-weight: 600; text-shadow: 0 1px 1px rgba(0, 0, 0, 0.45); }
    .evals { font-variant-numeric: tabular-nums; color: color-mix(in srgb, currentColor 70%, transparent); }
    .board-wrap { width: min(400px, 78vw); margin: 0 auto; }
    .prompt { margin: 8px 0 0; min-height: 2.4em; }
    .prompt.right { color: #2e7d32; font-weight: 500; }
    .prompt.wrong { color: #c62828; }
    .cost { margin: 2px 0 0; font-size: 0.85rem; color: color-mix(in srgb, currentColor 70%, transparent); }
    .empty, .summary { margin: 12px 0; }
    .dialog-actions { display: flex; align-items: center; gap: 8px; margin-top: 10px; flex-wrap: wrap; }
    .spacer { flex: 1 1 auto; }
  `],
})
export class MistakesTrainerComponent {
  readonly SIDES: readonly ('white' | 'black')[] = ['white', 'black'];
  readonly preferences = inject(PreferencesService);

  readonly side = signal<'white' | 'black'>('white');
  readonly index = signal(0);
  readonly phase = signal<Phase>('ask');
  readonly boardFen = signal('');
  readonly lastMove = signal<[string, string] | undefined>(undefined);
  /** SAN des zuletzt danebengegangenen Zuges — steht in der Rückmeldung. */
  readonly tried = signal('');
  readonly solved = signal(0);

  readonly list = computed<Mistake[]>(() => this.side() === 'white' ? this.data.bySide.white : this.data.bySide.black);
  readonly current = computed<Mistake | null>(() => this.list()[this.index()] ?? null);
  readonly flipped = computed(() => this.side() === 'black');
  readonly last = computed(() => this.index() >= this.list().length - 1);
  readonly bothSides: boolean;

  /** Auf dieser Aufgabe schon danebengegriffen? Dann zählt sie nicht als selbst gefunden. */
  private missedHere = false;

  constructor(@Inject(MAT_DIALOG_DATA) public data: MistakesTrainerData) {
    this.bothSides = data.bySide.white.length > 0 && data.bySide.black.length > 0;
    this.side.set(data.side);
    this.start(0);
  }

  color(c: MoveClass): string { return MOVE_CLASS_COLORS[c]; }
  fmt(m: Mistake): string { return `${formatEval(m.evalBefore)} → ${formatEval(m.evalAfter)}`; }
  lost(m: Mistake): string { return m.lostPercent.toFixed(1); }

  onMove(e: UserBoardMove): void {
    const m = this.current();
    if (!m || this.phase() !== 'ask') return;
    this.boardFen.set(e.fen);
    this.lastMove.set([e.from, e.to]);
    if (sameMove(e.from + e.to, m.bestUci)) {
      if (!this.missedHere) this.solved.update(n => n + 1);
      this.phase.set('right');
    } else {
      this.missedHere = true;
      this.tried.set(e.san);
      this.phase.set('wrong');
    }
  }

  /** Zurück auf die Ausgangsstellung — das Brett übernimmt den Nutzerzug selbst, es MUSS neu gebunden werden. */
  retry(): void {
    const m = this.current();
    if (!m) return;
    this.boardFen.set(m.fenBefore);
    this.lastMove.set(undefined);
    this.phase.set('ask');
  }

  showSolution(): void {
    const m = this.current();
    if (!m) return;
    this.missedHere = true;
    this.boardFen.set(fenAfterUci(m.fenBefore, m.bestUci) ?? m.fenBefore);
    this.lastMove.set([m.bestUci.slice(0, 2), m.bestUci.slice(2, 4)]);
    this.phase.set('shown');
  }

  next(): void { this.start(this.index() + 1); }

  restart(): void {
    this.solved.set(0);
    this.start(0);
  }

  chooseSide(s: 'white' | 'black'): void {
    if (s === this.side()) return;
    this.side.set(s);
    this.solved.set(0);
    this.start(0);
  }

  private start(i: number): void {
    const m = this.list()[i] ?? null;
    this.index.set(i);
    this.missedHere = false;
    this.tried.set('');
    this.lastMove.set(undefined);
    this.boardFen.set(m?.fenBefore ?? '');
    this.phase.set(m ? 'ask' : 'done');
  }
}
