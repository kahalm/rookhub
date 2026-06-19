import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { CourseService, CourseListItem, CourseChapter } from './course.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { ReprocessBannerComponent } from '../../shared/reprocess-banner/reprocess-banner.component';
import { saveBookOffline, removeBookOffline, cachedBookFileNames } from '../puzzles/book-offline.util';

@Component({
  selector: 'app-course-list',
  standalone: true,
  imports: [
    CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressBarModule, MatTooltipModule, LoadingSpinnerComponent, TranslateModule,
    ReprocessBannerComponent
  ],
  template: `
    <div class="courses-container">
      <h1>{{ 'courses.title' | translate }}</h1>
      <p class="intro">{{ 'courses.intro' | translate }}</p>

      <app-reprocess-banner section="courses" (done)="loadCourses()" />

      @if (loading) {
        <app-loading-spinner />
      } @else if (courses.length === 0) {
        <p class="empty-hint">{{ 'courses.emptyHint' | translate }}</p>
      } @else {
        @if (publicCourses.length > 0) {
          <section class="course-section">
            <h2>{{ 'courses.sectionPublic' | translate }}</h2>
            <p class="section-hint">{{ 'courses.sectionPublicHint' | translate }}</p>
            <div class="course-grid">
              @for (c of publicCourses; track c.bookId) {
                <ng-container *ngTemplateOutlet="cardTpl; context: { $implicit: c }"></ng-container>
              }
            </div>
          </section>
        }
        @if (chessableCourses.length > 0) {
          <section class="course-section">
            <h2>{{ 'courses.sectionChessable' | translate }}</h2>
            <p class="section-hint">{{ 'courses.sectionChessableHint' | translate }}</p>
            <div class="course-grid">
              @for (c of chessableCourses; track c.bookId) {
                <ng-container *ngTemplateOutlet="cardTpl; context: { $implicit: c }"></ng-container>
              }
            </div>
          </section>
        }
      }
    </div>

    <ng-template #cardTpl let-c>
      <mat-card class="course-card">
        <mat-card-content>
          <div class="card-title">{{ c.displayName }}</div>
          <div class="card-meta">
            <span>{{ 'courses.puzzleCount' | translate:{ count: c.puzzleCount } }}</span>
            @if (c.difficulty) { <span class="meta-sep">·</span><span>{{ c.difficulty }}</span> }
            @if (c.rating) { <span class="meta-sep">·</span><span>{{ c.rating }}/10</span> }
          </div>

          <div class="progress-row">
            <mat-progress-bar mode="determinate" [value]="c.progressPercent"></mat-progress-bar>
            <span class="progress-label">{{ c.solvedCount }}/{{ c.puzzleCount }}</span>
          </div>

          @if (c.puzzleCount > 0 && c.solvedCount >= c.puzzleCount) {
            <p class="done-hint"><mat-icon>emoji_events</mat-icon> {{ 'courses.completed' | translate }}</p>
          }
        </mat-card-content>

        <div class="card-footer">
          <div class="action-row">
            <div class="primary-actions">
              <button mat-flat-button color="primary"
                      [routerLink]="['/courses', c.bookId, 'sequential']" [disabled]="c.puzzleCount === 0">
                <mat-icon>format_list_numbered</mat-icon>{{ 'courses.sequential' | translate }}
              </button>
              <button mat-stroked-button
                      [routerLink]="['/courses', c.bookId, 'random']" [disabled]="c.puzzleCount === 0">
                <mat-icon>shuffle</mat-icon>{{ 'courses.random' | translate }}
              </button>
            </div>
            <div class="util-actions">
              <button mat-icon-button
                      [matTooltip]="(isOffline(c) ? 'courses.offlineRemoveTooltip' : 'courses.offlineSaveTooltip') | translate"
                      [disabled]="c.puzzleCount === 0 || savingOffline === c.bookId"
                      (click)="toggleOffline(c)">
                <mat-icon>{{ isOffline(c) ? 'cloud_done' : 'cloud_download' }}</mat-icon>
              </button>
              <button mat-icon-button [matTooltip]="'courses.downloadPgnTooltip' | translate"
                      [disabled]="c.puzzleCount === 0 || downloadingPgn === c.bookId" (click)="downloadPgn(c)">
                <mat-icon>download</mat-icon>
              </button>
              <button mat-icon-button [matTooltip]="'courses.resetTooltip' | translate"
                      [disabled]="c.solvedCount === 0" (click)="reset(c)">
                <mat-icon>restart_alt</mat-icon>
              </button>
            </div>
          </div>

          @if (c.puzzleCount > 0) {
            <div class="chapters-block">
              <button class="chapters-toggle" (click)="toggleChapters(c)"
                      [attr.aria-expanded]="expandedBook === c.bookId">
                <mat-icon class="toggle-icon">{{ expandedBook === c.bookId ? 'expand_less' : 'expand_more' }}</mat-icon>
                <span>{{ 'courses.chapters' | translate }}@if (chaptersByBook[c.bookId]) { ({{ chaptersByBook[c.bookId].length }}) }</span>
              </button>
              @if (expandedBook === c.bookId) {
                @if (loadingChapters === c.bookId) {
                  <app-loading-spinner />
                } @else if (chaptersByBook[c.bookId]?.length) {
                  <ul class="chapter-list">
                    @for (ch of chaptersByBook[c.bookId]; track ch.index) {
                      <li class="chapter-row">
                        <span class="chapter-name" [title]="ch.name || ('courses.noChapter' | translate)">
                          {{ ch.name || ('courses.noChapter' | translate) }}
                        </span>
                        <div class="chapter-progress">
                          <mat-progress-bar class="chapter-bar" mode="determinate" [value]="ch.progressPercent"></mat-progress-bar>
                          <span class="chapter-label">{{ ch.solvedCount }}/{{ ch.puzzleCount }}</span>
                        </div>
                        <div class="chapter-btns">
                          <button mat-icon-button color="primary" [matTooltip]="'courses.sequential' | translate"
                                  [routerLink]="['/courses', c.bookId, 'chapter', ch.index, 'sequential']">
                            <mat-icon>format_list_numbered</mat-icon>
                          </button>
                          <button mat-icon-button [matTooltip]="'courses.random' | translate"
                                  [routerLink]="['/courses', c.bookId, 'chapter', ch.index, 'random']">
                            <mat-icon>shuffle</mat-icon>
                          </button>
                        </div>
                      </li>
                    }
                  </ul>
                } @else {
                  <p class="chapter-empty">{{ 'courses.chaptersEmpty' | translate }}</p>
                }
              }
            </div>
          }
        </div>
      </mat-card>
    </ng-template>
  `,
  styles: [`
    .courses-container { max-width: 1100px; margin: 24px auto; padding: 0 16px; }
    .intro { color: color-mix(in srgb, currentColor 60%, transparent); margin-bottom: 16px; }
    .empty-hint { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; padding: 16px 0; }
    .course-section { margin-bottom: 28px; }
    .course-section h2 { font-size: 1.05rem; font-weight: 600; margin: 0 0 2px; letter-spacing: .01em; }
    .section-hint { color: color-mix(in srgb, currentColor 60%, transparent); font-size: 0.88rem; margin: 0 0 10px; }
    .course-grid {
      display: grid; gap: 12px;
      grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
    }
    .course-card {
      display: flex; flex-direction: column;
      padding: 0;
      mat-card-content { padding: 14px 16px 8px; }
    }
    .card-title { font-size: 0.95rem; font-weight: 600; line-height: 1.35; margin-bottom: 2px; }
    .card-meta {
      display: flex; align-items: center; gap: 4px; flex-wrap: wrap;
      font-size: 0.8rem; color: color-mix(in srgb, currentColor 55%, transparent);
      margin-bottom: 8px;
    }
    .meta-sep { opacity: 0.5; }
    .progress-row { display: flex; align-items: center; gap: 8px; }
    .progress-row mat-progress-bar { flex: 1; --mdc-linear-progress-track-height: 5px; --mdc-linear-progress-active-indicator-height: 5px; border-radius: 3px; }
    .progress-label { font-variant-numeric: tabular-nums; font-size: 0.78rem; color: color-mix(in srgb, currentColor 55%, transparent); white-space: nowrap; min-width: 46px; text-align: right; }
    .done-hint { display: flex; align-items: center; gap: 4px; color: #4caf50; font-size: 0.82rem; font-weight: 500; margin: 6px 0 0; }
    .done-hint mat-icon { font-size: 16px; width: 16px; height: 16px; }

    .card-footer { padding: 0 16px 12px; border-top: 1px solid color-mix(in srgb, currentColor 8%, transparent); margin-top: 2px; }

    .action-row { display: flex; align-items: center; justify-content: space-between; padding-top: 8px; }
    .primary-actions { display: flex; gap: 6px; }
    .primary-actions button { font-size: 0.82rem; height: 32px; line-height: 32px; padding: 0 10px; }
    .primary-actions mat-icon { font-size: 16px; width: 16px; height: 16px; margin-right: 4px; vertical-align: middle; }
    .util-actions { display: flex; align-items: center; }
    .util-actions .mat-mdc-icon-button { width: 32px; height: 32px; padding: 4px; }
    .util-actions mat-icon { font-size: 18px; }

    .chapters-block { margin-top: 6px; }
    .chapters-toggle {
      display: flex; align-items: center; gap: 4px; background: none; border: none; cursor: pointer;
      color: inherit; font-size: 0.8rem; opacity: 0.6; padding: 2px 0;
      transition: opacity .15s;
      &:hover { opacity: 1; }
    }
    .toggle-icon { font-size: 16px; width: 16px; height: 16px; }
    .chapter-list { list-style: none; margin: 4px 0 0; padding: 0; }
    .chapter-row { display: flex; align-items: center; gap: 8px; padding: 3px 0; }
    .chapter-name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.82rem; }
    .chapter-progress { display: flex; align-items: center; gap: 6px; flex-shrink: 0; }
    .chapter-bar { width: 70px; --mdc-linear-progress-track-height: 4px; --mdc-linear-progress-active-indicator-height: 4px; }
    .chapter-label { font-variant-numeric: tabular-nums; font-size: 0.75rem; color: color-mix(in srgb, currentColor 55%, transparent); white-space: nowrap; min-width: 36px; }
    .chapter-btns { display: flex; flex-shrink: 0; }
    .chapter-btns .mat-mdc-icon-button { width: 30px; height: 30px; padding: 4px; }
    .chapter-btns mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .chapter-empty { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; font-size: 0.82rem; margin: 4px 0 0; }
  `]
})
export class CourseListComponent implements OnInit {
  courses: CourseListItem[] = [];
  loading = false;
  savingOffline: number | null = null;
  downloadingPgn: number | null = null;
  private offlineFiles = new Set<string>();

