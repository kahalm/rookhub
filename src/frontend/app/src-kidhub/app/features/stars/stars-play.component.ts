import {
  ChangeDetectionStrategy, Component, DestroyRef, HostListener, computed, inject, signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { PuzzleBoardComponent } from '@rh/features/puzzles/puzzle-board.component';
import {
  Rng, STARS_PER_STAGE, STAR_STAGES, STAR_SVG, StarPiece, StarPuzzle, StarStage, generateStarPuzzle, reachable,
  squareIndex, squareName, starFen,
} from '../../core/kids-stars';
import { KidsStarsStore } from '../../core/kids-stars.store';
import { isAdvanceKey } from '../../core/kids-keys';
import { WRONG_HOLD_MS } from '../../shared/kids-puzzle.component';
import { KID_BACK, KID_SHORT, KID_STACKED } from '../../shared/kids-layout';

/** Figurenzeichen fuer Stufenkarte und Kopfzeile (weisse Figuren, wie auf dem Brett). */
export function pieceGlyph(piece: StarPiece): string {
  return { R: '♖', B: '♗', Q: '♕', N: '♘' }[piece];
}

/** Was die Eule sagt. */
export type StarsStatus = 'play' | 'good' | 'empty' | 'deadEnd' | 'solved';

/**
 * Eine Stufe der Sternenjagd: `STARS_PER_STAGE` Aufgaben hintereinander, jede frisch gewuerfelt (Regeln und Generator in
 * `kids-stars.ts`). Gezogen werden darf ueberall hin, wohin die Figur kommt — ein Zug auf ein leeres Feld oder auf
 * einen Stern, nach dem nicht mehr alle zu holen sind, bleibt `WRONG_HOLD_MS` stehen und wird zurueckgenommen
 * (wie in den Stufen). Tipps kosten nichts: erst leuchtet der naechste Stern, dann zeigt ein Pfeil den Zug.
 * Aller Anzeige-Zustand in Signalen (Timer, OnPush).
 */
@Component({
  selector: 'kid-stars-play',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, PuzzleBoardComponent],
  template: `
    <header class="head">
      <a class="back" routerLink="/stars">← {{ 'kids.stars.title' | translate }}</a>
      @if (stage(); as s) {
        <h1><span aria-hidden="true">{{ glyph(s.piece) }}</span> {{ 'kids.stars.stage' | translate: { stage: s.stage } }}
          · {{ 'kids.stars.piece.' + s.piece | translate }}</h1>
        @if (!complete()) {
          <span class="round">{{ 'kids.stars.round' | translate: { n: round() + 1, total: perStage } }}</span>
        }
      }
    </header>

    @if (locked()) {
      <p class="info">🔒 {{ 'kids.levels.lockedHint' | translate }}</p>
      <p class="info"><a routerLink="/stars">{{ 'kids.stars.title' | translate }}</a></p>
    } @else if (complete()) {
      <section class="complete">
        <div class="confetti" aria-hidden="true">🌟</div>
        <h2>{{ 'kids.stars.completed' | translate }}</h2>
        <div class="buttons">
          @if (nextStage(); as n) {
            <a class="btn primary" [routerLink]="['/stars', n]">{{ 'kids.levels.nextLevel' | translate }} ▶</a>
          }
          <button type="button" class="btn" (click)="again()">↻ {{ 'kids.levels.again' | translate }}</button>
          <a class="btn" routerLink="/stars">{{ 'kids.stars.title' | translate }}</a>
        </div>
      </section>
    } @else if (puzzle(); as p) {
      <div class="puzzle">
        <div class="task-slot">
          <p class="task">{{ 'kids.stars.task' | translate }}</p>
          <p class="left" [attr.aria-label]="'kids.stars.left' | translate: { count: starsLeft().length }">
            @for (s of p.solution; track s; let i = $index) {
              <span [class.eaten]="i < step()">{{ i < step() ? '✔' : '⭐' }}</span>
            }
          </p>
        </div>
        <div class="board">
          <app-puzzle-board
            [fen]="fen()"
            orientation="white"
            turnColor="white"
            [dests]="dests()"
            [lastMove]="lastMove()"
            [viewOnly]="!interactive()"
            [reviewShapes]="shapes()"
            [allowFullscreen]="false"
            boardTheme="blue"
            (moveMade)="onMove($event)" />
        </div>
        <div class="side">
          <div class="bubble" [class]="'bubble status-' + status()" role="status" aria-live="polite">
            <span class="owl" aria-hidden="true">🦉</span>
            <p>{{ 'kids.stars.feedback.' + status() | translate: { count: starsLeft().length } }}</p>
          </div>
          <div class="actions">
            @if (status() === 'solved') {
              <button type="button" class="big next" (click)="next()">
                {{ 'common.next' | translate }} ▶ <kbd class="key">{{ 'kids.spaceKey' | translate }}</kbd>
              </button>
            } @else {
              <button type="button" class="big hint" (click)="showHint()" [disabled]="!interactive()">
                💡 {{ 'kids.hint' | translate }}
              </button>
            }
          </div>
        </div>
      </div>
    } @else if (failed()) {
      <p class="info">{{ 'kids.loadError' | translate }}</p>
    }
  `,
  styles: [KID_BACK, `
    :host { display: block; max-width: 1320px; margin: 0 auto; padding: 8px 16px 24px; }
    .head { display: flex; align-items: center; gap: 8px 16px; flex-wrap: wrap; margin: 0 auto 12px;
            max-width: var(--kid-row, 1068px); }
    .head h1 { margin: 0; font-size: 1.5rem; color: var(--kid-title); }
    .round { margin-left: auto; font-size: 1.2rem; font-weight: 800; }
    .info { text-align: center; font-size: 1.2rem; }
    .puzzle {
      display: grid; justify-content: center; align-items: start; column-gap: 28px; row-gap: 14px;
      grid-template-columns: var(--kid-board, 640px) minmax(280px, 400px);
      grid-template-rows: auto 1fr;
      grid-template-areas: "board task" "board side";
    }
    .task-slot { grid-area: task; display: flex; flex-direction: column; gap: 6px; }
    .task { margin: 0; font-size: 1.5rem; font-weight: 800; line-height: 1.3; color: var(--kid-title); }
    .left { margin: 0; font-size: 1.6rem; letter-spacing: 4px; }
    .left .eaten { color: var(--kid-green-strong); font-weight: 800; }
    .board {
      grid-area: board; width: var(--kid-board, 640px);
      border-radius: 14px; overflow: hidden; box-shadow: 0 6px 0 var(--kid-shadow);
    }
    .board ::ng-deep .cg-wrap coords { font-size: 11px; opacity: 1; }
    .side { grid-area: side; display: flex; flex-direction: column; gap: 14px; }
    .bubble {
      display: flex; gap: 12px; align-items: center; padding: 14px 16px; border-radius: 22px;
      background: var(--kid-card); box-shadow: 0 4px 0 var(--kid-shadow); font-size: 1.25rem; font-weight: 700;
    }
    .bubble p { margin: 0; }
    .owl { font-size: 2.4rem; line-height: 1; }
    .status-good, .status-solved { background: var(--kid-good-bg); color: var(--kid-good-fg); }
    .status-empty, .status-deadEnd { background: var(--kid-bad-bg); color: var(--kid-bad-fg); animation: wiggle .35s; }
    .actions { display: flex; gap: 12px; }
    .big {
      flex: 1; font: inherit; font-size: 1.3rem; font-weight: 800; padding: 14px 18px; border: 0;
      border-radius: 18px; cursor: pointer; box-shadow: 0 5px 0 var(--kid-shadow); color: #fff;
    }
    .big:active { transform: translateY(3px); box-shadow: 0 2px 0 var(--kid-shadow); }
    .big:disabled { opacity: .5; cursor: default; }
    .hint { background: var(--kid-yellow); color: #3d2c00; }
    .next { background: var(--kid-green-strong); }
    .key { display: none; }
    @media (hover: hover) and (pointer: fine) {
      .key {
        display: inline-block; margin-left: 10px; padding: 1px 8px; border-radius: 6px; vertical-align: middle;
        font: inherit; font-size: .75rem; font-weight: 700; background: rgba(0, 0, 0, .2);
        border: 1px solid rgba(255, 255, 255, .6);
      }
    }
    .complete { text-align: center; padding: 30px 12px; }
    .confetti { font-size: 4.5rem; animation: pop .8s ease-out; }
    .complete h2 { font-size: 2.2rem; margin: 6px 0; color: var(--kid-title); }
    .buttons { display: flex; flex-wrap: wrap; gap: 12px; justify-content: center; margin-top: 16px; }
    .btn {
      font: inherit; font-size: 1.25rem; font-weight: 800; padding: 14px 22px; border-radius: 18px; border: 0;
      background: var(--kid-card); color: inherit; text-decoration: none; cursor: pointer; box-shadow: 0 5px 0 var(--kid-shadow);
    }
    .btn.primary { background: var(--kid-green-strong); color: #fff; }
    @keyframes wiggle { 25% { transform: translateX(-6px); } 75% { transform: translateX(6px); } }
    @keyframes pop { 0% { transform: scale(.2) rotate(-30deg); } 70% { transform: scale(1.2); } }
    @media ${KID_STACKED} {
      .puzzle {
        grid-template-columns: var(--kid-board, 640px);
        grid-template-rows: auto;
        grid-template-areas: "task" "board" "side";
        row-gap: 12px;
      }
      .board { width: 100%; min-width: 0; }
      .task, .left { text-align: center; }
      .task { font-size: 1.3rem; }
      .round { margin-left: 0; }
    }
    @media ${KID_SHORT} {
      .head { flex-wrap: nowrap; gap: 12px; }
      .head h1 { min-width: 0; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; font-size: 1.25rem; }
      .back, .round { flex-shrink: 0; white-space: nowrap; }
    }
  `],
})
export class StarsPlayComponent {
  readonly store = inject(KidsStarsStore);
  private readonly router = inject(Router);

