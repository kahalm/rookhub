import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { MarkNotesDialogComponent } from './mark-notes-dialog.component';
import { MarkedPositionsService } from './marked-positions.service';
import { addMarkTag, knownMarkTag } from './mark-tags';

/** Schlagworte + Kommentar am „+"-Knopf (0.862.0, Wunsch 2026-10-27). */
describe('MarkNotesDialogComponent', () => {
  const FEN = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1';
  const KEY = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3';

  function setup() {
    const ref = { close: jasmine.createSpy('close') };
    TestBed.configureTestingModule({
      imports: [MarkNotesDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { currentUser: { userId: 7 } } },
        { provide: MAT_DIALOG_DATA, useValue: { fen: FEN, origin: { context: 'analysis' } } },
        { provide: MatDialogRef, useValue: ref }],
    });
    const http = TestBed.inject(HttpTestingController);
    const service = TestBed.inject(MarkedPositionsService);
    service.ensureLoaded();
    http.expectOne('/api/positions/marked').flush([{ id: 1, fen: FEN, positionKey: KEY, context: 'analysis', createdAt: '',
      tags: ['tactic', 'Mein Motiv'], comment: 'alt' }]);
    const fixture = TestBed.createComponent(MarkNotesDialogComponent);
    fixture.detectChanges();
    return { fixture, http, ref, service, el: fixture.nativeElement as HTMLElement };
  }

  it('zeigt die gespeicherten Schlagworte, Klick schaltet, eigenes Wort per Enter, Speichern schickt beides', () => {
    const { fixture, http, ref, service, el } = setup();
    const chip = (text: string) => [...el.querySelectorAll<HTMLButtonElement>('.mn-chip')]
      .find(b => b.textContent!.includes(text))!;
    expect(chip('markPosition.tag.tactic').classList).toContain('on');
    expect(chip('Mein Motiv')).toBeTruthy();
    expect((el.querySelector('textarea') as HTMLTextAreaElement).value).toBe('alt');

    chip('markPosition.tag.tactic').click();                 // ab
    chip('markPosition.tag.endgame').click();                // an
    const input = el.querySelector('.mn-input') as HTMLInputElement;
    input.value = 'Läuferpaar'; input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    const ta = el.querySelector('textarea') as HTMLTextAreaElement;
    ta.value = '  neu  '; ta.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (el.querySelector('button.save') as HTMLButtonElement).click();
    const put = http.expectOne(r => r.method === 'PUT' && r.url === '/api/positions/marked/notes');
    expect(put.request.body).toEqual({ fen: FEN, tags: ['Mein Motiv', 'endgame', 'Läuferpaar'], comment: 'neu' });
    put.flush({ id: 1, fen: FEN, positionKey: KEY, context: 'analysis', createdAt: '', tags: ['Mein Motiv', 'endgame', 'Läuferpaar'], comment: 'neu' });
    expect(ref.close).toHaveBeenCalledWith('saved');
    expect(service.notesOf(FEN)).toEqual({ tags: ['Mein Motiv', 'endgame', 'Läuferpaar'], comment: 'neu' });
  });

  it('Speichern scheitert → Meldung, Fenster bleibt; „entfernen" nimmt die Markierung zurück', () => {
    const { fixture, http, ref, service, el } = setup();
    (el.querySelector('button.save') as HTMLButtonElement).click();
    http.expectOne(r => r.method === 'PUT').flush('x', { status: 500, statusText: 'x' });
    fixture.detectChanges();
    expect(el.querySelector('[role=alert]')).not.toBeNull();
    expect(ref.close).not.toHaveBeenCalled();

    (el.querySelector('button.remove') as HTMLButtonElement).click();
    http.expectOne(r => r.method === 'DELETE').flush(null);
    expect(ref.close).toHaveBeenCalledWith('removed');
    expect(service.isMarked(FEN)).toBeFalse();
    expect(service.notesOf(FEN).tags).toEqual([]);
  });

  it('Schlagworte: feste Codes klein, keine Doppel, kein „;", Grenze 12', () => {
    expect(knownMarkTag(' Tactic ')).toBe('tactic');
    expect(knownMarkTag('Motiv')).toBeNull();
    expect(addMarkTag(['tactic'], 'TACTIC')).toBeNull();
    expect(addMarkTag([], 'a;b')).toBeNull();
    expect(addMarkTag([], '   ')).toBeNull();
    expect(addMarkTag(Array.from({ length: 12 }, (_, i) => 't' + i), 'x')).toBeNull();
    expect(addMarkTag(['Motiv'], 'Endgame')).toEqual(['Motiv', 'endgame']);
  });
});
