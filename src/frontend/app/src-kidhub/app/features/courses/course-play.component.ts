import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, afterRenderEffect, computed, inject, linkedSignal,
  signal, viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { KidsApiService, KidsCourseLine } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';
import { KidsTask, parseAltMoves, splitMoves } from '../../core/kids-solver';
import { KidsPuzzleComponent } from '../../shared/kids-puzzle.component';
import { KID_BACK, KID_SHORT, KID_STACKED } from '../../shared/kids-layout';
import { KidsErrorComponent } from '../../shared/kids-error.component';
import { splitCourseText } from './course-text';

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
  imports: [RouterLink, TranslatePipe, KidsPuzzleComponent, KidsErrorComponent],
  template: `
    <header class="head">
      <a class="back" routerLink="/courses">← {{ 'kids.courses.title' | translate }}</a>
      @if (title()) { <h1>📚 {{ title() }}</h1> }
      @if (lines().length > 0) {
        <span class="count">{{ 'kids.courses.progress' | translate: { done: solvedCount(), total: lines().length } }}</span>
      }
    </header>

    @if (failed()) {
      <kid-error (retry)="load(bookId())" />
    } @else if (loading()) {
      <p class="info">{{ 'kids.loading' | translate }}</p>
    } @else if (current(); as line) {
      @if (task(); as t) {
        <kid-puzzle [task]="t" [moveComments]="line.moveComments" (solved)="onSolved()" (next)="onNext()">
          <!-- Neben dem Brett (am Handy darueber): so bekommt das Brett am PC die Fensterhoehe. -->
          <div kidTask class="line-text">
            @if (line.chapterLabel ?? line.chapter; as chapter) { <p class="chapter">{{ chapter }}</p> }
            @if (line.titleLabel ?? line.title; as heading) { <h2 class="line-title">{{ heading }}</h2> }
            <!-- UI-Sweep 2026-10-11 (r2-k-course-heading): Zwischenüberschriften aus dem Kommentar fett absetzen -->
            <!-- UI-Sweep 2026-10-11 (r2-k-course-scroll): kein eigener Scrollkasten mehr — ein langer Text wird unten
                 ausgeblendet und lässt sich mit „Weiterlesen ▾" ganz aufklappen. -->
            <!-- d2-k-course-intro: am Handy zugeklappt nur 3 Zeilen; „Schließen" neben „Weiterlesen" blendet die Karte
                 aus und holt das Brett ins Bild (Wunsch des Nutzers). „Text zeigen" holt sie zurück. -->
            @if (introBlocks().length && introHidden()) {
              <button type="button" class="more show-intro" (click)="introHidden.set(false)">
                <span aria-hidden="true">📖</span> {{ 'kids.courses.showIntro' | translate }}
              </button>
            } @else if (introBlocks().length) {
              <div class="intro-wrap" [class.clipped]="introClipped()">
                <div #intro class="intro" [class.open]="introOpen()" id="kid-course-intro">
                  @for (b of introBlocks(); track $index) {
                    @if (b.kind === 'h') { <p class="hl">{{ b.text }}</p> } @else { <p>{{ b.text }}</p> }
                  }
                </div>
                @if (introClipped() || introOpen()) {
                  <div class="intro-actions">
                    <button type="button" class="more" aria-controls="kid-course-intro" [attr.aria-expanded]="introOpen()"
                            (click)="introOpen.set(!introOpen())">
                      {{ (introOpen() ? 'kids.courses.readLess' : 'kids.courses.readMore') | translate }}
                      <span aria-hidden="true">{{ introOpen() ? '▴' : '▾' }}</span>
                    </button>
                    <button type="button" class="more close-intro" (click)="closeIntro()">
                      <span aria-hidden="true">✕</span> {{ 'common.close' | translate }}
                    </button>
                  </div>
                }
              </div>
            }
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
  styles: [KID_BACK, `
    :host { display: block; max-width: 1320px; margin: 0 auto; padding: 8px 16px 24px; }
    .head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin: 0 auto 12px;
            max-width: var(--kid-row, 1068px); }
    .head h1 { flex: 1; margin: 0; font-size: 1.5rem; color: var(--kid-title); }
    .count { font-size: 1.1rem; font-weight: 800; text-decoration: none; color: inherit; }
    .info { text-align: center; font-size: 1.2rem; }
    .line-text { display: flex; flex-direction: column; gap: 6px; }
    .chapter { margin: 0; font-weight: 700; opacity: .75; }
    .line-title { margin: 0; font-size: 1.35rem; color: var(--kid-title); }
    .intro-wrap { position: relative; display: flex; flex-direction: column; border-radius: 16px; background: var(--kid-card); }
    .intro {
      margin: 0; padding: 12px 16px; font-size: 1.1rem; line-height: 1.45; white-space: pre-line;
      max-height: 40vh; overflow: hidden;
    }
    .intro.open { max-height: none; }
    /* Verlauf über die letzten Zeilen: zeigt, dass unten noch Text steht (statt einer halb abgeschnittenen Zeile). */
    .intro-wrap.clipped .intro:not(.open) {
      -webkit-mask-image: linear-gradient(to bottom, #000 calc(100% - 3.2em), transparent);
      mask-image: linear-gradient(to bottom, #000 calc(100% - 3.2em), transparent);
    }
    .more {
      align-self: center; margin: 0 0 10px; font: inherit; font-size: 1.05rem; font-weight: 800; cursor: pointer;
      min-height: 44px; padding: 6px 18px; border-radius: 999px; border: 2px solid var(--kid-title);
      background: var(--kid-card); color: var(--kid-title); box-shadow: 0 3px 0 var(--kid-shadow);
    }
    .more:active { transform: translateY(2px); box-shadow: 0 1px 0 var(--kid-shadow); }
    .intro-actions { display: flex; justify-content: center; flex-wrap: wrap; gap: 8px; margin: 0 0 10px; }
    .intro-actions .more { margin: 0; }
    .show-intro { align-self: center; margin: 0; font-size: 1rem; }
    .intro p { margin: 0; }
    .intro p + p { margin-top: 8px; }
    .intro .hl { font-weight: 800; color: var(--kid-title); }
    .intro .hl + p { margin-top: 2px; }
    @media ${KID_STACKED} {
      .chapter, .line-title { text-align: center; }
      /* d2-k-course-intro: zugeklappt drei Zeilen statt 30vh (~8 Zeilen) — das Brett begann sonst erst bei 69 % der
         Höhe. Am PC (Text neben dem Brett) bleiben 40vh. */
      .intro { max-height: calc(3 * 1.45em); }   /* Inhaltshöhe: drei Zeilen, das Polster (2 × 12 px) kommt dazu */
      .intro-wrap.clipped .intro:not(.open) {
        -webkit-mask-image: linear-gradient(to bottom, #000 calc(100% - 1.6em), transparent);
        mask-image: linear-gradient(to bottom, #000 calc(100% - 1.6em), transparent);
      }
      .intro-actions { margin: 0 0 6px; }
      .intro-actions .more { padding: 4px 16px; }
    }
    /* Handy quer: EINE Titelzeile ueber dem Brett (damit rechnet die App-Huelle) — ein langer Kurstitel wird gekuerzt,
       statt umzubrechen und die unterste Reihe aus dem Bild zu schieben. */
    @media ${KID_SHORT} {
      .head { flex-wrap: nowrap; }
      .head h1 { min-width: 0; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; font-size: 1.25rem; }
      .back, .count { flex-shrink: 0; white-space: nowrap; }
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
  /** Einleitung der Linie in Überschriften und Absätze zerlegt (UI-Sweep 2026-10-11, r2-k-course-heading). */
  readonly introBlocks = computed(() => splitCourseText(this.current()?.comment));
  /** UI-Sweep 2026-10-11 (r2-k-course-scroll): Einleitung ganz aufgeklappt? Jede neue Linie beginnt zugeklappt. */
  readonly introOpen = linkedSignal<KidsCourseLine | null, boolean>({ source: this.current, computation: () => false });
  /** d2-k-course-intro: Einleitung per „Schließen" ausgeblendet? Jede neue Linie zeigt sie wieder. */
  readonly introHidden = linkedSignal<KidsCourseLine | null, boolean>({ source: this.current, computation: () => false });
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  /** Ragt die (zugeklappte) Einleitung über ihre Höhe hinaus? Gemessen nach dem Zeichnen. */
  readonly introClipped = signal(false);
  private readonly introEl = viewChild<ElementRef<HTMLElement>>('intro');
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
    // Nach jedem Zeichnen nachmessen (neue Linie, Sprache, Fenstergröße beim nächsten Zeichnen): nur zugeklappt
    // entscheidet die Höhe, ob „Weiterlesen" nötig ist.
    afterRenderEffect(() => {
      const el = this.introEl()?.nativeElement;
      this.introBlocks();
      if (!el || this.introOpen()) return;
      this.introClipped.set(el.scrollHeight > el.clientHeight + 1);
    });
    const route = inject(ActivatedRoute);
    route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(params => {
      this.load(Number(params.get('bookId')));
    });
  }

  /** „Schließen": Erklärkarte weg und das Brett ins Bild holen (Wunsch zu d2-k-course-intro). */
  closeIntro(): void {
    this.introOpen.set(false);
    this.introHidden.set(true);
    afterNextRender(() => {
      const board = this.host.nativeElement.querySelector<HTMLElement>('kid-puzzle .board');
      board?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }, { injector: this.injector });
  }

  /** Linien des Kurses holen — beim Oeffnen und ueber „Nochmal" der Fehlerkachel. */
  load(bookId: number): void {
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
