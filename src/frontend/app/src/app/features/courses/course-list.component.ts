import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { CourseService, CourseListItem, CourseChapter } from './course.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { ReprocessBannerComponent } from '../../shared/reprocess-banner/reprocess-banner.component';
import { saveBookOffline, removeBookOffline, cachedBookFileNames } from '../puzzles/book-offline.util';
import { UploadCourseDialogComponent, UploadCourseDialogResult } from './upload-course-dialog.component';

@Component({
  selector: 'app-course-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatProgressBarModule, MatTooltipModule, MatDialogModule,
    LoadingSpinnerComponent, TranslateModule, ReprocessBannerComponent
  ],
  template: `
    <div class="courses-container">
      <div class="header">
        <h1>{{ 'courses.title' | translate }}</h1>
        <button mat-raised-button color="primary" [disabled]="uploading" (click)="openUploadDialog()">
          <mat-icon>{{ uploading ? 'hourglass_empty' : 'upload_file' }}</mat-icon>
          {{ (uploading ? 'courses.upload.uploading' : 'courses.upload.button') | translate }}
        </button>
      </div>
      <p class="intro">{{ 'courses.intro' | translate }}</p>

      <app-reprocess-banner section="courses" (done)="loadCourses()" />

      @if (loading) {
        <app-loading-spinner />
      } @else if (courses.length === 0) {
        <p class="empty-hint">{{ 'courses.emptyHint' | translate }}</p>
      } @else {
        <mat-form-field appearance="outline" class="list-search" subscriptSizing="dynamic">
          <mat-icon matPrefix>search</mat-icon>
          <input matInput [(ngModel)]="search" [placeholder]="'courses.searchPlaceholder' | translate"
                 [attr.aria-label]="'common.search' | translate">
          @if (search) {
            <button matSuffix mat-icon-button (click)="search = ''" [attr.aria-label]="'common.clear' | translate">
              <mat-icon>close</mat-icon>
            </button>
          }
        </mat-form-field>
        @if (filtered.length === 0) {
          <p class="empty-hint">{{ 'courses.noMatch' | translate:{ query: search } }}</p>
        }
        @if (inProgressCourses.length > 0) {
          <section class="course-section">
            <h2>{{ 'courses.sectionInProgress' | translate }}</h2>
            <p class="section-hint">{{ 'courses.sectionInProgressHint' | translate }}</p>
            <div class="course-grid">
              @for (c of inProgressCourses; track c.bookId) {
                <ng-container *ngTemplateOutlet="cardTpl; context: { $implicit: c }"></ng-container>
              }
            </div>
          </section>
        }
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
              <button mat-icon-button class="pin-btn" [class.pinned]="c.isPinned"
                      [matTooltip]="(c.isPinned ? 'courses.unpinTooltip' : 'courses.pinTooltip') | translate"
                      [disabled]="pinning === c.bookId" (click)="togglePin(c)">
                <mat-icon>push_pin</mat-icon>
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
              <button mat-icon-button [matTooltip]="'courses.resetTooltip' | translate"
                      [disabled]="c.solvedCount === 0" (click)="reset(c)">
                <mat-icon>restart_alt</mat-icon>
              </button>
              <button mat-icon-button [matTooltip]="'courses.convertToRepertoireTooltip' | translate"
                      [disabled]="converting === c.bookId" (click)="convertToRepertoire(c)">
                <mat-icon>library_books</mat-icon>
              </button>
              @if (c.isOwned) {
                <button mat-icon-button class="delete-btn" [matTooltip]="'courses.deleteTooltip' | translate"
                        [disabled]="deleting === c.bookId" (click)="deleteCourse(c)">
                  <mat-icon>delete</mat-icon>
                </button>
              }
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
    .header { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
    .header h1 { margin: 0; }
    .intro { color: color-mix(in srgb, currentColor 60%, transparent); margin: 8px 0 16px; }
    .list-search { width: 100%; max-width: 360px; display: block; margin-bottom: 16px; }
    .empty-hint { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; padding: 16px 0; }

    .delete-btn { color: color-mix(in srgb, #e53935 80%, currentColor); }
    /* Pin-Symbol: gedämpft wenn nicht angepinnt, in Primärfarbe + leicht gekippt wenn angepinnt. */
    .pin-btn mat-icon { opacity: 0.55; transition: opacity .15s, color .15s; }
    .pin-btn.pinned mat-icon { opacity: 1; color: var(--mdc-theme-primary, #3f51b5); transform: rotate(0); }
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
  /** Freitext-Suche (filtert clientseitig nach Kurstitel). */
  search = '';
  loading = false;
  savingOffline: number | null = null;
  downloadingPgn: number | null = null;
  /** Läuft gerade ein Upload eines eigenen Kurs-PGN? */
  uploading = false;
  /** bookId, der gerade gelöscht wird (Button-Sperre). */
  deleting: number | null = null;
  /** bookId, dessen Pin gerade umgeschaltet wird (Button-Sperre). */
  pinning: number | null = null;
  /** bookId, der gerade in ein Repertoire umgewandelt wird (Button-Sperre). */
  converting: number | null = null;
  private offlineFiles = new Set<string>();

  /** Aufgeklapptes Buch (Kapitelübersicht) bzw. null. */
  expandedBook: number | null = null;
  /** Lazy geladene Kapitel je Buch (bookId → Kapitel). */
  chaptersByBook: Record<number, CourseChapter[]> = {};
  /** Buch, dessen Kapitel gerade geladen werden. */
  loadingChapters: number | null = null;

  constructor(private courseService: CourseService, private snackbar: SnackbarService, private translate: TranslateService, private dialog: MatDialog) {}

  /** Angefangene, noch nicht abgeschlossene Kurse — „In Arbeit". Erscheinen ZUSÄTZLICH oben,
   *  bleiben aber auch in ihrer normalen Sektion (öffentlich/Chessable). Reihenfolge = zuletzt
   *  verwendet zuerst (durch sortCourses bereits vorsortiert). */
  /** Kurse nach Suchtext gefiltert (Titel, case-insensitive); Basis für alle Sektionen. */
  get filtered(): CourseListItem[] {
    const q = this.search.trim().toLowerCase();
    if (!q) return this.courses;
    return this.courses.filter(c => (c.displayName || '').toLowerCase().includes(q));
  }

  get inProgressCourses(): CourseListItem[] {
    // „In Arbeit" = tatsächlich begonnen (≥1 gelöst) und noch nicht fertig. Nach einem Reset
    // ist solvedCount=0 → der Kurs verschwindet hier wieder (auch wenn lastActivityAt gesetzt bleibt).
    return this.filtered.filter(c => c.solvedCount > 0 && c.solvedCount < c.puzzleCount);
  }

  /** Öffentliche Kurse — über eine Gruppe freigegeben (bzw. globale Admin-Bücher). */
  get publicCourses(): CourseListItem[] {
    return this.filtered.filter(c => !c.isOwned);
  }

  /** Eigene, selbst importierte Chessable-Kurse. */
  get chessableCourses(): CourseListItem[] {
    return this.filtered.filter(c => c.isOwned);
  }

  isOffline(c: CourseListItem): boolean {
    return this.offlineFiles.has(c.fileName);
  }

  /** Kurs fürs Dashboard an-/abpinnen (optimistisch, Rollback bei Fehler). */
  togglePin(c: CourseListItem): void {
    const target = !c.isPinned;
    this.pinning = c.bookId;
    c.isPinned = target; // optimistisch
    const req = target ? this.courseService.pinCourse(c.bookId) : this.courseService.unpinCourse(c.bookId);
    req.subscribe({
      next: () => {
        this.pinning = null;
        this.snackbar.info(this.translate.instant(target ? 'courses.pinned' : 'courses.unpinned', { name: c.displayName }), { action: 'common.ok', duration: 2000 });
      },
      error: () => {
        c.isPinned = !target; // Rollback
        this.pinning = null;
        this.snackbar.info(this.translate.instant('courses.pinFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
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
        saveBookOffline(c.fileName, puzzles, c.bookId);
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

  /** Öffnet den Upload-Dialog; startet nach Bestätigung den Upload. */
  openUploadDialog(): void {
    if (this.uploading) return;
    const ref = this.dialog.open<UploadCourseDialogComponent, void, UploadCourseDialogResult>(
      UploadCourseDialogComponent, { width: '440px', maxWidth: '95vw' });
    ref.afterClosed().subscribe(result => {
      if (!result) return;
      this.uploadCourseFile(result.file, result.name);
    });
  }

  /** Lädt eine PGN-Datei als persönlichen Kurs hoch und sortiert das Ergebnis in die Liste ein. */
  uploadCourseFile(file: File, name: string): void {
    this.uploading = true;
    this.courseService.uploadCourse(file, name).subscribe({
      next: course => {
        this.uploading = false;
        // Neuen Kurs einsortieren (statt kompletten Reload) + Menü/Navbar-Zugriff neu prüfen lassen.
        this.courses = this.sortCourses([...this.courses.filter(c => c.bookId !== course.bookId), course]);
        this.courseService.notifyAccessChanged();
        this.snackbar.info(this.translate.instant('courses.upload.success', { name: course.displayName, count: course.puzzleCount }), { action: 'common.ok', duration: 3000 });
      },
      error: err => {
        this.uploading = false;
        const msg = err?.error?.message || this.translate.instant('courses.upload.failed');
        this.snackbar.info(msg, { action: 'common.ok', duration: 4000 });
      }
    });
  }

  /** Eigenen Kurs löschen (mit Rückfrage). */
  deleteCourse(course: CourseListItem): void {
    if (!confirm(this.translate.instant('courses.deleteConfirm', { name: course.displayName }))) return;
    this.deleting = course.bookId;
    this.courseService.deleteCourse(course.bookId).subscribe({
      next: () => {
        this.deleting = null;
        this.courses = this.courses.filter(c => c.bookId !== course.bookId);
        delete this.chaptersByBook[course.bookId];
        this.courseService.notifyAccessChanged();
      },
      error: () => {
        this.deleting = null;
        this.snackbar.info(this.translate.instant('courses.deleteFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }

  /** Kurs in ein neues Repertoire umwandeln (Original bleibt). */
  convertToRepertoire(course: CourseListItem): void {
    this.converting = course.bookId;
    this.courseService.convertToRepertoire(course.bookId).subscribe({
      next: rep => {
        this.converting = null;
        // Verschieben: ein EIGENER Kurs wurde serverseitig entfernt → auch aus der Liste nehmen.
        // Geteilte Gruppen-/Admin-Kurse bleiben bestehen (gehören dem User nicht).
        if (course.isOwned) {
          this.courses = this.courses.filter(c => c.bookId !== course.bookId);
          delete this.chaptersByBook[course.bookId];
          this.courseService.notifyAccessChanged();
        }
        this.snackbar.info(this.translate.instant('courses.convertedToRepertoire', { name: rep.name }), { action: 'common.ok', duration: 3000 });
      },
      error: () => {
        this.converting = null;
        this.snackbar.info(this.translate.instant('courses.convertToRepertoireFailed'), { action: 'common.ok', duration: 3000 });
      }
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
