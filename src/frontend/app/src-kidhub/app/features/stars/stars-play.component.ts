import {
  ChangeDetectionStrategy, Component, DestroyRef, HostListener, computed, inject, signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { PuzzleBoardComponent } from '@rh/features/puzzles/puzzle-board.component';
import {
  MAX_STARS, Rng, STARS_PER_STAGE, maxStars, reachableStars, solveStars, STAR_STAGES, STAR_SVG, StarPiece, StarPuzzle, StarStage, generateStarPuzzle, reachable,
  squareIndex, squareName, starFen,
} from '../../core/kids-stars';
import { KidsStarsStore } from '../../core/kids-stars.store';
import { isAdvanceKey } from '../../core/kids-keys';
import { WRONG_HOLD_MS } from '../../shared/kids-puzzle.component';
import { KID_BACK, KID_COORDS, KID_SHORT, KID_STACKED } from '../../shared/kids-layout';

/** Figurenzeichen fuer Stufenkarte und Kopfzeile (weisse Figuren, wie auf dem Brett). */
export function pieceGlyph(piece: StarPiece): string {
  return { R: '♖', B: '♗', Q: '♕', N: '♘' }[piece];
}

/** Deckel der Pruefung „geht es von hier noch auf?" bei Aufgaben mit mehreren Wegen. */
const OPEN_CHECK_NODES = 60_000;

/** Sternzahlen im freien Modus (Knoepfe; mehr ueber das Zahlenfeld). */
const FREE_KEY = 'rh-kids-stars-free';

/** Die zuletzt gewaehlte Figur und Sternzahl des freien Modus — Unsinn faellt auf Turm mit 3 Sternen. */
export function readFreeChoice(): { piece: StarPiece; count: number } {
  try {
    const v = JSON.parse(localStorage.getItem(FREE_KEY) ?? 'null') as { piece?: unknown; count?: unknown } | null;
    const n = v?.count as number;
    if (v && ['R', 'B', 'N', 'Q'].includes(v.piece as string) && Number.isInteger(n) && n >= 2 && n <= MAX_STARS) {
      return { piece: v.piece as StarPiece, count: v.count as number };
    }
  } catch { /* kein Speicher */ }
  return { piece: 'R', count: 3 };
}

/** Was die Eule sagt. */
export type StarsStatus = 'play' | 'good' | 'empty' | 'deadEnd' | 'solved';

/**
 * Eine Stufe der Sternenjagd: Aufgaben hintereinander mit wachsender Sternzahl (`StarStage.counts`), jede frisch gewuerfelt (Regeln und Generator in
 * `kids-stars.ts`). Gezogen werden darf ueberall hin, wohin die Figur kommt — ein Zug auf ein leeres Feld oder auf
 * einen Stern, nach dem nicht mehr alle zu holen sind, bleibt `WRONG_HOLD_MS` stehen und wird zurueckgenommen
 * (wie in den Stufen). Tipps kosten nichts: erst leuchtet der naechste Stern, dann zeigt ein Pfeil den Zug.
 * Aller Anzeige-Zustand in Signalen (Timer, OnPush).
 */
@Component({
  selector: 'kid-stars-play',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, PuzzleBoardComponent, NgTemplateOutlet],
  template: `
    <header class="head" [class.free-head]="free()">
      <a class="back" routerLink="/stars">← {{ 'kids.stars.title' | translate }}</a>
      @if (free()) {
        <h1><span class="dice" aria-hidden="true">🎲</span> {{ 'kids.stars.free.title' | translate }}</h1>
        <span class="round solved-pill" [attr.aria-label]="'kids.stars.free.solved' | translate: { n: freeSolved() }">✓ {{ freeSolved() }}</span>
      } @else if (stage(); as s) {
        <h1><span aria-hidden="true">{{ glyph(s.piece) }}</span> {{ 'kids.stars.stage' | translate: { stage: s.stage } }}
          · {{ 'kids.stars.piece.' + s.piece | translate }}</h1>
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
          @if (free()) {
            <ng-container [ngTemplateOutlet]="picker" />
            @if (!p.unique) { <p class="note">{{ 'kids.stars.free.manyWays' | translate }}</p> }
          }
          @if (!free()) {
            <!-- Aufgabe x von 6 als Balken ueber der Aufgabe, nicht verloren am rechten Rand der Kopfzeile
                 (UI-Sweep 2026-10-10, k-task-pos). -->
            <div class="rounds">
              <div class="segments" aria-hidden="true" [style.--rounds]="roundSlots().length">
                @for (n of roundSlots(); track n) { <span [class.on]="n <= round()"></span> }
              </div>
              <span class="round">{{ 'kids.stars.round' | translate: { n: round() + 1, total: perStage } }}</span>
            </div>
          }
          <p class="task">{{ 'kids.stars.task' | translate }}</p>
          <!-- Grosse Sterne: gefressen gold, offen als grauer Umriss (k-stars-progress). -->
          <p class="left" role="img" [attr.aria-label]="'kids.stars.left' | translate: { count: starsLeft().length }">
            @if (p.stars.length <= 10) {
              @for (s of p.solution; track s; let i = $index) {
                <span [class.eaten]="i < step()">{{ i < step() ? '★' : '☆' }}</span>
              }
            } @else {
              <span class="eaten">★</span> <span class="count">{{ starsLeft().length }} / {{ p.stars.length }}</span>
            }
          </p>
        </div>
        <div class="board">
          <app-puzzle-board
            [autoSelect]="selectedSquare()"
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
    } @else if (failed() && free()) {
      <div class="free-failed">
        <ng-container [ngTemplateOutlet]="picker" />
        <p class="info">{{ 'kids.stars.free.tooMany' | translate }}</p>
      </div>
    } @else if (failed()) {
      <p class="info">{{ 'kids.loadError' | translate }}</p>
    }

    <ng-template #picker>
            <!-- Freies Spiel (UI-Sweep 2026-10-10, k-free-controls, zweiter Vorschlag): Figur als EIN Umschalter,
                 Sternzahl als grosser Zaehler mit − / + (2 bis Hoechstwert der Figur; gedrueckt halten zaehlt schneller). -->
            <div class="seg" role="group" [attr.aria-label]="'kids.stars.free.piece' | translate">
              @for (pc of freePieces; track pc) {
                <button type="button" class="seg-btn" [class.on]="pc === freePiece()" [attr.aria-pressed]="pc === freePiece()"
                        [title]="'kids.stars.piece.' + pc | translate" [attr.aria-label]="'kids.stars.piece.' + pc | translate"
                        (click)="chooseFree(pc, shownCount())">{{ glyph(pc) }}</button>
              }
            </div>
            <div class="stepper" role="group" [attr.aria-label]="'kids.stars.free.count' | translate">
              <button type="button" class="step" [disabled]="shownCount() <= 2" [attr.aria-label]="'kids.stars.free.less' | translate"
                      (pointerdown)="holdStep(-1, $event)" (pointerup)="releaseStep()" (pointerleave)="releaseStep()"
                      (pointercancel)="releaseStep()" (click)="clickStep(-1)">−</button>
              <span class="count" aria-live="polite">{{ shownCount() }} <span class="count-star" aria-hidden="true">★</span></span>
              <button type="button" class="step" [disabled]="shownCount() >= freeMax()" [attr.aria-label]="'kids.stars.free.more' | translate"
                      (pointerdown)="holdStep(1, $event)" (pointerup)="releaseStep()" (pointerleave)="releaseStep()"
                      (pointercancel)="releaseStep()" (click)="clickStep(1)">+</button>
            </div>
            <p class="range">{{ 'kids.stars.free.range' | translate: { max: freeMax() } }}</p>
    </ng-template>
  `,
  styles: [KID_BACK, KID_COORDS, `
    :host { display: block; max-width: 1320px; margin: 0 auto; padding: 8px 16px 24px; }
    .head { display: flex; align-items: center; gap: 8px 16px; flex-wrap: wrap; margin: 0 auto 12px;
            max-width: var(--kid-row, 1068px); }
    .head h1 { margin: 0; font-size: 1.5rem; color: var(--kid-title); }
    .round { margin-left: auto; font-size: 1.2rem; font-weight: 800; }
    /* Freies Spiel: Zurueck · Titel · ✓ n in EINER Zeile (k-free-controls), am Handy etwas kleiner und ohne Wuerfel. */
    .head.free-head { flex-wrap: nowrap; }
    .head.free-head h1 { white-space: nowrap; min-width: 0; overflow: hidden; text-overflow: ellipsis; }
    .head.free-head .solved-pill { margin-left: auto; flex-shrink: 0; white-space: nowrap; }
    @media (max-width: 480px) {
      .head.free-head h1 { font-size: 1.2rem; }
      .head.free-head .dice { display: none; }
    }
    .solved-pill { padding: 4px 12px; border-radius: 999px; background: var(--kid-good-bg); color: var(--kid-good-fg); font-size: 1.1rem; }
    .rounds { max-width: 260px; }
    .rounds .round { display: block; margin: 4px 0 0; font-size: .95rem; font-weight: 700; color: #4f5d6e; }
    .segments { display: grid; grid-template-columns: repeat(var(--rounds, 6), 1fr); gap: 4px; }
    .segments span { height: 10px; border-radius: 4px; box-sizing: border-box; border: 1.5px solid #7a8494; }
    .segments span.on { background: var(--kid-green); border-color: var(--kid-green-strong); }
    .info { text-align: center; font-size: 1.2rem; }
    .puzzle {
      display: grid; justify-content: center; align-items: start; column-gap: 28px; row-gap: 14px;
      grid-template-columns: var(--kid-board, 640px) minmax(280px, 400px);
      grid-template-rows: auto 1fr;
      grid-template-areas: "board task" "board side";
    }
    .task-slot { grid-area: task; display: flex; flex-direction: column; gap: 6px; }
    .pick { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
    .free-failed { display: flex; flex-direction: column; gap: 10px; align-items: center; max-width: 520px; margin: 0 auto; }
    .pick-label { font-size: 1.3rem; }
    .note { margin: 0; font-size: 1rem; opacity: .85; }
    .seg { display: grid; grid-template-columns: repeat(4, 1fr); gap: 4px; padding: 4px; border-radius: 16px;
           background: var(--kid-card); box-shadow: 0 3px 0 var(--kid-shadow); }
    .seg-btn { font: inherit; font-size: 1.8rem; line-height: 1; min-height: 48px; border: 0; border-radius: 12px;
               background: transparent; color: inherit; cursor: pointer; }
    .seg-btn.on { background: var(--kid-green-strong); color: #fff; }
    .stepper { display: flex; align-items: center; justify-content: center; gap: 16px; }
    .step { font: inherit; font-size: 1.8rem; font-weight: 800; width: 52px; height: 52px; border: 0; border-radius: 50%;
            background: var(--kid-card); color: inherit; cursor: pointer; box-shadow: 0 3px 0 var(--kid-shadow);
            touch-action: manipulation; user-select: none; }
    .step:disabled { opacity: .4; cursor: default; }
    .count { min-width: 3.4em; text-align: center; font-size: 2rem; font-weight: 800; }
    .count-star { color: #f5b400; }
    .range { margin: 0; text-align: center; font-size: .9rem; color: #4f5d6e; }
    .chip {
      font: inherit; font-size: 1.2rem; font-weight: 800; min-width: 44px; min-height: 44px; padding: 4px 10px;
      border: 0; border-radius: 14px; background: var(--kid-card); color: inherit; cursor: pointer;
      box-shadow: 0 3px 0 var(--kid-shadow);
    }
    .chip.glyph-chip { font-size: 1.8rem; line-height: 1; }
    .chip.on { background: var(--kid-green-strong); color: #fff; }
    .task { margin: 0; font-size: 1.5rem; font-weight: 800; line-height: 1.3; color: var(--kid-title); }
    .left { margin: 0; font-size: 30px; line-height: 1.15; letter-spacing: 6px; color: #7a8494; }
    .left .eaten { color: #f5b400; text-shadow: 0 1px 0 #a87400; }
    .left .count { font-size: 1.4rem; font-weight: 800; letter-spacing: 0; color: var(--kid-title); vertical-align: middle; }
    .board {
      grid-area: board; width: var(--kid-board, 640px);
      border-radius: 14px; overflow: hidden; box-shadow: 0 6px 0 var(--kid-shadow);
    }
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
      .rounds { align-self: center; width: 100%; }
      .rounds .round { text-align: center; }
      .pick { justify-content: center; }
      .task { font-size: 1.3rem; }
      .head .round { margin-left: 0; }
    }
    @media ${KID_SHORT} {
      .head { flex-wrap: nowrap; gap: 12px; }
      .head h1 { min-width: 0; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; font-size: 1.25rem; }
      .back, .head .round { flex-shrink: 0; white-space: nowrap; }
    }
  `],
})
export class StarsPlayComponent {
  readonly store = inject(KidsStarsStore);
  private readonly router = inject(Router);

