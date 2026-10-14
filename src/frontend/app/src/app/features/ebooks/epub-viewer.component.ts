import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, HostListener, Input, NgZone, OnChanges,
  OnDestroy, Output, SimpleChanges, ViewChild, inject, signal,
} from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';

/** Lesestelle im EPUB: Anteil (0–1) und CFI (genaue Stelle, wird gespeichert). */
export interface EpubLocation { fraction: number; cfi: string; }
export interface EpubTocEntry { label: string; href: string; }
/** Ein erkanntes Diagrammbild (0.775.0): Kapitel (Spine, 1-basiert) und Bildpfad im EPUB. */
export interface EpubDiagramImage { id: number; chapter: number; ref: string; title?: string; }

/** Minimal-Schnittstelle von epub.js, die hier gebraucht wird (das Paket bringt eigene, sehr breite Typen mit). */
interface EpubBook {
  ready: Promise<unknown>;
  loaded: { navigation: Promise<{ toc: { label: string; href: string }[] }> };
  locations: { generate(chars: number): Promise<unknown>; percentageFromCfi(cfi: string): number; length(): number };
  renderTo(el: HTMLElement, opts: Record<string, unknown>): EpubRendition;
  spine: { get(index: number): { href: string } | null };
  /** Im Archiv-Modus ersetzt epub.js Bildadressen durch blob:-URLs — parallel dazu die Pfade im Buch. */
  resources?: { urls?: string[]; replacementUrls?: string[] };
  destroy(): void;
}
interface EpubRendition {
  display(target?: string): Promise<unknown>;
  next(): Promise<unknown>;
  prev(): Promise<unknown>;
  on(event: string, cb: (...args: unknown[]) => void): void;
  themes: { default(rules: Record<string, Record<string, string>>): void };
  hooks: { content: { register(fn: (c: EpubContents) => void): void } };
  getContents(): EpubContents[] | EpubContents;
  destroy(): void;
}

interface EpubContents { document: Document; cfiFromNode(node: Node): string; }

