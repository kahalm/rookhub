import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApplicationRef, signal } from '@angular/core';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from '../../../core/auth.service';
import { MarkNotesDialogComponent } from '../../../shared/mark-position/mark-notes-dialog.component';
import { positionKey } from '../../../shared/mark-position/marked-positions.service';
import { PreferencesService } from '../../../core/preferences.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { GamesService } from '../games.service';
import { ClubGameSummaryComponent } from './club-game-summary.component';
import { ClubGameSummaryService } from './club-game-summary.service';
import { SUMMARY_ENGINE_SESSION, SummaryGameBoardComponent } from './summary-game-board.component';
import { SummaryBoardSheetComponent } from './summary-board-sheet.component';
import type { LiveEngineSession } from '../live-engine-session';

/** Engine-Attrappe: merkt sich die gerechneten Stellungen, liefert eine feste Linie und einen Pfeil. */
function fakeEngine() {
  const synced: string[] = [];
  const session = {
    synced, destroyed: false,
    sync: (_i: number, fen: string) => { synced.push(fen); },
    lines: signal([{ evalText: '+0.3', san: '1. e4 e5', positive: true }]),
    depth: signal(12),
    arrows: signal([{ from: 'e2', to: 'e4', brush: 'blue' }]),
    destroy() { this.destroyed = true; },
  };
  return session;
}

const PGN = '1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 *';
const prefs = { boardTheme: 'brown', pieceSet: 'cburnett' };
/** Der „+“-Knopf am Brett lädt die eigenen Markierungen — ohne Anmeldung, über die Test-HTTP-Schicht. */
const MARK_HTTP = [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: { currentUser: null, isLoggedIn: false } }];

