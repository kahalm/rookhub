import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';

/** Direkter Konstruktor-Test (ohne TestBed) der Link-Auswahl. */
function make(data: any): SharePuzzleDialogComponent {
  const snackbar: any = { copy: () => {} };
  const translate: any = { instant: (k: string) => k };
  return new SharePuzzleDialogComponent(data, snackbar, translate);
}

describe('SharePuzzleDialogComponent', () => {
  it('activeUrl returns the (single) share link as-is — Tracking ist immer an, kein Param', () => {
    const c = make({ url: 'http://x/puzzles/book/5?single=1', source: 'book' });
    expect(c.activeUrl).toBe('http://x/puzzles/book/5?single=1');
  });

  it('toggle switches between current and previous puzzle link', () => {
    const c = make({
      url: 'http://x/puzzles/book/5?single=1',
      previousUrl: 'http://x/puzzles/book/4?single=1',
      source: 'book',
    });
    expect(c.activeUrl).toBe('http://x/puzzles/book/5?single=1');
    c.toggle();
    expect(c.activeUrl).toBe('http://x/puzzles/book/4?single=1');
  });
  it('nutzt das Standard-Dialograster: h2 mat-dialog-title, Inhalt, Knopfzeile (d7-rk-dialog-titel-uneinheitlich)', () => {
    TestBed.configureTestingModule({
      imports: [SharePuzzleDialogComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { url: 'http://x/puzzles/5', previousUrl: 'http://x/puzzles/4' } },
      ],
    });
    const fixture = TestBed.createComponent(SharePuzzleDialogComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const title = el.querySelector('[mat-dialog-title]')!;
    expect(title.tagName).toBe('H2');
    expect(title.classList).not.toContain('dialog-title');
    expect(el.querySelector('mat-dialog-content .link-input')).not.toBeNull();
    expect(el.querySelectorAll('mat-dialog-actions button').length).toBe(2);
    expect(el.querySelector('.dialog-actions')).toBeNull();
    // Symbolknopf „Kopieren“ trägt Name für Screenreader (appIconLabel).
    expect(el.querySelector('.link-row button')!.getAttribute('aria-label')).toBe('common.copy');
    TestBed.resetTestingModule();
  });
});
