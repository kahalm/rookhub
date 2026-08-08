import { Component, OnDestroy, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { CourseDetail, CourseLine, CourseManageChapter, CourseService } from './course.service';
import { formatScore, maxPoints } from './calc/calc-review.util';
import { AddLinesDialogComponent, AddLinesDialogData } from './add-lines-dialog.component';
import { SnackbarService } from '../../core/snackbar.service';
import { downloadBlob } from '../../shared/download.util';

/**
 * Kurs-Detailseite (`/courses/:bookId`): Metadaten, eigener Fortschritt und — neu — die
 * Kapitel-VERWALTUNG. Hier legt man Kapitel an und füllt sie, indem man eine Liste von Stellungen
 * hineinkopiert (Memo-Dialog, siehe {@link AddLinesDialogComponent}); außerdem Kapitel umbenennen
 * bzw. löschen, einzelne Linien löschen und den eigenen Fortschritt je Kapitel zurücksetzen.
 *
 * <p>Inhaltliche Änderungen darf nur der Besitzer bzw. ein Admin (`canManage` aus dem Backend);
 * den eigenen Fortschritt darf jeder zurücksetzen, der den Kurs sehen kann.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-course-detail',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatIconModule,
    MatMenuModule, MatProgressBarModule, MatProgressSpinnerModule, MatSlideToggleModule,
    MatTooltipModule, MatDialogModule, TranslatePipe,
  ],
  templateUrl: './course-detail.component.html',
  styleUrls: ['./course-detail.component.scss'],
})
export class CourseDetailComponent implements OnInit, OnDestroy {
  bookId!: number;
  detail: CourseDetail | null = null;
  loading = true;
  loadError = false;

  /** Aufgeklappte Kapitel → geladene Linien (Key: Kapitelname bzw. '' für „ohne Kapitel"). */
  linesByChapter: Record<string, CourseLine[]> = {};
  expanded: Record<string, boolean> = {};
  loadingLines: Record<string, boolean> = {};

  /** Kapitel, das gerade umbenannt wird (Key), + Entwurf. */
  renamingKey: string | null = null;
  renameDraft = '';
  busy = false;

  private subs = new Subscription();

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private courses: CourseService,
    private dialog: MatDialog,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.bookId = Number(this.route.snapshot.paramMap.get('bookId'));
    this.load();
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }

  private load(): void {
    this.loading = true;
    this.loadError = false;
    this.subs.add(this.courses.getDetail(this.bookId).subscribe({
      next: detail => {
        this.detail = detail;
        this.loading = false;
        // Aufgeklappte Kapitel nachladen (nach Änderungen).
        for (const key of Object.keys(this.expanded)) {
          if (this.expanded[key]) this.loadLines(this.keyToChapter(key));
        }
      },
      error: () => { this.loading = false; this.loadError = true; },
    }));
  }

  // ===== Kapitel-Schlüssel („ohne Kapitel" = '') ============================

  key(chapter: CourseManageChapter | string | null): string {
    if (chapter === null) return '';
    return typeof chapter === 'string' ? chapter : (chapter.name ?? '');
  }

  private keyToChapter(key: string): string | null {
    return key.length === 0 ? null : key;
  }

  chapterLabel(chapter: CourseManageChapter): string {
    return chapter.name ?? this.translate.instant('courses.noChapter');
  }

  /**
   * Punktestand der Selbstbewertung („14 / 24"); leer, wenn es kein Kalkulationsbuch ist. Das
   * Maximum gehört IMMER dazu — nackte Punkte sagen ohne die Zahl der Stellungen nichts; fehlt es
   * vom Server, ergibt es sich aus den Stellungen (4 je Stellung).
   */
  get calcScore(): string {
    const d = this.detail;
    if (!d?.isCalculation || d.calcPoints == null) return '';
    return formatScore(d.calcPoints, d.calcMaxPoints ?? maxPoints(d.puzzleCount));
  }

  // ===== Starten ============================================================

  /** Wohin der Start-Knopf führt: Kalkulationsbuch → Kalkulations-Modus, sonst der letzte Modus. */
  get startLink(): unknown[] {
    if (!this.detail) return ['/courses'];
    if (this.detail.isCalculation) return ['/courses', this.bookId, 'calc'];
    return ['/courses', this.bookId, this.detail.lastMode === 'random' ? 'random' : 'sequential'];
  }

  get startLabel(): string {
    if (this.detail?.isCalculation) return 'courses.calculate';
    return this.detail?.lastMode === 'random' ? 'courses.random' : 'courses.sequential';
  }

  chapterStartLink(chapter: CourseManageChapter): unknown[] | null {
    if (!this.detail) return null;
    if (this.detail.isCalculation) {
      return chapter.firstLineId == null ? null : ['/courses', this.bookId, 'calc'];
    }
    if (chapter.solverIndex == null) return null;
    const mode = this.detail.lastMode === 'random' ? 'random' : 'sequential';
    return ['/courses', this.bookId, 'chapter', chapter.solverIndex, mode];
  }

  /** Im Kalkulations-Modus wird die Stellung über `?pos=` angesprungen. */
  chapterStartParams(chapter: CourseManageChapter): Record<string, unknown> | null {
    if (!this.detail?.isCalculation) return null;
    return chapter.firstLineId == null ? null : { pos: chapter.firstLineId };
  }

  /**
   * Schaltet den Kalkulations-Modus des Kurses um (nur Besitzer/Admin, `canManage`). Danach die
   * Detailseite neu laden: Start-Knopf, Fortschritts-Zählung und die Kapitel-Startlinks hängen
   * alle am Flag. Schlägt es fehl, stellt das Nachladen den Schalter auf den echten Stand zurück.
   */
  setCalculation(value: boolean): void {
    if (!this.detail || this.detail.isCalculation === value) return;
    this.busy = true;
    this.subs.add(this.courses.setCalculation(this.bookId, value).subscribe({
      next: () => { this.busy = false; this.load(); },
      error: () => { this.busy = false; this.fail('courses.detail.calcToggleFailed'); this.load(); },
    }));
  }

  // ===== Linien eines Kapitels ==============================================

  toggleChapter(chapter: CourseManageChapter): void {
    const k = this.key(chapter);
    this.expanded[k] = !this.expanded[k];
    if (this.expanded[k] && !this.linesByChapter[k]) this.loadLines(chapter.name);
  }

  private loadLines(chapter: string | null): void {
    const k = this.key(chapter);
    this.loadingLines[k] = true;
    this.subs.add(this.courses.getChapterLines(this.bookId, chapter).subscribe({
      next: lines => { this.linesByChapter[k] = lines; this.loadingLines[k] = false; },
      error: () => { this.loadingLines[k] = false; this.fail('courses.detail.linesLoadFailed'); },
    }));
  }

  // ===== Inhalte pflegen ====================================================

  addChapter(): void {
    this.openAddLines({ chapter: undefined, chapterLocked: false });
  }

  addLines(chapter: CourseManageChapter): void {
    this.openAddLines({ chapter: chapter.name, chapterLocked: true });
  }

  private openAddLines(part: Pick<AddLinesDialogData, 'chapter' | 'chapterLocked'>): void {
    if (!this.detail) return;
    const ref = this.dialog.open(AddLinesDialogComponent, {
      data: { bookId: this.bookId, displayName: this.detail.displayName, ...part } as AddLinesDialogData,
      autoFocus: 'first-tabbable',
    });
    this.subs.add(ref.afterClosed().subscribe(changed => {
      if (changed) {
        // Frisch gefüllte Kapitel gleich aufklappen ist nicht nötig — die Zähler stimmen sofort.
        this.linesByChapter = {};
        this.load();
      }
    }));
  }

  startRename(chapter: CourseManageChapter): void {
    this.renamingKey = this.key(chapter);
    this.renameDraft = chapter.name ?? '';
  }

  cancelRename(): void {
    this.renamingKey = null;
    this.renameDraft = '';
  }

  commitRename(chapter: CourseManageChapter): void {
    const next = this.renameDraft.trim();
    if (next === (chapter.name ?? '')) { this.cancelRename(); return; }
    this.busy = true;
    this.subs.add(this.courses.renameChapter(this.bookId, chapter.name, next || null).subscribe({
      next: () => {
        this.busy = false;
        this.cancelRename();
        this.linesByChapter = {};
        this.expanded = {};
        this.load();
      },
      error: err => {
        this.busy = false;
        this.snackbar.warn(err?.error?.message || this.translate.instant('courses.detail.renameFailed'));
      },
    }));
  }

  deleteChapter(chapter: CourseManageChapter): void {
    const name = this.chapterLabel(chapter);
    if (!confirm(this.translate.instant('courses.detail.deleteChapterConfirm',
      { name, count: chapter.lineCount }))) return;
    this.busy = true;
    this.subs.add(this.courses.deleteChapter(this.bookId, chapter.name).subscribe({
      next: res => {
        this.busy = false;
        this.snackbar.quick(this.translate.instant('courses.detail.chapterDeleted', { count: res.deleted }));
        this.linesByChapter = {};
        this.expanded = {};
        this.load();
      },
      error: () => { this.busy = false; this.fail('courses.detail.deleteChapterFailed'); },
    }));
  }

  deleteLine(chapter: CourseManageChapter, line: CourseLine): void {
    if (!confirm(this.translate.instant('courses.detail.deleteLineConfirm', { line: line.round }))) return;
    this.busy = true;
    this.subs.add(this.courses.deleteLine(this.bookId, line.id).subscribe({
      next: () => {
        this.busy = false;
        // Kein optimistisches Herausfiltern: `load()` zieht Zähler UND die Linien des
        // aufgeklappten Kapitels frisch nach — das ist die einzige Quelle der Wahrheit.
        this.load();
      },
      error: () => { this.busy = false; this.fail('courses.detail.deleteLineFailed'); },
    }));
  }

  // ===== Eigener Fortschritt ================================================

  resetChapter(chapter: CourseManageChapter): void {
    if (!confirm(this.translate.instant('courses.detail.resetChapterConfirm',
      { name: this.chapterLabel(chapter) }))) return;
    this.busy = true;
    this.subs.add(this.courses.resetChapter(this.bookId, chapter.name).subscribe({
      next: res => {
        this.busy = false;
        this.snackbar.quick(this.translate.instant('courses.detail.chapterReset', { count: res.cleared }));
        this.load();
      },
      error: () => { this.busy = false; this.fail('courses.detail.resetChapterFailed'); },
    }));
  }

  resetCourse(): void {
    if (!this.detail) return;
    if (!confirm(this.translate.instant('courses.detail.resetCourseConfirm',
      { name: this.detail.displayName }))) return;
    this.busy = true;
    this.subs.add(this.courses.reset(this.bookId).subscribe({
      next: () => { this.busy = false; this.load(); },
      error: () => { this.busy = false; this.fail('courses.detail.resetCourseFailed'); },
    }));
  }

  togglePin(): void {
    if (!this.detail) return;
    const pinned = this.detail.isPinned;
    const call = pinned ? this.courses.unpinCourse(this.bookId) : this.courses.pinCourse(this.bookId);
    this.subs.add(call.subscribe({
      next: () => { if (this.detail) this.detail.isPinned = !pinned; },
      error: () => this.fail('courses.detail.pinFailed'),
    }));
  }

  downloadPgn(): void {
    if (!this.detail) return;
    const name = this.detail.fileName || `course-${this.bookId}.pgn`;
    this.subs.add(this.courses.downloadPgn(this.bookId).subscribe({
      next: blob => downloadBlob(blob, name),
      error: () => this.fail('courses.downloadFailed'),
    }));
  }

  private fail(key: string): void {
    this.snackbar.warn(this.translate.instant(key));
  }
}
