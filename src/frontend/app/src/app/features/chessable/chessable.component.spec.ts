import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ChessableComponent, REPCHECK_CHROME_URL, REPCHECK_FIREFOX_URL } from './chessable.component';

describe('ChessableComponent', () => {
  function render(): HTMLElement {
    TestBed.configureTestingModule({
      imports: [ChessableComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(ChessableComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('links only to the RepCheck extension in both stores, opening in a new tab', () => {
    const links = Array.from(render().querySelectorAll('a'));
    expect(links.map(a => a.getAttribute('href'))).toEqual([REPCHECK_CHROME_URL, REPCHECK_FIREFOX_URL]);
    for (const a of links) {
      expect(a.getAttribute('target')).toBe('_blank');
      expect(a.getAttribute('rel')).toContain('noopener');
    }
  });

  it('offers no way to store a bearer or start an import', () => {
    // Der Import über RookHub ist abgeschaltet — kein Eingabefeld, kein Knopf außer „Mehr".
    const el = render();
    expect(el.querySelectorAll('input, textarea').length).toBe(0);
    expect(Array.from(el.querySelectorAll('button')).map(b => b.className)).toEqual(['more-toggle']);
  });

  // UI-Sweep 2026-10-11 (d2-rk-chessable-erklaertext): Kernsatz über den Knöpfen, Begründung hinter „Mehr".
  it('shows the lead sentence and keeps the reasoning behind „Mehr"', () => {
    TestBed.configureTestingModule({
      imports: [ChessableComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(ChessableComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('chessable.useExtension.title');
    expect(el.textContent).toContain('chessable.useExtension.lead');
    expect(el.textContent).not.toContain('chessable.useExtension.body');
    const toggle = el.querySelector('p > button.more-toggle') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(el.querySelector('.why')?.textContent).toContain('chessable.useExtension.body');
  });
});