  /** Aufgeklapptes Buch (Kapitelübersicht) bzw. null. */
  expandedBook: number | null = null;
  /** Lazy geladene Kapitel je Buch (bookId → Kapitel). */
  chaptersByBook: Record<number, CourseChapter[]> = {};
  /** Buch, dessen Kapitel gerade geladen werden. */
  loadingChapters: number | null = null;

  constructor(private courseService: CourseService, private snackbar: SnackbarService, private translate: TranslateService) {}

  /** Öffentliche Kurse — über eine Gruppe freigegeben (bzw. globale Admin-Bücher). */
  get publicCourses(): CourseListItem[] {
    return this.courses.filter(c => !c.isOwned);
  }

  /** Eigene, selbst importierte Chessable-Kurse. */
  get chessableCourses(): CourseListItem[] {
    return this.courses.filter(c => c.isOwned);
  }

  isOffline(c: CourseListItem): boolean {
    return this.offlineFiles.has(c.fileName);
  }

  /** Kapitelübersicht eines Buchs auf-/zuklappen; Kapitel werden beim ersten Öffnen lazy geladen. */
  toggleChapters(c: CourseListItem): void {
    if (this.expandedBook === c.bookId) { this.expandedBook = null; return; }
    this.expandedBook = c.bookId;
    if (this.chaptersByBook[c.bookId]) return; // schon geladen
    this.loadingChapters = c.bookId;
    this.courseService.getChapters(c.bookId).subscribe({
      next: chapters => {
        this.chaptersByBook[c.bookId] = chapters;
        this.loadingChapters = null;
      },
      error: () => {
        this.loadingChapters = null;
        this.snackbar.info(this.translate.instant('courses.chaptersLoadFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }

  /** Buch offline speichern (alle Puzzles laden + cachen) bzw. den Cache wieder entfernen. */
  toggleOffline(c: CourseListItem): void {
    if (this.isOffline(c)) {
      removeBookOffline(c.fileName);
      this.offlineFiles.delete(c.fileName);
      this.snackbar.info(this.translate.instant('courses.offlineRemoved', { name: c.displayName }), { action: 'common.ok', duration: 2000 });
      return;
    }
    this.savingOffline = c.bookId;
    this.courseService.getBookPuzzles(c.bookId).subscribe({
      next: puzzles => {
        saveBookOffline(c.fileName, puzzles);
        this.offlineFiles.add(c.fileName);
        this.savingOffline = null;
        this.snackbar.info(this.translate.instant('courses.offlineSaved', { name: c.displayName, count: puzzles.length }), { action: 'common.ok', duration: 2500 });
      },
      error: () => {
        this.savingOffline = null;
        this.snackbar.info(this.translate.instant('courses.offlineFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }

  ngOnInit(): void {
    this.offlineFiles = new Set(cachedBookFileNames());
    this.loadCourses();
  }

  /**
   * Angefangene Kurse (lastActivityAt gesetzt) nach vorn — nach letzter Verwendung absteigend;
   * noch nicht angefangene danach, alphabetisch. Wird je Sektion (öffentlich/Chessable) durch die
   * order-erhaltenden Filter-Getter beibehalten.
   */
  private sortCourses(list: CourseListItem[]): CourseListItem[] {
    return [...list].sort((a, b) => {
      const aT = a.lastActivityAt ? Date.parse(a.lastActivityAt) : null;
      const bT = b.lastActivityAt ? Date.parse(b.lastActivityAt) : null;
      if (aT !== null && bT !== null) return bT - aT;   // beide angefangen → zuletzt verwendet zuerst
      if (aT !== null) return -1;                        // nur a angefangen → vor b
      if (bT !== null) return 1;                         // nur b angefangen → vor a
      return a.displayName.localeCompare(b.displayName); // beide nicht → alphabetisch
    });
  }

  loadCourses(): void {
    this.loading = true;
    this.courseService.getCourses().subscribe({
      next: courses => {
        this.courses = this.sortCourses(courses);
        this.loading = false;
      },
      error: () => {
        this.snackbar.info(this.translate.instant('courses.loadFailed'), { action: 'common.ok', duration: 3000 });
        this.loading = false;
      }
    });
  }

  reset(course: CourseListItem): void {
    if (!confirm(this.translate.instant('courses.resetConfirm', { name: course.displayName }))) return;
    this.courseService.reset(course.bookId).subscribe({
      next: p => {
        course.solvedCount = p.solvedCount;
        course.progressPercent = p.progressPercent;
        delete this.chaptersByBook[course.bookId]; // Kapitel-Fortschritt neu laden beim nächsten Öffnen
      },
      error: () => this.snackbar.info(this.translate.instant('courses.resetFailed'), { action: 'common.ok', duration: 3000 })
    });
  }

  downloadPgn(course: CourseListItem): void {
    this.downloadingPgn = course.bookId;
    this.courseService.downloadPgn(course.bookId).subscribe({
      next: blob => {
        this.downloadingPgn = null;
        const safe = (course.displayName || 'course').replace(/[^A-Za-z0-9]+/g, '_');
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = `${safe}.pgn`;
        a.click();
        URL.revokeObjectURL(a.href);
      },
      error: () => {
        this.downloadingPgn = null;
        this.snackbar.info(this.translate.instant('courses.downloadFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }
}
