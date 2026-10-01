import { ChangeDetectionStrategy, Component, DestroyRef, HostListener, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { KidsApiService, KidsEndlessPuzzle } from '../../core/kids-api.service';
import {
  ENDLESS_BLOCK, ENDLESS_FREE_HINTS, ENDLESS_LIVES, ENDLESS_REFILL_AT, EndlessThresholds, endlessThresholds, endlessWindows,
} from '../../core/kids-endless';
import { KidsEndlessStore } from '../../core/kids-endless.store';
import { isAdvanceKey } from '../../core/kids-keys';
import { KidsTask, splitMoves } from '../../core/kids-solver';
import { KidsPuzzleComponent, WRONG_HOLD_MS } from '../../shared/kids-puzzle.component';
import { KID_STACKED } from '../../shared/kids-layout';

/**
 * Endlos-Modus: Aufgabe um Aufgabe, jede ein bisschen schwerer (Kurve in `kids-endless.ts`), bis die drei
 * Herzen weg sind. Ein Fehler kostet ein Herz, ein Tipp erst ab dem zweiten in derselben Aufgabe — hoechstens ein
 * Herz je Aufgabe, und das Kind loest die Aufgabe trotzdem zu Ende (wie in den Stufen). Die Puzzles kommen in Bloecken vom Server; die Kurve des
 * Laufs steht beim Start fest (aus den bisherigen Laeufen).
 */
@Component({
  selector: 'kid-endless-play',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, KidsPuzzleComponent],
  template: `
    <header class="head">
      <a class="back" routerLink="/">← {{ 'kids.back' | translate }}</a>
      <h1><span aria-hidden="true">♾️</span> {{ 'kids.endless.title' | translate }}</h1>
      <span class="hearts" role="img" [attr.aria-label]="'kids.endless.hearts' | translate: { count: lives() }">
        @for (h of heartSlots; track $index) {
          <span [class.lost]="$index >= lives()">{{ $index < lives() ? '❤️' : '🤍' }}</span>
        }
      </span>
      <span class="score">⭐ {{ solved() }}</span>
    </header>

    @if (over()) {
      <section class="complete">
        <div class="confetti" aria-hidden="true">{{ newBest() ? '🏆' : '💪' }}</div>
        <h2>{{ 'kids.endless.over' | translate }}</h2>
        <p class="result">{{ 'kids.endless.result' | translate: { count: solved() } }}</p>
        @if (newBest()) {
          <p class="best new">{{ 'kids.endless.newBest' | translate }}</p>
        } @else if (store.best() > 0) {
          <p class="best">{{ 'kids.endless.best' | translate: { count: store.best() } }}</p>
        }
        <div class="buttons">
          <button type="button" class="btn primary" (click)="start()">↻ {{ 'kids.endless.again' | translate }}</button>
          <a class="btn" routerLink="/">{{ 'kids.back' | translate }}</a>
        </div>
      </section>
    } @else if (failed()) {
      <p class="info">{{ 'kids.loadError' | translate }}</p>
      <p class="info"><button type="button" class="btn" (click)="start()">↻ {{ 'kids.endless.again' | translate }}</button></p>
    } @else if (task(); as t) {
      <kid-puzzle [task]="t" (mistake)="onMistake()" (hinted)="onHint($event)" (solved)="onSolved()" (next)="onNext()">
        <div kidTask class="side-text">
          <p class="task">{{ 'kids.endless.task' | translate }}</p>
          @if (store.best() > 0) { <p class="best">🏆 {{ 'kids.endless.best' | translate: { count: store.best() } }}</p> }
        </div>
      </kid-puzzle>
    } @else {
      <p class="info">{{ 'kids.loading' | translate }}</p>
    }
  `,
  styles: [`
    :host { display: block; max-width: 1320px; margin: 0 auto; padding: 8px 16px 24px; }
    .head { display: flex; align-items: center; gap: 8px 16px; flex-wrap: wrap; margin: 0 auto 12px;
            max-width: var(--kid-row, 1068px); }
    .head h1 { margin: 0; font-size: 1.5rem; color: var(--kid-title); }
    .back { font-size: 1.1rem; font-weight: 800; text-decoration: none; color: inherit; }
    .hearts { margin-left: auto; font-size: 1.5rem; letter-spacing: 2px; }
    .hearts .lost { opacity: .45; }
    .score { font-size: 1.3rem; font-weight: 800; color: var(--kid-title); }
    .info { text-align: center; font-size: 1.2rem; }
    .side-text { display: flex; flex-direction: column; gap: 6px; }
    .task { margin: 0; font-size: 1.5rem; font-weight: 800; line-height: 1.3; color: var(--kid-title); }
    .best { margin: 0; font-size: 1.05rem; font-weight: 700; opacity: .85; }
    .best.new { font-size: 1.6rem; color: #c77c00; opacity: 1; }
    .complete { text-align: center; padding: 30px 12px; }
    .confetti { font-size: 4.5rem; animation: pop .8s ease-out; }
    .complete h2 { font-size: 2.2rem; margin: 6px 0; color: var(--kid-title); }
    .result { font-size: 1.4rem; font-weight: 700; margin: 6px 0 10px; }
    .buttons { display: flex; flex-wrap: wrap; gap: 12px; justify-content: center; margin-top: 16px; }
    .btn {
      font: inherit; font-size: 1.25rem; font-weight: 800; padding: 14px 22px; border-radius: 18px; border: 0;
      background: var(--kid-card); color: inherit; text-decoration: none; cursor: pointer; box-shadow: 0 5px 0 var(--kid-shadow);
    }
    .btn.primary { background: var(--kid-green-strong); color: #fff; }
    @keyframes pop { 0% { transform: scale(.2) rotate(-30deg); } 70% { transform: scale(1.2); } }
    @media ${KID_STACKED} {
      .hearts { margin-left: 0; }
      .task { text-align: center; font-size: 1.3rem; }
      .best { text-align: center; }
    }
  `],
})
export class EndlessPlayComponent {
  private readonly api = inject(KidsApiService);
  readonly store = inject(KidsEndlessStore);

