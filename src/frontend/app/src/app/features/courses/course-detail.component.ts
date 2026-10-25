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
import { MatSlideToggle, MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { CourseDetail, CourseLine, CourseManageChapter, CourseService } from './course.service';
import { formatScore, maxPoints } from './calc/calc-review.util';
import { CalcSeriesController } from './calc-series.controller';
import { CalculationService } from './calc/calculation.service';
import { CalcEdition, CalcEditionsService } from './calc-editions.service';
import { AddLinesDialogComponent, AddLinesDialogData } from './add-lines-dialog.component';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { SendToWorksheetComponent } from '../worksheets/send-to-worksheet.component';
import { WorksheetService } from '../worksheets/worksheet.service';
import { itemsFromLines } from '../worksheets/worksheet-items.util';
import { downloadBlob } from '../../shared/download.util';
import { pgnFileName } from '../../shared/pgn-export.util';
import { CourseLanguageService, CourseRef } from './course-language.service';
import { OfflineLanguageMeta, labelOr, offlineLanguageStale } from './course-language.util';
import { CourseLangPickerComponent } from './course-lang-picker.component';
import { CourseTranslationsComponent } from './course-translations.component';
import { getBookOfflineLanguage, saveBookOffline } from '../puzzles/book-offline.util';
import { AuthService } from '../../core/auth.service';
import { WeeklyService, nextWeeklySlot } from '../weekly/weekly.service';
import { courseTagList } from './course-detail-text.util';
import { setThemeKeys } from './course-themes.util';
import { ClampMoreDirective } from '../../shared/clamp-more/clamp-more.directive';
import { ChapterRowComponent } from './chapter-row.component';

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
    MatTooltipModule, IconLabelDirective, MatDialogModule, TranslatePipe, SendToWorksheetComponent,
    CourseLangPickerComponent, CourseTranslationsComponent, ClampMoreDirective, ChapterRowComponent,
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

  /** Kalk-Serie (Ausgaben, Verteiler) und Kapitelpunkte — eigener Zustandshalter, siehe {@link CalcSeriesController}. */
  readonly series: CalcSeriesController;

  /** Darf der aktuelle Nutzer die Ausgaben pflegen? (Besitzer/Admin — kommt aus dem Kurs-DTO.) */
  get canManageEditions(): boolean { return !!this.detail?.canManage; }

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private courses: CourseService,
    calc: CalculationService,
    calcEditions: CalcEditionsService,
    private dialog: MatDialog,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private worksheets: WorksheetService,
    readonly courseLang: CourseLanguageService,
    private confirm: ConfirmService,
    private auth: AuthService,
    private weekly: WeeklyService,
  ) {
    const page = this;
    this.series = new CalcSeriesController({
      get bookId() { return page.bookId; },
      get canManage() { return page.canManageEditions; },
      setBusy: busy => { page.busy = busy; },
      track: sub => page.subs.add(sub),
    }, calc, calcEditions, dialog, snackbar, translate);
  }

  // ===== Wochenpost aus diesem Kapitel ======================================

  /** Darf Wochenposts anlegen (Recht `weeklyposts.manage`, Admin eingeschlossen). */
  get canCreateWeekly(): boolean { return this.auth.has('weeklyposts.manage'); }

  /** „Wochenpost erstellen" am Kapitel: derselbe Dialog wie auf der Wochenpost-Seite, Buch und Kapitel vorbelegt,
   *  Termin = letzter Wochenpost + 7 Tage (wie dort). Gibt es die Liste gerade nicht, gilt heute 19:00. */
  async createWeeklyFromChapter(chapter: CourseManageChapter): Promise<void> {
    if (chapter.solverIndex == null) return;
    const { WeeklyFromChapterDialogComponent } = await import('../weekly/weekly-from-chapter-dialog.component');
    const open = (latest: string | null) => {
      const slot = nextWeeklySlot(latest);
      this.dialog.open(WeeklyFromChapterDialogComponent, {
        data: { date: slot.date, time: slot.time, bookId: this.bookId, chapterIndex: chapter.solverIndex! },
        width: '480px', maxWidth: '95vw',
      }).afterClosed().subscribe(post => {
        if (post) this.snackbar.info(this.translate.instant('weekly.created'), { action: 'common.ok', duration: 3000 });
      });
    };
    this.subs.add(this.weekly.getAll().subscribe({
      next: posts => open(posts.length ? posts.reduce((a, b) => (a.scheduledAt > b.scheduledAt ? a : b)).scheduledAt : null),
      error: () => open(null),
    }));
  }

  // ===== Sprache der Kommentare (Kurs-Übersetzung, Stufe C) =================

  /** Dieser Kurs für die Sprachwahl (Dateiname, sobald das Detail da ist). */
  get langRef(): CourseRef {
    return { bookId: this.bookId, fileName: this.detail?.fileName ?? null };
  }

  /** Was die Offline-Kopie dieses Kurses über ihre Sprache weiß (`null` = keine Kopie). */
  private offlineMeta: OfflineLanguageMeta | null = null;
  /** Läuft gerade das Neu-Herunterladen der Offline-Kopie? */
  redownloading = false;

  /** Liegt eine Offline-Kopie in einer anderen Sprache als der gewählten? (Kalkulationsbücher haben keine.) */
  get offlineStale(): boolean {
    if (!this.detail || this.detail.isCalculation) return false;
    return offlineLanguageStale(this.offlineMeta, this.courseLang.requestLang(this.langRef),
      this.courseLang.languages(this.langRef));
  }

  /** Sprache gewählt: merken und die Seite in ihr neu laden (Kapitelnamen). */
  pickLanguage(lang: string): void {
    this.courseLang.setChoice(this.langRef, lang);
    this.load();
  }

  /** Die Übersicht nennt neue Sprachen — ist die gewählte darunter, stehen neue Kapitelnamen bereit. */
  onLanguagesChanged(): void {
    this.load();
  }

  /** Offline-Kopie in der gewählten Sprache neu holen (ersetzt die alte). */
  redownloadOffline(): void {
    const d = this.detail;
    if (!d || this.redownloading) return;
    const lang = this.courseLang.requestLang(this.langRef);
    this.redownloading = true;
    this.subs.add(this.courses.getBookPuzzles(this.bookId, lang).subscribe({
      next: puzzles => {
        this.redownloading = false;
        const fileName = puzzles?.[0]?.bookFileName || d.fileName;
        if (!puzzles?.length || !saveBookOffline(fileName, puzzles, this.bookId, lang)) {
          this.fail('courses.lang.offlineUpdateFailed');
          return;
        }
        this.courseLang.noteLines(this.langRef, puzzles);
        this.offlineMeta = getBookOfflineLanguage(d.fileName);
        this.snackbar.quick(this.translate.instant('courses.lang.offlineUpdated'));
      },
      error: () => { this.redownloading = false; this.fail('courses.lang.offlineUpdateFailed'); },
    }));
  }

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
    this.subs.add(this.courses.getDetail(this.bookId, this.courseLang.requestLang(this.langRef)).subscribe({
      next: detail => {
        this.detail = detail;
        this.loading = false;
        this.courseLang.rememberFile(this.bookId, detail.fileName);
        this.offlineMeta = getBookOfflineLanguage(detail.fileName);
        // Kalkulationsbuch: Kapitel-Punkte der Selbstbewertung nachladen (eigener Endpoint, hält die
        // Kurs-Detail-DTO frei von Kalkulations-Feldern). Nur bewertete Kapitel bekommen eine Zahl.
        this.series.reload(detail.isCalculation);
        // Aufgeklappte Kapitel nachladen (nach Änderungen).
        for (const key of Object.keys(this.expanded)) {
          if (this.expanded[key]) this.loadLines(this.keyToChapter(key));
        }
      },
      error: () => { this.loading = false; this.loadError = true; },
    }));
  }

  /** Punkte-Anzeige eines Kapitels („14 / 24") — leer, wenn nicht bewertet / kein Kalkulationsbuch. */
  calcChapterScore(chapter: CourseManageChapter): string { return this.series.chapterScore(chapter); }

  // ===== Kalkulations-Ausgaben (Calc-Serie) — Zustand in CalcSeriesController ===

  edition(chapter: CourseManageChapter): CalcEdition | null { return this.series.edition(chapter); }
  openEdition(chapter: CourseManageChapter): void { this.series.openEdition(chapter); }
  openMembers(): void { this.series.openMembers(); }

  // ===== Kapitel-Schlüssel („ohne Kapitel" = '') ============================

  key(chapter: CourseManageChapter | string | null): string {
    if (chapter === null) return '';
    return typeof chapter === 'string' ? chapter : (chapter.name ?? '');
  }

  private keyToChapter(key: string): string | null {
    return key.length === 0 ? null : key;
  }

  /** ANGEZEIGTER Kapitelname: die Übersetzung, sonst das Original — der Schlüssel bleibt `name`. */
  chapterLabel(chapter: CourseManageChapter): string {
    return labelOr(chapter.label, chapter.name) ?? this.translate.instant('courses.noChapter');
  }

  /**
   * Punktestand der Selbstbewertung („14 / 24"); leer, wenn es kein Kalkulationsbuch ist. Das
   * Maximum gehört IMMER dazu — nackte Punkte sagen ohne die Zahl der Stellungen nichts; fehlt es
   * vom Server, ergibt es sich aus den Stellungen (4 je Stellung).
   */
  /** Beschreibung aufgeklappt? Zu Beginn zu, wenn sie lang ist (v1-rk-kursseite-beschreibung-vorlauf-handy). */
  descExpanded = false;

  /** Schlagwörter einzeln als Abzeichen statt des rohen Komma-Strings (v1-rk-kursseite-schlagwoerter-ohne-leerzeichen). */
  get tagList(): string[] { return courseTagList(this.detail?.tags); }

  /** Thema-Abzeichen: nur gesetzte Themen, keine Vorgabe „Taktik“ (d3-rk-kurs-thema-taktik-vorgabe). */
  get themeChips(): string[] {
    return this.detail && !this.detail.isCalculation ? setThemeKeys(this.detail.themes) : [];
  }

  get calcScore(): string {
    const d = this.detail;
    if (!d?.isCalculation || d.calcPoints == null) return '';
    return formatScore(d.calcPoints, d.calcMaxPoints ?? maxPoints(d.puzzleCount));
  }

  /** UI-Sweep 2026-10-11 (r2-rp-sourcefile): stammt der Kurs von Chessable? Erkannt am Dateinamen
   *  (`chessable-u…`, RepCheck-Importe `…_firstkey.pgn`) bzw. an den Tags — für die Zeile „Quelle". */
  isChessableSource(d: CourseDetail): boolean {
    return /chessable|_firstkey\.pgn$/i.test(d.fileName) || /chessable/i.test(d.tags ?? '');
  }

  /** Zeile „Quelldatei“ nur für Besitzer/Admin (r2-rp-sourcefile) — und nicht, wenn der Name vom Server erzeugt ist
   *  (`user-u{Id}-{Guid}.pgn` bei selbst angelegten Kursen): Konto-Id + GUID sagen dem Besitzer nichts, DESIGN 2 Nr. 7
   *  (d5-rk-quelldatei-interner-dateiname). Admins sehen ihn weiter. */
  get showFileName(): boolean {
    const d = this.detail;
    if (!d?.canManage || !d.fileName) return false;
    return this.auth.isAdmin || !/^user-u\d+-[0-9a-f]{32}\.pgn$/i.test(d.fileName);
  }

  /** Kurs ohne lösbare Aufgaben (Studienbuch: nur kommentierte Partien) — kein „0/0"-Fortschritt, „Linien
   *  durchsehen" wird der Hauptknopf (b1-rk-studienbuch-fortschritt-0-0). Kalkulationsbücher zählen Stellungen. */
  get isEmptyQuiz(): boolean {
    return !!this.detail && !this.detail.isCalculation && this.detail.puzzleCount === 0;
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
   * alle am Flag. Schlägt es fehl, wird der Schalter (`toggle` = `$event.source`) ausdrücklich
   * zurückgestellt: er kippt beim Klick selbst um, und das Nachladen allein stellte ihn NICHT zurück —
   * die Vorlage behält den Inhalt währenddessen stehen, `d.isCalculation` bleibt gleich, also schreibt
   * Angular `[checked]` nicht neu.
   */
  setCalculation(value: boolean, toggle?: Pick<MatSlideToggle, 'checked'>): void {
    if (!this.detail || this.detail.isCalculation === value) return;
    this.busy = true;
    this.subs.add(this.courses.setCalculation(this.bookId, value).subscribe({
      next: () => { this.busy = false; this.load(); },
      error: () => {
        this.busy = false;
        if (toggle) toggle.checked = !value;
        this.fail('courses.detail.calcToggleFailed');
        this.load();
      },
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
    this.confirm.ask('courses.detail.deleteChapterConfirm', { name, count: chapter.lineCount }).subscribe(ok => {
      if (!ok) return;
      this.busy = true;
      this.subs.add(this.courses.deleteChapter(this.bookId, chapter.name).subscribe({
        next: res => {
          this.busy = false;
          this.snackbar.quick(this.translate.instant(res.deleted === 1 ? 'courses.detail.chapterDeletedOne' : 'courses.detail.chapterDeleted', { count: res.deleted }));
          this.linesByChapter = {};
          this.expanded = {};
          this.load();
        },
        error: () => { this.busy = false; this.fail('courses.detail.deleteChapterFailed'); },
      }));
    });
  }

  deleteLine(chapter: CourseManageChapter, line: CourseLine): void {
    this.confirm.ask('courses.detail.deleteLineConfirm', { line: line.round }).subscribe(ok => {
      if (!ok) return;
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
    });
  }

  // ===== Eigener Fortschritt ================================================

  resetChapter(chapter: CourseManageChapter): void {
    this.confirm.ask('courses.detail.resetChapterConfirm', { name: this.chapterLabel(chapter) }).subscribe(ok => {
      if (!ok) return;
      this.busy = true;
      this.subs.add(this.courses.resetChapter(this.bookId, chapter.name).subscribe({
        next: res => {
          this.busy = false;
          this.snackbar.quick(this.translate.instant(res.cleared === 1 ? 'courses.detail.chapterResetOne' : 'courses.detail.chapterReset', { count: res.cleared }));
          this.load();
        },
        error: () => { this.busy = false; this.fail('courses.detail.resetChapterFailed'); },
      }));
    });
  }

  resetCourse(): void {
    if (!this.detail) return;
    this.confirm.ask('courses.detail.resetCourseConfirm', { name: this.detail.displayName }).subscribe(ok => {
      if (!ok) return;
      this.busy = true;
      this.subs.add(this.courses.reset(this.bookId).subscribe({
        next: () => { this.busy = false; this.load(); },
        error: () => { this.busy = false; this.fail('courses.detail.resetCourseFailed'); },
      }));
    });
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

  /** „Mit E-Book verknüpfen“: der Server sucht das Buch mit den meisten gleichen Stellungen und merkt sich je Linie
   *  das Diagramm — im Löser steht dann „Im Buch · S. n“. */
  linkEbook(): void {
    if (!this.detail || this.busy) return;
    this.busy = true;
    this.subs.add(this.courses.linkEbook(this.bookId).subscribe({
      next: r => {
        this.busy = false;
        if (r.ebookId == null) this.snackbar.info(this.translate.instant('courses.linkEbookNone'), { duration: 5000 });
        else this.snackbar.success(this.translate.instant('courses.linkEbookDone',
          { linked: r.linked, lines: r.lines, title: r.ebookTitle ?? '' }), { duration: 6000 });
      },
      error: () => { this.busy = false; this.fail('courses.linkEbookFailed'); },
    }));
  }

  toggleSkipInfoLines(): void {
    if (!this.detail) return;
    const skip = !this.detail.skipInfoLines;
    this.subs.add(this.courses.setSkipInfoLines(this.bookId, skip).subscribe({
      next: r => { if (this.detail) this.detail.skipInfoLines = r.skipInfoLines; },
      error: () => this.fail('courses.skipInfoLinesFailed'),
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

  downloadChapterPgn(chapter: CourseManageChapter): void {
    if (!this.detail) return;
    const name = pgnFileName(this.detail.displayName, chapter.name ?? 'no_chapter');
    this.subs.add(this.courses.downloadChapterPgn(this.bookId, chapter.name).subscribe({
      next: blob => downloadBlob(blob, name),
      error: () => this.fail('courses.downloadFailed'),
    }));
  }

  downloadLinePgn(line: CourseLine): void {
    if (!this.detail) return;
    const name = pgnFileName(this.detail.displayName, `${line.round} ${line.title ?? ''}`);
    this.subs.add(this.courses.downloadLinePgn(this.bookId, line.id).subscribe({
      next: blob => downloadBlob(blob, name),
      error: () => this.fail('courses.downloadFailed'),
    }));
  }

  // ===== Aufgabenblatt =====
  // „An Aufgabenblatt senden" schickt die STELLUNGEN, nicht die Auswahl: was hier einmal
  // hinübergeht, bleibt so, wie es war — auch wenn der Kurs später neu importiert wird.

  /** Ganzer Kurs → Blatt (`target` null = Zwischenablage). */
  sendCourseToWorksheet(target: number | null): void {
    this.sendLines(target, () => true);
  }

  /** Ein Kapitel → Blatt. */
  sendChapterToWorksheet(chapter: CourseManageChapter, target: number | null): void {
    const wanted = chapter.name?.trim() || '';
    this.sendLines(target, p => (p.chapter?.trim() || '') === wanted);
  }

  /** Die als Karteikarte markierten Linien → Blatt (dieselbe Auswahl wie „markierte Karten"). */
  sendMarkedToWorksheet(target: number | null): void {
    this.subs.add(this.courses.getFlashcardMarks(this.bookId).subscribe({
      next: marks => {
        const ids = new Set(marks.lineIds);
        this.sendLines(target, p => ids.has(p.id));
      },
      error: () => this.fail('worksheets.send.error'),
    }));
  }

  /** Gemeinsamer Weg: Linien des Kurses holen, filtern, als Stellungen schicken. */
  private sendLines(target: number | null, keep: (p: { id: number; chapter?: string | null }) => boolean): void {
    this.subs.add(this.courses.getBookPuzzles(this.bookId).subscribe({
      next: puzzles => this.worksheets.sendAndNotify(target, itemsFromLines(this.bookId, puzzles.filter(keep))),
      error: () => this.fail('worksheets.send.error'),
    }));
  }

  private fail(key: string): void {
    this.snackbar.warn(this.translate.instant(key));
  }
}
