import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { BreakpointObserver } from '@angular/cdk/layout';
import { TranslatePipe } from '@ngx-translate/core';
import { ChessBoardComponent } from '../../../shared/pgn-viewer/chess-board.component';
import { PreferencesService } from '../../../core/preferences.service';
import { applyUci, tryLoadFen } from '../../../shared/chess/puzzle-move.util';
import { RouterLink } from '@angular/router';
import { SummaryExercise, SummaryExerciseGroup, exerciseKey, exerciseReaderQuery, exerciseSolvable } from './club-game-summary.service';
import { SummaryExerciseSolverComponent } from './summary-exercise-solver.component';
import { PuzzleThemePipe } from '../../puzzles/puzzle-theme.pipe';

/** So viele Stellen sind offen; der Rest liegt hinter „Weitere Stellen“. */
export const OPEN_GROUPS = 3;

/** Zugnummer einer Stellung VOR dem Halbzug `ply` (Partie ab der Grundstellung): „15.“ bzw. „15…“. */
export function exerciseMoveLabel(ply: number): string {
  return `${Math.floor(ply / 2) + 1}${ply % 2 === 0 ? '.' : '…'}`;
}

/** Karte einer Aufgabe: Stellung NACH dem Zug des Gegners (Buch: die Diagrammstellung), Blick aus Sicht des Lösers. `solveIndex` =
 * Platz unter den lösbaren Aufgaben der Stelle (`null` = nur „Beispiel im Buch“, die Karte führt in den Leser). */
interface ExerciseCard {
  ex: SummaryExercise; key: string; fen: string; lastMove?: [string, string]; flipped: boolean; solveIndex: number | null;
  query: Record<string, string | number>;
}

function cardOf(ex: SummaryExercise, solveIndex: number | null): ExerciseCard {
  const base = { ex, key: exerciseKey(ex), solveIndex, query: exerciseReaderQuery(ex) };
  const chess = tryLoadFen(ex.fen);
  if (ex.source === 'book') return { ...base, fen: ex.fen, flipped: chess?.turn() === 'b' };
  const first = ex.moves[0];
  if (!chess || !first) return { ...base, fen: ex.fen, flipped: false };
  try { applyUci(chess, first); } catch { return { ...base, fen: ex.fen, flipped: false }; }
  return { ...base, fen: chess.fen(), lastMove: [first.slice(0, 2), first.slice(2, 4)], flipped: chess.turn() === 'b' };
}

/** Lösbare Aufgaben einer Stelle in Kartenreihenfolge (der Löser blättert nur durch diese). */
function solvableOf(exercises: SummaryExercise[]): SummaryExercise[] { return exercises.filter(exerciseSolvable); }

/**
 * „Empfohlene Übungen“ am Ende der Zusammenfassung (0.960.0, Wunsch 2026-11-13): je Stelle der Partie (gespieltes oder verpasstes
 * Matt, seit 0.961.0 auch verpasste oder zugelassene Taktik wie Gabel, Fesselung, Spieß) die Lichess-Aufgaben mit demselben Bild bzw.
 * Motiv, als Karten mit Brett — seit 0.962.0 zuerst Diagramme aus Büchern, die der Betrachter lesen darf (Quelle + Sprung in den Leser
 * an der Karte; ohne bestätigte Buchlösung nur als „Beispiel im Buch“). Ein Klick löst direkt auf der Seite — ab 900 px im
 * Abschnitt über den Karten, darunter als Blatt (wie das Brett der Zusammenfassung, DESIGN 5.6). Ohne Stellen zeigt die Seite den
 * Abschnitt gar nicht.
 */
