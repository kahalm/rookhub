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
    // Der Import über RookHub ist abgeschaltet — kein Eingabefeld, kein Knopf.
    expect(render().querySelectorAll('input, textarea, button').length).toBe(0);
  });

  it('shows the explanation keys', () => {
    const text = render().textContent ?? '';
    expect(text).toContain('chessable.useExtension.title');
    expect(text).toContain('chessable.useExtension.body');
  });
});
