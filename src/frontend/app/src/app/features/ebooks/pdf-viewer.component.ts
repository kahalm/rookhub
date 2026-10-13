import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, NgZone, OnChanges, OnDestroy, Output,
  SimpleChanges, ViewChild, inject, signal,
} from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { PdfDocument, PdfEngineLoader, PdfLoadingTask, PdfPage } from './pdf-engine';
import { currentPage, fitWidthZoom, pagesToRender } from './reader-progress.util';

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

  @ViewChild('scroll', { static: true }) private scrollRef!: ElementRef<HTMLDivElement>;
  @ViewChild('pages', { static: true }) private pagesRef!: ElementRef<HTMLDivElement>;

  readonly loading = signal(false);
  readonly error = signal(false);

  private engine = inject(PdfEngineLoader);
  private zone = inject(NgZone);
  private doc: PdfDocument | null = null;
  /** Der Ladevorgang — nur er kann Dokument + Worker-Verbindung wieder freigeben (`destroy`). */
  private task: PdfLoadingTask | null = null;
  private slots: PageSlot[] = [];
  private canvases: HTMLCanvasElement[] = [];
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
    if (changes['zoom'] && !changes['zoom'].firstChange && this.doc) this.applyZoom();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.resizeObserver?.disconnect();
    cancelAnimationFrame(this.scrollRaf);
    void this.task?.destroy();
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
      canvas.setAttribute('role', 'img');
      canvas.setAttribute('aria-label', `${slot.index}`);
      wrap.appendChild(canvas);
      frag.appendChild(wrap);
      this.canvases.push(canvas);
    }
    this.pagesRef.nativeElement.appendChild(frag);
  }

  private fitZoom(pageWidth: number): number {
    return fitWidthZoom(this.scrollRef.nativeElement.clientWidth, pageWidth);
  }

  private applyZoom(): void {
    const first = this.slots[0];
    if (!first) return;
    const z = this.zoom ?? this.fitZoom(first.width);
    this.scale = z;
    this.zoomApplied.emit(z);
    for (const slot of this.slots) {
      const canvas = this.canvases[slot.index - 1];
      canvas.style.width = `${Math.round(slot.width * z)}px`;
      canvas.style.height = `${Math.round(slot.height * z)}px`;
      slot.rendered = 0;   // neu zeichnen, sobald sichtbar
    }
    this.renderVisible();
  }

  private onResize(): void {
    if (this.zoom == null && this.doc) this.zone.run(() => this.applyZoom());
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
        if (slot.rendered !== this.scale && !slot.rendering) void this.renderPage(slot, canvas);
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
    const scaleAtStart = this.scale;
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
    if (!this.destroyed && this.scale !== scaleAtStart) this.renderVisible();
  }
}