  readonly perStage = STARS_PER_STAGE;
  readonly glyph = pieceGlyph;
  /** Zufall der Aufgaben — Tests setzen einen festen. */
  rng: Rng = Math.random;

  readonly stage = signal<StarStage | null>(null);
  readonly locked = signal(false);
  readonly failed = signal(false);
  readonly complete = signal(false);
  readonly round = signal(0);
  readonly puzzle = signal<StarPuzzle | null>(null);
  /** Gefressene Sterne der laufenden Aufgabe. */
  readonly step = signal(0);
  readonly status = signal<StarsStatus>('play');
  readonly holding = signal(false);
  readonly fen = signal('8/8/8/8/8/8/8/8 w - - 0 1');
  readonly lastMove = signal<[Key, Key] | undefined>(undefined);
  private readonly marks = signal<DrawShape[]>([]);
  private hintLevel = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;

  /** Wo die Figur steht. */
  private readonly pos = computed(() => {
    const p = this.puzzle();
    return p ? (this.step() === 0 ? p.start : p.solution[this.step() - 1]) : -1;
  });
  readonly starsLeft = computed(() => this.puzzle()?.solution.slice(this.step()) ?? []);
  readonly interactive = computed(() => !this.holding() && this.status() !== 'solved');
  readonly dests = computed(() => {
    const p = this.puzzle();
    const map = new Map<Key, Key[]>();
    if (!p || !this.interactive()) return map;
    const targets = reachable(p.piece, this.pos(), new Set(this.starsLeft())).map(squareName) as Key[];
    if (targets.length) map.set(squareName(this.pos()) as Key, targets);
    return map;
  });
  readonly shapes = computed<DrawShape[]>(() => [
    ...this.starsLeft().map(sq => ({ orig: squareName(sq) as Key, customSvg: { html: STAR_SVG } })),
    ...this.marks(),
  ]);
  readonly nextStage = computed(() => {
    const cur = this.stage()?.stage;
    return cur != null && STAR_STAGES.some(s => s.stage === cur + 1) ? cur + 1 : null;
  });

