import {
  ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { LocaleService } from '../../core/locale.service';
import { SnackbarService } from '../../core/snackbar.service';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { formatQuietUntil } from '../games/quiet-hours.util';
import { languageName } from './course-lang-picker.component';
import { CourseLanguageService } from './course-language.service';
import { languagesFromOverview } from './course-language.util';
import { CourseService, CourseTranslationJob, CourseTranslations } from './course.service';

/** Offen = wartet oder läuft — nur solche Aufträge zeigt der Kasten, und nur solange fragt er nach. */
export function isOpenJob(job: Pick<CourseTranslationJob, 'status'>): boolean {
  return job.status === 'queued' || job.status === 'running';
}

/** Fortschritt eines Auftrags in Prozent (fertige UND gescheiterte Linien zählen als abgearbeitet). */
export function jobPercent(job: Pick<CourseTranslationJob, 'linesTotal' | 'linesDone' | 'linesFailed'>): number {
  if (!job.linesTotal || job.linesTotal <= 0) return 0;
  return Math.max(0, Math.min(100, Math.round(100 * (job.linesDone + job.linesFailed) / job.linesTotal)));
}

/**
 * Übersetzungs-Kasten der Kursseite (Stufe C der Kurs-Übersetzung, 0.549.0): welche Sprachen es
 * gibt (mit Fortschritt), was wartet oder läuft, und „Übersetzen in …" über die 25 Sprachen der App.
 *
 * <p>Regeln aus Stufe B (Server, `CourseTranslationJobService`), die hier nur ANGEZEIGT werden: je
 * Person ein offener Auftrag (Admin unbegrenzt) — dann bleibt der Knopf aus, mit Hinweis und Link auf
 * den Kurs, der gerade läuft; in den Sperrzeiten wird angenommen und gewartet („pausiert bis …");
 * zurückziehen darf man den EIGENEN wartenden Auftrag (Admin jeden offenen). Ohne Anmeldung nur lesend.</p>
 *
 * <p>Alles aus HTTP liegt in Signalen (OnPush, Angular 22 + fetch). Solange ein Auftrag offen ist,
 * fragt der Kasten alle {@link PollMs} nach und hört auf, sobald nichts mehr offen ist. Ein 404
 * (Kurs anonym nicht öffentlich) blendet den Kasten aus.</p>
 */
@Component({
  selector: 'app-course-translations',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatMenuModule, MatTooltipModule,
    TranslatePipe, HelpHintComponent,
  ],
  template: `
    @if (overview(); as o) {
      <mat-card class="tr-card">
        <mat-card-content>
          <div class="tr-head">
            <span class="tr-title">{{ 'courses.translations.title' | translate }}</span>
            <app-help-hint [text]="'courses.translations.help' | translate" />
          </div>
          <p class="tr-source">
            {{ 'courses.translations.source' | translate:{ name: sourceName() } }}
          </p>

          @if (languageRows().length) {
            <ul class="tr-list">
              @for (row of languageRows(); track row.code) {
                <li class="tr-lang">
                  {{ 'courses.translations.lines' | translate:{ name: row.name, done: (row.done | number), total: (row.total | number) } }}
                </li>
              }
            </ul>
          }

          @if (openJobs().length || failedMine()) {
            <ul class="tr-list">
              @for (job of openJobs(); track job.id) {
                <li class="tr-job" [attr.data-job]="job.id">
                  <span class="tr-job-lang">{{ name(job.language) }}</span>
                  <span class="tr-job-status">· {{ statusText(job) }}</span>
                  @if (job.requestedByMe) { <span class="tr-mine">{{ 'courses.translations.mine' | translate }}</span> }
                  @if (canWithdraw(job)) {
                    <button mat-button type="button" class="tr-withdraw" [disabled]="busy()" (click)="withdraw(job)">
                      {{ 'courses.translations.withdraw' | translate }}
                    </button>
                  }
                </li>
              }
              @if (failedMine(); as f) {
                <li class="tr-job tr-job--failed" [attr.title]="f.lastError || null">
                  <span class="tr-job-lang">{{ name(f.language) }}</span>
                  <span class="tr-job-status">· {{ 'courses.translations.status.failed' | translate }}</span>
                </li>
              }
            </ul>
          }

          <div class="tr-request">
            @if (!loggedIn()) {
              <p class="tr-hint">{{ 'courses.translations.loginHint' | translate }}</p>
            } @else if (!o.available) {
              <p class="tr-hint">{{ 'courses.translations.unavailable' | translate }}</p>
            } @else {
              <button mat-stroked-button type="button" class="tr-request-btn" [matMenuTriggerFor]="reqMenu"
                      [disabled]="!o.canRequest || busy() || requestOptions().length === 0">
                <mat-icon>translate</mat-icon>{{ 'courses.translations.request' | translate }}
              </button>
              <mat-menu #reqMenu="matMenu">
                @for (opt of requestOptions(); track opt.code) {
                  <button mat-menu-item type="button" (click)="request(opt.code)">{{ opt.label }}</button>
                }
              </mat-menu>
              @if (!o.canRequest && o.myOpenJob; as mine) {
                <p class="tr-hint tr-limit">
                  @if (mine.bookId === bookId()) {
                    {{ 'courses.translations.limitHere' | translate:{ language: name(mine.language) } }}
                  } @else {
                    {{ 'courses.translations.limit' | translate }}
                    <a [routerLink]="['/courses', mine.bookId]">{{ mine.bookName }}</a> · {{ name(mine.language) }}
                  }
                </p>
              }
            }
          </div>
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: [`
    :host { display: block; }
    .tr-head { display: flex; align-items: center; gap: 6px; }
    .tr-title { font-weight: 600; }
    .tr-source { margin: 4px 0 6px; color: color-mix(in srgb, currentColor 70%, transparent); font-size: 0.9rem; }
    .tr-list { list-style: none; margin: 0 0 6px; padding: 0; }
    .tr-list li { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; padding: 2px 0; font-size: 0.9rem; }
    .tr-job-lang { font-weight: 500; }
    .tr-mine { font-size: 0.78rem; color: color-mix(in srgb, currentColor 55%, transparent); }
    .tr-job--failed .tr-job-status { color: var(--mat-sys-error, #b3261e); }
    .tr-withdraw { min-width: 0; }
    .tr-request { display: flex; flex-direction: column; align-items: flex-start; gap: 4px; margin-top: 4px; }
    .tr-request-btn mat-icon { margin-right: 4px; }
    .tr-hint { margin: 0; font-size: 0.85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
  `],
})
export class CourseTranslationsComponent {
  /** Takt des Nachfragens, solange ein Auftrag wartet oder läuft. */
  static readonly PollMs = 15_000;

  private readonly courses = inject(CourseService);
  private readonly courseLang = inject(CourseLanguageService);
  private readonly auth = inject(AuthService);
  private readonly locale = inject(LocaleService);
  private readonly translate = inject(TranslateService);
  private readonly snackbar = inject(SnackbarService);

  readonly bookId = input.required<number>();
  /** Die Sprachliste des Kurses hat sich geändert (Quelle zuerst) — die Seite lädt ihre Labels neu. */
  readonly languagesChanged = output<string[]>();

  readonly overview = signal<CourseTranslations | null>(null);
  readonly busy = signal(false);

  private loadSub?: Subscription;
  private pollSub?: Subscription;
  private lastLanguages = '';

  readonly sourceName = computed(() => {
    const src = this.overview()?.sourceLanguage;
    if (!src || src === 'und') return this.translate.instant('courses.translations.sourceUnknown');
    return this.name(src);
  });

  /** Vorhandene Übersetzungen (ohne die Quelle) mit Fortschritt. */
  readonly languageRows = computed(() => {
    const o = this.overview();
    if (!o) return [];
    return (o.languages ?? [])
      .filter(l => l.language !== o.sourceLanguage && l.linesTranslated > 0)
      .map(l => ({ code: l.language, name: this.name(l.language), done: l.linesTranslated, total: l.linesTotal }));
  });

  readonly openJobs = computed(() => (this.overview()?.jobs ?? []).filter(isOpenJob));

  /** Der eigene zuletzt gescheiterte Auftrag (sonst stünde der Nutzer ohne Auskunft da). */
  readonly failedMine = computed(() => {
    const jobs = this.overview()?.jobs ?? [];
    return jobs.find(j => j.requestedByMe && j.status === 'failed'
      && !jobs.some(o => isOpenJob(o) && o.language === j.language)) ?? null;
  });

  /**
   * „Übersetzen in …": alle 25 Sprachen der App (Reihenfolge der Sprachauswahl) — ohne die Quelle,
   * ohne schon VOLLSTÄNDIGE und ohne solche, für die schon ein Auftrag offen ist.
   */
  readonly requestOptions = computed(() => {
    const o = this.overview();
    if (!o) return [];
    const done = new Set((o.languages ?? [])
      .filter(l => l.linesTotal > 0 && l.linesTranslated >= l.linesTotal).map(l => l.language));
    const open = new Set((o.jobs ?? []).filter(isOpenJob).map(j => j.language));
    return this.locale.languages
      .filter(l => l.code !== o.sourceLanguage && !done.has(l.code) && !open.has(l.code))
      .map(l => ({ code: l.code, label: l.label }));
  });

  constructor() {
    effect(() => {
      const id = this.bookId();
      untracked(() => this.load(id));
    });
    inject(DestroyRef).onDestroy(() => {
      this.loadSub?.unsubscribe();
      this.pollSub?.unsubscribe();
    });
  }

  loggedIn(): boolean {
    return this.auth.isLoggedIn;
  }

  name(code: string | null | undefined): string {
    return languageName(code, this.locale);
  }

  /** Zurückziehen: den eigenen WARTENDEN; ein Admin jeden offenen (ein laufender bricht dann ab). */
  canWithdraw(job: CourseTranslationJob): boolean {
    if (!this.loggedIn()) return false;
    if (this.auth.isAdmin) return isOpenJob(job);
    return job.requestedByMe && job.status === 'queued';
  }

  /** „wartet · Platz 3", „läuft · 42 %", „pausiert bis Fr., 14:00". */
  statusText(job: CourseTranslationJob): string {
    const quiet = formatQuietUntil(this.overview()?.quietUntil, this.courseLang.uiLanguage());
    if (quiet) return this.translate.instant('courses.translations.status.paused', { time: quiet });
    if (job.status === 'running') {
      return this.translate.instant('courses.translations.status.running', { percent: jobPercent(job) });
    }
    return job.queuePosition
      ? this.translate.instant('courses.translations.status.queued', { pos: job.queuePosition })
      : this.translate.instant('courses.translations.status.waiting');
  }

  /** Übersicht (neu) laden; danach entscheidet {@link schedulePoll}, ob nachgefragt wird. */
  load(bookId: number = this.bookId()): void {
    this.loadSub?.unsubscribe();
    this.loadSub = this.courses.getTranslations(bookId).subscribe({
      next: o => this.apply(bookId, o),
      error: (err: HttpErrorResponse) => {
        // Kein Zugang (anonym, nicht öffentlich) oder älterer Server: der Kasten fällt weg. Andere
        // Fehler lassen den letzten Stand stehen und versuchen es beim nächsten Takt wieder.
        if (err?.status === 404) { this.overview.set(null); this.pollSub?.unsubscribe(); return; }
        this.schedulePoll(this.overview());
      },
    });
  }

  private apply(bookId: number, o: CourseTranslations): void {
    this.overview.set(o);
    this.courseLang.noteOverview({ bookId }, o);
    const langs = languagesFromOverview(o);
    const sig = langs.join(',');
    if (sig !== this.lastLanguages) {
      const first = this.lastLanguages === '';
      this.lastLanguages = sig;
      if (!first) this.languagesChanged.emit(langs);
    }
    this.schedulePoll(o);
  }

  /** Nachfragen, SOLANGE ein Auftrag offen ist — sonst ruht der Kasten. */
  private schedulePoll(o: CourseTranslations | null): void {
    this.pollSub?.unsubscribe();
    this.pollSub = undefined;
    if (!o || !(o.jobs ?? []).some(isOpenJob)) return;
    this.pollSub = timer(CourseTranslationsComponent.PollMs).subscribe(() => this.load());
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
        this.snackbar.warn(this.reasonText(err, 'courses.translations.requestFailed'));
        // Ein 409 (Limit) bringt den offenen Auftrag mit — der Kasten zeigt dann Hinweis und Link.
        this.load();
      },
    });
  }

  withdraw(job: CourseTranslationJob): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.courses.withdrawTranslation(this.bookId(), job.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.snackbar.quick(this.translate.instant('courses.translations.withdrawn'));
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.snackbar.warn(this.reasonText(err, 'courses.translations.withdrawFailed'));
        this.load();
      },
    });
  }

  /** Den Satz zur Absage formuliert die Seite — der Server schickt nur den Grund. */
  private reasonText(err: HttpErrorResponse, fallbackKey: string): string {
    const reason = typeof err?.error?.reason === 'string' ? err.error.reason as string : null;
    if (reason) {
      const key = `courses.translations.reason.${reason}`;
      const text = this.translate.instant(key);
      if (text && text !== key) return text;
    }
    return this.translate.instant(fallbackKey);
  }
}
