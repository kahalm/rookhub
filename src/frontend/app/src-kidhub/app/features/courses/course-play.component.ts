import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { KidsApiService, KidsCourseLine } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';
import { KidsTask, parseAltMoves, splitMoves } from '../../core/kids-solver';
import { KidsPuzzleComponent } from '../../shared/kids-puzzle.component';
import { KID_STACKED } from '../../shared/kids-layout';

/** Naechste ungeloeste Linie ab `from` (einschliesslich), am Ende von vorn — `-1`, wenn alle geloest sind. */
export function nextUnsolved(lines: { id: number }[], solved: ReadonlySet<number>, from: number): number {
  for (let k = 0; k < lines.length; k++) {
    const i = (from + k) % lines.length;
    if (!solved.has(lines[i].id)) return i;
  }
  return -1;
}

/**
 * Einen Kinderkurs durcharbeiten: die Aufgaben in Buchreihenfolge, jede nur, bis sie einmal geloest
 * ist. Der Einleitungstext der Linie steht ueber dem Brett, der Kommentar nach dem letzten Zug
 * erscheint, sobald die Aufgabe geloest ist.
 */
@Component({
  selector: 'kid-course-play',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, KidsPuzzleComponent],
  template: `
    <header class="head">
      <a class="back" routerLink="/courses">← {{ 'kids.courses.title' | translate }}</a>
      @if (title()) { <h1>📚 {{ title() }}</h1> }
      @if (lines().length > 0) {
        <span class="count">{{ 'kids.courses.progress' | translate: { done: solvedCount(), total: lines().length } }}</span>
      }
    </header>

    @if (failed()) {
      <p class="info">{{ 'kids.loadError' | translate }}</p>
    } @else if (loading()) {
      <p class="info">{{ 'kids.loading' | translate }}</p>
    } @else if (current(); as line) {
      @if (task(); as t) {
        <kid-puzzle [task]="t" [moveComments]="line.moveComments" (solved)="onSolved()" (next)="onNext()">
          <!-- Neben dem Brett (am Handy darueber): so bekommt das Brett am PC die Fensterhoehe. -->
          <div kidTask class="line-text">
            @if (line.chapterLabel ?? line.chapter; as chapter) { <p class="chapter">{{ chapter }}</p> }
            @if (line.titleLabel ?? line.title; as heading) { <h2 class="line-title">{{ heading }}</h2> }
            @if (line.comment) { <p class="intro">{{ line.comment }}</p> }
          </div>
        </kid-puzzle>
      }
    } @else if (lines().length > 0) {
      <section class="done">
        <div class="confetti" aria-hidden="true">🏅</div>
        <h2>{{ 'kids.courses.completed' | translate }}</h2>
        <div class="buttons">
          <button type="button" class="btn" (click)="restart()">↻ {{ 'kids.courses.restart' | translate }}</button>
          <a class="btn" routerLink="/">{{ 'kids.back' | translate }}</a>
        </div>
      </section>
    } @else {
      <p class="info">{{ 'kids.courses.empty' | translate }}</p>
    }
  `,
  styles: [`
    :host { display: block; max-width: 1320px; margin: 0 auto; padding: 8px 16px 24px; }
    .head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin: 0 auto 12px;
            max-width: var(--kid-row, 1068px); }
    .head h1 { flex: 1; margin: 0; font-size: 1.5rem; color: var(--kid-title); }
    .back, .count { font-size: 1.1rem; font-weight: 800; text-decoration: none; color: inherit; }
    .info { text-align: center; font-size: 1.2rem; }
    .line-text { display: flex; flex-direction: column; gap: 6px; }
    .chapter { margin: 0; font-weight: 700; opacity: .75; }
    .line-title { margin: 0; font-size: 1.35rem; color: var(--kid-title); }
    .intro {
      margin: 0; padding: 12px 16px; border-radius: 16px; background: var(--kid-card);
      font-size: 1.1rem; line-height: 1.45; white-space: pre-line; max-height: 40vh; overflow-y: auto;
    }
    @media ${KID_STACKED} {
      .chapter, .line-title { text-align: center; }
      .intro { max-height: 30vh; }
    }
    .done { text-align: center; padding: 30px 12px; }
    .confetti { font-size: 4.5rem; }
    .done h2 { font-size: 2rem; color: var(--kid-title); }
    .buttons { display: flex; flex-wrap: wrap; gap: 12px; justify-content: center; }
    .btn {
      font: inherit; font-size: 1.2rem; font-weight: 800; padding: 14px 22px; border-radius: 18px; border: 0;
      background: var(--kid-card); color: inherit; text-decoration: none; cursor: pointer; box-shadow: 0 5px 0 var(--kid-shadow);
    }
  `],
})
export class CoursePlayComponent {
  private readonly api = inject(KidsApiService);
  private readonly progress = inject(KidsProgressStore);
  private readonly translate = inject(TranslateService);

  readonly bookId = signal(0);
  readonly lines = signal<KidsCourseLine[]>([]);
  readonly index = signal(-1);
  readonly loading = signal(true);
  readonly failed = signal(false);

  readonly title = computed(() => this.lines()[0]?.bookTitle ?? null);
  readonly current = computed(() => this.lines()[this.index()] ?? null);
  readonly solvedCount = computed(() => {
    const solved = new Set(this.progress.course(this.bookId()).solved);
    return this.lines().filter(l => solved.has(l.id)).length;
  });
  readonly task = computed<KidsTask | null>(() => {
    const line = this.current();
    return line ? {
      fen: line.fen,
      moves: splitMoves(line.moves),
      startPly: line.startPly,
      altMoves: parseAltMoves(line.altMoves),
    } : null;
  });

  constructor() {
    const route = inject(ActivatedRoute);
    route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(params => {
      this.load(Number(params.get('bookId')));
    });
  }

  private load(bookId: number): void {
    this.bookId.set(bookId);
    this.loading.set(true);
    this.failed.set(false);
    const lang = this.translate.currentLang() || undefined;
    this.api.coursePuzzles(bookId, lang).subscribe({
      next: lines => {
        this.lines.set(lines);
        this.index.set(nextUnsolved(lines, new Set(this.progress.course(bookId).solved), 0));
        this.loading.set(false);
      },
      error: () => { this.failed.set(true); this.loading.set(false); },
    });
  }

  onSolved(): void {
    const line = this.current();
    if (line) this.progress.recordCourseSolved(this.bookId(), line.id);
  }

  onNext(): void {
    const solved = new Set(this.progress.course(this.bookId()).solved);
    this.index.set(nextUnsolved(this.lines(), solved, this.index() + 1));
  }

  restart(): void {
    this.progress.resetCourse(this.bookId());
    this.index.set(nextUnsolved(this.lines(), new Set(), 0));
  }
}
