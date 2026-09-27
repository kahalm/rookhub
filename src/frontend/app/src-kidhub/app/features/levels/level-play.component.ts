import { ChangeDetectionStrategy, Component, DestroyRef, HostListener, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { KidsApiService, KidsLevel, KidsLevelDetail } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';
import { KidsTask, splitMoves } from '../../core/kids-solver';
import { themeIcon, themeNameKey, themeTaskKey } from '../../core/kids-themes';
import { KidsPuzzleComponent } from '../../shared/kids-puzzle.component';
import { isAdvanceKey } from '../../core/kids-keys';

/**
 * Eine Stufe spielen: zehn Aufgaben nacheinander, oben die Punkte fuer den Fortschritt, am Ende
 * Sterne. Der laufende Durchgang ist gemerkt — wer mittendrin geht, macht bei der naechsten Aufgabe
 * weiter. Die Punkte stehen in der Titelzeile, der Aufgabentext neben dem Brett — so bekommt das Brett
 * am PC die ganze Fensterhoehe.
 */
@Component({
  selector: 'kid-level-play',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, KidsPuzzleComponent],
  template: `
    <header class="head">
      <a class="back" routerLink="/levels">← {{ 'kids.levels.title' | translate }}</a>
      @if (detail(); as d) {
        <h1><span aria-hidden="true">{{ icon() }}</span> {{ 'kids.levels.level' | translate: { level: d.level } }}
          · {{ nameKey() | translate }}</h1>
        @if (finished() === null && !locked()) {
          <ol class="dots" [attr.aria-label]="'kids.levels.progress' | translate: { done: index(), total: d.puzzles.length }">
            @for (p of d.puzzles; track p.id; let i = $index) {
              <li [class.solved]="i < index()" [class.now]="i === index()"></li>
            }
          </ol>
        }
      }
    </header>

    @if (failed()) {
      <p class="info">{{ 'kids.loadError' | translate }}</p>
    } @else if (locked()) {
      <p class="info">🔒 {{ 'kids.levels.lockedHint' | translate }}</p>
      <p class="info"><a routerLink="/levels">{{ 'kids.levels.title' | translate }}</a></p>
    } @else if (detail(); as d) {
      @if (finished() === null) {
        @if (task(); as t) {
          <kid-puzzle [task]="t" (solved)="onSolved($event.mistakes)" (next)="onNext()">
            <p kidTask class="task">{{ taskKey() | translate }}</p>
          </kid-puzzle>
        }
      } @else {
        <!-- NICHT „done": die Punkte oben tragen die Klasse ebenfalls, und die Regeln dieser Ansicht
             (30px Innenabstand) machten aus dem ersten gruenen Punkt ein grosses Oval. -->
        <section class="complete">
          <div class="confetti" aria-hidden="true">🎉</div>
          <h2>{{ 'kids.levels.completed' | translate }}</h2>
          <p class="big-stars" [attr.aria-label]="'kids.levels.stars' | translate: { stars: finished() }">
            @for (s of [1, 2, 3]; track s) { <span [class.on]="s <= finished()!">★</span> }
          </p>
          <div class="buttons">
            @if (nextLevel(); as n) {
              <a class="btn primary" [routerLink]="['/levels', n]">{{ 'kids.levels.nextLevel' | translate }} ▶</a>
            }
            <button type="button" class="btn" (click)="again()">↻ {{ 'kids.levels.again' | translate }}</button>
            <a class="btn" routerLink="/levels">{{ 'kids.levels.title' | translate }}</a>
          </div>
        </section>
      }
    } @else {
      <p class="info">{{ 'kids.loading' | translate }}</p>
    }
  `,
  styles: [`
    :host { display: block; max-width: 1320px; margin: 0 auto; padding: 8px 16px 24px; }
    .head { display: flex; align-items: center; gap: 8px 16px; flex-wrap: wrap; margin: 0 auto 12px;
            max-width: calc(max(var(--kid-board, 640px), 300px) + 428px); }
    .head h1 { margin: 0; font-size: 1.5rem; color: var(--kid-title); }
    .back { font-size: 1.1rem; font-weight: 800; text-decoration: none; color: inherit; }
    .info { text-align: center; font-size: 1.2rem; }
    .dots { list-style: none; display: flex; gap: 8px; padding: 0; margin: 0 0 0 auto; }
    .dots li { width: 18px; height: 18px; border-radius: 50%; background: var(--kid-card); box-shadow: inset 0 0 0 2px var(--kid-shadow); }
    .dots li.solved { background: var(--kid-green); box-shadow: none; }
    .dots li.now { background: var(--kid-yellow); box-shadow: none; transform: scale(1.25); }
    .task { margin: 0; font-size: 1.5rem; font-weight: 800; line-height: 1.3; color: var(--kid-title); }
    .complete { text-align: center; padding: 30px 12px; }
    .confetti { font-size: 4.5rem; animation: pop .8s ease-out; }
    .complete h2 { font-size: 2.2rem; margin: 6px 0; color: var(--kid-title); }
    .big-stars { font-size: 3.6rem; letter-spacing: 8px; margin: 8px 0 20px; color: #d0d0d0; }
    .big-stars .on { color: #ffb300; text-shadow: 0 3px 0 #c77c00; }
    .buttons { display: flex; flex-wrap: wrap; gap: 12px; justify-content: center; }
    .btn {
      font: inherit; font-size: 1.25rem; font-weight: 800; padding: 14px 22px; border-radius: 18px; border: 0;
      background: var(--kid-card); color: inherit; text-decoration: none; cursor: pointer; box-shadow: 0 5px 0 var(--kid-shadow);
    }
    .btn.primary { background: var(--kid-green); color: #fff; }
    @keyframes pop { 0% { transform: scale(.2) rotate(-30deg); } 70% { transform: scale(1.2); } }
    @media (max-width: 760px) {
      .dots { margin: 4px auto 0; flex-basis: 100%; justify-content: center; }
      .task { text-align: center; font-size: 1.3rem; }
    }
  `],
})
export class LevelPlayComponent {
  private readonly api = inject(KidsApiService);
  private readonly progress = inject(KidsProgressStore);
  private readonly router = inject(Router);

