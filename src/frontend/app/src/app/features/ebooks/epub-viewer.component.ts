import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, HostListener, Input, NgZone, OnChanges,
  OnDestroy, Output, SimpleChanges, ViewChild, inject, signal,
} from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import type { EpubMissedSpot } from './ebook.service';

/** Lesestelle im EPUB: Anteil (0–1) und CFI (genaue Stelle, wird gespeichert). */
export interface EpubLocation { fraction: number; cfi: string; }
export interface EpubTocEntry { label: string; href: string; }
/** Ein erkanntes Diagrammbild (0.775.0): Kapitel (Spine, 1-basiert) und Bildpfad im EPUB. */
export interface EpubDiagramImage { id: number; chapter: number; ref: string; title?: string; }

/** Minimal-Schnittstelle von epub.js, die hier gebraucht wird (das Paket bringt eigene, sehr breite Typen mit). */
interface EpubBook {
  ready: Promise<unknown>;
  open(input: string, what?: string): Promise<unknown>;
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
  themes: { default(rules: Record<string, Record<string, string>>): void; fontSize(size: string): void };
  currentLocation(): { start?: { cfi?: string } } | null;
  resize(width?: number, height?: number): void;
  hooks: { content: { register(fn: (c: EpubContents) => void): void } };
  getContents(): EpubContents[] | EpubContents;
  destroy(): void;
}

interface EpubContents { document: Document; cfiFromNode(node: Node): string; sectionIndex?: number; }

/** `ePub(data)` (Archiv) bzw. `ePub(options)` (leeres Buch, danach `open`). */
/** Darstellung des EPUB-Lesers. */
export type EpubLayout = 'pages' | 'scroll';

type EpubFactory = (input: ArrayBuffer | Record<string, unknown>) => EpubBook;

/** Wie lange das Öffnen als Ordner (container.xml + OPF) höchstens dauern darf, bevor das ganze Archiv geladen wird. */
const DIRECTORY_OPEN_TIMEOUT_MS = 20_000;
/** Das Vermessen (`locations.generate`) lädt JEDES Kapitel einmal — erst eine Weile nach der ersten Seite. */
const LOCATIONS_DELAY_MS = 4_000;