  constructor() {
    const destroy = inject(DestroyRef);
    destroy.onDestroy(() => clearTimeout(this.timer));
    inject(ActivatedRoute).paramMap.pipe(takeUntilDestroyed(destroy)).subscribe(params => {
      const no = Number(params.get('stage'));
      const stage = STAR_STAGES.find(s => s.stage === no);
      if (!stage) {
        void this.router.navigate(['/stars']);
        return;
      }
      this.open(stage);
    });
  }

  private open(stage: StarStage): void {
    this.stage.set(stage);
    this.complete.set(false);
    this.locked.set(!this.store.isOpen(stage.stage));
    if (this.locked()) return;
    this.round.set(0);
    this.newPuzzle();
  }

  again(): void {
    const s = this.stage();
    if (s) this.open(s);
  }

  private newPuzzle(): void {
    clearTimeout(this.timer);
    const s = this.stage();
    const p = s ? generateStarPuzzle(s.piece, s.stars, this.rng) : null;
    this.failed.set(!p);
    this.puzzle.set(p);
    this.step.set(0);
    this.status.set('play');
    this.holding.set(false);
    this.marks.set([]);
    this.lastMove.set(undefined);
    this.hintLevel = 0;
    if (p) this.fen.set(starFen(p.piece, p.start));
  }