  readonly perStage = STARS_PER_STAGE;
  /** Die Abschnitte des Fortschrittsbalkens (0 … Aufgaben−1). */
  readonly roundSlots = computed(() => Array.from({ length: this.stage()?.counts.length ?? STARS_PER_STAGE }, (_, i) => i));
  readonly glyph = pieceGlyph;
  /** Zufall der Aufgaben — Tests setzen einen festen. */
  rng: Rng = Math.random;

  readonly stage = signal<StarStage | null>(null);
  /** Freier Modus (`/stars/free`): Figur und Sternzahl waehlt das Kind, Aufgaben ohne Ende, kein Fortschritt. */
  readonly free = signal(false);
  readonly freePieces: readonly StarPiece[] = ['R', 'B', 'N', 'Q'];
  readonly freePiece = signal<StarPiece>('R');
  readonly freeCount = signal(3);
  readonly freeSolved = signal(0);
  readonly locked = signal(false);
  readonly failed = signal(false);
  readonly complete = signal(false);
  readonly round = signal(0);
  readonly puzzle = signal<StarPuzzle | null>(null);
  /** Gefressene Sterne der laufenden Aufgabe, in Reihenfolge. */
  private readonly eaten = signal<number[]>([]);
  readonly step = computed(() => this.eaten().length);
  /** Die restlichen Sterne in einer Reihenfolge, die aufgeht — bei eindeutigen Aufgaben der Rest der Loesung. */
  private readonly route = signal<number[]>([]);
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
    const e = this.eaten();
    return p ? (e.length ? e[e.length - 1] : p.start) : -1;
  });
  readonly starsLeft = computed(() => this.route());
  /** Die Figur ist immer ausgewaehlt: das Kind tippt nur noch das Zielfeld (Wunsch 2026-10-10). */
  readonly selectedSquare = computed(() => this.puzzle() && this.interactive() ? squareName(this.pos()) as Key : undefined);
  readonly freeMax = computed(() => maxStars(this.freePiece()));
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
    destroy.onDestroy(() => { clearTimeout(this.timer); clearTimeout(this.applyTimer); this.releaseStep(); });
    const route = inject(ActivatedRoute);
    if (route.snapshot?.data?.['free']) {
      this.free.set(true);
      const saved = readFreeChoice();
      this.chooseFree(saved.piece, saved.count);
      return;
    }
    route.paramMap.pipe(takeUntilDestroyed(destroy)).subscribe(params => {
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

  /** Angezeigte Sternzahl — waehrend des Zaehlens schon die neue, die Aufgabe kommt erst nach einer kurzen Pause. */
  readonly pendingCount = signal<number | null>(null);
  readonly shownCount = computed(() => this.pendingCount() ?? this.freeCount());
  private stepTimer: ReturnType<typeof setTimeout> | undefined;
  private repeatTimer: ReturnType<typeof setTimeout> | undefined;
  private applyTimer: ReturnType<typeof setTimeout> | undefined;
  private heldSteps = 0;

  /** Ein Schritt − / + (ohne neue Aufgabe; die kommt `ApplyDelayMs` nach dem letzten Schritt). */
  stepCount(delta: number): void {
    const next = Math.min(Math.max(this.shownCount() + delta, 2), this.freeMax());
    if (next === this.shownCount()) return;
    this.pendingCount.set(next);
    clearTimeout(this.applyTimer);
    this.applyTimer = setTimeout(() => this.applyPending(), StarsPlayComponent.ApplyDelayMs);
  }

  applyPending(): void {
    clearTimeout(this.applyTimer);
    const n = this.pendingCount();
    this.pendingCount.set(null);
    if (n !== null && n !== this.freeCount()) this.chooseFree(this.freePiece(), n);
  }

  /** Gedrueckt halten: nach `HoldDelayMs` zaehlt es von selbst weiter. Der erste Schritt kommt ueber `click`. */
  holdStep(delta: number, event: PointerEvent): void {
    if (event.button !== 0) return;
    this.releaseStep();
    this.heldSteps = 0;
    this.stepTimer = setTimeout(() => {
      const tick = () => { this.heldSteps++; this.stepCount(delta); this.repeatTimer = setTimeout(tick, StarsPlayComponent.RepeatMs); };
      tick();
    }, StarsPlayComponent.HoldDelayMs);
  }

  releaseStep(): void {
    clearTimeout(this.stepTimer);
    clearTimeout(this.repeatTimer);
  }

  /** Klick = ein Schritt — ausser das Halten hat schon gezaehlt (dann waere der Klick ein Schritt zu viel). */
  clickStep(delta: number): void {
    if (this.heldSteps > 0) { this.heldSteps = 0; return; }
    this.stepCount(delta);
  }

  static readonly ApplyDelayMs = 450;
  static readonly HoldDelayMs = 400;
  static readonly RepeatMs = 90;

  /** Freie Zahl hinter der 8: uebernommen, wenn sie zwischen 2 und dem Hoechstwert der Figur liegt. */
  typedCount(event: Event): void {
    const input = event.target as HTMLInputElement;
    const n = Math.round(Number(input.value));
    if (!Number.isFinite(n) || input.value === '') return;
    const count = Math.min(Math.max(n, 2), this.freeMax());
    input.value = String(count);
    if (count !== this.freeCount()) this.chooseFree(this.freePiece(), count);
  }

  /** Freier Modus: Auswahl uebernehmen (und auf dem Geraet merken), sofort eine neue Aufgabe. */
  chooseFree(piece: StarPiece, count: number): void {
    clearTimeout(this.applyTimer);
    this.pendingCount.set(null);
    count = Math.min(count, maxStars(piece));
    this.freePiece.set(piece);
    this.freeCount.set(count);
    this.stage.set({ stage: 0, piece, counts: [count] });
    this.round.set(0);
    try { localStorage.setItem(FREE_KEY, JSON.stringify({ piece, count })); } catch { /* egal */ }
    this.newPuzzle();
  }

  again(): void {
    const s = this.stage();
    if (s) this.open(s);
  }

  private newPuzzle(): void {
    clearTimeout(this.timer);
    const s = this.stage();
    const p = s ? generateStarPuzzle(s.piece, s.counts[this.round()], this.rng) : null;
    this.failed.set(!p);
    this.puzzle.set(p);
    this.eaten.set([]);
    this.route.set(p ? [...p.solution] : []);
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
    const left = this.route();
    let accepted: number[] | null = dest === left[0] ? left.slice(1) : null;
    if (!accepted && !p.unique && left.includes(dest)) {
      // Mehrere Wege: zaehlt, wenn es von dort aus noch aufgeht. Weiss die Suche es nicht (Deckel), gilt der Zug.
      const rest = left.filter(sq => sq !== dest);
      const found = solveStars(p.piece, dest, rest, 1, OPEN_CHECK_NODES);
      if (found === null) accepted = rest;
      else if (found.length) accepted = found[0];
    }
    if (accepted) {
      this.hintLevel = 0;
      this.eaten.update(e => [...e, dest]);
      this.route.set(accepted);
      this.fen.set(starFen(p.piece, dest));
      this.lastMove.set([event.orig, event.dest]);
      if (accepted.length === 0) {
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
    const p = this.puzzle();
    let target = this.route()[0];
    // Nach einem Zug, den die Suche nicht zu Ende pruefen konnte, ist die gemerkte Reihenfolge nur geraten.
    if (p && !p.unique && target !== undefined && !reachableStars(p.piece, this.pos(), new Set(this.route())).includes(target)) {
      const found = solveStars(p.piece, this.pos(), this.route(), 1, OPEN_CHECK_NODES);
      target = found?.[0]?.[0] as number;
      if (found?.length) this.route.set(found[0]);
    }
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
    if (this.free()) {
      this.freeSolved.update(n => n + 1);
      this.newPuzzle();
      return;
    }
    if (this.round() + 1 < s.counts.length) {
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
