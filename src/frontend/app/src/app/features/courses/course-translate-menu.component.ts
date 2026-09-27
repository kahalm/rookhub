import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LocaleService } from '../../core/locale.service';
import { SnackbarService } from '../../core/snackbar.service';
import { languageName } from './course-lang-picker.component';
import { CourseLanguageService } from './course-language.service';
import {
  isOpenJob, requestableLanguages, translationJobStatusText, translationReasonText,
} from './course-translations.component';
import { CourseService, CourseTranslationJob, CourseTranslations } from './course.service';

/**
 * Menü-Eintrag „Übersetzung anfordern" mit Sprach-Untermenü — für das ⋮-Menü des Kurs-Lösers
 * (Wunsch 2026-09-27: „auch wenn ich einen Kurs durchspiele"). Dieselben Regeln wie der Kasten auf der
 * Kursseite ({@link CourseTranslationsComponent}): je Person ein offener Auftrag, Admin unbegrenzt,
 * in der Sperrzeit angenommen und „pausiert"; die Helfer dafür stehen dort und werden hier nur benutzt.
 *
 * <p>Gehört IN ein `<mat-menu>`; der Host spannt keine Box auf (`display: contents`, wie
 * `SendToWorksheetComponent`), sonst bricht die Tastaturnavigation. Der Stand wird erst beim Aufklappen
 * geholt — der Löser lädt sonst bei jeder Linie eine Übersicht, die fast nie jemand ansieht.</p>
 */
@Component({
  selector: 'app-course-translate-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <button mat-menu-item class="ctm-trigger" [matMenuTriggerFor]="trMenu" (menuOpened)="load()">
      <mat-icon>translate</mat-icon>
      <span>{{ 'courses.translations.menu' | translate }}</span>
    </button>

    <mat-menu #trMenu="matMenu" class="ctm-menu">
      @if (overview(); as o) {
        @for (job of openJobs(); track job.id) {
          <button mat-menu-item type="button" disabled class="ctm-job" [attr.data-job]="job.id">
            <span>{{ name(job.language) }} · {{ statusText(job) }}</span>
          </button>
        }
        @if (!o.available) {
          <button mat-menu-item type="button" disabled class="ctm-hint">
            <span>{{ 'courses.translations.unavailable' | translate }}</span>
          </button>
        } @else if (!o.canRequest) {
          <button mat-menu-item type="button" disabled class="ctm-hint">
            <span>{{ limitText() }}</span>
          </button>
        } @else {
          @for (opt of options(); track opt.code) {
            <button mat-menu-item type="button" class="ctm-lang" [attr.data-lang]="opt.code"
                    [disabled]="busy()" (click)="request(opt.code)">
              <span>{{ opt.label }}</span>
            </button>
          } @empty {
            <button mat-menu-item type="button" disabled class="ctm-hint">
              <span>{{ 'courses.translations.reason.nothing-to-translate' | translate }}</span>
            </button>
          }
        }
      } @else if (failed()) {
        <button mat-menu-item type="button" disabled class="ctm-hint">
          <span>{{ 'courses.translations.unavailable' | translate }}</span>
        </button>
      } @else {
        <button mat-menu-item type="button" disabled class="ctm-hint">
          <span>{{ 'common.loading' | translate }}</span>
        </button>
      }
    </mat-menu>
  `,
  styles: [`:host { display: contents; }`],
})
export class CourseTranslateMenuComponent {
  private readonly courses = inject(CourseService);
  private readonly courseLang = inject(CourseLanguageService);
  private readonly locale = inject(LocaleService);
  private readonly translate = inject(TranslateService);
  private readonly snackbar = inject(SnackbarService);

  readonly bookId = input.required<number>();

  readonly overview = signal<CourseTranslations | null>(null);
  readonly failed = signal(false);
  readonly busy = signal(false);

  readonly openJobs = computed(() => (this.overview()?.jobs ?? []).filter(isOpenJob));
  readonly options = computed(() => requestableLanguages(this.overview(), this.locale.languages));

  /** Je Person ein Auftrag: welcher läuft (hier ohne Link — im Menü wäre er ein zweiter Weg weg vom Brett). */
  readonly limitText = computed(() => {
    const mine = this.overview()?.myOpenJob;
    if (!mine) return this.translate.instant('courses.translations.reason.user-limit');
    return mine.bookId === this.bookId()
      ? this.translate.instant('courses.translations.limitHere', { language: this.name(mine.language) })
      : `${this.translate.instant('courses.translations.limit')} ${mine.bookName} · ${this.name(mine.language)}`;
  });

  name(code: string | null | undefined): string {
    return languageName(code, this.locale);
  }

  statusText(job: CourseTranslationJob): string {
    return translationJobStatusText(job, this.overview(), this.translate, this.courseLang.uiLanguage());
  }

  load(): void {
    const bookId = this.bookId();
    this.failed.set(false);
    this.courses.getTranslations(bookId).subscribe({
      next: o => {
        this.overview.set(o);
        this.courseLang.noteOverview({ bookId }, o);
      },
      error: () => { if (!this.overview()) this.failed.set(true); },
    });
  }

  request(language: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.courses.requestTranslation(this.bookId(), language).subscribe({
      next: res => {
        this.busy.set(false);
        this.snackbar.success(this.translate.instant(
          res.status === 200 ? 'courses.translations.joined' : 'courses.translations.requested'), { duration: 3500 });
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.snackbar.warn(translationReasonText(err, 'courses.translations.requestFailed', this.translate));
        this.load();
      },
    });
  }
}