/**
 * EPUB-Leser (0.770.0) auf epub.js: Seitenweise (paginiert), ‹ ›, Pfeiltasten und Wischen; Lesestelle als CFI.
 * Öffnen: mit `dirSrc` (Basis `…/api/ebooks/{id}/epub/{token}/`) „als Ordner“ — epub.js holt nur container.xml, OPF,
 * Navigation, das gezeigte Kapitel und dessen Bilder einzeln vom Server statt des ganzen Archivs (Dvoretsky: 84 MB vor der
 * ersten Seite). Schlägt das fehl, wie früher die ganze Datei (`src`) als ArrayBuffer.
 * epub.js kommt per dynamischem Import — eigener Lazy-Chunk, nur wenn ein EPUB geöffnet wird. Skripte im Buch bleiben aus
 * (epub.js-Vorgabe), gerendert wird in ein about:blank-iframe, das die CSP der Seite erbt.
 * Erkannte Diagrammbilder (0.775.0, `images`) bekommen im Kapitel eine Markierung; Doppelklick (oder Enter) meldet
 * `imagePick`. Zuordnung über den Bildpfad im Buch (`epubImagePath`): im Archiv tauscht epub.js die Adressen gegen
 * blob:-URLs (Rückübersetzung über `book.resources`), als Ordner bleiben sie relativ zum Kapitel und werden gegen die
 * Basis aufgelöst; zur Not über den Dateinamen.
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
  /** Basis-Adresse des Buchs „als Ordner“ (mit Schrägstrich am Ende); leer = ganzes Archiv aus `src`. */
  @Input() dirSrc: string | null = null;
  @Input() initialLocator: string | null = null;
  @Output() located = new EventEmitter<EpubLocation>();
  @Output() toc = new EventEmitter<EpubTocEntry[]>();
  @Output() failed = new EventEmitter<unknown>();
  @Input() images: readonly EpubDiagramImage[] = [];
  @Input() activeImage: number | null = null;
  @Output() imagePick = new EventEmitter<number>();
  /** Doppelklick auf ein Bild OHNE erkanntes Diagramm oder auf Text (0.798.0): der Leser fragt „Ist hier ein Diagramm …?“. */
  @Output() missedPick = new EventEmitter<EpubMissedSpot>();
  /** Darstellung (0.791.0): `pages` = eine Spalte, geblättert; `scroll` = fließend, das Buch als durchgehender Text. */
  @Input() layout: EpubLayout = 'pages';
  /** Schriftgröße in Prozent (100 = Vorgabe des Buchs). */
  @Input() fontScale = 100;
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
  /** Absolute Basis, wenn das Buch als Ordner offen ist (für die Bildzuordnung). */
  private dirBase: string | null = null;
  private locationsTimer: ReturnType<typeof setTimeout> | null = null;
  /** Bereich wird schmaler/breiter (Brett-Spalte gezogen, Fenster, Vollbild): epub.js misst nur beim Aufbau — ohne
   *  Nachmessen läuft der Text über den Rand (0.792.2). Gebündelt per Animationsframe. */
  private resizeObs: ResizeObserver | null = null;
  private resizeRaf = 0;
  private lastSize = '';

  async ngAfterViewInit(): Promise<void> {
    try {
      const mod = await import('epubjs');
      if (this.destroyed) return;
      const book = await this.openBook((mod as unknown as { default: EpubFactory }).default);
      if (!book) return;
      this.book = book;
      const rendition = this.createRendition(book);
      if (typeof ResizeObserver !== 'undefined') {
        this.resizeObs = new ResizeObserver(() => this.onAreaResize());
        this.resizeObs.observe(this.area.nativeElement);
      }
      await rendition.display(this.initialLocator || undefined).catch(() => rendition.display());
      this.zone.run(() => this.loading.set(false));
      book.loaded.navigation.then(nav => this.zone.run(() => this.toc.emit(nav.toc.map(t => ({ label: t.label.trim(), href: t.href })))))
        .catch(() => {});
      // Anteile genau erst nach dem Vermessen (im Hintergrund, Sekunden bei großen Büchern). Es lädt jedes Kapitel einmal
      // (als Ordner: je Kapitel eine Anfrage, ohne Bilder) — darum erst nach der ersten Seite und mit Abstand.
      this.locationsTimer = setTimeout(() => {
        this.locationsTimer = null;
        if (!this.destroyed) void book.locations.generate(1600).catch(() => {});
      }, LOCATIONS_DELAY_MS);
    } catch (e) {
      if (this.destroyed) return;
      this.zone.run(() => { this.loading.set(false); this.error.set(true); this.failed.emit(e); });
    }
  }

  /**
   * Buch öffnen: zuerst als Ordner (`dirSrc`), sonst/bei Fehler das ganze Archiv aus `src`. `null` = Komponente schon weg.
   * Als Ordner ohne Ersetzungen (`replacements: 'none'`): Bilder, CSS und Schriften lädt das Kapitel-iframe selbst über
   * die `<base>` des Kapitels — gleiche Herkunft, die CSP braucht dafür nichts Neues.
   */
  async openBook(ePub: EpubFactory): Promise<EpubBook | null> {
    if (this.dirSrc) {
      const base = new URL(this.dirSrc, document.baseURI).href;
      const book = ePub({ replacements: 'none' });
      try {
        await withTimeout(book.open(base, 'directory'), DIRECTORY_OPEN_TIMEOUT_MS);
        if (this.destroyed) { book.destroy(); return null; }
        this.dirBase = base;
        return book;
      } catch {
        try { book.destroy(); } catch { /* halb offen */ }
        if (this.destroyed) return null;
      }
    }
    const res = await fetch(this.src, { credentials: 'same-origin' });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.arrayBuffer();
    if (this.destroyed) return null;
    this.dirBase = null;
    return ePub(data);
  }

  /**
   * Darstellung aufbauen: „Seite“ = paginiert, EINE Spalte (vorher `spread: auto` — ab 800 px immer zwei Spalten,
   * Nutzerwunsch 15.10.); „Fließend“ = alle Kapitel untereinander zum Scrollen (continuous manager).
   */
  private createRendition(book: EpubBook): EpubRendition {
    const opts = this.layout === 'scroll'
      ? { width: '100%', height: '100%', flow: 'scrolled', manager: 'continuous', spread: 'none' }
      : { width: '100%', height: '100%', flow: 'paginated', spread: 'none' };
    const rendition = book.renderTo(this.area.nativeElement, opts);
    this.rendition = rendition;
    rendition.themes.default({ body: { 'line-height': '1.5' }, img: { 'max-width': '100%' } });
    rendition.themes.fontSize(`${this.fontScale}%`);
    rendition.hooks.content.register(c => this.markImages(c));
    rendition.hooks.content.register(c => this.listenMissed(c));
    // Mausrad im Kapitel (iframe): Ereignisse von dort erreichen den Host nicht (0.780.1).
    rendition.hooks.content.register((c: { document?: Document }) => c.document?.addEventListener('wheel',
      (ev: WheelEvent) => this.zone.run(() => this.onWheel(ev)), { passive: true }));
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
    return rendition;
  }

  /** Darstellung wechseln: an derselben Stelle neu aufbauen. */
  private async relayout(): Promise<void> {
    const book = this.book, old = this.rendition;
    if (!book || !old) return;
    let cfi: string | undefined;
    try { cfi = old.currentLocation()?.start?.cfi; } catch { /* egal */ }
    try { old.destroy(); } catch { /* schon weg */ }
    this.area.nativeElement.replaceChildren();
    const r = this.createRendition(book);
    await r.display(cfi).catch(() => r.display());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['layout'] && !changes['layout'].firstChange) void this.relayout();
    if (changes['fontScale'] && !changes['fontScale'].firstChange) this.rendition?.themes.fontSize(`${this.fontScale}%`);
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

  /**
   * Doppelklick im Kapitel, der KEIN erkanntes Diagramm trifft (die haben eigene Handler mit `data-rh-ref`): Bild → dessen
   * Pfad im Buch, sonst Text (`ref` = null); dazu das Kapitel (Spine-href). Ein Handler je Kapiteldokument.
   */
  private listenMissed(c: EpubContents): void {
    const doc = c.document;
    if (!doc || (doc as Document & { __rhMissed?: boolean }).__rhMissed) return;
    (doc as Document & { __rhMissed?: boolean }).__rhMissed = true;
    doc.addEventListener('dblclick', ev => {
      const target = ev.target as Element | null;
      if (!target || typeof target.closest !== 'function' || target.closest('[data-rh-ref]')) return;
      const imgEl = target.closest('img, image');
      let ref: string | null = null;
      if (imgEl) {
        const res = this.book?.resources;
        const blobs = new Map<string, string>();
        (res?.replacementUrls ?? []).forEach((u, k) => { const path = res?.urls?.[k]; if (u && path) blobs.set(u, path); });
        const raw = imgEl.getAttribute('src') ?? imgEl.getAttribute('xlink:href') ?? imgEl.getAttribute('href') ?? '';
        ref = epubImagePath(raw, doc.baseURI, this.dirBase, blobs) || null;
      }
      const index = typeof c.sectionIndex === 'number' ? c.sectionIndex : this.chapter() - 1;
      const chapter = this.book?.spine.get(index)?.href ?? null;
      if (!ref && !chapter) return;
      this.zone.run(() => this.missedPick.emit({ ref, chapter }));
    });
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
        + '.rh-diagram:hover,.rh-diagram:focus{outline-color:#6750a4}.rh-diagram--active{outline:3px solid #6750a4}'
        // Touch: kein Überfahren — Diagramme dauerhaft gestrichelt (c1-eb-reader-mobile-diagrams-undiscoverable).
        + '@media (hover:none){.rh-diagram{outline-color:#6750a4}}';
      doc.head?.appendChild(st);
    }
    doc.querySelectorAll<Element>('img, image').forEach(el => {
      const raw = el.getAttribute('src') ?? el.getAttribute('xlink:href') ?? el.getAttribute('href') ?? '';
      const path = epubImagePath(raw, doc.baseURI, this.dirBase, blobs);
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

  /** Neue Größe an epub.js weitergeben (nur bei echter Änderung, höchstens einmal je Frame). */
  onAreaResize(): void {
    cancelAnimationFrame(this.resizeRaf);
    this.resizeRaf = requestAnimationFrame(() => {
      const el = this.area.nativeElement;
      const w = Math.floor(el.clientWidth), h = Math.floor(el.clientHeight);
      const key = `${w}x${h}`;
      if (!w || !h || key === this.lastSize) return;
      const first = this.lastSize === '';
      this.lastSize = key;
      if (first) return;   // erste Messung = Aufbau, epub.js hat schon die richtige Größe
      try { this.rendition?.resize(w, h); } catch { /* Darstellung gerade im Umbau */ }
    });
  }

  ngOnDestroy(): void {
    this.resizeObs?.disconnect();
    cancelAnimationFrame(this.resizeRaf);
    this.destroyed = true;
    if (this.locationsTimer) clearTimeout(this.locationsTimer);
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

  /** Gesammelte Raddrehung seit dem letzten Umblättern und dessen Zeitpunkt (Touchpads liefern viele kleine Schritte). */
  private wheelAcc = 0;
  private lastWheelTurn = 0;
  static readonly WHEEL_STEP = 40;   // eine Raste: Chrome 100 px, Firefox 3 Zeilen ≈ 48 px
  static readonly WHEEL_PAUSE_MS = 350;

  /** Mausrad blättert seitenweise (0.780.1): das Buch ist paginiert, Scrollen gäbe es sonst nicht. Runter/rechts = weiter. */
  @HostListener('wheel', ['$event'])
  onWheel(e: WheelEvent): void {
    if (this.layout === 'scroll') return;   // fließend: das Rad scrollt selbst
    const d = Math.abs(e.deltaY) >= Math.abs(e.deltaX) ? e.deltaY : e.deltaX;
    if (!d) return;
    const now = Date.now();
    if (now - this.lastWheelTurn < EpubViewerComponent.WHEEL_PAUSE_MS) { this.wheelAcc = 0; return; }
    // Zeilen-/Seitenmodus (Firefox) in Pixel umrechnen.
    const px = e.deltaMode === 1 ? d * 16 : e.deltaMode === 2 ? d * 400 : d;
    if (Math.sign(px) !== Math.sign(this.wheelAcc)) this.wheelAcc = 0;
    this.wheelAcc += px;
    if (Math.abs(this.wheelAcc) < EpubViewerComponent.WHEEL_STEP) return;
    if (this.wheelAcc > 0) this.next(); else this.prev();
    this.wheelAcc = 0;
    this.lastWheelTurn = now;
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

/**
 * Bildadresse im Kapitel → Pfad im EPUB-ZIP (wie `EbookDiagram.ImageRef`). Archiv: blob:-URL über die Ersetzungs-Tabelle
 * von epub.js. Ordner: Adresse gegen die Basis des Kapitels (`<base>`) auflösen und die Buch-Basis
 * (`…/epub/{token}/`) abschneiden. Sonst der rohe Pfad.
 */
export function epubImagePath(raw: string, baseUri: string | null, bookBase: string | null, blobs: ReadonlyMap<string, string>): string {
  const blob = blobs.get(raw);
  if (blob) return normalizePath(blob);
  if (bookBase && baseUri && raw) {
    try {
      const abs = new URL(raw, baseUri);
      abs.hash = '';
      abs.search = '';
      if (abs.href.startsWith(bookBase)) return normalizePath(abs.href.slice(bookBase.length));
    } catch { /* keine gültige Adresse */ }
  }
  return normalizePath(raw);
}

function withTimeout<T>(p: Promise<T>, ms: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const t = setTimeout(() => reject(new Error('timeout')), ms);
    p.then(v => { clearTimeout(t); resolve(v); }, e => { clearTimeout(t); reject(e); });
  });
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
