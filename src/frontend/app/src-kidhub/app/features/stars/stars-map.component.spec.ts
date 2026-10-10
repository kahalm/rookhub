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
  });
});
