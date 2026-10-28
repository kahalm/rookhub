import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService } from '@ngx-translate/core';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { MarkNotesDialogComponent } from './mark-notes-dialog.component';
import { MarkedPositionsService } from './marked-positions.service';
import { addMarkTag, knownMarkTag } from './mark-tags';

/** Schlagworte + Kommentar am „+"-Knopf (0.862.0, Wunsch 2026-10-27). */
describe('MarkNotesDialogComponent', () => {
  const FEN = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1';
  const KEY = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3';

  function setup(edit = true) {
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
    const el = fixture.nativeElement as HTMLElement;
    if (edit) { (el.querySelector('.mn-pencil') as HTMLButtonElement).click(); fixture.detectChanges(); }
    return { fixture, http, ref, service, el };
  }

  // 0.865.0, Wunsch 2026-10-28: „zeig beim Klicken auf + normal die hinterlegte Klassifizierung + Kommentar, irgendwo klein ein
  // Bleistift zum Bearbeiten, dann stehen alle zum Auswählen + Edit für Kommentar"
  it('öffnet zum Lesen: nur die vergebenen Schlagworte und die Bemerkung, Bleistift schaltet in die Bearbeitung', () => {
    const { fixture, el } = setup(false);
    const chips = [...el.querySelectorAll('.mn-chip')].map(c => c.textContent!.trim());
    expect(chips).toEqual(['markPosition.tag.tactic', 'Mein Motiv']);
    expect(el.querySelector('.mn-mine')?.textContent).toBe('alt');
    expect(el.querySelector('textarea')).toBeNull();
    expect(el.querySelector('button.save')).toBeNull();
    expect(el.querySelector('button.close')).not.toBeNull();
    (el.querySelector('.mn-pencil') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelectorAll('.mn-chip').length).toBeGreaterThan(14);   // alle festen zum Auswählen
    expect((el.querySelector('textarea') as HTMLTextAreaElement).value).toBe('alt');
    expect(el.querySelector('button.save')).not.toBeNull();
  });

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

  // 0.862.1 (Wunsch 2026-10-27: „merk dir beim Bearbeiten auch die alten, damit ich ggf. Gelöschtes wiederherstellen kann").
  it('„Frühere Fassungen" lädt den Verlauf erst beim Aufklappen; „Wiederherstellen" übernimmt eine in die Felder', () => {
    const { fixture, http, el } = setup();
    expect(el.querySelector('.mn-hist')).toBeNull();
    (el.querySelector('.mn-hist-toggle') as HTMLButtonElement).click();
    const get = http.expectOne(r => r.url === '/api/positions/marked/notes/history');
    expect(get.request.params.get('fen')).toBe(FEN);
    get.flush([{ id: 5, tags: ['sacrifice', 'Läuferpaar'], comment: 'gelöscht', createdAt: '2026-10-27T20:00:00Z' }]);
    fixture.detectChanges();
    expect(el.querySelector('.mn-hist-tags')?.textContent).toContain('Läuferpaar');
    (el.querySelector('.mn-hist .mn-add') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect((el.querySelector('textarea') as HTMLTextAreaElement).value).toBe('gelöscht');
    (el.querySelector('button.save') as HTMLButtonElement).click();
    expect(http.expectOne(r => r.method === 'PUT').request.body)
      .toEqual({ fen: FEN, tags: ['sacrifice', 'Läuferpaar'], comment: 'gelöscht' });
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

  // d7-rk-markieren-dialog-speichern-eigene-zeile: echte Dialoggröße (440 px) — Entfernen, Abbrechen, Speichern in EINER Zeile.
  it('Aktionszeile bricht bei 440 px nicht um (deutsche Texte)', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'de' }),
        { provide: AuthService, useValue: { currentUser: { userId: 7 } } }],
    });
    const t = TestBed.inject(TranslateService);
    t.setTranslation('de', { common: { cancel: 'Abbrechen', save: 'Speichern', close: 'Schließen' },
      markPosition: { title: 'Interessante Stellung', remove: 'Markierung entfernen' } });
    t.use('de');
    const ref = TestBed.inject(MatDialog).open(MarkNotesDialogComponent,
      { data: { fen: FEN, origin: { context: 'analysis' } }, width: '440px', maxWidth: '94vw' });
    ref.componentInstance.editing.set(true);
    for (let i = 0; i < 3; i++) { TestBed.tick(); await new Promise(r => setTimeout(r, 20)); }
    const actions = document.querySelector('.cdk-overlay-container mat-dialog-actions') as HTMLElement;
    const top = (sel: string) => (actions.querySelector(sel) as HTMLElement).getBoundingClientRect().top;
    const save = top('.save');
    expect(top('.remove')).toBe(save);
    expect(top('button[mat-dialog-close]')).toBe(save);
    ref.close();
  });
});
