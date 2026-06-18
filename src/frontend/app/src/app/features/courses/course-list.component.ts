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
        <mat-card-header>
          <mat-card-title>{{ c.displayName }}</mat-card-title>
          <mat-card-subtitle>
            {{ 'courses.puzzleCount' | translate:{ count: c.puzzleCount } }}
            @if (c.difficulty) { · {{ c.difficulty }} }
            @if (c.rating) { · {{ c.rating }}/10 }
          </mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <div class="progress-row">
            <mat-progress-bar mode="determinate" [value]="c.progressPercent"></mat-progress-bar>
            <span class="progress-label">{{ c.solvedCount }}/{{ c.puzzleCount }} ({{ c.progressPercent }}%)</span>
          </div>
          @if (c.puzzleCount > 0 && c.solvedCount >= c.puzzleCount) {
            <p class="done-hint"><mat-icon>emoji_events</mat-icon> {{ 'courses.completed' | translate }}</p>
          }

          @if (c.puzzleCount > 0) {
            <div class="chapters-block">
              <button mat-button class="chapters-toggle" (click)="toggleChapters(c)"
                      [attr.aria-expanded]="expandedBook === c.bookId">
                <mat-icon>{{ expandedBook === c.bookId ? 'expand_less' : 'expand_more' }}</mat-icon>
                {{ 'courses.chapters' | translate }}@if (chaptersByBook[c.bookId]) { ({{ chaptersByBook[c.bookId].length }}) }
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
                        <mat-progress-bar class="chapter-bar" mode="determinate" [value]="ch.progressPercent"></mat-progress-bar>
                        <span class="chapter-label">{{ ch.solvedCount }}/{{ ch.puzzleCount }}</span>
                        <button mat-icon-button color="primary" [matTooltip]="'courses.sequential' | translate"
                                [routerLink]="['/courses', c.bookId, 'chapter', ch.index, 'sequential']">
                          <mat-icon>format_list_numbered</mat-icon>
                        </button>
                        <button mat-icon-button [matTooltip]="'courses.random' | translate"
                                [routerLink]="['/courses', c.bookId, 'chapter', ch.index, 'random']">
                          <mat-icon>shuffle</mat-icon>
                        </button>
                      </li>
                    }
                  </ul>
                } @else {
                  <p class="chapter-empty">{{ 'courses.chaptersEmpty' | translate }}</p>
                }
              }
            </div>
          }
        </mat-card-content>
        <mat-card-actions class="course-actions">
          <button mat-raised-button color="primary"
                  [routerLink]="['/courses', c.bookId, 'sequential']" [disabled]="c.puzzleCount === 0">
            <mat-icon>format_list_numbered</mat-icon> {{ 'courses.sequential' | translate }}
          </button>
          <button mat-stroked-button
                  [routerLink]="['/courses', c.bookId, 'random']" [disabled]="c.puzzleCount === 0">
            <mat-icon>shuffle</mat-icon> {{ 'courses.random' | translate }}
          </button>
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
          <span class="spacer"></span>
          <button mat-icon-button [matTooltip]="'courses.resetTooltip' | translate"
                  [disabled]="c.solvedCount === 0" (click)="reset(c)">
            <mat-icon>restart_alt</mat-icon>
          </button>
        </mat-card-actions>
      </mat-card>
    </ng-template>
  `,
  styles: [`
    .courses-container { max-width: 1100px; margin: 24px auto; padding: 0 16px; }
    .intro { color: color-mix(in srgb, currentColor 60%, transparent); margin-bottom: 16px; }
    .empty-hint { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; padding: 16px 0; }
    .course-section { margin-bottom: 28px; }
    .course-section h2 { font-size: 1.15rem; font-weight: 600; margin: 0 0 2px; }
    .section-hint { color: color-mix(in srgb, currentColor 60%, transparent); font-size: 0.9rem; margin: 0 0 12px; }
    .course-grid {
      display: grid; gap: 16px;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
    }
    .course-card { display: flex; flex-direction: column; }
    .progress-row { display: flex; align-items: center; gap: 10px; margin: 8px 0 4px; }
    .progress-row mat-progress-bar { flex: 1; }
    .progress-label { font-variant-numeric: tabular-nums; font-size: 0.85rem; color: color-mix(in srgb, currentColor 70%, transparent); white-space: nowrap; }
    .done-hint { display: flex; align-items: center; gap: 4px; color: #2e7d32; font-weight: 500; margin: 4px 0 0; }
    .done-hint mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .course-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .spacer { flex: 1 1 auto; }
    .chapters-block { margin-top: 8px; border-top: 1px solid color-mix(in srgb, currentColor 12%, transparent); padding-top: 4px; }
    .chapters-toggle { padding: 0 8px; min-width: 0; font-weight: 500; }
    .chapter-list { list-style: none; margin: 4px 0 0; padding: 0; }
    .chapter-row { display: grid; grid-template-columns: minmax(80px, 1fr) 90px auto auto auto; align-items: center; gap: 8px; padding: 2px 0; }
    .chapter-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.9rem; }
    .chapter-bar { width: 90px; }
    .chapter-label { font-variant-numeric: tabular-nums; font-size: 0.8rem; color: color-mix(in srgb, currentColor 70%, transparent); white-space: nowrap; }
    .chapter-row .mat-mdc-icon-button { width: 36px; height: 36px; padding: 6px; }
    .chapter-empty { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; font-size: 0.85rem; margin: 4px 0 0; }
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

  loadCourses(): void {
    this.loading = true;
    this.courseService.getCourses().subscribe({
      next: courses => {
        this.courses = courses;
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
