import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideTranslateService } from '@ngx-translate/core';
import { LineupsApiService } from '../core/lineups';
import { MovesEditorComponent } from './moves-editor.component';

describe('MovesEditorComponent', () => {
  let fixture: ComponentFixture<MovesEditorComponent>;
  let api: jasmine.SpyObj<LineupsApiService>;
  const KEY = { tnr: 4711, round: 2, matchNo: 1, board: 3 };

  function render(initial: string | null = null): MovesEditorComponent {
    fixture.componentRef.setInput('key', KEY);
    fixture.componentRef.setInput('title', 'Brett 3: Ackermann, Anna – Brunner, Bert');
    fixture.componentRef.setInput('initial', initial);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    api = jasmine.createSpyObj<LineupsApiService>('LineupsApiService', ['lineups', 'saveMoves']);
    TestBed.configureTestingModule({ imports: [MovesEditorComponent], providers: [{ provide: LineupsApiService, useValue: api }, provideTranslateService({ fallbackLang: 'de' })] });
    fixture = TestBed.createComponent(MovesEditorComponent);
  });

  it('startet mit den hinterlegten Zügen in deutscher Schreibweise', () => {
    const c = render('e4 c5 Nf3');
    expect(c.plies()).toEqual(['e4', 'c5', 'Nf3']);
    expect(c.text()).toBe('1.e4 c5 2.Sf3');
    expect((fixture.nativeElement as HTMLElement).querySelector('textarea')?.value).toBe('1.e4 c5 2.Sf3');
  });

  it('Brett und Text bleiben gleich: Zug am Brett schreibt den Text, Tippen stellt das Brett, Zurück nimmt einen Halbzug', () => {
    const c = render();
    c.onBoardMove({ from: 'e2', to: 'e4', san: 'e4', fen: '' });
    c.onBoardMove({ from: 'c7', to: 'c5', san: 'c5', fen: '' });
    expect(c.text()).toBe('1.e4 c5');
    c.onType('1.d4 Sf6 2.c4');
    expect(c.plies()).toEqual(['d4', 'Nf6', 'c4']);
    expect(c.fen()).toContain('rnbqkb1r/pppppppp/5n2/8/2PP4/8/PP2PPPP/RNBQKBNR b');
    c.back();
    expect(c.plies()).toEqual(['d4', 'Nf6']);
    expect(c.text()).toBe('1.d4 Sf6');
  });

  it('ein falscher getippter Zug: Hinweis mit dem Zug, Brett bis davor, Speichern gesperrt', () => {
    const c = render();
    c.onType('1.e4 e5 2.Ke3');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(c.plies()).toEqual(['e4', 'e5']);
    expect(el.querySelector('.err')?.textContent).toContain('Zug 2. Ke3');
    expect((el.querySelector('.me-save') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('speichert englische SAN und meldet das Ergebnis; Löschen schickt leer', async () => {
    api.saveMoves.and.resolveTo('e4 c5');
    const c = render('e4');
    const saved: (string | null)[] = [];
    c.saved.subscribe(m => saved.push(m));
    c.onType('e4 c5');
    await c.save();
    expect(api.saveMoves).toHaveBeenCalledWith(KEY, 'e4 c5');
    api.saveMoves.and.resolveTo(null);
    await c.remove();
    expect(api.saveMoves).toHaveBeenCalledWith(KEY, '');
    expect(saved).toEqual(['e4 c5', null]);
  });

  it('zeigt die Absage des Servers', async () => {
    api.saveMoves.and.rejectWith(new HttpErrorResponse({ status: 403, error: { reason: 'notYours' } }));
    const c = render();
    c.onType('e4');
    await c.save();
    expect(c.problem()).toContain('nicht ändern');
  });
});
