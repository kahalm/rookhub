import { TestBed } from '@angular/core/testing';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { SheetReadingStatusComponent } from './sheet-reading-status.component';

/** Die deutschen Texte der Uhr wie in public/i18n/de.json (Parität prüft i18n-parity.spec). */
const DE = { sheetEdit: { status: {
  waitingWhy: 'der Leser schaut jede Minute nach neuen Formularen',
  sinceDays: 'seit\u00A0{{days}}\u00A0Tagen', sinceDaysOne: 'seit\u00A01\u00A0Tag',
  sinceDaysHours: 'seit\u00A0{{days}}\u00A0Tagen {{hours}}\u00A0Std.', sinceDaysHoursOne: 'seit\u00A01\u00A0Tag {{hours}}\u00A0Std.',
} } };

describe('SheetReadingStatusComponent', () => {
  function render(status: string, createdAgoS: number, startedAgoS: number | null) {
    TestBed.configureTestingModule({ imports: [SheetReadingStatusComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    const f = TestBed.createComponent(SheetReadingStatusComponent);
    f.componentRef.setInput('status', status);
    f.componentRef.setInput('createdAt', new Date(Date.now() - createdAgoS * 1000).toISOString());
    f.componentRef.setInput('startedAt', startedAgoS == null ? null : new Date(Date.now() - startedAgoS * 1000).toISOString());
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    return { f, el, lines: Array.from(el.querySelectorAll('.rs-line')) as HTMLElement[] };
  }

  it('wartet: eine Zeile mit laufender Uhr, ohne Häkchen', () => {
    const { lines, f } = render('pending', 75, null);
    expect(lines.length).toBe(1);
    expect(lines[0].querySelector('.rs-check')).toBeNull();
    expect(lines[0].querySelector('.rs-clock')?.textContent?.trim()).toBe('1:15');
    f.destroy();
  });

  it('abgeholt: ✓ mit der Wartezeit, darunter „Wird gelesen" ab dem Abholen', () => {
    const { lines, f } = render('running', 200, 50);
    expect(lines.length).toBe(2);
    expect(lines[0].querySelector('.rs-check')?.textContent).toBe('✓');
    expect(lines[0].querySelector('.rs-time')?.textContent?.trim()).toBe('2:30');
    expect(lines[1].textContent).toContain('sheetEdit.status.reading');
    expect(lines[1].querySelector('.rs-clock')?.textContent?.trim()).toBe('0:50');
    f.destroy();
  });

  it('lange gewartet: Hinweis, dass der Leser wohl nicht läuft (ab einer Stunde mit Stunden)', () => {
    const { el, lines, f } = render('pending', 3 * 3600 + 5, null);
    expect(lines[0].querySelector('.rs-clock')?.textContent?.trim()).toBe('3:00:05');
    expect(el.querySelector('.rs-long')).not.toBeNull();
    f.destroy();
  });

  // d6-l-wartet-leser-gedankenstrich: nach 12 Tagen stand „298:59:11", und am Handy rutschte der Nachsatz als eigenes
  // Flex-Element mit „– der Leser …" vorn in die nächste Zeile.
  it('ab einem Tag zählt die Uhr in Tagen und Stunden („seit 13 Tagen 10 Std.")', () => {
    TestBed.configureTestingModule({ imports: [SheetReadingStatusComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    TestBed.inject(TranslateService).setTranslation('de', DE);
    TestBed.inject(TranslateService).use('de');
    const at = (s: number) => {
      const g = TestBed.createComponent(SheetReadingStatusComponent);
      g.componentRef.setInput('status', 'pending');
      g.componentRef.setInput('createdAt', new Date(Date.now() - s * 1000).toISOString());
      g.detectChanges();
      const t = (g.nativeElement as HTMLElement).querySelector('.rs-clock')!.textContent!.trim();
      g.destroy();
      return t;
    };
    expect(at(13 * 86400 + 10 * 3600 + 5)).toBe('seit\u00A013\u00A0Tagen 10\u00A0Std.');
    expect(at(86400 + 60)).toBe('seit\u00A01\u00A0Tag');
    expect(at(86400 + 2 * 3600)).toBe('seit\u00A01\u00A0Tag 2\u00A0Std.');
    expect(at(23 * 3600 + 59 * 60)).toBe('23:59:00');
  });

  it('schmal (Handy): der Nachsatz beginnt nie mit „–" am Zeilenanfang, sondern hängt an der Uhr', () => {
    TestBed.configureTestingModule({ imports: [SheetReadingStatusComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    TestBed.inject(TranslateService).setTranslation('de', DE);
    TestBed.inject(TranslateService).use('de');
    const f = TestBed.createComponent(SheetReadingStatusComponent);
    f.componentRef.setInput('status', 'pending');
    f.componentRef.setInput('createdAt', new Date(Date.now() - 13 * 86400 * 1000).toISOString());
    const el = f.nativeElement as HTMLElement;
    document.body.appendChild(el);
    try {
      for (const w of [180, 240, 300, 358]) {
        el.style.cssText = `display:block;width:${w}px;font:16px/1.4 sans-serif`;
        f.detectChanges();
        const clock = el.querySelector('.rs-clock')!.getClientRects();
        const why = el.querySelector('.rs-why')!.getClientRects();
        // Die erste Zeile des Nachsatzes (mit seinem „–") ist die letzte Zeile der Uhr.
        expect(Math.abs(why[0].top - clock[clock.length - 1].top)).withContext(`${w} px`).toBeLessThan(4);
        expect(getComputedStyle(el.querySelector('.rs-line')!).display).toBe('block');
      }
    } finally {
      f.destroy();
      el.remove();
    }
  });
});