@Component({
  selector: 'app-summary-exercises',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent, MatButtonModule, MatIconModule, TranslatePipe, SummaryExerciseSolverComponent, PuzzleThemePipe, RouterLink],
  template: `
    <section class="exercises">
      <h2>{{ 'games.summary.exercises.title' | translate }}</h2>
      <p class="intro">{{ 'games.summary.exercises.intro' | translate }}</p>
      @for (g of visible(); track g.group.ply; let gi = $index) {
        <div class="group">
          <h3>
            <button type="button" class="ply" (click)="showPly.emit(g.group.ply)">{{ moveLabel(g.group.ply) }}</button>
            {{ ('games.summary.exercises.reason.' + g.group.reason) | translate: { n: g.group.mateIn ?? 1, theme: (g.group.theme | puzzleTheme) } }}
          </h3>
          @if (g.group.alsoPlies.length) {
            <p class="also">{{ 'games.summary.exercises.also' | translate: { moves: alsoLabel(g.group.alsoPlies) } }}</p>
          }
          @if (open()?.group === gi) {
            <div class="inline-solver">
              <app-summary-exercise-solver [exercises]="g.solvable" [(index)]="openIndex" (result)="mark($event)" />
              <button mat-button type="button" class="close-solver" (click)="open.set(null)">
                <mat-icon>close</mat-icon> {{ 'common.close' | translate }}
              </button>
            </div>
          }
          <div class="cards">
            @for (c of g.cards; track c.key; let i = $index) {
              <div class="card" [class.active]="c.solveIndex !== null && open()?.group === gi && openIndex() === c.solveIndex">
                @if (c.solveIndex !== null) {
                  <button type="button" class="card-hit" (click)="solve(gi, c.solveIndex)"
                          [attr.aria-label]="('games.summary.exercises.solve' | translate) + ' ' + (i + 1)">
                    <app-chess-board [fen]="c.fen" [lastMove]="c.lastMove" [flipped]="c.flipped"
                                     [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                    <span class="card-meta">
                      @if (results().get(c.key); as r) { <mat-icon class="mark" [class.ok]="r === 'solved'">{{ r === 'solved' ? 'check_circle' : 'cancel' }}</mat-icon> }
                      <span>{{ ('games.summary.exercises.match.' + matchKey(c.ex.match, g.group)) | translate: { n: g.group.mateIn ?? 1 } }}</span>
                      @if (!c.ex.book) { <span class="src">Lichess · {{ c.ex.rating }}</span> }
                    </span>
                  </button>
                } @else {
                  <a class="card-hit" [routerLink]="['/ebooks', c.ex.book!.ebookId]" [queryParams]="c.query">
                    <app-chess-board [fen]="c.fen" [flipped]="c.flipped" [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                    <span class="card-meta">
                      <span>{{ ('games.summary.exercises.match.' + matchKey(c.ex.match, g.group)) | translate: { n: g.group.mateIn ?? 1 } }}</span>
                      <span class="src">{{ 'games.summary.exercises.bookExample' | translate }}</span>
                    </span>
                  </a>
                }
                @if (c.ex.book; as b) {
                  <a class="book" [routerLink]="['/ebooks', b.ebookId]" [queryParams]="c.query">
                    <mat-icon aria-hidden="true">menu_book</mat-icon>
                    <span><em>{{ b.title }}</em>@if (b.author) { · {{ b.author }} }
                      · @if (b.chapter) { {{ b.chapter }} } @else { {{ (b.pdfPage ? 'games.summary.exercises.bookPage' : 'games.summary.exercises.bookPart') | translate: { n: b.page } }} }</span>
                  </a>
                }
              </div>
            }
          </div>
        </div>
      }
      @if (hidden() > 0) {
        <button mat-stroked-button type="button" class="more" (click)="expanded.set(true)">
          {{ 'games.summary.exercises.more' | translate: { count: hidden() } }}
        </button>
      }
    </section>
  `,
  styles: [`
    .exercises h2 { font-size: 1.25rem; margin: 24px 0 8px; }
    .intro, .also { color: var(--mat-sys-on-surface-variant); font-size: 0.875rem; margin: 0 0 8px; }
    .group { margin: 12px 0 16px; }
    .group h3 { font-size: 1.05rem; margin: 12px 0 6px; }
    .ply { all: unset; cursor: pointer; font-weight: 700; color: var(--rh-accent); text-decoration: underline dotted; text-underline-offset: 2px; }
    .ply:focus-visible { outline: 2px solid var(--rh-accent); }
    .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 10px; }
    .card { box-sizing: border-box; display: flex; flex-direction: column; gap: 4px; padding: 6px;
      border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; min-width: 0; }
    .card:hover, .card:focus-within { border-color: var(--rh-accent); }
    .card.active { border-color: var(--rh-accent); box-shadow: 0 0 0 1px var(--rh-accent); }
    .card-hit { all: unset; box-sizing: border-box; cursor: pointer; display: flex; flex-direction: column; gap: 4px; min-width: 0; color: inherit; }
    .card-hit:focus-visible { outline: 2px solid var(--rh-accent); outline-offset: 2px; }
    .card app-chess-board { pointer-events: none; }
    .card-meta { display: flex; flex-wrap: wrap; align-items: center; gap: 2px 6px; font-size: 0.8rem; }
    .book { display: flex; align-items: flex-start; gap: 4px; font-size: 0.75rem; color: var(--mat-sys-on-surface-variant); text-decoration: none;
      overflow-wrap: anywhere; }
    .book:hover, .book:focus-visible { color: var(--rh-accent); text-decoration: underline; }
    .book mat-icon { font-size: 16px; width: 16px; height: 16px; flex: none; }
    .src { color: var(--mat-sys-on-surface-variant); }
    .mark { font-size: 18px; width: 18px; height: 18px; color: var(--rh-error); }
    .mark.ok { color: var(--rh-success); }
    .inline-solver { margin: 8px 0 12px; padding: 12px; border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; }
    .close-solver { margin-top: 4px; }
  `],
})
export class SummaryExercisesComponent {
  readonly groups = input.required<SummaryExerciseGroup[]>();
  /** Klick auf die Zugnummer einer Stelle → die Seite zeigt die Stellung am Brett. */
  readonly showPly = output<number>();

  readonly preferences = inject(PreferencesService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);