  readonly heartSlots = Array.from({ length: ENDLESS_LIVES });
  readonly current = signal<KidsEndlessPuzzle | null>(null);
  readonly lives = signal(ENDLESS_LIVES);
  readonly solved = signal(0);
  readonly over = signal(false);
  readonly newBest = signal(false);
  readonly failed = signal(false);
  readonly task = computed<KidsTask | null>(() => {
    const p = this.current();
    return p ? { fen: p.fen, moves: splitMoves(p.moves), startPly: 0 } : null;
  });

  private thresholds: EndlessThresholds = endlessThresholds([]);
  private queue: KidsEndlessPuzzle[] = [];
  private requested = 0;
  private used = new Set<number>();
  private fetching = false;
  /** Laufnummer — eine Antwort aus einem abgebrochenen Lauf landet nicht im neuen. */
  private runId = 0;
  private missedThis = false;
  private maxClean = 0;
  private firstMistake: number | null = null;
  private overTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.overTimer));
    this.start();
  }

  /** Neuer Lauf: Kurve aus den bisherigen Laeufen, Herzen voll. */
  start(): void {
    clearTimeout(this.overTimer);
    this.runId++;
    this.thresholds = endlessThresholds(this.store.runs());
    this.queue = [];
    this.requested = 0;
    this.used = new Set();
    this.fetching = false;
    this.missedThis = false;
    this.maxClean = 0;
    this.firstMistake = null;
    this.lives.set(ENDLESS_LIVES);
    this.solved.set(0);
    this.over.set(false);
    this.newBest.set(false);
    this.failed.set(false);
    this.current.set(null);
    this.fetch();
  }

  /** Der erste Tipp je Aufgabe ist frei, ab dem zweiten kostet er wie ein Fehler. */
  onHint(count: number): void {
    if (count > ENDLESS_FREE_HINTS) this.onMistake();
  }

  /** Erster Fehler (oder bezahlter Tipp) in dieser Aufgabe: ein Herz weg. Beim letzten ist der Lauf nach der Pause vorbei. */
  onMistake(): void {
    const puzzle = this.current();
    if (this.missedThis || this.over() || !puzzle) return;
    this.missedThis = true;
    if (this.firstMistake === null) this.firstMistake = puzzle.rating;
    this.lives.update(l => l - 1);
    // Die Pause, in der der falsche Zug stehen bleibt — erst danach das Ende.
    if (this.lives() <= 0) this.overTimer = setTimeout(() => this.finish(), WRONG_HOLD_MS);
  }

  onSolved(): void {
    const puzzle = this.current();
    if (this.over() || !puzzle || this.lives() <= 0) return;
    this.solved.update(n => n + 1);
    if (!this.missedThis) this.maxClean = Math.max(this.maxClean, puzzle.rating);
  }

  onNext(): void {
    if (!this.over()) this.advance();
  }

  /** Vorbei: Leertaste/Enter = „Nochmal". */
  @HostListener('document:keydown', ['$event'])
  onKey(event: KeyboardEvent): void {
    if (!this.over() || !isAdvanceKey(event)) return;
    event.preventDefault();
    this.start();
  }

  private advance(): void {
    const next = this.queue.shift();
    this.missedThis = false;
    this.current.set(next ?? null);
    if (this.queue.length < ENDLESS_REFILL_AT) this.fetch();
  }

  private fetch(): void {
    if (this.fetching) return;
    this.fetching = true;
    const run = this.runId;
    const windows = endlessWindows(this.requested, ENDLESS_BLOCK, this.thresholds);
    this.requested += ENDLESS_BLOCK;
    this.api.endlessBatch(windows, [...this.used]).subscribe({
      next: puzzles => {
        if (run !== this.runId) return;
        this.fetching = false;
        puzzles.forEach(p => this.used.add(p.id));
        this.queue.push(...puzzles);
        if (this.current() === null && !this.over()) {
          if (this.queue.length > 0) this.advance();
          // Schon der erste Block leer: da stimmt etwas nicht (kein Bestand) — kein „Herzen weg".
          else if (this.solved() === 0 && this.requested === ENDLESS_BLOCK) this.failed.set(true);
          // Spaeter nichts mehr zu holen (so schwer gibt es keine Aufgaben mehr): der Lauf endet.
          else this.finish();
        }
      },
      error: () => {
        if (run !== this.runId) return;
        this.fetching = false;
        if (this.current() === null) this.failed.set(true);
      },
    });
  }

  private finish(): void {
    if (this.over()) return;
    this.over.set(true);
    this.current.set(null);
    this.newBest.set(this.store.record({
      at: Date.now(), solved: this.solved(), maxRating: this.maxClean, firstMistakeRating: this.firstMistake,
    }));
  }
}
