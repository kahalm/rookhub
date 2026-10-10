import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { KidsApiService, KidsCourse } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';
import { KidsErrorComponent } from '../../shared/kids-error.component';
import { KID_BACK, KID_PAGE_WIDTH } from '../../shared/kids-layout';

/** Die fuer Kinder freigegebenen Kurse (ein Admin schaltet sie in der Buecherverwaltung frei). */
@Component({
  selector: 'kid-course-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, KidsErrorComponent],
  template: `
    <header class="head">
      <a class="back" routerLink="/">← {{ 'kids.back' | translate }}</a>
      <h1>{{ 'kids.courses.title' | translate }}</h1>
    </header>
    @if (loading()) {
      <p class="info">{{ 'kids.loading' | translate }}</p>
    } @else if (failed()) {
      <kid-error (retry)="load()" />
    } @else if (courses().length === 0) {
      <!-- Freundliche Leer-Karte mit Wegen zum Spielen statt einer grauen Zeile (UI-Sweep 2026-10-10, k-courses-empty). -->
      <section class="empty">
        <span class="owl" aria-hidden="true">🦉</span>
        <h2>{{ 'kids.courses.soon' | translate }}</h2>
        <div class="ways">
          <a class="way levels" routerLink="/levels">{{ 'kids.courses.playLevels' | translate }}</a>
          <a class="way endless" routerLink="/endless">{{ 'kids.courses.playEndless' | translate }}</a>
        </div>
      </section>
    } @else {
      <div class="list">
        @for (c of courses(); track c.bookId) {
          <a class="course" [routerLink]="['/courses', c.bookId]">
            <span class="icon" aria-hidden="true">📚</span>
            <span class="text">
              <span class="name">{{ c.title }}</span>
              @if (c.description) { <span class="desc">{{ c.description }}</span> }
              <span class="meta">{{ 'kids.courses.progress' | translate: { done: solved(c), total: c.puzzleCount } }}</span>
            </span>
          </a>
        }
      </div>
    }
  `,
  styles: [KID_BACK, `
    :host { display: block; max-width: ${KID_PAGE_WIDTH.list}px; box-sizing: border-box; margin: 0 auto; padding: 16px; }
    /* Drei Spalten, die aeusseren gleich breit: der Titel steht genau in der Mitte, egal wie breit „← Start" ist. */
    .head { display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; gap: 12px; margin-bottom: 16px; }
    .head .back { justify-self: start; }
    .head h1 { margin: 0; font-size: 1.9rem; color: var(--kid-title); text-align: center; }
    .empty {
      display: flex; flex-direction: column; align-items: center; gap: 6px; text-align: center; padding: 26px 18px 30px;
      border-radius: 24px; background: #eef4fb; color: #23344a; box-shadow: 0 5px 0 var(--kid-shadow);
    }
    .empty .owl { font-size: 4rem; line-height: 1.1; }
    .empty h2 { margin: 0; font-size: 1.6rem; font-weight: 800; }
    .ways { display: flex; flex-wrap: wrap; justify-content: center; gap: 12px; margin-top: 14px; }
    .way {
      display: inline-flex; align-items: center; justify-content: center; box-sizing: border-box; min-height: 48px;
      padding: 10px 22px; border-radius: 999px; color: #fff; text-decoration: none; font-size: 1.2rem; font-weight: 800;
      box-shadow: 0 4px 0 var(--kid-shadow);
    }
    .way:active { transform: translateY(3px); box-shadow: 0 1px 0 var(--kid-shadow); }
    .way.levels { background: var(--kid-green-strong); }
    .way.endless { background: #1d63b5; }
    .info { text-align: center; font-size: 1.2rem; }
    .list { display: flex; flex-direction: column; gap: 14px; }
    .course {
      display: flex; gap: 16px; align-items: center; padding: 18px; border-radius: 24px; text-decoration: none;
      color: inherit; background: var(--kid-peach); box-shadow: 0 5px 0 var(--kid-shadow);
    }
    .icon { font-size: 2.8rem; }
    .text { display: flex; flex-direction: column; gap: 4px; }
    .name { font-size: 1.4rem; font-weight: 800; }
    .desc, .meta { font-size: 1rem; opacity: .85; }
  `],
})
export class CourseListComponent {
  private readonly api = inject(KidsApiService);
  private readonly progress = inject(KidsProgressStore);
  private readonly translate = inject(TranslateService);

  readonly courses = signal<KidsCourse[]>([]);
  readonly loading = signal(true);
  /** Abruf gescheitert — eigener Zustand, damit ein Fehler nicht wie „Noch keine Kurse" aussieht. */
  readonly failed = signal(false);

  constructor() {
    this.load();
  }

  /** Kurse holen — beim Oeffnen und ueber „Nochmal" der Fehlerkachel. */
  load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.courses(this.translate.currentLang() ?? undefined).subscribe({
      next: courses => { this.courses.set(courses); this.loading.set(false); },
      error: () => { this.failed.set(true); this.loading.set(false); },
    });
  }

  /**
   * Geloeste Linien des Kurses, hoechstens so viele, wie er hat: der Stand behaelt die Ids geloeschter Linien (der
   * Server entfernt sie nur bei „Von vorn", jeder Abgleich bringt sie zurueck), und die Liste kennt nur die Zahl,
   * nicht die Ids der Linien — ungedeckelt stand „10 von 8 geschafft" da (Codereview 2026-09-29, F7-014). Genau
   * (Schnitt mit den aktuellen Linien) zaehlt die Kursseite.
   */
  solved(c: KidsCourse): number {
    return Math.min(this.progress.course(c.bookId).solved.length, c.puzzleCount);
  }
}