  readonly expanded = signal(false);
  readonly open = signal<{ group: number } | null>(null);
  readonly openIndex = signal(0);
  /** Erster Ausgang je Aufgabe, Schlüssel {@link exerciseKey}. */
  readonly results = signal<ReadonlyMap<string, 'solved' | 'failed'>>(new Map());

  private readonly all = computed(() => this.groups().map(group => {
    const solvable = solvableOf(group.exercises);
    return { group, solvable, cards: group.exercises.map(ex => cardOf(ex, exerciseSolvable(ex) ? solvable.indexOf(ex) : null)) };
  }));
  readonly visible = computed(() => this.expanded() ? this.all() : this.all().slice(0, OPEN_GROUPS));
  readonly hidden = computed(() => this.all().length - this.visible().length);

  moveLabel(ply: number): string { return exerciseMoveLabel(ply); }

  /** Art der Übereinstimmung: bei Mattbildern strict/family/theme, bei taktischen Stellen „gleiches Motiv, gleiche Figuren“ bzw. „gleiches Motiv“. */
  matchKey(match: SummaryExercise['match'], group: SummaryExerciseGroup): string {
    if (!group.theme) return match;
    return match === 'strict' ? 'strictTactic' : 'sameTheme';
  }
  alsoLabel(plies: number[]): string { return plies.map(exerciseMoveLabel).join(', '); }

  solve(group: number, index: number): void {
    const g = this.visible()[group];
    if (!g || !g.solvable[index]) return;
    if (this.breakpoints.isMatched('(max-width: 899.98px)')) {
      SummaryExerciseSheetComponent.open(this.dialog, { exercises: g.solvable, index, onResult: r => this.mark(r) });
      return;
    }
    this.openIndex.set(index);
    this.open.set({ group });
  }

  mark(r: { key: string; solved: boolean }): void {
    this.results.update(m => new Map(m).set(r.key, r.solved ? 'solved' : 'failed'));
  }
}

export interface SummaryExerciseSheetData { exercises: SummaryExercise[]; index: number; onResult(r: { key: string; solved: boolean }): void; }

const SWIPE_CLOSE_PX = 80;

/** Löser als Blatt am Handy — dasselbe Muster wie {@link SummaryBoardSheetComponent} (Griff, X, Wischen, Zurück-Taste). */
@Component({
  selector: 'app-summary-exercise-sheet',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SummaryExerciseSolverComponent, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="sheet-head" (touchstart)="touchStart($event)" (touchend)="touchEnd($event)">
      <span class="grip" aria-hidden="true"></span>
      <h2 class="sheet-title">{{ 'games.summary.exercises.title' | translate }}</h2>
      <button mat-icon-button type="button" class="sheet-close" (click)="close()" [attr.aria-label]="'common.close' | translate">
        <mat-icon>close</mat-icon>
      </button>
    </div>
    <div class="sheet-body">
      <app-summary-exercise-solver [exercises]="data.exercises" [(index)]="index" (result)="data.onResult($event)" />
    </div>
  `,
  styles: [`
    :host { display: flex; flex-direction: column; max-height: 100dvh; }
    .sheet-head { position: relative; display: flex; align-items: center; gap: 8px; padding: 14px 8px 4px 16px; touch-action: none; }
    .grip { position: absolute; top: 6px; left: 50%; width: 36px; height: 4px; margin-left: -18px; border-radius: 2px;
      background: color-mix(in srgb, currentColor 30%, transparent); }
    .sheet-title { flex: 1 1 auto; margin: 0; font-size: 1.05rem; }
    .sheet-body { overflow-y: auto; padding: 0 12px 12px; }
  `],
})
export class SummaryExerciseSheetComponent {
  readonly data = inject<SummaryExerciseSheetData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<SummaryExerciseSheetComponent>);
  readonly index = signal(this.data.index);
  private startY: number | null = null;

  close(): void { this.ref.close(); }
  touchStart(e: TouchEvent): void { this.startY = e.touches[0]?.clientY ?? null; }
  touchEnd(e: TouchEvent): void {
    const end = e.changedTouches[0]?.clientY;
    if (this.startY !== null && end !== undefined && end - this.startY > SWIPE_CLOSE_PX) this.close();
    this.startY = null;
  }

  static open(dialog: MatDialog, data: SummaryExerciseSheetData): MatDialogRef<SummaryExerciseSheetComponent> {
    const narrow = typeof matchMedia === 'function' && matchMedia('(max-width: 599.98px)').matches;
    history.pushState({ ...(history.state ?? {}), rhSummarySheet: true }, '');
    const ref = dialog.open(SummaryExerciseSheetComponent, {
      ...(narrow
        ? { width: '100vw', maxWidth: '100vw', position: { bottom: '0' }, panelClass: ['solve-mode-panel', 'solve-mode-sheet'] }
        : { width: '560px', maxWidth: '94vw', panelClass: 'solve-mode-panel' }),
      maxHeight: '100dvh', hasBackdrop: true, backdropClass: 'solve-mode-backdrop', autoFocus: 'dialog', data,
    });
    ref.afterClosed().subscribe(() => { if (history.state?.rhSummarySheet) history.back(); });
    return ref;
  }
}