describe('SummaryGameBoardComponent', () => {
  let fixture: ComponentFixture<SummaryGameBoardComponent>;
  let c: SummaryGameBoardComponent;
  const el = () => fixture.nativeElement as HTMLElement;
  const click = (sel: string) => { el().querySelector<HTMLButtonElement>(sel)!.click(); fixture.detectChanges(); };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SummaryGameBoardComponent],
      providers: [...MARK_HTTP, provideTranslateService({ fallbackLang: 'en' }), { provide: PreferencesService, useValue: prefs }],
    });
    fixture = TestBed.createComponent(SummaryGameBoardComponent);
    fixture.componentRef.setInput('pgn', PGN);
    c = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('blättert mit den Knöpfen und bleibt an den Enden stehen', () => {
    expect(c.ply()).toBe(0);
    expect(el().querySelector<HTMLButtonElement>('.nav-back')!.disabled).toBeTrue();
    click('.nav-forward');
    click('.nav-forward');
    expect(c.ply()).toBe(2);
    click('.nav-back');
    expect(c.ply()).toBe(1);
    click('.nav-end');
    expect(c.ply()).toBe(6);
    expect(el().querySelector<HTMLButtonElement>('.nav-forward')!.disabled).toBeTrue();
    click('.nav-start');
    expect(c.ply()).toBe(0);
  });

  it('Klick auf einen Zug stellt die Stellung NACH diesem Zug aufs Brett', () => {
    click('.move[data-index="2"]');
    expect(c.ply()).toBe(3);
    expect(el().querySelector('.move.active')!.textContent).toContain('f3');
    expect(c.positions().fens[c.ply()]).toContain('RNBQKB1R');
  });

  it('Zugliste nummeriert beide Farben in einer Zeile', () => {
    expect(el().querySelectorAll('.move-row').length).toBe(3);
  });

  it('Pfeiltasten blättern, im Eingabefeld nicht', () => {
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true }));
    expect(c.ply()).toBe(1);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', cancelable: true }));
    expect(c.ply()).toBe(0);

    const input = document.createElement('input');
    document.body.appendChild(input);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
    input.remove();
    expect(c.ply()).toBe(0);
  });

  it('showPly springt auf den Halbzug, begrenzt ihn und übernimmt die Blickrichtung des Diagramms', () => {
    c.showPly(4, true);
    expect(c.ply()).toBe(4);
    expect(c.isFlipped()).toBeTrue();
    c.showPly(99);
    expect(c.ply()).toBe(6);
  });

  const mv = (san: string, fen: string) => ({ san, uci: 'x', fen, from: 'a1', to: 'a2' });
  const FEN_A = 'r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3';
  const FEN_B = 'rnbqkbnr/pppp1ppp/8/8/3pP3/8/PPP2PPP/RNBQKBNR w KQkq - 0 3';

  it('showLine: solange die Linie der Partie folgt, ist es ein Partiezug', () => {
    const line = ['e4', 'e5', 'Nf3'].map(s => mv(s, 'f'));
    c.showLine(line, 2);
    expect(c.ply()).toBe(2);
    expect(c.variation()).toBeNull();
  });

  it('showLine: Abzweig wird Variante, ←/→ laufen durch, ← am Anfang führt zur Partie', () => {
    const line = [mv('e4', 'a'), mv('c5', FEN_A), mv('Nf3', FEN_A)];
    c.showLine(line, 3);
    fixture.detectChanges();
    expect(c.variation()!.base).toBe(1);
    expect(c.variation()!.at).toBe(2);
    expect(c.ply()).toBe(1);
    expect(el().querySelector('.var-move.active')!.textContent).toContain('Nf3');
    c.back();
    expect(c.variation()!.at).toBe(1);
    c.back();
    expect(c.variation()!.at).toBe(0);
    expect(c.currentFen()).toBe(c.positions().fens[1]);
    c.back();
    expect(c.variation()).toBeNull();
    expect(c.ply()).toBe(1);
  });

  it('eigener Zug: Partiezug läuft weiter, anderer Zug ist eine Variante, Blättern verwirft sie', () => {
    c.onUserMove({ from: 'e2', to: 'e4', san: 'e4', fen: c.positions().fens[1] });
    expect(c.ply()).toBe(1);
    expect(c.variation()).toBeNull();
    c.onUserMove({ from: 'c7', to: 'c5', san: 'c5', fen: FEN_A });
    expect(c.variation()!.line.map(m => m.san)).toEqual(['c5']);
    expect(c.currentFen()).toBe(FEN_A);
    c.onUserMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: FEN_A });
    c.goToVariation(1);
    c.onUserMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: FEN_A });
    expect(c.variation()!.line.map(m => m.san)).toEqual(['c5', 'Nc3']);
    c.goTo(4);
    expect(c.variation()).toBeNull();
    expect(c.ply()).toBe(4);
  });

  it('Knopf „zurück zur Partie“ beendet die Variante', () => {
    c.onUserMove({ from: 'd2', to: 'd4', san: 'd4', fen: FEN_A });
    fixture.detectChanges();
    click('.var-exit');
    expect(c.variation()).toBeNull();
    expect(c.ply()).toBe(0);
  });

  it('Engine-Knopf: rechnet die Stellung auf dem Brett, zeigt Linie + Pfeil, aus beendet die Sitzung', () => {
    const fake = fakeEngine();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [SummaryGameBoardComponent],
      providers: [...MARK_HTTP, provideTranslateService({ fallbackLang: 'en' }), { provide: PreferencesService, useValue: prefs },
        { provide: SUMMARY_ENGINE_SESSION, useValue: () => fake as unknown as LiveEngineSession }],
    });
    fixture = TestBed.createComponent(SummaryGameBoardComponent);
    fixture.componentRef.setInput('pgn', PGN);
    c = fixture.componentInstance;
    fixture.detectChanges();
    expect(el().querySelector('.engine')).toBeNull();
    click('.nav-engine');
    expect(c.engineOn()).toBeTrue();
    expect(el().querySelector('.engine-lines li')!.textContent).toContain('+0.3');
    expect(c.arrows().length).toBe(1);
    expect(fake.synced.at(-1)).toBe(c.positions().fens[0]);
    c.goTo(2);
    fixture.detectChanges();
    expect(fake.synced.at(-1)).toBe(c.positions().fens[2]);
    // eigener Zug (Variante): die Engine rechnet die neue Stellung
    c.onUserMove({ from: 'd2', to: 'd4', san: 'd4', fen: FEN_A });
    fixture.detectChanges();
    expect(fake.synced.at(-1)).toBe(FEN_A);
    click('.nav-engine');
    expect(fake.destroyed).toBeTrue();
    expect(el().querySelector('.engine')).toBeNull();
  });

  it('jump + engineAtStart (Blatt): startet auf dem Sprung, Engine an', () => {
    const fake = fakeEngine();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [SummaryGameBoardComponent],
      providers: [...MARK_HTTP, provideTranslateService({ fallbackLang: 'en' }), { provide: PreferencesService, useValue: prefs },
        { provide: SUMMARY_ENGINE_SESSION, useValue: () => fake as unknown as LiveEngineSession }],
    });
    fixture = TestBed.createComponent(SummaryGameBoardComponent);
    fixture.componentRef.setInput('pgn', PGN);
    fixture.componentRef.setInput('inSheet', true);
    fixture.componentRef.setInput('jump', { ply: 5, flipped: true });
    fixture.componentRef.setInput('engineAtStart', true);
    c = fixture.componentInstance;
    fixture.detectChanges();
    expect(c.ply()).toBe(5);
    expect(c.isFlipped()).toBeTrue();
    expect(c.engineOn()).toBeTrue();
    expect(el().classList).toContain('in-sheet');
  });

  /** „+“ am Seitenbrett (Wunsch 2026-11-14: „summary um den (+) knopf erweitern“). */
  describe('„+“-Knopf', () => {
    let http: HttpTestingController;
    let dialog: { open: jasmine.Spy };
    const mark = () => el().querySelector<HTMLButtonElement>('.nav app-mark-position-button button.mark')!;
    const post = () => http.expectOne(r => r.method === 'POST' && r.url === '/api/positions/marked');

    function boot(marked: string[] = [], source: object = { clubGameId: 7 }) {
      dialog = { open: jasmine.createSpy('open') };
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [SummaryGameBoardComponent],
        providers: [...MARK_HTTP, provideTranslateService({ fallbackLang: 'en' }), { provide: PreferencesService, useValue: prefs },
          { provide: MatDialog, useValue: dialog }],
      });
      http = TestBed.inject(HttpTestingController);
      fixture = TestBed.createComponent(SummaryGameBoardComponent);
      fixture.componentRef.setInput('pgn', PGN);
      fixture.componentRef.setInput('markSource', source);
      c = fixture.componentInstance;
      fixture.detectChanges();
      http.expectOne('/api/positions/marked').flush(marked.map((fen, i) => ({ id: i + 1, fen, positionKey: positionKey(fen), context: 'analysis', createdAt: '' })));
      fixture.detectChanges();
    }

    it('steht in der Knopfzeile hinter dem Engine-Knopf', () => {
      boot();
      const nav = Array.from(el().querySelector('.nav')!.children).map(n => n.className.split(' ').find(k => k.startsWith('nav-')));
      expect(nav).toEqual(['nav-start', 'nav-back', 'nav-forward', 'nav-end', 'nav-engine', 'nav-mark']);
      expect(mark().getAttribute('aria-pressed')).toBe('false');
    });

    it('markiert die Partiestellung auf dem Brett mit FEN, Halbzug und Partie; danach öffnet das Notizfenster', () => {
      boot();
      c.goTo(3);
      fixture.detectChanges();
      mark().click();
      fixture.detectChanges();
      const req = post();
      expect(req.request.body).toEqual({ fen: c.positions().fens[3], clubGameId: 7, context: 'analysis', ply: 3 });
      expect(req.request.body.fen).toContain('RNBQKB1R b');   // nach 2.Nf3, Schwarz am Zug
      expect(mark().classList).toContain('on');
      req.flush({});
      expect(dialog.open).toHaveBeenCalledOnceWith(MarkNotesDialogComponent, jasmine.objectContaining({
        data: { fen: c.positions().fens[3], origin: { clubGameId: 7, context: 'analysis', ply: 3 } } }));
      // weiterblättern: andere Stellung, nicht markiert; zurück: wieder markiert
      click('.nav-forward');
      expect(mark().classList).not.toContain('on');
      click('.nav-back');
      expect(mark().classList).toContain('on');
      http.verify();
    });

    it('schon markierte Stellung: Knopf grün, Klick öffnet nur das Fenster (kein zweites Speichern)', () => {
      const fens = c.positions().fens;
      boot([fens[2]]);
      expect(mark().classList).not.toContain('on');
      c.goTo(2);
      fixture.detectChanges();
      expect(mark().classList).toContain('on');
      expect(mark().getAttribute('aria-pressed')).toBe('true');
      mark().click();
      http.expectNone(r => r.method === 'POST');
      expect(dialog.open).toHaveBeenCalledTimes(1);
      http.verify();
    });

    it('eigene Variante: markiert ihre Stellung mit Abzweig und Zügen statt Halbzug; an der Abzweigstelle wieder die Partiestellung', () => {
      boot();
      c.goTo(2);
      c.onUserMove({ from: 'd2', to: 'd4', san: 'd4', fen: FEN_A });
      c.onUserMove({ from: 'e5', to: 'd4', san: 'exd4', fen: FEN_B });
      fixture.detectChanges();
      mark().click();
      const req = post();
      expect(req.request.body).toEqual({ fen: FEN_B, clubGameId: 7, context: 'analysis', ply: null, variationFromPly: 2, variationUci: 'd2d4 e5d4' });
      req.flush({});
      // ein Zug zurück in der Variante: nur noch deren erster Zug
      c.goToVariation(1);
      fixture.detectChanges();
      expect(c.markOrigin()).toEqual({ clubGameId: 7, context: 'analysis', ply: null, variationFromPly: 2, variationUci: 'd2d4' });
      expect(mark().classList).not.toContain('on');
      // Abzweigstelle (0 Variantenzüge) = Stellung der Partie nach Halbzug 2
      c.goToVariation(0);
      fixture.detectChanges();
      expect(c.currentFen()).toBe(c.positions().fens[2]);
      expect(c.markOrigin()).toEqual({ clubGameId: 7, context: 'analysis', ply: 2 });
    });

    it('Variante aus dem Text (showLine): Abzweig = letzter gemeinsamer Halbzug', () => {
      boot([], { savedGameId: 5 });
      const line = [
        { san: 'e4', uci: 'e2e4', fen: c.positions().fens[1], from: 'e2', to: 'e4' },
        { san: 'c5', uci: 'c7c5', fen: FEN_A, from: 'c7', to: 'c5' },
        { san: 'Nf3', uci: 'g1f3', fen: FEN_B, from: 'g1', to: 'f3' },
      ];
      c.showLine(line, 3);
      fixture.detectChanges();
      mark().click();
      const req = post();
      expect(req.request.body).toEqual({ fen: FEN_B, savedGameId: 5, context: 'analysis', ply: null, variationFromPly: 1, variationUci: 'c7c5 g1f3' });
      req.flush({});
    });

    it('OnPush ohne detectChanges: der Zustand „markiert“ rendert nach dem Klick von selbst', async () => {
      boot();
      fixture.autoDetectChanges();
      mark().click();
      await fixture.whenStable();
      expect(mark().classList).toContain('on');
      // Server lehnt ab → zurückgedreht, wieder ohne Anstoß von außen
      spyOn(TestBed.inject(SnackbarService), 'warn');
      post().flush({ reason: 'limit' }, { status: 400, statusText: 'Bad Request' });
      await fixture.whenStable();
      expect(mark().classList).not.toContain('on');
      expect(dialog.open).not.toHaveBeenCalled();
    });

    it('ohne Partie-Herkunft (Link /cg/:token): nur Stellung und Halbzug', () => {
      boot([], {});
      mark().click();
      const req = post();
      expect(req.request.body).toEqual({ fen: c.positions().fens[0], context: 'analysis', ply: 0 });
      req.flush({});
    });
  });

  it('Blickrichtung ohne Sprung kommt vom Aufrufer', () => {
    expect(c.isFlipped()).toBeFalse();
    fixture.componentRef.setInput('flipped', true);
    expect(c.isFlipped()).toBeTrue();
  });
});

