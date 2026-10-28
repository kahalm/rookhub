import { ChangeDetectionStrategy, ChangeDetectorRef, Component, DoCheck, HostListener, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog } from '@angular/material/dialog';
import { EbookMissedDiagramDialogComponent, MissedAnswer } from './ebook-missed-diagram-dialog.component';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { EbookPositionDialogComponent, EbookPositionDialogData } from './ebook-position-dialog.component';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { AuthService } from '../../core/auth.service';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { Ebook, EbookDiagram, EbookFile, EbookService, EpubMissedSpot, READABLE_FORMATS, ebookAuthorNames, formatBytes } from './ebook.service';
import { PdfViewerComponent, PdfZone } from './pdf-viewer.component';
import { EpubDiagramImage, EpubLayout, EpubLocation, EpubTocEntry, EpubViewerComponent } from './epub-viewer.component';
import { readRaw, localStore, writeRaw } from '../../core/local-json-store';
import { EbookBoardComponent } from './ebook-board.component';
import { nextZoom, parsePageInput } from './reader-progress.util';
import { NAV_DOCKED_CLASS } from '../../shell/navbar.component';
import { EinkService } from '../../core/eink.service';
import { whiteToMove } from '../../shared/chess/fen-side';

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
export class EbookReaderComponent implements OnInit, DoCheck, OnDestroy {
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
  /** Diagramm aus dem Kurs-Sprung (`?diagram=`), wird nach dem Laden der Diagramme aufs Brett gelegt. */
  private jumpDiagramId: number | null = null;

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
  private dialog = inject(MatDialog);
  private confirm = inject(ConfirmService);
  private ebooks = inject(EbookService);
  private snackbar = inject(SnackbarService);
  private auth = inject(AuthService);
  /** Melden („Diagramm falsch“) gibt es nur mit Konto (0.778.0: der Leser geht auch ohne Anmeldung). */
  get loggedIn(): boolean { return this.auth.isLoggedIn; }
  readonly authorNames = ebookAuthorNames;
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);
  /** E-Ink-Modus: Pfeil hoch/runter blättert eine Bildschirmseite, Tippzonen links/rechts am Buch (DESIGN 3.6). */
  readonly eink = inject(EinkService).enabled;
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

  /**
   * EINE Leiste (0.785.x): solange die Leser-Leiste steht (Buch geladen), dockt die App-Kopfleiste oben rechts auf ihr an
   * (body.nav-docked, Regeln in navbar.component) — Lade-/Fehlerzustände behalten die normale Kopfleiste.
   */
  get docked(): boolean { return !this.loading && !this.notFound && !this.error && !!this.ebook; }
  private dockedApplied = false;

  ngDoCheck(): void {
    const want = this.docked;
    if (want === this.dockedApplied) return;
    this.dockedApplied = want;
    document.body.classList.toggle(NAV_DOCKED_CLASS, want);
  }

  ngOnDestroy(): void {
    document.body.classList.remove(EbookReaderComponent.BODY_CLASS);
    document.body.classList.remove(NAV_DOCKED_CLASS);
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
        // Sprung aus dem Kurs („Im Buch“, 2026-10-15): ?page=…&diagram=… schlägt die gemerkte Lesestelle.
        const q = this.route.snapshot.queryParamMap;
        const jumpPage = Number(q?.get('page')) || 0;
        this.jumpDiagramId = Number(q?.get('diagram')) || null;
        this.initialPage = jumpPage > 0 ? jumpPage : book.progress?.page ?? 1;
        this.epubLocator = book.progress?.locator ?? null;
        this.page.set(this.initialPage);
        if (book.pageCount) this.pageCount.set(book.pageCount);
        if (book.diagramState === 'done' && book.diagramCount > 0) this.loadDiagrams(book.id);
        this.ebooks.accessToken().subscribe({
          next: token => {
            this.token = token;
            this.viewFormat.set(book.primaryFormat && READABLE_FORMATS.has(book.primaryFormat) ? book.primaryFormat : null);
            this.applyViewFormat();
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

  /** Liest der Browser gerade das EPUB? (Bevorzugtes Format, 0.793.0; „Als PDF lesen“ wechselt.) */
  get isEpub(): boolean { return this.viewFormat() === 'epub'; }

  /** Format, in dem gerade gelesen wird (`epub`/`pdf`); `null` = nichts lesbar (nur Download). */
  readonly viewFormat = signal<string | null>(null);

  /** Lesbare Formate dieses Buchs außer dem gerade gezeigten (für „Als … lesen“). */
  get otherReadableFormats(): string[] {
    const fmts = new Set((this.ebook?.files ?? []).map(f => f.format).filter(f => READABLE_FORMATS.has(f)));
    return [...fmts].filter(f => f !== this.viewFormat());
  }

  /** Datei zum gewählten Format (wie der Server, `EbookFormats.Primary`): beim PDF die abgeleitete Lesefassung vor dem
   *  Original, beim EPUB das echte vor dem Fließtext-EPUB aus dem PDF (0.802.0). */
  private fileFor(format: string): EbookFile | undefined {
    const files = (this.ebook?.files ?? []).filter(f => f.format === format);
    return (format === 'pdf' ? files.find(f => f.derived) : files.find(f => !f.derived)) ?? files[0];
  }

  private applyViewFormat(): void {
    const book = this.ebook, fmt = this.viewFormat();
    const file = fmt ? this.fileFor(fmt) : undefined;
    // Hauptformat ohne fileId (der Server liefert dann genau diese Datei, die Adresse bleibt wie bisher).
    this.fileUrl = book && file ? this.ebooks.fileUrl(book.id, this.token, fmt === book.primaryFormat ? {} : { fileId: file.id }) : null;
    this.epubDirUrl = book && fmt === 'epub' ? this.ebooks.epubBaseUrl(book.id, this.token) : null;
  }

  /** „Als PDF lesen“ / „Als EPUB lesen“: anderes Format desselben Buchs (Diagramme bleiben dieselben). */
  readAs(format: string): void {
    if (format === this.viewFormat() || !READABLE_FORMATS.has(format)) return;
    this.viewFormat.set(format);
    this.applyViewFormat();
    this.cdr.markForCheck();
  }

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
    } else if (this.eink()) {
      this.viewer?.pageBy(down ? 1 : -1);
    } else {
      this.viewer?.scrollByPx(down ? 80 : -80);
    }
    e.preventDefault();
  }

  /** Tippzone im E-Ink-Modus: EPUB eine Seite, PDF eine Bildschirmhöhe — springend, ohne Gleiten. */
  turn(dir: 1 | -1): void {
    if (this.isEpub) { if (dir > 0) this.epubNext(); else this.epubPrev(); }
    else this.viewer?.pageBy(dir);
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

  /** Platz über und unter dem Brett im Panel (Kopf mit ‹ ›, Leiste, Kopfzeile der Seite) — das Brett ist so breit wie das Panel. */
  static readonly PANEL_CHROME_H = 200;

  /**
   * Breite begrenzen: mindestens 260 px; das Buch behält mindestens 30 % des Fensters, und das Brett (so breit wie das
   * Panel) bleibt samt Kopf in der Fensterhöhe — sonst rutschen seine Knöpfe aus dem Bild.
   */
  static clampPanel(width: number, viewport: number, viewportHeight = Infinity): number {
    const max = Math.min(viewport * 0.7, viewportHeight - EbookReaderComponent.PANEL_CHROME_H);
    return Math.round(Math.max(EbookReaderComponent.PANEL_MIN, Math.min(width, max)));
  }

  /** Fenstergröße — die gemerkte Breite gilt nur, soweit sie ins JETZIGE Fenster passt (siehe effectivePanelWidth). */
  private readonly viewport = signal({ w: window.innerWidth, h: window.innerHeight });
  @HostListener('window:resize')
  onWindowResize(): void { this.viewport.set({ w: window.innerWidth, h: window.innerHeight }); }

  /**
   * Wirksame Panel-Breite: die gemerkte, aber gegen das aktuelle Fenster begrenzt (0.805.2). Vorher wurde sie nur beim
   * Ziehen begrenzt — am großen Monitor 1 200 px gezogen, im kleineren Fenster geöffnet, war das Panel breiter als das
   * Fenster: Buch und Trenner lagen links außerhalb, Zurücksetzen (Doppelklick auf den Trenner) ging nicht mehr.
   */
  readonly effectivePanelWidth = computed(() => {
    const w = this.panelWidth();
    const vp = this.viewport();
    return w == null ? null : EbookReaderComponent.clampPanel(w, vp.w, vp.h);
  });

  setPanelWidth(width: number): void {
    const w = EbookReaderComponent.clampPanel(width, window.innerWidth, window.innerHeight);
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
    const cur = this.effectivePanelWidth() ?? 380;
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
        // Zonen im PDF = Diagramme mit Seitenlage; Bilder im EPUB = Diagramme mit Bildpfad (auch aus dem PDF verknüpfte, 0.793.0).
        this.zones.set(list.items.filter(d => d.w > 0).map(d => ({
          id: d.id, page: d.page, x: d.x, y: d.y, w: d.w, h: d.h,
          title: d.label ? `${d.label} · ${title}` : title,
        })));
        this.epubImages.set(list.items.filter(d => !!d.imageRef).map(d => ({
          id: d.id, chapter: d.epubChapter ?? d.page, ref: d.imageRef!, title: d.label ? `${d.label} · ${title}` : title,
        })));
        // Sprung aus dem Kurs: das Diagramm gleich aufs Brett (EPUB zusätzlich hinblättern).
        const jump = this.jumpDiagramId;
        this.jumpDiagramId = null;
        if (jump != null) {
          const idx = list.items.findIndex(d => d.id === jump);
          if (idx >= 0) {
            this.pick(jump);
            const d = list.items[idx];
            if (this.isEpub && d.imageRef) setTimeout(() => this.epub?.revealImage(d.epubChapter ?? d.page, d.imageRef!));
          }
        }
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

  /** „Automatisch weiter“ nach dem Lösen: zum nächsten Diagramm, am letzten bleibt es stehen. */
  autoNextDiagram(): void {
    const list = this.diagrams();
    const cur = this.picked();
    if (!cur || list.findIndex(d => d.id === cur.id) >= list.length - 1) return;
    this.step(1);
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
    if (this.isEpub) { if (d.imageRef) this.epub?.revealImage(d.epubChapter ?? d.page, d.imageRef); }
    else if (d.w > 0) this.viewer?.revealZone(d);
  }

  /** Position in der Liste (für „3 / 1 144"). */
  get pickedIndex(): number {
    const cur = this.picked();
    return cur ? this.diagrams().findIndex(d => d.id === cur.id) + 1 : 0;
  }

  sideKey(d: EbookDiagram): string {
    if (!d.sideKnown) return 'ebooks.reader.sideUnknown';
    return whiteToMove(d.fen) ? 'common.whiteToMove' : 'common.blackToMove';
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

  /** Symbol zur Herkunft der Lösung im Brett-Kopf (Text dazu: {@link solutionNoteKey}). */
  solutionNoteIcon(d: EbookDiagram): string {
    switch (d.solutionCheck) {
      case 'engine': return 'memory';
      case 'replaced': return 'published_with_changes';
      case 'doubtful': return 'warning_amber';
      default: return 'verified';
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

  /** Doppelklick auf eine Stelle ohne erkanntes Diagramm: „Fehlt ein Diagramm?“ / „Text falsch“ / Abbrechen — die Antwort
   *  wird für die Auswertung gespeichert (Text falsch = kein fehlendes Diagramm). */
  askMissedDiagram(ebook: Ebook, at: { page: number; x: number; y: number }): void {
    if (this.diagramsPending(ebook)) return;
    if (!this.loggedIn) return;
    this.dialog.open(EbookMissedDiagramDialogComponent, { maxWidth: '30rem' }).afterClosed().subscribe((answer: MissedAnswer) => {
      if (!answer) return;
      this.reportMissed(ebook, answer, at.page, at.x, at.y);
    });
  }

  /** EPUB (0.798.0): dieselbe Rückfrage, Stelle = Bildpfad + Kapitel. „Text falsch“ bietet danach das PDF an der Stelle an
   *  (Fließtext-EPUB aus einem PDF: Druckseite aus dem Seitenanker, {@link epubPageAt}). */
  askMissedEpub(ebook: Ebook, spot: EpubMissedSpot): void {
    if (this.diagramsPending(ebook)) return;
    if (!this.loggedIn) return;
    this.dialog.open(EbookMissedDiagramDialogComponent, { maxWidth: '30rem' }).afterClosed().subscribe((answer: MissedAnswer) => {
      if (!answer) return;
      this.reportMissed(ebook, answer, 0, 0, 0, spot);
      if (answer === 'text' && this.otherReadableFormats.includes('pdf')) this.offerPdfAt(spot.page ?? null);
    });
  }

  /** Diagramme des Buchs noch nicht verarbeitet (0.871.0, Wunsch 28.10.): dann fehlt kein Diagramm, die Erkennung ist nur
   *  noch nicht gelaufen — das sagen statt „Fehlt ein Diagramm?“ zu fragen (auch ohne Anmeldung). */
  private diagramsPending(ebook: Ebook): boolean {
    if (ebook.diagramState !== 'pending') return false;
    this.snackbar.quick(this.translate.instant('ebooks.reader.diagramsPending'));
    return true;
  }

  private reportMissed(ebook: Ebook, answer: 'diagram' | 'text', page: number, x: number, y: number, spot?: EpubMissedSpot): void {
    const isDiagram = answer === 'diagram';
    this.ebooks.reportMissedDiagram(ebook.id, page, x, y, isDiagram, spot).subscribe({
      next: () => { if (isDiagram) this.snackbar.quick(this.translate.instant('ebooks.reader.missedThanks')); },
      error: () => this.snackbar.warn(this.translate.instant('ebooks.reader.missedFailed')),
    });
  }

  /** „Die Stelle im PDF nachsehen?“ → PDF öffnen, ungefähr an der Seite (ohne bekannte Seite am Anfang des Buchs). */
  private offerPdfAt(page: number | null): void {
    this.confirm.ask('ebooks.reader.checkPdfTitle', undefined, { confirm: this.translate.instant('ebooks.reader.checkPdfYes') })
      .subscribe(ok => {
        if (!ok) return;
        if (page && page > 0) { this.initialPage = page; this.page.set(page); }
        this.readAs('pdf');
      });
  }

  /** „Stellung korrigieren“ (0.798.0, ⋮ des Bretts, nur Verwalter): Editor öffnen, speichern, das Brett zeigt sofort die neue Stellung. */
  correctPosition(d: EbookDiagram): void {
    const ebook = this.ebook;
    if (!ebook?.canManage) return;
    const data: EbookPositionDialogData = { fen: d.fen, flipped: !whiteToMove(d.fen) };
    this.dialog.open(EbookPositionDialogComponent, { data, width: '32rem', maxWidth: '94vw', autoFocus: 'dialog' }).afterClosed().subscribe((fen: string | undefined) => {
      if (!fen) return;
      this.ebooks.correctDiagramPosition(ebook.id, d.id, fen).subscribe({
        next: updated => {
          const merged: EbookDiagram = { ...d, ...updated, reportedByMe: d.reportedByMe, reportCount: d.reportCount };
          this.diagrams.update(list => list.map(x => x.id === d.id ? merged : x));
          if (this.picked()?.id === d.id) this.picked.set(merged);
          this.snackbar.quick(this.translate.instant('ebooks.board.correctSaved'));
          this.cdr.markForCheck();
        },
        error: () => this.snackbar.warn(this.translate.instant('ebooks.board.correctFailed')),
      });
    });
  }

  // ── EPUB-Darstellung (0.791.0): Seite/fließend (Vorgabe fließend seit 0.803.2) + Schriftgröße, auf dem Gerät gemerkt ──────────────────
  static readonly EPUB_LAYOUT_KEY = 'rh.ebooks.epubLayout';
  static readonly EPUB_FONT_KEY = 'rh.ebooks.epubFont';
  static readonly EPUB_FONT_STEPS = [80, 90, 100, 110, 125, 140, 160, 180, 200];
  readonly epubLayout = signal<EpubLayout>(readRaw(localStore(), EbookReaderComponent.EPUB_LAYOUT_KEY) === 'pages' ? 'pages' : 'scroll');
  readonly epubFont = signal<number>(EbookReaderComponent.loadEpubFont());

  static loadEpubFont(): number {
    const v = Number(readRaw(localStore(), EbookReaderComponent.EPUB_FONT_KEY));
    return EbookReaderComponent.EPUB_FONT_STEPS.includes(v) ? v : 100;
  }

  toggleEpubLayout(): void {
    const next: EpubLayout = this.epubLayout() === 'scroll' ? 'pages' : 'scroll';
    this.epubLayout.set(next);
    writeRaw(localStore(), EbookReaderComponent.EPUB_LAYOUT_KEY, next);
  }

  epubFontBy(delta: 1 | -1): void {
    const steps = EbookReaderComponent.EPUB_FONT_STEPS;
    const i = Math.max(0, steps.indexOf(this.epubFont()));
    const v = steps[Math.min(steps.length - 1, Math.max(0, i + delta))];
    this.epubFont.set(v);
    writeRaw(localStore(), EbookReaderComponent.EPUB_FONT_KEY, String(v));
  }
}
