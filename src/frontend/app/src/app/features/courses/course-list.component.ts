import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CourseService, CourseListItem, CourseChapter } from './course.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { ReprocessBannerComponent } from '../../shared/reprocess-banner/reprocess-banner.component';
import { saveBookOffline, removeBookOffline, cachedBookFileNames, saveCourseListCache, loadCourseListCache } from '../puzzles/book-offline.util';
import { downloadBlob } from '../../shared/download.util';
import { UploadCourseDialogComponent, UploadCourseDialogResult } from './upload-course-dialog.component';
import { ShareCourseDialogComponent, ShareCourseDialogData } from './share-course-dialog.component';
import { LinkCourseDialogComponent, LinkCourseDialogData } from './link-course-dialog.component';
import { CourseThemesDialogComponent, CourseThemesDialogData } from './course-themes-dialog.component';
import { AuthService } from '../../core/auth.service';
import { CourseCardComponent } from './course-card.component';

@Component({
  selector: 'app-course-list',
  changeDetection: ChangeDetectionStrategy.Default,
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatDialogModule,
    LoadingSpinnerComponent, TranslatePipe, ReprocessBannerComponent, CourseCardComponent
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

      @if (offlineList) {
        <div class="offline-banner">
          <mat-icon>cloud_off</mat-icon>
          <span>{{ 'courses.offlineListHint' | translate }}</span>
        </div>
      }

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
                <app-course-card [course]="c"
                  [pinning]="pinning === c.bookId"
                  [savingOffline]="savingOffline === c.bookId"
                  [downloadingPgn]="downloadingPgn === c.bookId"
                  [converting]="converting === c.bookId"
                  [deleting]="deleting === c.bookId"
                  [offline]="isOffline(c)"
                  [canManageThemes]="canManageThemes(c)"
                  [expanded]="expandedBook === c.bookId"
                  [loadingChapters]="loadingChapters === c.bookId"
                  [chapters]="chaptersByBook[c.bookId]"
                  (pinToggle)="togglePin(c)"
                  (offlineToggle)="toggleOffline(c)"
                  (pgnDownload)="downloadPgn(c)"
                  (progressReset)="reset(c)"
                  (convertRepertoire)="convertToRepertoire(c)"
                  (linkEdit)="openLinkDialog(c)"
                  (themesEdit)="openThemesDialog(c)"
                  (shareCourse)="openShareDialog(c)"
                  (deleteCourse)="deleteCourse(c)"
                  (chaptersToggle)="toggleChapters(c)"></app-course-card>
              }
            </div>
          </section>
        }
        @if (sharedCourses.length > 0) {
          <section class="course-section">
            <h2>{{ 'courses.sectionSharedWithMe' | translate }}</h2>
            <p class="section-hint">{{ 'courses.sectionSharedWithMeHint' | translate }}</p>
            <div class="course-grid">
              @for (c of sharedCourses; track c.bookId) {
                <app-course-card [course]="c"
                  [pinning]="pinning === c.bookId"
                  [savingOffline]="savingOffline === c.bookId"
                  [downloadingPgn]="downloadingPgn === c.bookId"
                  [converting]="converting === c.bookId"
                  [deleting]="deleting === c.bookId"
                  [offline]="isOffline(c)"
                  [canManageThemes]="canManageThemes(c)"
                  [expanded]="expandedBook === c.bookId"
                  [loadingChapters]="loadingChapters === c.bookId"
                  [chapters]="chaptersByBook[c.bookId]"
                  (pinToggle)="togglePin(c)"
                  (offlineToggle)="toggleOffline(c)"
                  (pgnDownload)="downloadPgn(c)"
                  (progressReset)="reset(c)"
                  (convertRepertoire)="convertToRepertoire(c)"
                  (linkEdit)="openLinkDialog(c)"
                  (themesEdit)="openThemesDialog(c)"
                  (shareCourse)="openShareDialog(c)"
                  (deleteCourse)="deleteCourse(c)"
                  (chaptersToggle)="toggleChapters(c)"></app-course-card>
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
                <app-course-card [course]="c"
                  [pinning]="pinning === c.bookId"
                  [savingOffline]="savingOffline === c.bookId"
                  [downloadingPgn]="downloadingPgn === c.bookId"
                  [converting]="converting === c.bookId"
                  [deleting]="deleting === c.bookId"
                  [offline]="isOffline(c)"
                  [canManageThemes]="canManageThemes(c)"
                  [expanded]="expandedBook === c.bookId"
                  [loadingChapters]="loadingChapters === c.bookId"
                  [chapters]="chaptersByBook[c.bookId]"
                  (pinToggle)="togglePin(c)"
                  (offlineToggle)="toggleOffline(c)"
                  (pgnDownload)="downloadPgn(c)"
                  (progressReset)="reset(c)"
                  (convertRepertoire)="convertToRepertoire(c)"
                  (linkEdit)="openLinkDialog(c)"
                  (themesEdit)="openThemesDialog(c)"
                  (shareCourse)="openShareDialog(c)"
                  (deleteCourse)="deleteCourse(c)"
                  (chaptersToggle)="toggleChapters(c)"></app-course-card>
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
                <app-course-card [course]="c"
                  [pinning]="pinning === c.bookId"
                  [savingOffline]="savingOffline === c.bookId"
                  [downloadingPgn]="downloadingPgn === c.bookId"
                  [converting]="converting === c.bookId"
                  [deleting]="deleting === c.bookId"
                  [offline]="isOffline(c)"
                  [canManageThemes]="canManageThemes(c)"
                  [expanded]="expandedBook === c.bookId"
                  [loadingChapters]="loadingChapters === c.bookId"
                  [chapters]="chaptersByBook[c.bookId]"
                  (pinToggle)="togglePin(c)"
                  (offlineToggle)="toggleOffline(c)"
                  (pgnDownload)="downloadPgn(c)"
                  (progressReset)="reset(c)"
                  (convertRepertoire)="convertToRepertoire(c)"
                  (linkEdit)="openLinkDialog(c)"
                  (themesEdit)="openThemesDialog(c)"
                  (shareCourse)="openShareDialog(c)"
                  (deleteCourse)="deleteCourse(c)"
                  (chaptersToggle)="toggleChapters(c)"></app-course-card>
              }
            </div>
          </section>
        }
      }
    </div>

  `,
  styles: [`
    .courses-container { max-width: 1100px; margin: 24px auto; padding: 0 16px; }
    .header { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
    .header h1 { margin: 0; }
    .intro { color: color-mix(in srgb, currentColor 60%, transparent); margin: 8px 0 16px; }
    .list-search { width: 100%; max-width: 360px; display: block; margin-bottom: 16px; }
    .empty-hint { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; padding: 16px 0; }

    .course-section { margin-bottom: 28px; }
    .offline-banner { display: flex; align-items: center; gap: 8px; margin: 0 0 1rem;
                      padding: 10px 12px; border-radius: 8px; font-size: 0.9rem;
                      background: color-mix(in srgb, currentColor 7%, transparent);
                      color: color-mix(in srgb, currentColor 80%, transparent); }
    .offline-banner mat-icon { flex: 0 0 auto; opacity: 0.7; }
    .course-section h2 { font-size: 1.05rem; font-weight: 600; margin: 0 0 2px; letter-spacing: .01em; }
    .section-hint { color: color-mix(in srgb, currentColor 60%, transparent); font-size: 0.88rem; margin: 0 0 10px; }
    .course-grid {
      display: grid; gap: 12px;
      grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
    }
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
  /** true = Server nicht erreichbar; Anzeige aus dem Offline-Cache (nur heruntergeladene Kurse). */
  offlineList = false;

  /** Aufgeklapptes Buch (Kapitelübersicht) bzw. null. */
  expandedBook: number | null = null;
  /** Lazy geladene Kapitel je Buch (bookId → Kapitel). */
  chaptersByBook: Record<number, CourseChapter[]> = {};
  /** Buch, dessen Kapitel gerade geladen werden. */
  loadingChapters: number | null = null;

  constructor(private courseService: CourseService, private snackbar: SnackbarService, private translate: TranslateService, private dialog: MatDialog, private auth: AuthService) {}

  /** Darf der aktuelle Nutzer die Themen-Tags dieses Kurses setzen? Admin (alle) oder Besitzer. */
  canManageThemes(course: CourseListItem): boolean {
    return this.auth.isAdmin || course.isOwned;
  }

  /** Öffnet den Themen-Multi-Select; speichert nach Bestätigung buch-global und aktualisiert die Karte. */
  openThemesDialog(course: CourseListItem): void {
    const ref = this.dialog.open<CourseThemesDialogComponent, CourseThemesDialogData, string[] | undefined>(
      CourseThemesDialogComponent, {
        width: '360px', maxWidth: '95vw',
        data: { bookId: course.bookId, displayName: course.displayName, themes: course.themes ?? [] },
      });
    ref.afterClosed().subscribe(themes => {
      if (!themes) return; // abgebrochen
      this.courseService.setCourseThemes(course.bookId, themes).subscribe({
        next: res => {
          course.themes = res.themes; // effektive Keys (Default „tactics" wenn leer)
          this.snackbar.info(this.translate.instant('courses.themes.saved', { name: course.displayName }), { action: 'common.ok', duration: 2000 });
        },
        error: () => this.snackbar.warn(this.translate.instant('courses.themes.error')),
      });
    });
  }

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
    // „In Arbeit" umfasst zwei Fälle:
    //  • tatsächlich begonnen (≥1 gelöst) und noch nicht fertig → nach Reset (solvedCount=0)
    //    verschwindet der Kurs hier wieder.
    //  • angepinnte, noch nicht fertige Kurse — der User will die ganz oben sehen (Schnellzugriff),
    //    auch bevor er sie zum ersten Mal angeklickt hat.
    // Angepinnte Kurse zuerst (nach Fortschritt absteigend, Vorlagen ganz oben), danach der Rest
    // (sortCourses hat schon nach lastActivityAt sortiert; hier bewahren wir die Ordnung).
    const eligible = this.filtered.filter(c =>
      c.puzzleCount > 0 && c.solvedCount < c.puzzleCount &&
      (c.isPinned || c.solvedCount > 0));
    return [
      ...eligible.filter(c => c.isPinned),
      ...eligible.filter(c => !c.isPinned),
    ];
  }

  /** Kurse, die andere Nutzer mit mir geteilt haben (eigene Sektion „Mit mir geteilt"). */
  get sharedCourses(): CourseListItem[] {
    return this.filtered.filter(c => c.isShared);
  }

  /** Öffentliche Kurse — über eine Gruppe freigegeben (bzw. globale Admin-Bücher);
   *  von anderen Nutzern geteilte Kurse stehen in ihrer eigenen Sektion. */
  get publicCourses(): CourseListItem[] {
    return this.filtered.filter(c => !c.isOwned && !c.isShared);
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
        this.offlineList = false;
        saveCourseListCache(courses);   // Offline-Fallback aktuell halten
        this.loading = false;
      },
      error: () => {
        // Offline/Server weg → letzte bekannte Liste zeigen, beschränkt auf heruntergeladene
        // (offline spielbare) Kurse. Ohne Cache bleibt es beim bisherigen Fehlerhinweis.
        const cached = loadCourseListCache<CourseListItem>().filter(c => this.offlineFiles.has(c.fileName));
        if (cached.length > 0) {
          this.courses = this.sortCourses(cached);
          this.offlineList = true;
        } else {
          this.snackbar.info(this.translate.instant('courses.loadFailed'), { action: 'common.ok', duration: 3000 });
        }
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

  /** Öffnet den „Kurs teilen"-Dialog (Freunde auswählen / Freigaben verwalten). */
  openShareDialog(course: CourseListItem): void {
    this.dialog.open<ShareCourseDialogComponent, ShareCourseDialogData>(
      ShareCourseDialogComponent, {
        width: '440px', maxWidth: '95vw',
        data: { bookId: course.bookId, courseName: course.displayName }
      });
  }

  /** Öffnet den „Kurs verknüpfen"-Dialog (Buch↔Workbook). Kandidaten = alle anderen Kurse der Liste. */
  openLinkDialog(course: CourseListItem): void {
    const candidates = this.courses
      .filter(c => c.bookId !== course.bookId)
      .map(c => ({ bookId: c.bookId, displayName: c.displayName }));
    const ref = this.dialog.open<LinkCourseDialogComponent, LinkCourseDialogData, boolean>(
      LinkCourseDialogComponent, {
        width: '440px', maxWidth: '95vw',
        data: {
          bookId: course.bookId, displayName: course.displayName,
          currentLinkedBookId: course.linkedBookId ?? null,
          currentLinkedName: course.linkedDisplayName ?? null,
          candidates,
        }
      });
    ref.afterClosed().subscribe(changed => { if (changed) this.loadCourses(); });
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
        downloadBlob(blob, `${safe}.pgn`);
      },
      error: () => {
        this.downloadingPgn = null;
        this.snackbar.info(this.translate.instant('courses.downloadFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }
}
