import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { SheetReadingStatusComponent } from './sheet-reading-status.component';

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
});
