import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, HostListener, Input, NgZone, OnDestroy,
  Output, ViewChild, inject, signal,
} from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';

/** Lesestelle im EPUB: Anteil (0–1) und CFI (genaue Stelle, wird gespeichert). */
export interface EpubLocation { fraction: number; cfi: string; }
export interface EpubTocEntry { label: string; href: string; }

/** Minimal-Schnittstelle von epub.js, die hier gebraucht wird (das Paket bringt eigene, sehr breite Typen mit). */
interface EpubBook {
  ready: Promise<unknown>;
  loaded: { navigation: Promise<{ toc: { label: string; href: string }[] }> };
  locations: { generate(chars: number): Promise<unknown>; percentageFromCfi(cfi: string): number; length(): number };
  renderTo(el: HTMLElement, opts: Record<string, unknown>): EpubRendition;
  destroy(): void;
}
interface EpubRendition {
  display(target?: string): Promise<unknown>;
  next(): Promise<unknown>;
  prev(): Promise<unknown>;
  on(event: string, cb: (...args: unknown[]) => void): void;
  themes: { default(rules: Record<string, Record<string, string>>): void };
  destroy(): void;
}

/**
 * EPUB-Leser (0.770.0) auf epub.js: Seitenweise (paginiert), ‹ ›, Pfeiltasten und Wischen; Lesestelle als CFI. Die Datei
 * wird als ArrayBuffer geholt (die Datei-Adresse trägt keine Endung, epub.js würde sie sonst als Ordner behandeln).
 * epub.js kommt per dynamischem Import — eigener Lazy-Chunk, nur wenn ein EPUB geöffnet wird. Skripte im Buch bleiben aus
 * (epub.js-Vorgabe), gerendert wird in ein about:blank-iframe, das die CSP der Seite erbt.
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
export class EpubViewerComponent implements AfterViewInit, OnDestroy {
  @Input({ required: true }) src = '';
  @Input() initialLocator: string | null = null;
  @Output() located = new EventEmitter<EpubLocation>();
  @Output() toc = new EventEmitter<EpubTocEntry[]>();
  @Output() failed = new EventEmitter<unknown>();

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
      rendition.on('relocated', (loc: unknown) => {
        const l = loc as { start?: { cfi?: string; percentage?: number } };
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
