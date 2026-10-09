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

  it('Stellung: Rechtsklick/langer Druck setzt in Schwarz, Ziehen verschiebt/setzt/entfernt, FEN folgt', () => {
    const c = render();
    c.setMode('position');
    c.pick('Q');
    c.onSquare(27, true);   // Rechtsklick d5 → schwarze Dame
    expect(c.setup()[27]).toBe('q');
    c.onSquare(28);         // Linksklick e5 → weiße Dame
    expect(c.setup()[28]).toBe('Q');
    c.onPieceMoved({ from: 52, to: 36 });   // e2 → e4 — Weiß 2 Züge, Schwarz 1 → Schwarz am Zug (Automatik)
    expect(c.fenText()).toBe('rnbqkbnr/pppppppp/8/3qQ3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1');
    c.onPieceRemoved({ from: 63 });         // Th1 weg → kein kurzes Rochaderecht
    expect(c.fenText()).toContain(' b Qkq - ');
    c.onPieceDropped({ piece: 'n', to: 40 }); // a3
    expect(c.setup()[40]).toBe('n');
    // Am Brett sichtbar: das Aufstell-Brett bekommt altPlace, sobald eine Figur gewählt ist
    fixture.detectChanges();
    const sq = (fixture.nativeElement as HTMLElement).querySelector('[data-i="27"]') as HTMLElement;
    const menu = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
    sq.dispatchEvent(menu);
    expect(menu.defaultPrevented).toBeTrue();
    expect(c.setup()[27]).toBe('');   // dieselbe schwarze Dame noch einmal = Feld leeren
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
    c.onFenInput('rnbqkbnr/pp3ppp/4p3/2pp4/4P3/2P2N2/PP1P1PPP/RNBQKBNR w - - 0 1');   // Sf3 gesetzt, Sg1 nicht weg
    expect(c.setupProblem()).toContain('Weiß hat 3 Springer');
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.me-suggest')?.disabled).toBeTrue();
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
    // Stellung kennt der Explorer gar nicht
    explorer.paths.and.resolveTo({ ...RESULT, opening: null, games: 0, paths: [] });
    const c = render();
    c.setMode('position');
    await c.suggest();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.me-none')?.textContent).toContain('Der Explorer kennt diese Stellung nicht (0 Partien) — Figuren und Seite am Zug prüfen');
    expect(el.querySelector('.me-truncated')).toBeNull();
    // Stellung bekannt, aber kein Weg dorthin (ohne Budget-Abbruch)
    explorer.paths.and.resolveTo({ ...RESULT, opening: null, games: 523125, paths: [] });
    await c.suggest();
    fixture.detectChanges();
    expect(el.querySelector('.me-none')?.textContent)
      .toContain('Stellung bekannt (≈ 520.000 Partien), aber keine Zugfolge innerhalb von 20 Halbzügen gefunden');
    expect(el.querySelector('.me-truncated')).toBeNull();
    expect(el.querySelector('.me-continue')).toBeNull();
    explorer.paths.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'noLocalExplorer' } }));
    await c.suggest();
    fixture.detectChanges();
    expect(c.searchError()).toContain('keinen lokalen');
    expect(el.querySelector('.me-suggest')).toBeNull();
  });

  const ITALIAN = 'r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R';

  // Prod 09.10.2026: D00 nach 15 Halbzügen, kalt 0 Wege am Budget, mit dem Speicher der vorigen Runde 5.
  const D00 = 'rnbq1rk1/pp3ppp/2pb1p2/8/2BP4/1QN1P3/PP3PPP/R3K1NR b KQ - 0 1';
  const DEEP_EMPTY: ExplorerPathsResult = { ...RESULT, opening: { eco: 'D00', name: 'Queen\'s Pawn Game: Levitsky Attack' }, games: 146,
    paths: [], truncated: true, queries: 240, cached: 0 };
  const DEEP_FOUND: ExplorerPathsResult = { ...DEEP_EMPTY, truncated: false, cached: 241, paths: [
    { moves: ['d4', 'd5', 'Bg5', 'Nf6', 'Bxf6', 'exf6', 'e3', 'Bd6', 'c4', 'c6', 'Nc3', 'O-O', 'Qb3', 'dxc4', 'Bxc4'], uci: [], estGames: 15, share: 0.1 },
  ] };

  function deferred<T>(): { promise: Promise<T>; resolve: (v: T) => void } {
    let resolve!: (v: T) => void;
    const promise = new Promise<T>(r => { resolve = r; });
    return { promise, resolve };
  }

  it('Runden: truncated → von selbst Runde 2 mit Fortschritt und den bisherigen Wegen; endet, sobald nicht mehr truncated', async () => {
    const partial: ExplorerPathsResult = { ...DEEP_EMPTY, paths: [DEEP_FOUND.paths[0]] };
    const second = deferred<ExplorerPathsResult>();
    explorer.paths.and.returnValues(Promise.resolve(partial), second.promise, Promise.resolve(DEEP_FOUND));
    const c = render();
    c.setMode('position');
    c.onFenInput(D00);
    const run = c.suggest();
    await fixture.whenStable();
    await new Promise(r => setTimeout(r));
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(explorer.paths).toHaveBeenCalledTimes(2);
    expect(el.querySelector('.me-searching')?.textContent).toContain('Runde 2 von 3 … (bisher 1 Weg)');
    expect(el.querySelectorAll('.me-path').length).toBe(1);   // Zwischenstand schon sichtbar
    second.resolve({ ...DEEP_FOUND });
    await run;
    fixture.detectChanges();
    expect(explorer.paths).toHaveBeenCalledTimes(2);           // Runde 2 war nicht mehr truncated → keine dritte
    expect(explorer.paths.calls.allArgs().every(a => a[0] === D00)).toBeTrue();
    expect(el.querySelector('.me-searching')).toBeNull();
    expect(el.querySelectorAll('.me-path').length).toBe(1);
    expect(el.querySelector('.me-continue')).toBeNull();
    expect(c.roundsDone()).toBe(2);
  });

  it('Runden: drei Runden ohne Weg → Meldung „in 3 Runden" und „Weiter suchen" fragt eine vierte', async () => {
    explorer.paths.and.resolveTo(DEEP_EMPTY);
    const c = render();
    c.setMode('position');
    c.onFenInput(D00);
    await c.suggest();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(explorer.paths).toHaveBeenCalledTimes(3);
    expect(el.querySelector('.me-none')?.textContent).toContain(
      'Stellung bekannt (≈ 146 Partien), aber in 3 Runden keine Zugfolge gefunden — tiefe');
    expect(el.querySelector('.me-none')?.textContent).toContain('„Weiter suchen" setzt fort');
    const more = el.querySelector<HTMLButtonElement>('.me-continue');
    expect(more?.textContent).toContain('Weiter suchen');
    explorer.paths.and.resolveTo(DEEP_FOUND);
    more!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(explorer.paths).toHaveBeenCalledTimes(4);
    expect(c.roundsDone()).toBe(4);
    expect(el.querySelectorAll('.me-path').length).toBe(1);
    expect(el.querySelector('.me-continue')).toBeNull();
    expect(el.querySelector('.me-none')).toBeNull();
  });

  it('Runden: ein 429 beendet die Runden mit Hinweis, der bisherige Stand bleibt', async () => {
    const partial: ExplorerPathsResult = { ...DEEP_EMPTY, paths: [DEEP_FOUND.paths[0]] };
    explorer.paths.and.returnValues(Promise.resolve(partial),
      Promise.reject(new HttpErrorResponse({ status: 429, error: null })), Promise.resolve(DEEP_FOUND));
    const c = render();
    c.setMode('position');
    c.onFenInput(D00);
    await c.suggest();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(explorer.paths).toHaveBeenCalledTimes(2);
    expect(c.searchError()).toContain('Zu viele Suchen');
    expect(el.querySelectorAll('.me-path').length).toBe(1);
    expect(el.querySelector('.me-truncated')?.textContent).toContain('Suche nach 1 Runde am Budget abgebrochen');
    expect(el.querySelector('.me-continue')).not.toBeNull();
    expect(c.searching()).toBeFalse();
  });

  it('Runden: eine geänderte Stellung bricht die laufenden Runden ab', async () => {
    const second = deferred<ExplorerPathsResult>();
    explorer.paths.and.returnValues(Promise.resolve(DEEP_EMPTY), second.promise, Promise.resolve(DEEP_EMPTY));
    const c = render();
    c.setMode('position');
    c.onFenInput(D00);
    const run = c.suggest();
    await new Promise(r => setTimeout(r));
    c.startPosition();
    second.resolve(DEEP_EMPTY);
    await run;
    expect(explorer.paths).toHaveBeenCalledTimes(2);
    expect(c.result()).toBeNull();
    expect(c.searching()).toBeFalse();
  });

  it('Stellung: Palette in zwei Reihen — K D T L S B über k d t l s b, ✕ eigener Knopf', () => {
    const c = render();
    c.setMode('position');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const imgs = [...el.querySelectorAll<HTMLButtonElement>('.me-palette .me-pc:not(.me-erase) img')].map(i => i.getAttribute('src') ?? '');
    const codes = imgs.map(src => { const f = src.split('/').pop()!.replace('.svg', ''); return f[0] === 'w' ? f[1] : f[1].toLowerCase(); });
    expect(codes).toEqual(['K', 'Q', 'R', 'B', 'N', 'P', 'k', 'q', 'r', 'b', 'n', 'p']);
    const erase = el.querySelector<HTMLButtonElement>('.me-palette .me-erase');
    expect(erase?.textContent).toContain('✕');
    expect(getComputedStyle(erase!).gridColumnStart).toBe('7');
  });

  it('Stellung: Automatik setzt Schwarz am Zug (Weiß 4 Züge, Schwarz 3), mit Hinweis', () => {
    const c = render();
    c.setMode('position');
    // Italienisch aufbauen: Ausgangsstellung, dann Züge als Verschiebungen
    c.onPieceMoved({ from: 52, to: 36 });   // e2-e4
    c.onPieceMoved({ from: 12, to: 28 });   // e7-e5
    c.onPieceMoved({ from: 62, to: 45 });   // Sg1-f3
    c.onPieceMoved({ from: 1, to: 18 });    // Sb8-c6
    c.onPieceMoved({ from: 61, to: 34 });   // Lf1-c4
    c.onPieceMoved({ from: 6, to: 21 });    // Sg8-f6
    c.onPieceMoved({ from: 51, to: 43 });   // d2-d3
    expect(c.fenText().split(' ')[0]).toBe(ITALIAN);
    expect(c.side()).toBe('b');
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.me-auto-side')?.textContent)
      .toContain('Seite am Zug automatisch: Schwarz (Weiß hat 4 Züge gemacht, Schwarz 3)');
  });

  it('Stellung: Umschalten von Hand hebt die Automatik auf — dann Warnung statt Umstellen', () => {
    const c = render();
    c.setMode('position');
    c.onPieceMoved({ from: 52, to: 36 });   // e2-e4 → Schwarz
    expect(c.side()).toBe('b');
    c.setSide('w');
    c.onPieceMoved({ from: 62, to: 45 });   // Sg1-f3: bleibt Weiß
    expect(c.side()).toBe('w');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.me-auto-side')).toBeNull();
    expect(el.querySelector('.me-side-warn')?.textContent)
      .toContain('Weiß am Zug passt nicht zur Figurenstellung — Schwarz hat weniger Züge gemacht');
    c.startPosition();   // neue Stellung: Automatik wieder an
    c.onPieceMoved({ from: 52, to: 36 });
    expect(c.side()).toBe('b');
  });

  it('Stellung: Server kennt die Stellung mit der anderen Seite am Zug → Meldung + „Seite wechseln und erneut suchen"', async () => {
    explorer.paths.and.resolveTo({ ...RESULT, opening: null, games: 0, paths: [], otherSideGames: 3_041_000 });
    const c = render();
    c.setMode('position');
    c.onFenInput(ITALIAN + ' w KQkq - 0 1');
    await c.suggest();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.me-other-side')?.textContent)
      .toContain('Mit Schwarz am Zug kennt der Explorer die Stellung (≈ 3 Mio. Partien)');
    explorer.paths.calls.reset();
    explorer.paths.and.resolveTo(RESULT);
    el.querySelector<HTMLButtonElement>('.me-switch-side')!.click();
    await fixture.whenStable();
    expect(c.side()).toBe('b');
    expect(explorer.paths).toHaveBeenCalledWith(ITALIAN + ' b KQkq - 0 1');
  });
});
