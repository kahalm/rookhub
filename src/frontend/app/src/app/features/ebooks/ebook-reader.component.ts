import { ChangeDetectionStrategy, ChangeDetectorRef, Component, HostListener, OnDestroy, OnInit, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { AuthService } from '../../core/auth.service';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { Ebook, EbookDiagram, EbookService, READABLE_FORMATS, formatBytes } from './ebook.service';
import { PdfViewerComponent, PdfZone } from './pdf-viewer.component';
import { EpubDiagramImage, EpubLocation, EpubTocEntry, EpubViewerComponent } from './epub-viewer.component';
import { readRaw, localStore, writeRaw } from '../../core/local-json-store';
import { EbookBoardComponent } from './ebook-board.component';
import { nextZoom, parsePageInput } from './reader-progress.util';

/**
 * Leser-Seite (`/ebooks/:id`): Werkzeugleiste (zurück, Titel, Seite x / N, Zoom, Download) und darunter der
 * PDF-Leser mit der ganzen Resthöhe. Hat das Buch erkannte Diagramme (0.766.0), steht rechts ein Brett: Doppelklick auf
 * ein Diagramm im Buch holt die Stellung herüber, ‹/› springen zum vorigen/nächsten Diagramm, „Analysieren" öffnet sie
 * im Analysebrett. Unter 900 px öffnet das Brett mit dem ersten Doppelklick als Vollbild über dem Buch (0.780.2; Schließen-Knopf, Escape). Der Lesestand wird 1,5 s nach dem letzten Seitenwechsel gespeichert und beim
 * nächsten Öffnen angesprungen. Formate, die der Browser nicht zeigt (EPUB, DJVU …), bekommen eine Download-Karte.
 */
@Component({
  // Default + markForCheck: Angular 22 refresht nach HTTP-Antworten keine unmarkierte View.
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-reader',
  standalone: true,
  imports: [CommonModule, RouterLink, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslatePipe,
    IconLabelDirective, LoadErrorComponent, LoadingSpinnerComponent, PdfViewerComponent, EpubViewerComponent, EbookBoardComponent],
  templateUrl: './ebook-reader.component.html',
  styleUrls: ['./ebook-reader.component.scss'],
})
export class EbookReaderComponent implements OnInit, OnDestroy {
  @ViewChild(PdfViewerComponent) private viewer?: PdfViewerComponent;
  @ViewChild(EpubViewerComponent) private epub?: EpubViewerComponent;

  ebook: Ebook | null = null;
  token = '';
  loading = true;
  error = false;
  /** 404/403: Buch gelöscht oder nicht freigegeben — kein Netzfehler, „Wiederholen“ hülfe nie. */
  notFound = false;
  /** Adresse der Hauptdatei (mit Ausweis), wenn sie der Browser zeigen kann. */
  fileUrl: string | null = null;
  /** EPUB „als Ordner“ (nur Container/OPF/Kapitel/Bilder einzeln laden); null bei PDF. */
  epubDirUrl: string | null = null;
  readonly page = signal(1);
  readonly pageCount = signal(0);
  /** `null` = Breite füllen. */
  readonly zoom = signal<number | null>(null);
  readonly appliedZoom = signal(1);
  readonly viewerFailed = signal(false);
  initialPage = 1;

  /** Erkannte Diagramme in Lesereihenfolge (leer, solange die Erkennung läuft oder das Buch ein Scan ist). */
  readonly diagrams = signal<EbookDiagram[]>([]);
  readonly zones = signal<PdfZone[]>([]);
  /** Das Diagramm auf dem Brett. */
  readonly picked = signal<EbookDiagram | null>(null);
  /** EPUB: Lesestelle in Prozent und Inhaltsverzeichnis. */
  readonly epubPercent = signal(0);
  readonly epubToc = signal<EpubTocEntry[]>([]);
  /** EPUB-Diagramme als Bildanker (0.775.0): Doppelklick aufs Bild im Buch holt die Stellung. */
  readonly epubImages = signal<EpubDiagramImage[]>([]);
  epubLocator: string | null = null;
  private epubSave$ = new Subject<EpubLocation>();

  /** Breite der Brettleiste (Desktop), per Trenner verschiebbar, je Gerät gemerkt. */
  readonly panelWidth = signal<number | null>(EbookReaderComponent.loadPanelWidth());
  static readonly PANEL_WIDTH_KEY = 'rh.ebooks.panelWidth';
  static readonly PANEL_MIN = 260;

  /** Schmale Ansicht: Brettleiste ein-/ausgeklappt. */
  readonly panelOpen = signal(false);

  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private ebooks = inject(EbookService);
  private snackbar = inject(SnackbarService);
  private auth = inject(AuthService);
  /** Melden („Diagramm falsch“) gibt es nur mit Konto (0.778.0: der Leser geht auch ohne Anmeldung). */
  get loggedIn(): boolean { return this.auth.isLoggedIn; }
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);
  private progress$ = new Subject<number>();
  private destroy$ = new Subject<void>();

  /**
   * Body-Klasse des Lesers: blendet die App-Fußzeile aus (styles.scss), damit die Seite genau fensterhoch ist und nur
   * der eigene Scrollkasten scrollt (c1-eb-ebook-leser-seite-scrollt-um-fusszeile, DESIGN 4.5).
   */
  static readonly BODY_CLASS = 'rd-reader-mode';

  ngOnInit(): void {
    document.body.classList.add(EbookReaderComponent.BODY_CLASS);
    this.progress$.pipe(debounceTime(1500), distinctUntilChanged(), takeUntil(this.destroy$)).subscribe(p => this.saveProgress(p));
    this.epubSave$.pipe(debounceTime(1500), takeUntil(this.destroy$)).subscribe(l => this.saveEpubProgress(l));
    this.load();
  }

  ngOnDestroy(): void {
    document.body.classList.remove(EbookReaderComponent.BODY_CLASS);
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.loading = true;
    this.error = false;
    this.notFound = false;
    this.ebooks.get(id).subscribe({
      next: book => {
        this.ebook = book;
        this.initialPage = book.progress?.page ?? 1;
        this.epubLocator = book.progress?.locator ?? null;
        this.page.set(this.initialPage);
        if (book.pageCount) this.pageCount.set(book.pageCount);
        if (book.diagramState === 'done' && book.diagramCount > 0) this.loadDiagrams(book.id);
        this.ebooks.accessToken().subscribe({
          next: token => {
            this.token = token;
            this.fileUrl = book.primaryFormat && READABLE_FORMATS.has(book.primaryFormat) ? this.ebooks.fileUrl(book.id, token) : null;
            this.epubDirUrl = book.primaryFormat === 'epub' ? this.ebooks.epubBaseUrl(book.id, token) : null;
            this.loading = false;
            this.cdr.markForCheck();
          },
          error: () => { this.loading = false; this.error = true; this.cdr.markForCheck(); },
        });
      },
      error: (err: unknown) => {
        this.loading = false;
        const status = err instanceof HttpErrorResponse ? err.status : 0;
        if (status === 404 || status === 403) this.notFound = true; else this.error = true;
        this.cdr.markForCheck();
      },
    });
  }

  get readable(): boolean { return this.fileUrl != null; }

  /** Liest der Browser ein EPUB (kein PDF da)? */
  get isEpub(): boolean { return this.ebook?.primaryFormat === 'epub'; }

  onEpubLocated(l: EpubLocation): void {
    this.epubPercent.set(Math.round(l.fraction * 100));
    this.epubSave$.next(l);
  }

  private saveEpubProgress(l: EpubLocation): void {
    if (!this.ebook) return;
    const page = Math.max(1, Math.min(100, Math.round(l.fraction * 100)));
    this.ebooks.setProgress(this.ebook.id, page, 100, l.cfi).subscribe({
      error: () => this.snackbar.warn(this.translate.instant('ebooks.reader.saveProgressFailed')),
    });
  }

  /**
   * Blättern per Tastatur, egal wo der Fokus liegt (0.779.1): nach einem Zug am Brett liegt er dort, und die Tasten kamen
   * im Buch nicht mehr an. Nicht in Eingabefeldern, Menüs und dort, wo das Buch die Taste selbst bekommt (Seiten-Scroll-
   * kasten des PDF, EPUB-Leser), und nicht, wenn jemand die Taste schon verarbeitet hat (Trenner, Menüs).
   */
  @HostListener('document:keydown', ['$event'])
  onDocumentKey(e: KeyboardEvent): void {
    if (e.defaultPrevented || e.altKey || e.ctrlKey || e.metaKey || !this.readable) return;
    // Handy: das Brett liegt als Vollbild über dem Buch — Escape schließt es (0.780.2).
    if (e.key === 'Escape' && this.panelOpen() && !(e.target as HTMLElement | null)?.closest?.('.cdk-overlay-container')) {
      this.panelOpen.set(false);
      return;
    }
    const t = e.target as HTMLElement | null;
    if (t?.closest?.('input, textarea, select, [contenteditable="true"], .cdk-overlay-container, .pv-scroll, app-epub-viewer, .rd-split')) return;
    const forward = e.key === 'ArrowRight' || e.key === 'PageDown';
    const back = e.key === 'ArrowLeft' || e.key === 'PageUp';
    const down = e.key === 'ArrowDown', up = e.key === 'ArrowUp';
    if (!forward && !back && !down && !up) return;
    if (this.isEpub) {
      if (forward || down) this.epubNext(); else this.epubPrev();
    } else if (forward || back) {
      this.viewer?.goTo(Math.max(1, Math.min(this.pageCount() || 1, this.page() + (forward ? 1 : -1))));
    } else {
      this.viewer?.scrollByPx(down ? 80 : -80);
    }
    e.preventDefault();
  }

  epubPrev(): void { this.epub?.prev(); }
  epubNext(): void { this.epub?.next(); }
  epubGo(href: string): void { this.epub?.goTo(href); }

  // ── Trenner Buch | Brett ───────────────────────────────────────────────────────────────────

  private static loadPanelWidth(): number | null {
    const raw = readRaw(localStore(), EbookReaderComponent.PANEL_WIDTH_KEY);
    const n = raw ? Number(raw) : NaN;
    return Number.isFinite(n) && n >= EbookReaderComponent.PANEL_MIN ? n : null;
  }

  /** Breite begrenzen: mindestens 260 px, das Buch behält mindestens 30 % des Fensters. */
  static clampPanel(width: number, viewport: number): number {
    return Math.round(Math.max(EbookReaderComponent.PANEL_MIN, Math.min(width, viewport * 0.7)));
  }

  setPanelWidth(width: number): void {
    const w = EbookReaderComponent.clampPanel(width, window.innerWidth);
    this.panelWidth.set(w);
    writeRaw(localStore(), EbookReaderComponent.PANEL_WIDTH_KEY, String(w));
  }

  /** Ziehen am Trenner: Breite = Abstand des Zeigers vom rechten Rand. */
  startResize(ev: PointerEvent): void {
    ev.preventDefault();
    const handle = ev.currentTarget as HTMLElement;
    handle.setPointerCapture?.(ev.pointerId);
    document.body.classList.add('rd-resizing');
    const move = (e: PointerEvent) => this.setPanelWidth(window.innerWidth - e.clientX);
    const up = () => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', up);
      handle.removeEventListener('pointercancel', up);
      document.body.classList.remove('rd-resizing');
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', up);
    handle.addEventListener('pointercancel', up);
  }

  /** Tastatur am Trenner: ←/→ verschieben um 24 px, Doppelklick setzt zurück. */
  resizeKey(ev: KeyboardEvent): void {
    const cur = this.panelWidth() ?? 380;
    if (ev.key === 'ArrowLeft') { this.setPanelWidth(cur + 24); ev.preventDefault(); }
    else if (ev.key === 'ArrowRight') { this.setPanelWidth(cur - 24); ev.preventDefault(); }
  }

  resetPanelWidth(): void {
    this.panelWidth.set(null);
    writeRaw(localStore(), EbookReaderComponent.PANEL_WIDTH_KEY, '');
  }

  get hasDiagrams(): boolean { return this.diagrams().length > 0; }

  private loadDiagrams(id: number): void {
    this.ebooks.diagrams(id).subscribe({
      next: list => {
        this.diagrams.set(list.items);
        const title = this.translate.instant('ebooks.reader.diagramZone');
        this.zones.set(list.items.filter(d => !d.imageRef).map(d => ({
          id: d.id, page: d.page, x: d.x, y: d.y, w: d.w, h: d.h,
          title: d.label ? `${d.label} · ${title}` : title,
        })));
        this.epubImages.set(list.items.filter(d => !!d.imageRef).map(d => ({
          id: d.id, chapter: d.page, ref: d.imageRef!, title: d.label ? `${d.label} · ${title}` : title,
        })));
        this.cdr.markForCheck();
      },
      // Ohne Diagramme bleibt der Leser einspaltig — das Buch ist trotzdem lesbar, kein Fehlerhinweis nötig.
      error: () => {},
    });
  }

  /** Doppelklick im Buch: Stellung aufs Brett. */
  pick(id: number): void {
    const d = this.diagrams().find(x => x.id === id);
    if (!d) return;
    this.picked.set(d);
    this.panelOpen.set(true);
  }

  /** ‹ / › — voriges/nächstes Diagramm, das Buch scrollt mit. Im Kreis: nach dem letzten kommt das erste, vor dem ersten das letzte. */
  step(delta: 1 | -1): void {
    const list = this.diagrams();
    if (list.length === 0) return;
    const cur = this.picked();
    const here = this.isEpub ? this.epub?.chapter() ?? 1 : this.page();
    let idx = cur ? list.findIndex(d => d.id === cur.id) + delta : (delta > 0 ? list.findIndex(d => d.page >= here) : -1);
    if (idx < 0) idx = delta > 0 ? 0 : list.length - 1;
    if (idx >= list.length) idx = 0;
    this.showAt(idx);
  }

  /** ⏮ / ⏭ — erstes bzw. letztes Diagramm des Buchs. */
  jumpDiagram(to: 'first' | 'last'): void {
    const n = this.diagrams().length;
    if (n === 0) return;
    this.showAt(to === 'first' ? 0 : n - 1);
  }

  private showAt(idx: number): void {
    const d = this.diagrams()[idx];
    if (!d) return;
    this.pick(d.id);
    if (d.imageRef) this.epub?.revealImage(d.page, d.imageRef);
    else this.viewer?.revealZone(d);
  }

  /** Position in der Liste (für „3 / 1 144"). */
  get pickedIndex(): number {
    const cur = this.picked();
    return cur ? this.diagrams().findIndex(d => d.id === cur.id) + 1 : 0;
  }

  sideKey(d: EbookDiagram): string {
    if (!d.sideKnown) return 'ebooks.reader.sideUnknown';
    return d.fen.split(' ')[1] === 'b' ? 'common.blackToMove' : 'common.whiteToMove';
  }

  /** Hinweis zur Herkunft/Prüfung der Lösung (i18n-Schlüssel) oder `null`. */
  solutionNoteKey(d: EbookDiagram): string | null {
    switch (d.solutionCheck) {
      case 'engine': return 'ebooks.reader.solutionEngine';
      case 'doubtful': return 'ebooks.reader.solutionDoubtful';
      case 'replaced': return 'ebooks.reader.solutionReplaced';
      case 'confirmed': return 'ebooks.reader.solutionConfirmed';
      default: return null;
    }
  }

  /** „+3,2“ / „Matt in 2“ für die Seite am Zug — `null` ohne Bewertung. */
  evalText(d: EbookDiagram): string | null {
    const v = d.engineEval;
    if (v == null) return null;
    if (Math.abs(v) >= 9000) {
      const n = 10000 - Math.abs(v);
      return this.translate.instant(v > 0 ? 'ebooks.reader.mateIn' : 'ebooks.reader.matedIn', { n });
    }
    const lang = this.translate.currentLang() ?? undefined;
    return (v > 0 ? '+' : '') + (v / 100).toLocaleString(lang, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  }

  /** „Zur Lösungsseite“ aus der Brett-Leiste. */
  goToSolutionPage(page: number): void {
    if (page) this.viewer?.goTo(page);
  }

  /** „Diagramm falsch": melden bzw. zweiter Klick nimmt die Meldung zurück. */
  toggleReport(d: EbookDiagram): void {
    if (!this.ebook) return;
    const next = !d.reportedByMe;
    this.ebooks.reportDiagram(this.ebook.id, d.id, next).subscribe({
      next: r => {
        const updated = { ...d, reportedByMe: r.reported, reportCount: Math.max(0, d.reportCount + (r.reported ? 1 : -1)) };
        this.diagrams.update(list => list.map(x => x.id === d.id ? updated : x));
        if (this.picked()?.id === d.id) this.picked.set(updated);
        this.snackbar.quick(this.translate.instant(r.reported ? 'ebooks.reader.reported' : 'ebooks.reader.reportWithdrawn'));
      },
      error: () => this.snackbar.warn(this.translate.instant('ebooks.reader.reportFailed')),
    });
  }

  onLoaded(e: { pageCount: number; fitZoom: number }): void {
    this.pageCount.set(e.pageCount);
    this.viewerFailed.set(false);
  }

  onViewerFailed(): void {
    this.viewerFailed.set(true);
    // Ein abgelaufener Ausweis ist der häufigste Grund — beim nächsten Öffnen kommt ein frischer.
    this.ebooks.forgetToken();
  }

  onPageChange(p: number): void {
    this.page.set(p);
    this.progress$.next(p);
  }

  private saveProgress(page: number): void {
    if (!this.ebook) return;
    this.ebooks.setProgress(this.ebook.id, page, this.pageCount() || null).subscribe({
      error: () => this.snackbar.warn(this.translate.instant('ebooks.reader.saveProgressFailed')),
    });
  }

  jump(raw: string): void {
    const p = parsePageInput(raw, this.pageCount());
    if (p == null) return;
    this.viewer?.goTo(p);
  }

  zoomBy(direction: 1 | -1): void {
    this.zoom.set(nextZoom(this.appliedZoom(), direction));
  }

  fitWidth(): void { this.zoom.set(null); }

  downloadUrl(fileId?: number): string {
    return this.ebook ? this.ebooks.fileUrl(this.ebook.id, this.token, { fileId, download: true }) : '';
  }

  size(bytes: number): string { return formatBytes(bytes, this.translate.currentLang() ?? undefined); }

  /** „Als Kurs anlegen“: Diagramme mit Lösung werden Aufgaben eines persönlichen Kurses, danach dorthin. */
  readonly creatingCourse = signal(false);
  createCourse(ebook: Ebook): void {
    if (this.creatingCourse()) return;
    this.creatingCourse.set(true);
    this.ebooks.createCourse(ebook.id).subscribe({
      next: c => {
        this.creatingCourse.set(false);
        this.snackbar.quick(this.translate.instant('ebooks.reader.courseCreated'));
        void this.router.navigate(['/courses', c.bookId]);
      },
      error: (e: HttpErrorResponse) => {
        this.creatingCourse.set(false);
        this.snackbar.warn(this.translate.instant(e.status === 400 ? 'ebooks.reader.courseNoSolutions' : 'ebooks.reader.courseFailed'));
      },
    });
  }
}
