import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { KidsApiService, KidsCourse } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';
import { KidsErrorComponent } from '../../shared/kids-error.component';
import { KID_BACK } from '../../shared/kids-layout';

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
      <p class="info">{{ 'kids.courses.empty' | translate }}</p>
    } @else {
      <div class="list">
        @for (c of courses(); track c.bookId) {
          <a class="course" [routerLink]="['/courses', c.bookId]">
            <span class="icon" aria-hidden="true">📚</span>
            <span class="text">
              <span class="name">{{ c.title }}</span>
              @if (c.description) { <span class="desc">{{ c.description }}</span> }
              <span class="meta">{{ 'kids.courses.progress' | translate: { done: solved(c.bookId), total: c.puzzleCount } }}</span>
            </span>
          </a>
        }
      </div>
    }
  `,
  styles: [KID_BACK, `
    :host { display: block; max-width: 820px; margin: 0 auto; padding: 16px; }
    .head { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
    .head h1 { flex: 1; margin: 0; font-size: 1.9rem; color: var(--kid-title); text-align: center; }
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

  solved(bookId: number): number {
    return this.progress.course(bookId).solved.length;
  }
}