  onMove(event: { orig: Key; dest: Key }): void {
    const p = this.puzzle();
    if (!p || !this.interactive()) return;
    const dest = squareIndex(event.dest);
    const from = this.pos();
    this.marks.set([]);
    if (dest === p.solution[this.step()]) {
      this.hintLevel = 0;
      this.step.update(n => n + 1);
      this.fen.set(starFen(p.piece, dest));
      this.lastMove.set([event.orig, event.dest]);
      if (this.step() >= p.solution.length) {
        this.status.set('solved');
      } else {
        this.status.set('good');
      }
      return;
    }
    // Daneben: der Zug bleibt kurz stehen (Zielfeld rot), dann steht die Figur wieder, wo sie war.
    const before = this.lastMove();
    this.status.set(this.starsLeft().includes(dest) ? 'deadEnd' : 'empty');
    this.holding.set(true);
    this.fen.set(starFen(p.piece, dest));
    this.lastMove.set([event.orig, event.dest]);
    this.marks.set([{ orig: event.dest, brush: 'red' }]);
    this.timer = setTimeout(() => {
      this.holding.set(false);
      this.fen.set(starFen(p.piece, from));
      this.lastMove.set(before);
      this.marks.set([]);
    }, WRONG_HOLD_MS);
  }

  /** Erster Druck: der naechste Stern leuchtet. Zweiter: ein Pfeil zeigt den Zug. */
  showHint(): void {
    const target = this.starsLeft()[0];
    if (target === undefined || !this.interactive()) return;
    this.hintLevel = Math.min(this.hintLevel + 1, 2);
    const to = squareName(target) as Key;
    this.marks.set(this.hintLevel === 1
      ? [{ orig: to, brush: 'yellow' }]
      : [{ orig: squareName(this.pos()) as Key, dest: to, brush: 'green' }]);
  }

  next(): void {
    const s = this.stage();
    if (!s || this.status() !== 'solved') return;
    if (this.round() + 1 < STARS_PER_STAGE) {
      this.round.update(r => r + 1);
      this.newPuzzle();
    } else {
      this.store.complete(s.stage);
      this.puzzle.set(null);
      this.complete.set(true);
    }
  }

  /** Leertaste/Enter: „Weiter" nach einer geloesten Aufgabe, „Naechste Stufe" nach einer geschafften. */
  @HostListener('document:keydown', ['$event'])
  onKey(event: KeyboardEvent): void {
    if (!isAdvanceKey(event)) return;
    if (this.complete()) {
      const n = this.nextStage();
      if (n === null) return;
      event.preventDefault();
      void this.router.navigate(['/stars', n]);
    } else if (this.status() === 'solved') {
      event.preventDefault();
      this.next();
    }
  }
}