  readonly detail = signal<KidsLevelDetail | null>(null);
  readonly allLevels = signal<KidsLevel[]>([]);
  readonly index = signal(0);
  /** Sterne des gerade beendeten Durchgangs; `null` = es wird noch gespielt. */
  readonly finished = signal<number | null>(null);
  readonly failed = signal(false);
  readonly locked = signal(false);

  readonly icon = computed(() => themeIcon(this.detail()?.theme ?? ''));
  readonly nameKey = computed(() => themeNameKey(this.detail()?.theme ?? ''));
  readonly taskKey = computed(() => themeTaskKey(this.detail()?.theme ?? ''));
  readonly task = computed<KidsTask | null>(() => {
    const p = this.detail()?.puzzles[this.index()];
    return p ? { fen: p.fen, moves: splitMoves(p.moves), startPly: 0 } : null;
  });
  readonly nextLevel = computed(() => {
    const cur = this.detail()?.level;
    if (cur == null) return null;
    return this.allLevels().some(l => l.level === cur + 1) ? cur + 1 : null;
  });

  constructor() {
    const route = inject(ActivatedRoute);
    route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(params => {
      const level = Number(params.get('level'));
      if (!Number.isInteger(level) || level < 1) {
        void this.router.navigate(['/levels']);
        return;
      }
      this.load(level);
    });
    this.api.levels().subscribe({ next: levels => this.allLevels.set(levels), error: () => {} });
  }

  private load(level: number): void {
    this.detail.set(null);
    this.finished.set(null);
    this.failed.set(false);
    this.locked.set(!this.progress.isUnlocked(level));
    if (this.locked()) return;

    this.api.level(level).subscribe({
      next: detail => {
        const run = this.progress.level(level).runIndex;
        // Letzte Aufgabe geloest, aber „Weiter" nicht mehr gedrueckt: der Durchgang ist fertig.
        if (run >= detail.puzzles.length) {
          this.detail.set(detail);
          this.finished.set(this.progress.completeRun(level));
          return;
        }
        this.index.set(Math.max(0, run));
        this.detail.set(detail);
      },
      error: () => this.failed.set(true),
    });
  }

  /** Stufe geschafft: Leertaste/Enter = „Naechste Stufe". Die Taste, die die letzte Aufgabe
   *  abschliesst, hat das Brett schon verbraucht (`defaultPrevented`) — die Sterne bleiben stehen. */
  @HostListener('document:keydown', ['$event'])
  onKey(event: KeyboardEvent): void {
    const next = this.nextLevel();
    if (this.finished() === null || next === null || !isAdvanceKey(event)) return;
    event.preventDefault();
    void this.router.navigate(['/levels', next]);
  }

  onSolved(mistakes: number): void {
    const d = this.detail();
    if (d) this.progress.recordSolved(d.level, mistakes);
  }

  onNext(): void {
    const d = this.detail();
    if (!d) return;
    if (this.index() + 1 < d.puzzles.length) {
      this.index.update(i => i + 1);
    } else {
      this.finished.set(this.progress.completeRun(d.level));
    }
  }

  again(): void {
    const d = this.detail();
    if (!d) return;
    this.progress.restartRun(d.level);
    this.index.set(0);
    this.finished.set(null);
  }
}
