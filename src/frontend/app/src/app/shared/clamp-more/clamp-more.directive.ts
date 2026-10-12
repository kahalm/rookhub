import { AfterViewInit, Directive, ElementRef, Input, NgZone, OnChanges, OnDestroy, inject } from '@angular/core';

/**
 * Zugeklappter Fließtext mit „Mehr" im Satz (DESIGN 4.6 Nr. 5, 5.11): der Text wird GEMESSEN auf höchstens
 * `clampLines` Zeilen gekürzt — am Wortende, mit „…" —, und der „Mehr"-Knopf steht direkt dahinter in der letzten
 * Zeile. Eine feste Zeichenzahl passte nur für eine Breite (Kursseite am Handy: 180 Zeichen = 5 Zeilen); CSS
 * `line-clamp` schneidet den Knopf am Satzende mit ab.
 *
 * Aufbau im Host (die Direktive schreibt NUR den Text des `.clamp-text`-Kinds und blendet `.clamp-more` aus, wenn
 * der ganze Text samt Knopf schon passt; Beschriftung, `aria-expanded` und Klick bleiben im Template):
 *
 *     <p class="description" id="x" [appClampMore]="text" [clampExpanded]="open">
 *       <span class="clamp-text"></span>
 *       <button type="button" class="clamp-more" aria-controls="x" [attr.aria-expanded]="open"
 *               (click)="open = !open">{{ (open ? 'common.less' : 'common.more') | translate }}</button>
 *     </p>
 *
 * Gemessen wird bei jeder Breitenänderung (ResizeObserver); solange der Host keine Breite hat (nicht im Dokument,
 * verborgen), steht der ganze Text da und der Knopf ist aus.
 */
@Directive({
  selector: '[appClampMore]',
  standalone: true,
})
export class ClampMoreDirective implements AfterViewInit, OnChanges, OnDestroy {
  /** Der volle Text. */
  @Input({ required: true }) appClampMore: string | null | undefined = '';
  /** Höchstzahl sichtbarer Zeilen im zugeklappten Zustand. */
  @Input() clampLines = 3;
  /** Aufgeklappt: ganzer Text, Knopf bleibt sichtbar („Weniger"). */
  @Input() clampExpanded = false;

  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly zone = inject(NgZone);
  private observer?: ResizeObserver;
  private viewReady = false;
  private lastWidth = -1;
  /** Ob der Text zuletzt gekürzt werden musste (für Tests und Aufrufer). */
  clamped = false;

  ngAfterViewInit(): void {
    this.viewReady = true;
    this.apply();
    if (typeof ResizeObserver !== 'undefined') {
      this.zone.runOutsideAngular(() => {
        this.observer = new ResizeObserver(() => {
          const w = this.host.clientWidth;
          if (w !== this.lastWidth) this.apply();
        });
        this.observer.observe(this.host);
      });
    }
  }

  ngOnChanges(): void {
    if (this.viewReady) this.apply();
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }

  /** Text neu setzen und — zugeklappt — auf `clampLines` Zeilen kürzen. */
  apply(): void {
    const textEl = this.host.querySelector<HTMLElement>('.clamp-text');
    if (!textEl) return;
    const more = this.host.querySelector<HTMLElement>('.clamp-more');
    const full = (this.appClampMore ?? '').trim();
    this.lastWidth = this.host.clientWidth;
    textEl.textContent = full;

    if (this.clampExpanded) {
      if (more) more.hidden = false;
      return;
    }
    if (this.lastWidth <= 0) {
      // Ohne Breite lässt sich nichts messen: ganz zeigen, Knopf aus — gemessen wird, sobald Platz da ist.
      this.clamped = false;
      if (more) more.hidden = true;
      return;
    }
    // Passt der ganze Text samt Knopf, braucht es keinen Knopf.
    if (more) more.hidden = false;
    if (this.fits()) {
      this.clamped = false;
      if (more) more.hidden = true;
      return;
    }
    this.clamped = true;
    // Längstes Wortende-Präfix, das samt „ …" und Knopf in die Zeilen passt (binäre Suche über die Wortgrenzen).
    const cuts: number[] = [];
    for (let i = 1; i < full.length; i++) if (/\s/.test(full[i]) && !/\s/.test(full[i - 1])) cuts.push(i);
    const prefix = (end: number) => full.slice(0, end).replace(/[\s,;:.–-]+$/, '') + ' …';
    let lo = 0;
    let hi = cuts.length - 1;
    let best = -1;
    while (lo <= hi) {
      const mid = (lo + hi) >> 1;
      textEl.textContent = prefix(cuts[mid]);
      if (this.fits()) { best = mid; lo = mid + 1; } else { hi = mid - 1; }
    }
    textEl.textContent = best >= 0 ? prefix(cuts[best]) : '…';
  }

  /** Höhe des Hosts (ohne Polster/Rahmen) ≤ `clampLines` Zeilen? */
  private fits(): boolean {
    const cs = getComputedStyle(this.host);
    let lineHeight = parseFloat(cs.lineHeight);
    if (!Number.isFinite(lineHeight)) lineHeight = parseFloat(cs.fontSize) * 1.2;
    const box = this.host.getBoundingClientRect().height
      - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom)
      - parseFloat(cs.borderTopWidth) - parseFloat(cs.borderBottomWidth);
    return box <= lineHeight * this.clampLines + 1;
  }
}
