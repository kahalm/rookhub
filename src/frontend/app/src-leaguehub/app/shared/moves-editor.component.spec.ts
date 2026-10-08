import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideTranslateService } from '@ngx-translate/core';
import { LineupsApiService } from '../core/lineups';
import { ExplorerPathsResult, ExplorerPathsService } from '../core/position-setup';
import { MovesEditorComponent } from './moves-editor.component';

describe('MovesEditorComponent', () => {
  let fixture: ComponentFixture<MovesEditorComponent>;
  let api: jasmine.SpyObj<LineupsApiService>;
  let explorer: jasmine.SpyObj<ExplorerPathsService>;
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
    explorer = jasmine.createSpyObj<ExplorerPathsService>('ExplorerPathsService', ['hasLocal', 'paths']);
    explorer.hasLocal.and.resolveTo(true);
    TestBed.configureTestingModule({ imports: [MovesEditorComponent], providers: [{ provide: LineupsApiService, useValue: api },
      { provide: ExplorerPathsService, useValue: explorer }, provideTranslateService({ fallbackLang: 'de' })] });
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

  // ---- Modus „Stellung" ----

  const NAJDORF = 'rnbqkb1r/1p2pppp/p2p1n2/8/3NP3/2N5/PPP2PPP/R1BQKB1R w KQkq - 0 1';
  const RESULT: ExplorerPathsResult = {
    opening: { eco: 'B90', name: 'Sicilian Defense: Najdorf Variation' }, games: 3103474, searched: 330, queries: 331,
    truncated: false, failed: false,
    paths: [
      { moves: ['e4', 'c5', 'Nf3', 'd6', 'd4', 'cxd4', 'Nxd4', 'Nf6', 'Nc3', 'a6'], uci: [], estGames: 2773801, share: 0.894 },
      { moves: ['e4', 'c5', 'Nf3', 'd6', 'Nc3', 'Nf6', 'd4', 'cxd4', 'Nxd4', 'a6'], uci: [], estGames: 73905, share: 0.024 },
    ],
  };

  it('Stellung: startet mit der Stellung nach den Zügen, Brett und FEN-Feld laufen synchron', async () => {
    const c = render('e4');
    c.setMode('position');
    expect(c.fenText()).toBe('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1');
    // Brett → FEN: Figur wählen, Feld antippen
    c.pick('Q');
    c.onSquare(27);   // d5
    expect(c.fenText()).toBe('rnbqkbnr/pppppppp/8/3Q4/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1');
    c.pick('x');
    c.onSquare(63);   // h1 leeren → weiß verliert das kurze Rochaderecht
    expect(c.fenText()).toContain(' b Qkq - ');
    // FEN → Brett
    c.onFenInput(NAJDORF);
    expect(c.setup()[36]).toBe('P');   // e4
    expect(c.setup()[35]).toBe('N');   // d4
    expect(c.side()).toBe('w');
    expect(c.fenError()).toBeFalse();
    c.onFenInput('kein fen');
    expect(c.fenError()).toBeTrue();
    expect(c.setup()[35]).toBe('N');   // Brett bleibt bei der letzten lesbaren Stellung
    await fixture.whenStable();
    expect(explorer.hasLocal).toHaveBeenCalled();
  });

  it('Stellung: ungültige Aufstellungen sperren die Suche mit Hinweis', () => {
    const c = render();
    c.setMode('position');
    c.clearBoard();
    expect(c.setupProblem()).toContain('genau einen König');
    c.onFenInput('4k3/8/8/8/8/8/8/P3K3 w - - 0 1');
    expect(c.setupProblem()).toContain('1. oder 8. Reihe');
    c.onFenInput('4k3/8/8/8/8/8/8/4K2R w - - 0 1');
    expect(c.setupProblem()).toBeNull();
    c.onFenInput('4k3/8/8/8/8/8/8/4R1K1 w - - 0 1');   // Schwarz im Schach, aber Weiß am Zug
    expect(c.setupProblem()).toContain('Schwarz steht im Schach');
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.me-suggest')?.disabled).toBeTrue();
    c.setSide('b');
    expect(c.setupProblem()).toBeNull();
  });

  it('Stellung: Vorschläge zeigen Eröffnung, Zugfolge deutsch und Partien; Klick übernimmt die Züge', async () => {
    explorer.paths.and.resolveTo(RESULT);
    const c = render();
    c.setMode('position');
    c.onFenInput(NAJDORF);
    await c.suggest();
    expect(explorer.paths).toHaveBeenCalledWith('rnbqkb1r/1p2pppp/p2p1n2/8/3NP3/2N5/PPP2PPP/R1BQKB1R w KQkq - 0 1');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.me-opening')?.textContent).toContain('Najdorf');
    const items = el.querySelectorAll<HTMLButtonElement>('.me-path');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('1.e4 c5 2.Sf3 d6 3.d4 cxd4 4.Sxd4 Sf6 5.Sc3 a6');
    expect(items[0].textContent).toContain('≈ 2,8 Mio. Partien');
    expect(items[0].textContent).toContain('89,4 %');
    items[1].click();
    fixture.detectChanges();
    expect(c.mode()).toBe('moves');
    expect(c.plies()).toEqual(['e4', 'c5', 'Nf3', 'd6', 'Nc3', 'Nf6', 'd4', 'cxd4', 'Nxd4', 'a6']);
    expect(c.text()).toBe('1.e4 c5 2.Sf3 d6 3.Sc3 Sf6 4.d4 cxd4 5.Sxd4 a6');
  });

  it('Stellung: keine Treffer und Fehler werden gesagt; ohne lokalen Explorer kein Knopf', async () => {
    explorer.paths.and.resolveTo({ ...RESULT, opening: null, paths: [] });
    const c = render();
    c.setMode('position');
    await c.suggest();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Keine Zugfolge gefunden');
    explorer.paths.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'noLocalExplorer' } }));
    await c.suggest();
    fixture.detectChanges();
    expect(c.searchError()).toContain('keinen lokalen');
    expect(el.querySelector('.me-suggest')).toBeNull();
  });
});
