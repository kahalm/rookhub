import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { MAX_PGN_CHARS, PgnImportDialogComponent } from './pgn-import-dialog.component';

describe('PgnImportDialogComponent', () => {
  let closed: unknown[];

  async function setup() {
    closed = [];
    await TestBed.configureTestingModule({
      imports: [PgnImportDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: (r?: unknown) => closed.push(r) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PgnImportDialogComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  it('uploads the pasted PGN and closes with the result when everything went through', async () => {
    const { c, http } = await setup();
    c.text.set('  1. e4 e5 *  ');
    c.upload();
    const req = http.expectOne('/api/games/import');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ pgn: '1. e4 e5 *' });
    req.flush({ imported: 1, duplicates: 0, truncated: false, ids: [7], failed: [] });
    expect(closed.length).toBe(1);
    expect((closed[0] as { ids: number[] }).ids).toEqual([7]);
  });

  it('stays open and lists the games that failed', async () => {
    const { fixture, c, http } = await setup();
    c.text.set('[White "Eva"]\n\n1. e4 e5 2. Ke3 *');
    c.upload();
    http.expectOne('/api/games/import').flush({
      imported: 0, duplicates: 1, truncated: false, ids: [3],
      failed: [{ index: 2, white: 'Eva', black: null, reason: 'illegal' }],
    });
    fixture.detectChanges();
    expect(closed.length).toBe(0);
    expect(c.result()?.failed.length).toBe(1);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.failed li').length).toBe(1);
  });

  it('refuses empty or oversized text without asking the server, and maps a server refusal', async () => {
    const { c, http } = await setup();
    c.text.set('   ');
    c.upload();
    expect(c.error()).toBe('empty');
    c.text.set('x'.repeat(MAX_PGN_CHARS + 1));
    c.upload();
    expect(c.error()).toBe('tooLarge');
    http.expectNone('/api/games/import');

    c.text.set('1. e4 *');
    c.upload();
    http.expectOne('/api/games/import').flush({ reason: 'tooLarge' }, { status: 400, statusText: 'Bad Request' });
    expect(c.error()).toBe('tooLarge');
    expect(c.busy()).toBeFalse();
  });

  it('puts the chosen file into the text field', async () => {
    const { c } = await setup();
    const file = new File(['[Event "x"]\n\n1. d4 d5 *'], 'partie.pgn', { type: 'application/x-chess-pgn' });
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [file] });
    await c.onFile({ target: input } as unknown as Event);
    expect(c.fileName()).toBe('partie.pgn');
    expect(c.text()).toContain('1. d4 d5');
  });
});
