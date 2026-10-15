import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, NgZone, OnChanges, OnDestroy, Output,
  SimpleChanges, ViewChild, inject, signal,
} from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { PdfDocument, PdfEngineLoader, PdfLoadingTask, PdfPage } from './pdf-engine';
import { WIDE_FIT_MAX_WIDTH, currentPage, fitWidthZoom, pagesToRender, readingAnchor, scrollTopForAnchor } from './reader-progress.util';
import { mediaSignal } from '../../shared/layout/wide-media';

/** Eine anklickbare Zone über der Seite (Lage als Anteil der Seite, 0–1). */
export interface PdfZone { id: number; page: number; x: number; y: number; w: number; h: number; title?: string; }

interface PageSlot {
  index: number;            // 1-basiert
  width: number;            // bei Faktor 1 (72 dpi)
  height: number;
  rendered: number;         // Faktor, mit dem die Leinwand zuletzt gezeichnet wurde (0 = leer)
  rendering: Promise<void> | null;
}

/**
 * Der PDF-Leser: alle Seiten untereinander in einem eigenen Scrollkasten, gezeichnet werden nur die sichtbaren plus
 * eine Sichtfeldhöhe davor und danach (Leinwände weiter weg werden wieder geleert — ein 3 000-Seiten-Buch bliebe sonst
 * nicht im Speicher). pdf.js kommt aus den Assets (`PdfEngineLoader`), die Datei per Bereichsanfragen.
 *
 * Meldet die aktuelle Seite (`pageChange`), die Seitenzahl (`loaded`) und Fehler (`failed`); `zoom` ist der Faktor
 * auf die 72-dpi-Breite, `fitWidth` rechnet ihn aus der Kastenbreite.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-pdf-viewer',
  standalone: true,
  imports: [MatProgressBarModule, TranslatePipe],
  template: `
    @if (loading()) { <mat-progress-bar mode="indeterminate" class="pv-bar"></mat-progress-bar> }
    <div class="pv-scroll" #scroll (scroll)="onScroll()" tabindex="0" [attr.aria-label]="'ebooks.reader.area' | translate">
      @if (error()) {
        <p class="pv-error" role="alert">{{ 'ebooks.reader.loadFailed' | translate }}</p>
      }
      <div class="pv-pages" #pages></div>
    </div>
  `,
  styles: [`
    :host { display: block; position: relative; height: 100%; min-height: 0; }
    .pv-bar { position: absolute; top: 0; left: 0; right: 0; z-index: 1; }
    .pv-scroll { height: 100%; overflow: auto; background: color-mix(in srgb, var(--mat-sys-surface) 85%, black); outline: none; }
    .pv-pages { display: flex; flex-direction: column; align-items: center; gap: 12px; padding: 12px 8px 40px; }
    .pv-pages canvas { display: block; background: #fff; box-shadow: 0 2px 10px rgba(0, 0, 0, .35); }
    /* Seiten-Hülle + Diagrammzonen werden per DOM-API angelegt — ohne die Kapselung des Templates, daher ::ng-deep. */
    :host ::ng-deep .pv-page { position: relative; }
    :host ::ng-deep .pv-zone {
      position: absolute; padding: 0; margin: 0; border: 2px solid transparent; border-radius: 3px;
      background: transparent; cursor: zoom-in;
    }
    :host ::ng-deep .pv-zone:hover, :host ::ng-deep .pv-zone:focus-visible {
      border-color: color-mix(in srgb, var(--mat-sys-primary) 70%, transparent);
      background: color-mix(in srgb, var(--mat-sys-primary) 8%, transparent); outline: none;
    }
    /* Touch (kein Überfahren, c1-eb-reader-mobile-diagrams-undiscoverable): Zonen dauerhaft gestrichelt, mit Hof aus
       on-primary — die Seite ist in beiden Designs weiß, der Hof hält den Rand sichtbar. */
    @media (hover: none) {
      :host ::ng-deep .pv-zone {
        border: 2px dashed var(--mat-sys-primary); box-shadow: 0 0 0 1px var(--mat-sys-on-primary);
      }
    }
    :host ::ng-deep .pv-zone--active { border-color: var(--mat-sys-primary); background: color-mix(in srgb, var(--mat-sys-primary) 12%, transparent); }
    .pv-error { text-align: center; padding: 2rem 1rem; color: var(--rh-error); }
  `],
})
export class PdfViewerComponent implements AfterViewInit, OnChanges, OnDestroy {
  /** Adresse der PDF-Datei (mit Ausweis). */
  @Input({ required: true }) src = '';
  /** Seite, die nach dem Laden angesprungen wird (Lesestand). */
  @Input() initialPage = 1;
  /** Zoomfaktor; `null` = Breite füllen. */
  @Input() zoom: number | null = null;

  @Output() pageChange = new EventEmitter<number>();
  @Output() loaded = new EventEmitter<{ pageCount: number; fitZoom: number }>();
  @Output() failed = new EventEmitter<unknown>();
  /** Der tatsächlich benutzte Faktor (nach „Breite füllen"), damit die Werkzeugleiste ihn anzeigen kann. */
  @Output() zoomApplied = new EventEmitter<number>();
  /** Zonen über den Seiten (erkannte Diagramme) — Doppelklick meldet `zonePick`. */
  @Input() zones: readonly PdfZone[] = [];
  /** Hervorgehobene Zone (das Diagramm, das gerade auf dem Brett steht). */
  @Input() activeZone: number | null = null;
  @Output() zonePick = new EventEmitter<number>();
  /** Doppelklick auf eine Seitenstelle OHNE Zone (0.788.0): Seite + Klickpunkt als Anteil der Seite. */
  @Output() blankDblclick = new EventEmitter<{ page: number; x: number; y: number }>();

  @ViewChild('scroll', { static: true }) private scrollRef!: ElementRef<HTMLDivElement>;
  @ViewChild('pages', { static: true }) private pagesRef!: ElementRef<HTMLDivElement>;

  readonly loading = signal(false);
  readonly error = signal(false);
  /** Breitbild-Stufe: „Breite füllen“ höchstens WIDE_FIT_MAX_WIDTH breit. */
  private readonly wide = mediaSignal();

  private engine = inject(PdfEngineLoader);
  private zone = inject(NgZone);
  private doc: PdfDocument | null = null;
  /** Der Ladevorgang — nur er kann Dokument + Worker-Verbindung wieder freigeben (`destroy`). */
  private task: PdfLoadingTask | null = null;
  private slots: PageSlot[] = [];
  private canvases: HTMLCanvasElement[] = [];
  private wraps: HTMLDivElement[] = [];
  private pageCache = new Map<number, PdfPage>();
  private scale = 1;
  private current = 0;
  private scrollRaf = 0;
  private resizeObserver: ResizeObserver | null = null;
  private destroyed = false;
  private loadSeq = 0;

  ngAfterViewInit(): void {
    this.resizeObserver = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => this.onResize());
    this.resizeObserver?.observe(this.scrollRef.nativeElement);
    if (this.src) void this.open();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['src'] && !changes['src'].firstChange) void this.open();
    if (changes['zoom'] && !changes['zoom'].firstChange && this.doc) this.keepReadingPlace(() => this.applyZoom());
    if ((changes['zones'] && !changes['zones'].firstChange) || changes['activeZone']) this.placeZones();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.resizeObserver?.disconnect();
    cancelAnimationFrame(this.scrollRaf);
    void this.task?.destroy();
  }

  /** Um ein Stück scrollen (Pfeiltasten aus dem Leser, wenn der Fokus woanders liegt — z. B. im Brett, 0.779.1). */
  scrollByPx(dy: number): void {
    this.scrollRef.nativeElement.scrollBy({ top: dy });
    this.onScroll();
  }

  /** Zu einer Seite springen (1-basiert). */
  goTo(page: number): void {
    const slot = this.slots[page - 1];
    if (!slot) return;
    const top = this.canvases[page - 1]?.parentElement?.offsetTop ?? 0;
    this.scrollRef.nativeElement.scrollTo({ top: Math.max(0, top - 12) });
    this.onScroll();
  }

  private async open(): Promise<void> {
    const seq = ++this.loadSeq;
    this.error.set(false);
    this.loading.set(true);
    this.pageCache.clear();
    this.pagesRef.nativeElement.replaceChildren();
    this.slots = [];
    this.canvases = [];
    this.wraps = [];
    this.current = 0;
    try {
      const lib = await this.engine.load();
      const old = this.task;
      this.doc = null;
      this.task = null;
      void old?.destroy();
      const task = lib.getDocument(this.engine.documentParams(this.src) as Parameters<typeof lib.getDocument>[0]);
      const doc = await task.promise;
      if (seq !== this.loadSeq || this.destroyed) { void task.destroy(); return; }
      this.task = task;
      this.doc = doc;
      const first = await doc.getPage(1);
      this.pageCache.set(1, first);
      const base = first.getViewport({ scale: 1 });
      for (let i = 1; i <= doc.numPages; i++)
        this.slots.push({ index: i, width: base.width, height: base.height, rendered: 0, rendering: null });
      this.buildCanvases();
      this.applyZoom();
      this.placeZones();
      this.loading.set(false);
      this.loaded.emit({ pageCount: doc.numPages, fitZoom: this.fitZoom(base.width) });
      if (this.initialPage > 1) requestAnimationFrame(() => this.goTo(this.initialPage));
      else this.onScroll();
    } catch (e) {
      if (seq !== this.loadSeq || this.destroyed) return;
      this.loading.set(false);
      this.error.set(true);
      this.failed.emit(e);
    }
  }

  private buildCanvases(): void {
    const frag = document.createDocumentFragment();
    for (const slot of this.slots) {
      const wrap = document.createElement('div');
      wrap.className = 'pv-page';
      wrap.dataset['page'] = String(slot.index);
      const canvas = document.createElement('canvas');
      canvas.width = 0;    // keine 300×150-Vorgabe-Bitmap je Seite — gezeichnet wird erst, was sichtbar ist
      canvas.height = 0;
      canvas.setAttribute('role', 'img');
      canvas.setAttribute('aria-label', `${slot.index}`);
      canvas.addEventListener('dblclick', ev => {
        const r = canvas.getBoundingClientRect();
        if (r.width <= 0 || r.height <= 0) return;
        const x = Math.min(1, Math.max(0, (ev.clientX - r.left) / r.width));
        const y = Math.min(1, Math.max(0, (ev.clientY - r.top) / r.height));
        this.zone.run(() => this.blankDblclick.emit({ page: slot.index, x, y }));
      });
      wrap.appendChild(canvas);
      frag.appendChild(wrap);
      this.wraps.push(wrap);
      this.canvases.push(canvas);
    }
    this.pagesRef.nativeElement.appendChild(frag);
  }

  /**
   * Legt je Zone ein Element über die Seite (Prozent der Seite → passt bei jedem Zoom ohne Neurechnen). Doppelklick
   * holt das Diagramm aufs Brett; per Tastatur Fokus + Enter. Bestehende Zonen werden ersetzt.
   */
  private placeZones(): void {
    for (const wrap of this.wraps) wrap.querySelectorAll('.pv-zone').forEach(z => z.remove());
    for (const zone of this.zones) {
      const wrap = this.wraps[zone.page - 1];
      if (!wrap) continue;
      const el = document.createElement('button');
      el.type = 'button';
      el.className = 'pv-zone' + (zone.id === this.activeZone ? ' pv-zone--active' : '');
      el.style.left = `${zone.x * 100}%`;
      el.style.top = `${zone.y * 100}%`;
      el.style.width = `${zone.w * 100}%`;
      el.style.height = `${zone.h * 100}%`;
      if (zone.title) { el.title = zone.title; el.setAttribute('aria-label', zone.title); }
      el.addEventListener('dblclick', ev => { ev.preventDefault(); this.zone.run(() => this.zonePick.emit(zone.id)); });
      el.addEventListener('keydown', ev => { if (ev.key === 'Enter') this.zone.run(() => this.zonePick.emit(zone.id)); });
      wrap.appendChild(el);
    }
  }

  /** Zur Seite einer Zone scrollen, sodass das Diagramm sichtbar ist. */
  revealZone(z: PdfZone): void {
    const wrap = this.wraps[z.page - 1];
    if (!wrap) return;
    const top = wrap.offsetTop + z.y * wrap.offsetHeight - 24;
    this.scrollRef.nativeElement.scrollTo({ top: Math.max(0, top), behavior: 'smooth' });
  }

  private fitZoom(pageWidth: number): number {
    return fitWidthZoom(this.scrollRef.nativeElement.clientWidth, pageWidth, 16, this.wide() ? WIDE_FIT_MAX_WIDTH : Infinity);
  }

  /**
   * Zoom einer Seite: „Breite einpassen“ passt JEDE Seite für sich ein (0.792.1) — vorher galt der Faktor von Seite 1 für
   * alle, und Doppelseiten-Scans im Querformat (z. B. Gambit-Bücher ab S. 3) ragten rechts aus dem Leser. Ein von Hand
   * gewählter Zoom gilt für alle Seiten.
   */
  private scaleFor(slot: PageSlot): number {
    return this.zoom ?? this.fitZoom(slot.width);
  }

  private applyZoom(): void {
    const first = this.slots[0];
    if (!first) return;
    const z = this.zoom ?? this.fitZoom(first.width);
    this.scale = z;
    this.zoomApplied.emit(z);
    for (const slot of this.slots) {
      const canvas = this.canvases[slot.index - 1];
      const sz = this.scaleFor(slot);
      canvas.style.width = `${Math.round(slot.width * sz)}px`;
      canvas.style.height = `${Math.round(slot.height * sz)}px`;
      slot.rendered = 0;   // neu zeichnen, sobald sichtbar
    }
    this.renderVisible();
  }

  private onResize(): void {
    if (this.zoom == null && this.doc) this.zone.run(() => this.keepReadingPlace(() => this.applyZoom()));
  }

  /**
   * Führt eine Änderung der Seitengrößen aus (neuer Zoom, andere Leserbreite) und hält dabei die Lesestelle: dieselbe
   * Seite, gleich weit hineingescrollt. Ohne das blieb `scrollTop` in Pixeln stehen, und schmalere Seiten schoben das
   * Buch Seiten weiter (0.798.1).
   */
  private keepReadingPlace(change: () => void): void {
    const el = this.scrollRef.nativeElement;
    const before = this.layout();
    const anchor = readingAnchor(el.scrollTop, before.tops, before.heights);
    change();
    const after = this.layout();
    const top = scrollTopForAnchor(anchor, after.tops, after.heights);
    if (Math.abs(el.scrollTop - top) >= 1) el.scrollTop = top;
    this.onScroll();
  }

  onScroll(): void {
    cancelAnimationFrame(this.scrollRaf);
    this.scrollRaf = requestAnimationFrame(() => {
      this.renderVisible();
      const { tops, heights } = this.layout();
      const el = this.scrollRef.nativeElement;
      const page = currentPage(el.scrollTop, el.clientHeight, tops, heights);
      if (page !== this.current) {
        this.current = page;
        this.zone.run(() => this.pageChange.emit(page));
      }
    });
  }

  private layout(): { tops: number[]; heights: number[] } {
    const tops: number[] = [];
    const heights: number[] = [];
    for (const canvas of this.canvases) {
      const wrap = canvas.parentElement!;
      tops.push(wrap.offsetTop);
      heights.push(wrap.offsetHeight);
    }
    return { tops, heights };
  }

  private renderVisible(): void {
    if (!this.doc) return;
    const el = this.scrollRef.nativeElement;
    const { tops, heights } = this.layout();
    const wanted = pagesToRender(el.scrollTop, el.clientHeight, tops, heights, 1);
    for (const slot of this.slots) {
      const canvas = this.canvases[slot.index - 1];
      if (wanted.has(slot.index)) {
        if (slot.rendered !== this.scaleFor(slot) && !slot.rendering) void this.renderPage(slot, canvas);
      } else if (slot.rendered !== 0 && !slot.rendering) {
        canvas.width = 0;   // Speicher frei — die CSS-Größe bleibt, das Layout springt nicht
        canvas.height = 0;
        slot.rendered = 0;
      }
    }
  }

  private async renderPage(slot: PageSlot, canvas: HTMLCanvasElement): Promise<void> {
    const doc = this.doc;
    if (!doc) return;
    let scaleAtStart = this.scaleFor(slot);
    slot.rendering = (async () => {
      let page = this.pageCache.get(slot.index);
      if (!page) {
        page = await doc.getPage(slot.index);
        this.pageCache.set(slot.index, page);
        if (this.pageCache.size > 24) {
          const oldest = this.pageCache.keys().next().value;
          if (oldest != null && oldest !== slot.index) this.pageCache.delete(oldest);
        }
      }
      if (this.destroyed || doc !== this.doc) return;
      const base = page.getViewport({ scale: 1 });
      if (Math.abs(base.width - slot.width) > 0.5 || Math.abs(base.height - slot.height) > 0.5) {
        // Seiten mit anderem Format als Seite 1 (Querformat-Tafel): Größe korrigieren.
        slot.width = base.width;
        slot.height = base.height;
        scaleAtStart = this.scaleFor(slot);
        canvas.style.width = `${Math.round(slot.width * scaleAtStart)}px`;
        canvas.style.height = `${Math.round(slot.height * scaleAtStart)}px`;
      }
      const dpr = Math.min(3, window.devicePixelRatio || 1);
      const viewport = page.getViewport({ scale: scaleAtStart * dpr });
      canvas.width = Math.floor(viewport.width);
      canvas.height = Math.floor(viewport.height);
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
      await page.render({ canvasContext: ctx, viewport, canvas } as unknown as Parameters<PdfPage['render']>[0]).promise;
      slot.rendered = scaleAtStart;
    })().catch(() => { slot.rendered = 0; }).finally(() => { slot.rendering = null; });
    await slot.rendering;
    if (!this.destroyed && this.scaleFor(slot) !== scaleAtStart) this.renderVisible();
  }
}