/**
 * EPUB-Leser (0.770.0) auf epub.js: Seitenweise (paginiert), ‹ ›, Pfeiltasten und Wischen; Lesestelle als CFI. Die Datei
 * wird als ArrayBuffer geholt (die Datei-Adresse trägt keine Endung, epub.js würde sie sonst als Ordner behandeln).
 * epub.js kommt per dynamischem Import — eigener Lazy-Chunk, nur wenn ein EPUB geöffnet wird. Skripte im Buch bleiben aus
 * (epub.js-Vorgabe), gerendert wird in ein about:blank-iframe, das die CSP der Seite erbt.
 * Erkannte Diagrammbilder (0.775.0, `images`) bekommen im Kapitel eine Markierung; Doppelklick (oder Enter) meldet
 * `imagePick`. Zuordnung über den Bildpfad im Buch — epub.js tauscht die Adressen gegen blob:-URLs, daher die Rückübersetzung
 * über `book.resources`, zur Not über den Dateinamen.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-epub-viewer',
  standalone: true,
  imports: [MatProgressBarModule, TranslatePipe],
  template: `
    @if (loading()) { <mat-progress-bar mode="indeterminate" class="ev-bar"></mat-progress-bar> }
    @if (error()) { <p class="ev-error" role="alert">{{ 'ebooks.reader.loadFailed' | translate }}</p> }
    <div class="ev-area" #area [attr.aria-label]="'ebooks.reader.area' | translate"></div>
  `,
  styles: [`
    :host { display: block; position: relative; height: 100%; min-height: 0; background: #fff; }
    .ev-bar { position: absolute; top: 0; left: 0; right: 0; z-index: 1; }
    .ev-area { height: 100%; width: 100%; }
    .ev-error { text-align: center; padding: 2rem 1rem; color: #b3261e; }
  `],
})
export class EpubViewerComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input({ required: true }) src = '';
  @Input() initialLocator: string | null = null;
  @Output() located = new EventEmitter<EpubLocation>();
  @Output() toc = new EventEmitter<EpubTocEntry[]>();
  @Output() failed = new EventEmitter<unknown>();
  @Input() images: readonly EpubDiagramImage[] = [];
  @Input() activeImage: number | null = null;
  @Output() imagePick = new EventEmitter<number>();
  /** Aktuelles Kapitel (Spine, 1-basiert). */
  readonly chapter = signal(1);

  @ViewChild('area', { static: true }) private area!: ElementRef<HTMLDivElement>;
  readonly loading = signal(true);
  readonly error = signal(false);

  private zone = inject(NgZone);
  private book: EpubBook | null = null;
  private rendition: EpubRendition | null = null;
  private destroyed = false;
  private touchX: number | null = null;

  async ngAfterViewInit(): Promise<void> {
    try {
      const [mod, res] = await Promise.all([import('epubjs'), fetch(this.src, { credentials: 'same-origin' })]);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.arrayBuffer();
      if (this.destroyed) return;
      const ePub = (mod as unknown as { default: (d: ArrayBuffer) => EpubBook }).default;
      const book = ePub(data);
      this.book = book;
      const rendition = book.renderTo(this.area.nativeElement, { width: '100%', height: '100%', flow: 'paginated', spread: 'auto' });
      this.rendition = rendition;
      rendition.themes.default({ body: { 'line-height': '1.5' }, img: { 'max-width': '100%' } });
      rendition.hooks.content.register(c => this.markImages(c));
      rendition.on('relocated', (loc: unknown) => {
        const l = loc as { start?: { cfi?: string; percentage?: number; index?: number } };
        if (typeof l.start?.index === 'number') this.chapter.set(l.start.index + 1);
        const cfi = l.start?.cfi;
        if (!cfi) return;
        let fraction = l.start?.percentage ?? 0;
        if (book.locations.length() > 0) fraction = book.locations.percentageFromCfi(cfi);
        this.zone.run(() => this.located.emit({ fraction, cfi }));
      });
      rendition.on('keyup', (e: unknown) => this.onKey(e as KeyboardEvent));
      await rendition.display(this.initialLocator || undefined).catch(() => rendition.display());
      this.zone.run(() => this.loading.set(false));
      book.loaded.navigation.then(nav => this.zone.run(() => this.toc.emit(nav.toc.map(t => ({ label: t.label.trim(), href: t.href })))))
        .catch(() => {});
      // Anteile genau erst nach dem Vermessen (im Hintergrund, Sekunden bei großen Büchern).
      void book.locations.generate(1600).catch(() => {});
    } catch (e) {
      if (this.destroyed) return;
      this.zone.run(() => { this.loading.set(false); this.error.set(true); this.failed.emit(e); });
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['images'] && !changes['images'].firstChange) || changes['activeImage']) this.contents().forEach(c => this.markImages(c));
  }

  /** Zum Kapitel springen und das Diagrammbild in den Blick holen. */
  async revealImage(chapter: number, ref: string): Promise<void> {
    const r = this.rendition, item = this.book?.spine.get(chapter - 1);
    if (!r || !item) return;
    await r.display(item.href).catch(() => {});
    for (const c of this.contents()) {
      const el = Array.from(c.document.querySelectorAll<Element>('[data-rh-ref]')).find(e => e.getAttribute('data-rh-ref') === ref);
      if (el) { await r.display(c.cfiFromNode(el)).catch(() => {}); return; }
    }
  }

  private contents(): EpubContents[] {
    const c = this.rendition?.getContents();
    return !c ? [] : Array.isArray(c) ? c : [c];
  }

  private markImages(c: EpubContents): void {
    const doc = c.document;
    if (!doc || this.images.length === 0) return;
    const byRef = new Map(this.images.map(i => [i.ref, i]));
    const byName = new Map<string, EpubDiagramImage | null>();
    for (const i of this.images) {
      const n = baseName(i.ref);
      byName.set(n, byName.has(n) ? null : i);   // doppelte Dateinamen: nicht raten
    }
    const res = this.book?.resources;
    const blobs = new Map<string, string>();
    (res?.replacementUrls ?? []).forEach((u, k) => { const path = res?.urls?.[k]; if (u && path) blobs.set(u, path); });
    if (!doc.getElementById('rh-diagram-style')) {
      const st = doc.createElement('style');
      st.id = 'rh-diagram-style';
      st.textContent = '.rh-diagram{cursor:zoom-in;outline:2px dashed transparent;outline-offset:2px}'
        + '.rh-diagram:hover,.rh-diagram:focus{outline-color:#6750a4}.rh-diagram--active{outline:3px solid #6750a4}';
      doc.head?.appendChild(st);
    }
    doc.querySelectorAll<Element>('img, image').forEach(el => {
      const raw = el.getAttribute('src') ?? el.getAttribute('xlink:href') ?? el.getAttribute('href') ?? '';
      const path = normalizePath(blobs.get(raw) ?? raw);
      const img = byRef.get(path) ?? byName.get(baseName(path)) ?? null;
      if (!img) return;
      el.classList.add('rh-diagram');
      el.classList.toggle('rh-diagram--active', img.id === this.activeImage);
      if (el.getAttribute('data-rh-ref') !== null) return;
      el.setAttribute('data-rh-ref', img.ref);
      el.setAttribute('tabindex', '0');
      if (img.title) el.setAttribute('title', img.title);
      el.addEventListener('dblclick', ev => { ev.preventDefault(); this.zone.run(() => this.imagePick.emit(img.id)); });
      el.addEventListener('keydown', ev => { if ((ev as KeyboardEvent).key === 'Enter') this.zone.run(() => this.imagePick.emit(img.id)); });
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    try { this.rendition?.destroy(); this.book?.destroy(); } catch { /* schon weg */ }
  }

  next(): void { void this.rendition?.next(); }
  prev(): void { void this.rendition?.prev(); }
  goTo(href: string): void { void this.rendition?.display(href); }

  @HostListener('keyup', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (e.key === 'ArrowRight' || e.key === 'PageDown') this.next();
    else if (e.key === 'ArrowLeft' || e.key === 'PageUp') this.prev();
  }

  @HostListener('touchstart', ['$event'])
  onTouchStart(e: TouchEvent): void { this.touchX = e.changedTouches[0]?.clientX ?? null; }

  @HostListener('touchend', ['$event'])
  onTouchEnd(e: TouchEvent): void {
    const x = e.changedTouches[0]?.clientX;
    if (this.touchX == null || x == null) return;
    const dx = x - this.touchX;
    this.touchX = null;
    if (Math.abs(dx) < 50) return;
    if (dx < 0) this.next(); else this.prev();
  }
}

function normalizePath(p: string): string {
  let v = p.split('#')[0].split('?')[0];
  try { v = decodeURIComponent(v); } catch { /* bleibt roh */ }
  return v.replace(/^\/+/, '');
}

function baseName(p: string): string {
  const v = normalizePath(p);
  return v.slice(v.lastIndexOf('/') + 1);
}
