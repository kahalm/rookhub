import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { StarsMapComponent } from './stars-map.component';
import { contrast, parseColor } from '@rh/testing/contrast';

describe('StarsMapComponent', () => {
  const KEY = 'rh-kids-stars-v1';
  beforeEach(() => {
    localStorage.removeItem(KEY);
    TestBed.configureTestingModule({
      imports: [StarsMapComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
  });
  afterEach(() => localStorage.removeItem(KEY));

  /** UI-Sweep 2026-10-10 (k-locked-contrast): gesperrte Stufen zeigten nur ein Schloss, hellgrau auf fast Weiss. */
  it('gesperrte Stufe: Figur und Name sichtbar, Schloss als Abzeichen, Text mit 4,5:1', () => {
    const f = TestBed.createComponent(StarsMapComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const locked = el.querySelectorAll<HTMLElement>('.stage.locked');
    expect(locked.length).toBe(19);
    const second = locked[0];
    expect(second.querySelector('.glyph')!.textContent).toContain('♗');      // Stufe 2: Laeufer
    expect(second.querySelector('.name')!.textContent).toContain('kids.stars.piece.B');
    expect(second.querySelector('.lock')!.textContent).toContain('🔒');
    expect(getComputedStyle(second.querySelector('.lock')!).position).toBe('absolute');

    const style = getComputedStyle(second);
    expect(Number(style.opacity)).toBe(1);
    const bg = parseColor(style.backgroundColor);
    for (const part of ['.num', '.name', '.meta']) {
      const s = getComputedStyle(second.querySelector(part)!);
      const [r, g, b, a] = parseColor(s.color);
      expect(contrast([r, g, b, a * Number(s.opacity)], bg)).withContext(part).toBeGreaterThanOrEqual(4.5);
    }
    // z1-k-locked-glyph: die Figur ist bedeutungstragende Grafik — feste Farbe mit mindestens 3:1 statt 45 % Deckkraft
    const glyph = getComputedStyle(second.querySelector('.glyph')!);
    const [gr, gg, gb, ga] = parseColor(glyph.color);
    expect(Number(glyph.opacity)).toBe(1);
    expect(contrast([gr, gg, gb, ga], bg)).toBeGreaterThanOrEqual(3);
  });

  /** Karte in einem iframe `w`×`h` — `bodyStyle` setzt `--kid-wide-max` wie die App-Huelle ab 2400 px. */
  function gridAt(host: HTMLElement, w: number, h: number, bodyStyle = '') {
    const css = Array.from(document.styleSheets).map(sheet => {
      try { return Array.from(sheet.cssRules).map(r => r.cssText).join('\n'); } catch { return ''; }
    }).join('\n');
    const frame = document.createElement('iframe');
    frame.style.cssText = `position: fixed; left: ${-w - 50}px; top: 0; width: ${w}px; height: ${h}px; border: 0;`;
    document.body.appendChild(frame);
    try {
      const doc = frame.contentDocument!;
      doc.open();
      doc.write(`<!doctype html><html><head><style>${css}</style></head><body style="margin:0;${bodyStyle}">${host.outerHTML}</body></html>`);
      doc.close();
      const tiles = Array.from(doc.querySelectorAll('.grid li')).map(t => t.getBoundingClientRect());
      const top = Math.round(tiles[0].top);
      return {
        host: doc.body.firstElementChild!.getBoundingClientRect().width,
        width: tiles[0].width,
        perRow: tiles.filter(r => Math.round(r.top) === top).length,
        name: parseFloat(frame.contentWindow!.getComputedStyle(doc.querySelector('.stage .name')!).fontSize),
      };
    } finally {
      frame.remove();
    }
  }

  /** a1-k-ultrawide-raster: ab 2400 px 1880 px breit und größere Kacheln (≥ 170 px, ≤ 10 je Reihe); 1920 px wie bisher. */
  it('Breitbild-Stufe: größere Kacheln statt nur mehr Spalten, 1920 px unverändert', () => {
    const f = TestBed.createComponent(StarsMapComponent);
    f.detectChanges();
    const host = f.nativeElement as HTMLElement;
    const uw = gridAt(host, 3440, 1440, '--kid-wide-max: 1880px;');
    expect(uw.host).toBeCloseTo(1880, 0);
    expect(uw.width).toBeGreaterThanOrEqual(170);
    expect(uw.perRow).toBeLessThanOrEqual(10);
    expect(uw.name).toBeCloseTo(19.2, 1);
    const hd = gridAt(host, 1920, 1080);
    expect(hd.host).toBeCloseTo(1440, 0);
    expect(hd.width).toBeLessThan(170);
    expect(hd.name).toBeLessThan(19);
  });
});