describe('ClubGameSummaryComponent mit Seitenbrett', () => {
  const route = { snapshot: { paramMap: new Map([['id', '7'], ['token', 'abc']]), data: { mode: 'club-share' } } };

  function setup(pgn: string, markdown: string, mobile = false, result = '*'): ComponentFixture<ClubGameSummaryComponent> {
    TestBed.configureTestingModule({
      imports: [ClubGameSummaryComponent],
      providers: [
        ...MARK_HTTP, provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), provideNoopAnimations(),
        { provide: BreakpointObserver, useValue: { isMatched: () => mobile, observe: () => of({ matches: mobile, breakpoints: {} }) } },
        { provide: SUMMARY_ENGINE_SESSION, useValue: () => fakeEngine() as unknown as LiveEngineSession },
        { provide: ActivatedRoute, useValue: route },
        { provide: PreferencesService, useValue: prefs },
        { provide: GamesService, useValue: { sharedClubGame: () => of({ pgn, white: 'A', black: 'B', result, ownerSide: 'black' }) } },
        { provide: ClubGameSummaryService, useValue: { getShared: () => of({ status: 'done', markdown }), exercises: () => of([]) } },
      ],
    });
    const fixture = TestBed.createComponent(ClubGameSummaryComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('Knopf am Diagramm springt das Seitenbrett auf dessen Halbzug', () => {
    const fixture = setup(PGN, 'Text\n\n```diagram\nply: 3\norientation: white\n```\n');
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.to-board')!.click();
    fixture.detectChanges();
    const board = fixture.componentInstance.sideBoard()!;
    expect(board.ply()).toBe(3);
    expect(board.isFlipped()).toBeFalse();
    expect(host.querySelector('.to-ply')).not.toBeNull();
    expect(host.querySelector('.summary-page.with-board')).not.toBeNull();
  });

  it('Züge im Text sind klickbar: Partiezug springt, Variante zeigt sich, Fettschrift bleibt', () => {
    const fixture = setup(PGN, '**2.Sf3** war gut, besser wäre 2.Sc3 Sf6 3.f4 gewesen.\n\n```diagram\nply: 1\ncaption: Nach 1.e4\n```\n');
    const host = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(host.querySelectorAll<HTMLButtonElement>('button.mv'));
    expect(buttons.map(b => b.textContent)).toEqual(['2.Sf3', '2.Sc3', 'Sf6', '3.f4', '1.e4']);
    expect(buttons[0].classList).toContain('mv-b');
    const board = fixture.componentInstance.sideBoard()!;
    buttons[0].click();
    expect(board.ply()).toBe(3);
    expect(board.variation()).toBeNull();
    buttons[3].click();
    expect(board.ply()).toBe(2);
    expect(board.variation()!.line.map(m => m.san)).toEqual(['Nc3', 'Nf6', 'f4']);
    expect(board.variation()!.at).toBe(3);
    buttons[4].click();
    expect(board.ply()).toBe(1);
    expect(board.variation()).toBeNull();
  });

  it('ohne Züge kein Brett und kein Knopf', () => {
    const fixture = setup('', 'Text\n\n```diagram\nply: 0\n```\n');
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('app-summary-game-board')).toBeNull();
    expect(host.querySelector('.to-board')).toBeNull();
    expect(host.querySelector('.summary-page.with-board')).toBeNull();
  });

  /** „Die Partie“ (Wunsch 2026-11-13). */
  const MD = '# A – B\n\nKopf\n\n## Zusammenfassung\n\nEine Kurzpartie.\n\n## Schlüsselstellungen\n\n```diagram\nply: 3\n```\n';

  it('„Die Partie“ steht hinter der Zusammenfassung, mit allen Halbzügen und dem Ergebnis am Ende', () => {
    const fixture = setup('1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1-0', MD, false, '1-0');
    const host = fixture.nativeElement as HTMLElement;
    const heads = Array.from(host.querySelectorAll('.summary-text h2')).map(h => h.textContent!.trim());
    expect(heads).toEqual(['Zusammenfassung', 'games.summary.theGame', 'Schlüsselstellungen']);
    const line = host.querySelector('.game-line')!;
    expect(Array.from(line.querySelectorAll('.gm-no')).map(n => n.textContent)).toEqual(['1.', '2.', '3.']);
    expect(Array.from(line.querySelectorAll('button.gm')).map(b => b.textContent)).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5', 'a6']);
    expect(line.lastElementChild!.classList).toContain('gm-result');
    expect(line.lastElementChild!.textContent).toBe('1-0');
    // keine Zug-Knöpfe des Textes (.mv) dazugekommen
    expect(host.querySelectorAll('.game-line button.mv').length).toBe(0);
  });

  it('Klick auf einen Halbzug stellt das Seitenbrett auf die Stellung danach und hebt ihn hervor', () => {
    const fixture = setup(PGN, MD);
    const host = fixture.nativeElement as HTMLElement;
    const buttons = host.querySelectorAll<HTMLButtonElement>('button.gm');
    buttons[3].click();
    fixture.detectChanges();
    const board = fixture.componentInstance.sideBoard()!;
    expect(board.ply()).toBe(4);
    expect(board.currentFen()).toBe(board.positions().fens[4]);
    expect(host.querySelector('button.gm.gm-on')!.textContent).toBe('Nc6');
  });

  it('ohne Ergebnis (*) kein Ergebnis am Ende; leeres oder kaputtes PGN → kein Block', () => {
    expect((setup(PGN, MD).nativeElement as HTMLElement).querySelector('.gm-result')).toBeNull();
    TestBed.resetTestingModule();
    expect((setup('', MD).nativeElement as HTMLElement).querySelector('.game-moves')).toBeNull();
    TestBed.resetTestingModule();
    expect((setup('1. e4 e5 2. Ke9', MD).nativeElement as HTMLElement).querySelector('.game-moves')).toBeNull();
  });

  it('„+“ am Seitenbrett: über den LeagueHub-Teilen-Link mit der Vereinspartie als Herkunft, ohne Anmeldung', () => {
    const fixture = setup(PGN, MD);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/positions/marked').flush([]);
    const host = fixture.nativeElement as HTMLElement;
    host.querySelectorAll<HTMLButtonElement>('button.gm')[1].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.sideBoard()!.markSource()).toEqual({ clubGameId: 7 });
    host.querySelector<HTMLButtonElement>('app-summary-game-board .nav-mark button.mark')!.click();
    const req = http.expectOne(r => r.method === 'POST' && r.url === '/api/positions/marked');
    expect(req.request.body).toEqual({ fen: fixture.componentInstance.sideBoard()!.positions().fens[2], clubGameId: 7, context: 'analysis', ply: 2 });
    req.flush({});
    TestBed.inject(MatDialog).closeAll();
  });

  describe('am Handy (unter 900 px)', () => {
    afterEach(() => TestBed.inject(MatDialog).closeAll());
    /** Das Blatt hängt an der Anwendung, nicht am Fixture. */
    const tick = () => TestBed.inject(ApplicationRef).tick();

    it('Zug-Klick öffnet das Brett als Blatt statt zu scrollen; Seitenbrett und Leseposition bleiben', () => {
      spyOn(history, 'pushState');
      const fixture = setup(PGN, '**2.Sf3** war gut.\n\n' + MD, true);
      const host = fixture.nativeElement as HTMLElement;
      const board = fixture.componentInstance.sideBoard()!;
      const scroll = spyOn(board['host'].nativeElement, 'scrollIntoView');
      host.querySelector<HTMLButtonElement>('button.mv')!.click();
      fixture.detectChanges();
      tick();
      const sheet = document.querySelector('app-summary-board-sheet')!;
      expect(sheet).not.toBeNull();
      expect(history.pushState).toHaveBeenCalled();
      expect(scroll).not.toHaveBeenCalled();
      expect(board.ply()).toBe(0);
      // im Blatt: dasselbe Brett mit Zugliste, auf 2.Nf3 (Halbzug 3), Engine an
      const inner = sheet.querySelector('app-summary-game-board')!;
      expect(inner.classList).toContain('in-sheet');
      expect(inner.querySelector('app-move-list')).not.toBeNull();
      expect(inner.querySelector('.move.active')!.textContent).toContain('f3');
      expect(inner.querySelector('.engine')).not.toBeNull();
    });

    it('Klick in „Die Partie“ und am Diagramm öffnen ebenfalls das Blatt; Schließen stellt nichts um', async () => {
      spyOn(history, 'pushState');
      const fixture = setup(PGN, MD, true);
      const host = fixture.nativeElement as HTMLElement;
      const board = fixture.componentInstance.sideBoard()!;
      host.querySelectorAll<HTMLButtonElement>('button.gm')[1].click();
      fixture.detectChanges();
      tick();
      expect(document.querySelector('app-summary-board-sheet .move.active')!.textContent).toContain('e5');
      document.querySelector<HTMLButtonElement>('app-summary-board-sheet .sheet-close')!.click();
      fixture.detectChanges();
      await fixture.whenStable();
      expect(document.querySelector('app-summary-board-sheet')).toBeNull();
      expect(board.ply()).toBe(0);
      expect(board.variation()).toBeNull();
      host.querySelector<HTMLButtonElement>('.to-board')!.click();
      fixture.detectChanges();
      expect(document.querySelector('app-summary-board-sheet')).not.toBeNull();
      expect(board.ply()).toBe(0);
    });

    it('„+“ im Blatt: gleiche Herkunft wie am Seitenbrett, markiert die Stellung des Blatts', () => {
      spyOn(history, 'pushState');
      const fixture = setup(PGN, MD, true);
      const http = TestBed.inject(HttpTestingController);
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button.gm')[3].click();
      fixture.detectChanges();
      tick();
      http.expectOne('/api/positions/marked').flush([]);
      document.querySelector<HTMLButtonElement>('app-summary-board-sheet .nav-mark button.mark')!.click();
      const req = http.expectOne(r => r.method === 'POST' && r.url === '/api/positions/marked');
      expect(req.request.body).toEqual(jasmine.objectContaining({ clubGameId: 7, context: 'analysis', ply: 4 }));
      expect(req.request.body.fen).toBe(fixture.componentInstance.sideBoard()!.positions().fens[4]);
      req.flush({});
    });

    it('Wischen am Griff nach unten schließt das Blatt', () => {
      spyOn(history, 'pushState');
      const fixture = setup(PGN, MD, true);
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button.gm')!.click();
      fixture.detectChanges();
      const ref = TestBed.inject(MatDialog).openDialogs[0];
      const sheet = ref.componentInstance as SummaryBoardSheetComponent;
      const close = spyOn(ref, 'close').and.callThrough();
      sheet.touchStart({ touches: [{ clientY: 100 }] } as unknown as TouchEvent);
      sheet.touchEnd({ changedTouches: [{ clientY: 130 }] } as unknown as TouchEvent);
      expect(close).not.toHaveBeenCalled();
      sheet.touchStart({ touches: [{ clientY: 100 }] } as unknown as TouchEvent);
      sheet.touchEnd({ changedTouches: [{ clientY: 260 }] } as unknown as TouchEvent);
      expect(close).toHaveBeenCalled();
    });
  });
});
