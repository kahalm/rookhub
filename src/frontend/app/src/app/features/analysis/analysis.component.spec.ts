import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltip } from '@angular/material/tooltip';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { Subject, of } from 'rxjs';
import { AnalysisComponent, DEPTH_OPTIONS } from './analysis.component';
import { AnalysisEngineService } from './analysis-engine.service';
import { AnalysisBoardComponent } from './analysis-board.component';
import { PositionSetupComponent } from './position-setup.component';
import { PositionRepertoiresComponent } from '../repertoire/position-repertoires.component';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { OpeningExplorerComponent } from './opening-explorer.component';
import { PositionMenuComponent } from './position-menu.component';
import { AnalysisMoveTreeComponent } from './analysis-move-tree.component';
import { ExternalEngineService } from './external-engine.service';
import { AnalysisHistoryService } from './analysis-history.service';
import { SnackbarService } from '../../core/snackbar.service';
import { AuthService } from '../../core/auth.service';
import { MaiaEngineService } from './maia/maia-engine.service';
import { MaiaSparringCardComponent } from './maia/maia-sparring-card.component';

/**
 * Fokussierter Test des Vorladens aus Query-Params (genutzt vom „Analysieren"-Button
 * der Puzzles): ?fen=…&moves=…&orientation=… → Linie ab fen aufbauen, ans Ende springen.
 */
function makeComponent(params: Record<string, string | null>, opts: {
  loggedIn?: boolean;
  engines?: { id: string; name: string; maxThreads: number; maxHash: number }[];
  locale?: string;
} = {}): any {
  const engine: any = {
    analysis$: new Subject(),
    engineFatalError$: new Subject(),   // Crash-Detection-Stream (seit 0.97.10), in ngOnInit subscribed
    remoteFallback$: new Subject(),     // External-Engine-Rückfall (seit 0.372.0)
    remoteInterrupted$: new Subject(),  // Abriss-Hinweis der Remote-Suche (seit 0.377.0)
    setMultiPv: jasmine.createSpy('setMultiPv'),
    setDepth: jasmine.createSpy('setDepth'),
    setRemoteEngine: jasmine.createSpy('setRemoteEngine'),
    analyze: jasmine.createSpy('analyze').and.returnValue(Promise.resolve()),
    stop: () => {},
    destroy: () => {},                  // in ngOnDestroy aufgerufen
  };
  const route: any = { snapshot: { queryParamMap: { get: (k: string) => params[k] ?? null } } };
  const snackBar: any = { open: () => {}, show: jasmine.createSpy('show'), warn: jasmine.createSpy('warn') };
  const router: any = { navigateByUrl: jasmine.createSpy('navigateByUrl') };
  // auth: die „Stellung in meinen Repertoires"-Karte wird nur eingeloggt gerendert.
  const auth: any = { isLoggedIn: opts.loggedIn ?? false };
  const externalEngines: any = {
    listEngines: () => new Subject(),   // Default: Liste kommt nie → bleibt bei WASM
    analyse: jasmine.createSpy('analyse'),
  };
  if (opts.engines) {
    const s = new Subject<any>();
    externalEngines.listEngines = () => { setTimeout(() => { s.next({ hasCredentials: true, tokenInvalid: false, engines: opts.engines }); s.complete(); }); return s.asObservable(); };
  }
  const cdr: any = { markForCheck: jasmine.createSpy('markForCheck'), detectChanges: () => {} };
  const translate: any = {
    instant: (k: string, p?: any) => p ? `${k}:${JSON.stringify(p)}` : k,
    currentLang: () => 'de',   // ngx-translate 18: Signal — engineChoices memoisiert darauf
  };
  // Analyse-Verlauf (0.603.0): save antwortet mit einer Kennung, die Specs zählen die Aufrufe.
  let nextId = 40;
  const history: any = {
    save: jasmine.createSpy('save').and.callFake((req: any) => of({ ...req, id: req.id ?? ++nextId, preview: '', moveCount: 0 })),
    get: jasmine.createSpy('get'),
    list: jasmine.createSpy('list').and.returnValue(of([])),
  };
  const dialog: any = { open: jasmine.createSpy('open') };
  // Maia-Sparring: chooseMove liefert ein Promise, das der Test über __maia.calls selbst auflöst/ablehnt.
  const maia: any = {
    calls: [] as { fen: string; elo: number; resolve: (uci: string | null) => void; reject: (e: unknown) => void }[],
    release: jasmine.createSpy('release'),
    statusValue: 'ready',
    prepare: jasmine.createSpy('prepare').and.returnValue(Promise.resolve(true)),
  };
  maia.status = () => maia.statusValue;
  maia.chooseMove = jasmine.createSpy('chooseMove').and.callFake((fen: string, elo: number) =>
    new Promise((resolve, reject) => maia.calls.push({ fen, elo, resolve, reject })));
  const c: any = new AnalysisComponent(engine, route, snackBar, router, auth, externalEngines, cdr, translate, opts.locale ?? 'de', history, dialog, maia);
  c.maiaDelayMs = 0;
  c.__maia = maia;
  c.__snackbar = snackBar;
  c.__history = history;
  c.__dialog = dialog;
  // Vergleichs-Engine ueber den Seam: sonst baut startCompare() den echten Service und
  // damit im Karma-Browser einen echten WASM-Worker bzw. laeuft in einen TypeError.
  c.__compareEngines = [] as any[];
  c.createCompareEngine = () => {
    // Spiegelt die oeffentliche Flaeche des Service so weit, wie die Specs sie brauchen:
    // fatalError$ und engineFatalError$ sind DASSELBE Subject (im echten Service ist das eine
    // die private Quelle des anderen), depthLimit/linesRequested werden aus den Settern
    // nachgefuehrt, und destroy() bleibt eine schlichte Funktion — die Specs legen selbst
    // einen spyOn darauf, und auf einen bestehenden Spy geht das nicht.
    const fatal = new Subject<string | null>();
    const ce: any = {
      analysis$: new Subject(),
      fatalError$: fatal,
      engineFatalError$: fatal,
      remoteFallback$: new Subject(),
      remoteInterrupted$: new Subject(),
      depthLimit: 0,
      linesRequested: 0,
      setMultiPv: jasmine.createSpy('setMultiPv').and.callFake((n: number) => { ce.linesRequested = n; }),
      setDepth: jasmine.createSpy('setDepth').and.callFake((d: number) => { ce.depthLimit = d; }),
      setRemoteEngine: jasmine.createSpy('setRemoteEngine'),
      analyze: jasmine.createSpy('analyze').and.returnValue(Promise.resolve()),
      stop: () => {},
      destroy: () => {},
    };
    c.__compareEngines.push(ce);
    return ce;
  };
  c.__cdr = cdr;
  c.__engine = engine;
  c.__externalEngines = externalEngines;
  return c;
}

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('AnalysisComponent query-param preload', () => {
  it('builds the line from fen + UCI moves and lands at the last ply', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e7e5,g1f3', orientation: 'black' });
    c.ngOnInit();

    expect(c.startFen).toBe(START);
    expect(c.orientation).toBe('black');
    expect(c.line.length).toBe(3);
    expect(c.line.map((n: any) => n.san)).toEqual(['e4', 'e5', 'Nf3']);
    expect(c.ply).toBe(3);                      // aktuelle (= letzte) Stellung
    expect(c.currentFen).toBe(c.line[2].fen);
    c.ngOnDestroy();
  });

  it('?pgn= (Sprung aus LeagueHub, 0.592.0): lädt die Partie und lässt das PGN im Feld stehen', () => {
    const pgn = '[White "Schwaz"]\n[Black "Hengl, Philip"]\n[Result "1-0"]\n\n1. e4 e5 2. Nf3 Nc6 1-0\n';
    const c = makeComponent({ pgn });
    c.ngOnInit();
    expect(c.line.map((n: any) => n.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6']);
    expect(c.pgnInput).toBe(pgn);                // zum Kopieren/Weiterbearbeiten
    c.ngOnDestroy();
  });

  // W3 F4-002: eine Stellungspartie (FEN-Kopf) aus „Partien" — früher wurde ab der Grundstellung nachgespielt, das warf
  // mitten in ngOnInit, und das Brett blieb ohne legale Züge. Seit dem Zugbaum (0.604.0) liest parsePgnTree den Kopf.
  it('?pgn= mit FEN-Kopf: die Züge laufen ab der FEN, das Brett ist spielbar', () => {
    const fen = '4k3/8/8/8/8/8/4P3/4K3 w - - 0 1';
    const c = makeComponent({ pgn: `[SetUp "1"]\n[FEN "${fen}"]\n\n1. Kd2 Kd7 2. e4 *` });
    c.ngOnInit();
    expect(c.startFen).toBe(fen);
    expect(c.line.map((n: any) => n.san)).toEqual(['Kd2', 'Kd7', 'e4']);
    expect(c.dests.size).toBeGreaterThan(0);
    c.ngOnDestroy();
  });

  it('von Hand geladen leert sich das PGN-Feld wie bisher', () => {
    const c = makeComponent({});
    c.ngOnInit();
    c.pgnInput = '1. d4 d5 *';
    c.loadPgn();
    expect(c.line.length).toBe(2);
    expect(c.pgnInput).toBe('');
    c.ngOnDestroy();
  });

  it('accepts space-separated moves too', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4 e7e5' });
    c.ngOnInit();
    expect(c.line.length).toBe(2);
    expect(c.ply).toBe(2);
    c.ngOnDestroy();
  });

  it('stops at the first illegal move (robust gegen kaputte Param)', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e2e4' });   // 2. Zug illegal
    c.ngOnInit();
    expect(c.line.length).toBe(1);
    c.ngOnDestroy();
  });

  it('without moves it just starts at the given fen (ply 0)', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    expect(c.line.length).toBe(0);
    expect(c.ply).toBe(0);
    c.ngOnDestroy();
  });
});

// Verhalten hinter den mobilen Tap-Zonen (links = prev, rechts = next): goTo clampt
// an beiden Grenzen, daher ist Tippen am Anfang/Ende ein No-op statt eines Fehlers.
describe('AnalysisComponent prev/next navigation (Tap-Zonen)', () => {
  it('prev/next bewegen sich durch die Linie und clampen an den Grenzen', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e7e5,g1f3' });
    c.ngOnInit();
    expect(c.ply).toBe(3);

    c.next();                 // bereits am Ende → bleibt
    expect(c.ply).toBe(3);

    c.prev();
    expect(c.ply).toBe(2);
    c.goTo(0);                // an den Anfang
    expect(c.ply).toBe(0);

    c.prev();                 // am Anfang → bleibt
    expect(c.ply).toBe(0);
    c.next();
    expect(c.ply).toBe(1);
    c.ngOnDestroy();
  });
});

describe('AnalysisComponent back-to-puzzle + depth', () => {
  it('reads the from param and navigates back to it', () => {
    const c = makeComponent({ fen: START, from: '/puzzles/123' });
    c.ngOnInit();
    expect(c.returnTo).toBe('/puzzles/123');
    c.backToPuzzle();
    expect((c as any).router.navigateByUrl).toHaveBeenCalledWith('/puzzles/123');
    c.ngOnDestroy();
  });

  it('ignores an unsafe from param (no back button)', () => {
    const c = makeComponent({ fen: START, from: 'https://evil.example/x' });
    c.ngOnInit();
    expect(c.returnTo).toBeNull();
    c.backToPuzzle();
    expect((c as any).router.navigateByUrl).not.toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('applies the configured max depth to the engine on init', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    expect((c as any).engine.setDepth).toHaveBeenCalledWith(c.depthSetting);
    c.ngOnDestroy();
  });

  it('onDepthChange re-applies depth to the engine', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.engineOn = true;
    c.depthSetting = 30;
    c.onDepthChange();
    expect((c as any).engine.setDepth).toHaveBeenCalledWith(30);
    c.ngOnDestroy();
  });
});

describe('AnalysisComponent external engine picker', () => {
  const ENGINES = [{ id: 'eei_a', name: 'SF Heim-PC', maxThreads: 8, maxHash: 512 }];
  const PROVIDER_KEY = 'rookhub_analysis_engine_provider';

  afterEach(() => { try { localStorage.removeItem(PROVIDER_KEY); } catch {} });

  it('does not query engines when logged out', () => {
    const c = makeComponent({ fen: START }, { loggedIn: false });
    spyOn(c.__externalEngines, 'listEngines').and.callThrough();
    c.ngOnInit();
    expect(c.__externalEngines.listEngines).not.toHaveBeenCalled();
    expect(c.externalEnginesList.length).toBe(0);
    c.ngOnDestroy();
  });

  it('fills the picker from the engine list when logged in', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    expect(c.externalEnginesList.length).toBe(1);
    expect(c.selectedEngineId).toBe('wasm');          // ohne gespeicherte Wahl bleibt es lokal
    c.ngOnDestroy();
  });

  it('restores the stored engine choice and wires it into the service', async () => {
    localStorage.setItem(PROVIDER_KEY, 'eei_a');
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    expect(c.selectedEngineId).toBe('eei_a');
    expect(c.__engine.setRemoteEngine).toHaveBeenCalledWith(ENGINES[0], jasmine.any(Function));
    c.ngOnDestroy();
  });

  it('ignores a stored engine that no longer exists', async () => {
    localStorage.setItem(PROVIDER_KEY, 'eei_gone');
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    expect(c.selectedEngineId).toBe('wasm');
    expect(c.__engine.setRemoteEngine).not.toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('onEngineSelect persists the choice and switches the service back to WASM', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));

    c.selectedEngineId = 'eei_a';
    c.onEngineSelect();
    expect(localStorage.getItem(PROVIDER_KEY)).toBe('eei_a');
    expect(c.__engine.setRemoteEngine).toHaveBeenCalledWith(ENGINES[0], jasmine.any(Function));

    c.selectedEngineId = 'wasm';
    c.onEngineSelect();
    expect(localStorage.getItem(PROVIDER_KEY)).toBe('wasm');
    expect(c.__engine.setRemoteEngine).toHaveBeenCalledWith(null, jasmine.any(Function));
    c.ngOnDestroy();
  });
});

// Angular-22-Falle: eine unmarkierte View rendert nach async/HTTP nicht neu. Die Engine-Zeilen
// kommen bei der externen Engine AUSSCHLIESSLICH aus einem HTTP-Stream — ohne markForCheck bliebe
// die Anzeige stehen, obwohl der Zustand stimmt. Dieser Test hält die Marke fest.
describe('AnalysisComponent change-detection marks', () => {
  it('marks the view when engine lines arrive', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.engineOn = true;
    c.__cdr.markForCheck.calls.reset();

    (c as any).onEngineUpdate(c.currentFen, 12, [
      { multipv: 1, depth: 12, scoreType: 'cp', score: 30, evalText: '+0.30', pvUci: ['e2e4'] },
    ]);

    expect(c.__cdr.markForCheck).toHaveBeenCalled();
    expect(c.displayLines.length).toBe(1);
    c.ngOnDestroy();
  });

  it('marks the view when the remote engine falls back to WASM', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.__cdr.markForCheck.calls.reset();

    c.__engine.remoteFallback$.next(true);

    expect(c.remoteFallback).toBeTrue();
    expect(c.__cdr.markForCheck).toHaveBeenCalled();
    c.ngOnDestroy();
  });
});

// Das (i) neben der Engine-Auswahl: nennt die Rechengeschwindigkeit der laufenden Analyse.
describe('AnalysisComponent speed hint', () => {
  it('says „measuring" until the engine reported a speed', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    expect(c.speedHint).toBe('analysis.speedWaiting');
    c.ngOnDestroy();
  });

  it('always uses kN — never switches to MN/s or N/s — with thousands separators and no decimals', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.engineOn = true;

    (c as any).onEngineUpdate(c.currentFen, 20, [], 8234567, 3450000);
    expect(c.speedHint).toContain('3.450 kN/s');     // NICHT „3,5 MN/s"
    expect(c.speedHint).toContain('8.235 kN');
    expect(c.speedHint).not.toContain('MN/s');

    (c as any).onEngineUpdate(c.currentFen, 20, [], 90000, 45000);
    expect(c.speedHint).toContain('45 kN/s');        // kleine Werte bleiben ebenfalls kN
    expect(c.speedHint).not.toContain('N/s'.replace('N/s', 'XX'));
    c.ngOnDestroy();
  });

  it('never throws, even with an unusable locale (getter runs during change detection)', () => {
    const c = makeComponent({ fen: START }, { locale: 'nicht-echt' });
    c.ngOnInit();
    c.engineOn = true;
    (c as any).onEngineUpdate(c.currentFen, 20, [], 1000, 2000);
    expect(() => c.speedHint).not.toThrow();
    c.ngOnDestroy();
  });

  it('reverts to „measuring" after switching position (no stale speed)', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.engineOn = true;
    (c as any).onEngineUpdate(c.currentFen, 20, [], 5000, 12345);
    expect(c.speedHint).not.toBe('analysis.speedWaiting');

    (c as any).onEngineUpdate(c.currentFen, 0, [], 0, 0);
    expect(c.speedHint).toBe('analysis.speedWaiting');
    c.ngOnDestroy();
  });
});

// Gemeldet aus Prod: die Analyse einer Mattstellung zeigte dauerhaft „Berechne…", obwohl bei
// Matt nichts zu rechnen ist (der Engine wird bewusst kein `go` geschickt). Statt zu schweigen
// muss die Karte das ERGEBNIS benennen. Stellung + Züge stammen aus der Meldung.
describe('AnalysisComponent terminal positions', () => {
  const MATE_FEN = '8/5Qpk/3Bp3/4P2p/8/7P/5PP1/r2r1nK1 b - - 0 1';
  const MATE_MOVES = 'f1g3,g1h2,h5h4,f2g3,d1h1';   // ...Rh1# → Weiß ist matt

  it('names the result instead of pretending to calculate (mate at the end of the line)', () => {
    const c = makeComponent({ fen: MATE_FEN, moves: MATE_MOVES });
    c.ngOnInit();

    expect(c.line.map((n: any) => n.san).join(' ')).toBe('Ng3+ Kh2 h4 fxg3 Rh1#');
    expect(c.terminal).toBe('mate-black-wins');
    expect(c.terminalText).toBe('analysis.mateBlackWins');
    expect(c.evalText).toBe('0-1');
    expect(c.whiteHeight).toBe(0);
    // Kein `go` an die Engine — es gibt keinen legalen Zug.
    expect((c as any).engine.analyze).not.toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('clears the terminal state when stepping back into a playable position', () => {
    const c = makeComponent({ fen: MATE_FEN, moves: MATE_MOVES });
    c.ngOnInit();
    expect(c.terminal).toBe('mate-black-wins');

    c.prev();                       // einen Halbzug zurück → wieder spielbar
    expect(c.terminal).toBeNull();
    expect(c.terminalText).toBe('');
    expect((c as any).engine.analyze).toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('recognises stalemate as a draw', () => {
    const c = makeComponent({ fen: '7k/5Q2/6K1/8/8/8/8/8 b - - 0 1' });   // Schwarz patt
    c.ngOnInit();
    expect(c.terminal).toBe('stalemate');
    expect(c.evalText).toBe('½-½');
    expect(c.whiteHeight).toBe(50);
    c.ngOnDestroy();
  });

  it('says nothing about a position that is merely check (engine keeps running)', () => {
    const c = makeComponent({ fen: MATE_FEN, moves: 'f1g3' });   // Ng3+ ist Schach, kein Matt
    c.ngOnInit();
    expect(c.terminal).toBeNull();
    expect((c as any).engine.analyze).toHaveBeenCalled();
    c.ngOnDestroy();
  });
});

// Zwei Grenzen, eine Kette: das Auswahlfeld bietet Tiefen an, der Service klemmt sie. Weichen
// sie auseinander, wählt man 50 und bekommt stillschweigend 40 — genau so war es vor 0.374.0.
describe('AnalysisComponent depth options', () => {
  it('offers every depth up to 50', () => {
    expect(Math.max(...DEPTH_OPTIONS)).toBe(50);
  });

  it('every offered depth survives the engine clamp unchanged', () => {
    const engine = new AnalysisEngineService();     // echter Service, kein Worker nötig
    for (const d of DEPTH_OPTIONS) {
      engine.setDepth(d);
      expect(engine.depthLimit).withContext(`Tiefe ${d} wurde gekappt`).toBe(d);
    }
  });
});

// Vergleichsmodus: eine ZWEITE Engine-Instanz rechnet dieselbe Stellung. Der heikle Teil ist
// nicht die Anzeige, sondern das Aufräumen — eine vergessene Instanz behielte ihren WASM-Worker
// und ihren laufenden Analyse-Strom, unsichtbar und dauerhaft.
describe('AnalysisComponent compare mode', () => {
  const ENGINES = [
    { id: 'eei_a', name: 'RookHub Server', maxThreads: 8, maxHash: 512 },
    { id: 'eei_b', name: 'RookHub PC', maxThreads: 30, maxHash: 4096 },
  ];
  const KEYS = ['rookhub_analysis_compare', 'rookhub_analysis_compare_engine', 'rookhub_analysis_engine_provider'];

  afterEach(() => { for (const k of KEYS) { try { localStorage.removeItem(k); } catch {} } });

  it('is off by default and creates no second engine', () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    expect(c.compareOn).toBeFalse();
    expect((c as any).compareEngine).toBeUndefined();
    c.ngOnDestroy();
  });

  it('creates a second engine when switched on and tears it down when switched off', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));

    c.compareOn = true;
    c.onCompareToggle();
    const second = (c as any).compareEngine;
    expect(second).toBeDefined();
    expect(second).not.toBe((c as any).engine);      // eigene Instanz, nicht der Singleton
    spyOn(second, 'destroy').and.callThrough();

    c.compareOn = false;
    c.onCompareToggle();
    expect(second.destroy).toHaveBeenCalled();
    expect((c as any).compareEngine).toBeUndefined();
    expect(c.compareLines.length).toBe(0);
    c.ngOnDestroy();
  });

  it('destroys the second engine on ngOnDestroy (no orphaned worker)', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    c.compareOn = true;
    c.onCompareToggle();
    const second = (c as any).compareEngine;
    spyOn(second, 'destroy').and.callThrough();

    c.ngOnDestroy();
    expect(second.destroy).toHaveBeenCalled();
  });

  it('never compares an engine with itself', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));

    c.selectedEngineId = 'eei_a';
    c.compareEngineId = 'eei_a';                     // dieselbe wie die Haupt-Engine
    c.compareOn = true;
    c.onCompareToggle();

    expect(c.compareEngineId).not.toBe('eei_a');     // wurde auf eine andere umgestellt
    c.ngOnDestroy();
  });

  it('labels both sides so it is clear which lines belong to which engine', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    c.selectedEngineId = 'eei_a';
    c.compareEngineId = 'eei_b';

    expect(c.mainEngineName).toBe('RookHub Server');
    expect(c.compareEngineName).toBe('RookHub PC');
    c.ngOnDestroy();
  });

  it('drops compare results that belong to a position already left', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    c.compareOn = true;
    c.onCompareToggle();

    (c as any).onCompareUpdate('8/8/8/8/8/8/8/K6k w - - 0 1', 20, [
      { multipv: 1, depth: 20, scoreType: 'cp', score: 50, evalText: '+0.50', pvUci: ['a1b1'] },
    ], 1000);

    expect(c.compareLines.length).toBe(0);           // fremde Stellung → verworfen
    c.ngOnDestroy();
  });

  it('passes depth and line count on to the second engine', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    c.compareOn = true;
    c.onCompareToggle();
    const second = (c as any).compareEngine;

    c.depthSetting = 35;
    c.onDepthChange();
    expect(second.depthLimit).toBe(35);

    c.linesCount = 4;
    c.onLinesChange();
    expect(second.linesRequested).toBe(4);
    c.ngOnDestroy();
  });
});

// Ein Vergleich mit falschem Etikett ist schlimmer als gar keiner: Fällt eine Seite auf die
// Browser-Engine zurück, MUSS die Beschriftung das sagen — sonst steht „RookHub PC" über
// Zahlen, die der Browser gerechnet hat.
describe('AnalysisComponent compare mode labelling on fallback', () => {
  // MUSS sein: onCompareToggle() schreibt rookhub_analysis_compare='1' nach localStorage.
  // Jasmine mischt die Spec-Reihenfolge per Default — ohne Aufraeumung sieht ein spaeter
  // laufendes „is off by default" den Rest und wird je nach Seed rot.
  const KEYS = ['rookhub_analysis_compare', 'rookhub_analysis_compare_engine',
                'rookhub_analysis_engine_provider'];
  afterEach(() => { for (const k of KEYS) { try { localStorage.removeItem(k); } catch {} } });

  const ENGINES = [
    { id: 'eei_a', name: 'RookHub Server', maxThreads: 8, maxHash: 512 },
    { id: 'eei_b', name: 'RookHub PC', maxThreads: 30, maxHash: 4096 },
  ];

  it('names the browser engine once the compare engine has fallen back', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    c.selectedEngineId = 'eei_a';
    c.compareEngineId = 'eei_b';
    c.compareOn = true;
    c.onCompareToggle();

    expect(c.compareEngineName).toBe('RookHub PC');
    (c as any).compareFallback = true;
    expect(c.compareEngineName).toBe('analysis.engineBrowser');
    c.ngOnDestroy();
  });

  it('does the same for the main engine', () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    c.externalEnginesList = ENGINES;
    c.selectedEngineId = 'eei_a';
    expect(c.mainEngineName).toBe('RookHub Server');

    c.remoteFallback = true;
    expect(c.mainEngineName).toBe('analysis.engineBrowser');
    c.ngOnDestroy();
  });

  it('resets the fallback flag when the comparison is switched off', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    c.compareOn = true;
    c.onCompareToggle();
    (c as any).compareFallback = true;

    c.compareOn = false;
    c.onCompareToggle();
    expect(c.compareFallback).toBeFalse();
    c.ngOnDestroy();
  });
});

// Befunde aus dem adversarialen Review des Vergleichsmodus — jeder Test hält eine der
// bestätigten Lücken fest.
describe('AnalysisComponent compare mode hardening', () => {
  const ENGINES = [
    { id: 'eei_a', name: 'RookHub Server', maxThreads: 8, maxHash: 512 },
    { id: 'eei_b', name: 'RookHub PC', maxThreads: 30, maxHash: 4096 },
  ];
  const KEYS = ['rookhub_analysis_compare', 'rookhub_analysis_compare_engine', 'rookhub_analysis_engine_provider'];
  afterEach(() => { for (const k of KEYS) { try { localStorage.removeItem(k); } catch {} } });

  const MATE_FEN = '8/5Qpk/3Bp3/4P2p/8/7P/5PP1/r2r1nK1 b - - 0 1';
  const MATE_MOVES = 'f1g3,g1h2,h5h4,f2g3,d1h1';

  it('does not start a search in a terminal position when depth or line count changes', () => {
    const c = makeComponent({ fen: MATE_FEN, moves: MATE_MOVES });
    c.ngOnInit();
    expect(c.terminal).toBe('mate-black-wins');
    (c as any).engine.analyze.calls.reset();

    c.engineOn = true;
    c.depthSetting = 35;
    c.onDepthChange();
    c.linesCount = 4;
    c.onLinesChange();

    expect((c as any).engine.analyze).not.toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('keeps both engines apart when the MAIN engine is switched onto the compare engine', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    c.selectedEngineId = 'eei_a';
    c.compareEngineId = 'eei_b';
    c.compareOn = true;
    c.onCompareToggle();

    c.selectedEngineId = 'eei_b';        // Haupt-Engine wandert auf die Vergleichs-Engine
    c.onEngineSelect();

    expect(c.compareEngineId).not.toBe('eei_b');
    c.ngOnDestroy();
  });

  it('separates the two sides after a reload that stored the same engine twice', async () => {
    localStorage.setItem('rookhub_analysis_engine_provider', 'eei_a');
    localStorage.setItem('rookhub_analysis_compare', '1');
    localStorage.setItem('rookhub_analysis_compare_engine', 'eei_a');   // dieselbe wie Haupt

    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));

    expect(c.selectedEngineId).toBe('eei_a');
    expect(c.compareEngineId).not.toBe('eei_a');
    c.ngOnDestroy();
  });

  it('falls back to the browser engine when the stored compare engine no longer exists', async () => {
    localStorage.setItem('rookhub_analysis_engine_provider', 'eei_a');   // Haupt = externe Engine
    localStorage.setItem('rookhub_analysis_compare', '1');
    localStorage.setItem('rookhub_analysis_compare_engine', 'eei_weg');  // abgemeldet

    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));

    expect(c.compareEngineId).toBe('wasm');
    expect(c.compareEngineName).toBe('analysis.engineBrowser');   // nie ein leeres Etikett
    c.ngOnDestroy();
  });

  it('picks a valid engine when the stored one is gone AND the browser slot is taken', async () => {
    // Haupt-Engine ist der Browser, die gespeicherte Vergleichs-Engine existiert nicht mehr:
    // „Browser" wäre dann ein Selbstvergleich, es muss also eine echte Engine gewählt werden.
    localStorage.setItem('rookhub_analysis_compare', '1');
    localStorage.setItem('rookhub_analysis_compare_engine', 'eei_weg');

    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));

    expect(c.selectedEngineId).toBe('wasm');
    expect(['eei_a', 'eei_b']).toContain(c.compareEngineId);
    expect(c.compareEngineName).not.toBe('');
    c.ngOnDestroy();
  });

  it('shows no comparison block when the switch is on but no second engine exists', () => {
    localStorage.setItem('rookhub_analysis_compare', '1');
    const c = makeComponent({ fen: START }, { loggedIn: false });   // keine Engine-Liste
    c.ngOnInit();

    expect(c.compareOn).toBeTrue();
    expect(c.compareRunning).toBeFalse();     // Template zeigt daran nichts an
    c.ngOnDestroy();
  });

  it('reports a crash of the compare engine instead of showing „calculating" forever', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    c.compareOn = true;
    c.onCompareToggle();
    expect(c.compareCrashed).toBeFalse();

    (c as any).compareEngine['fatalError$'].next('crash');
    expect(c.compareCrashed).toBeTrue();

    c.compareOn = false;
    c.onCompareToggle();
    expect(c.compareCrashed).toBeFalse();     // beim Abschalten zurückgesetzt
    c.ngOnDestroy();
  });
});

// Regressionsnetz fuer die Invarianten, die die Codereview als „von nichts erzwungen"
// beanstandet hat. Jede dieser Specs faellt ohne den zugehoerigen Fix um.
describe('AnalysisComponent compare mode invariants', () => {
  const KEYS = ['rookhub_analysis_compare', 'rookhub_analysis_compare_engine',
                'rookhub_analysis_engine_provider'];
  afterEach(() => { for (const k of KEYS) { try { localStorage.removeItem(k); } catch {} } });

  const ENGINES = [
    { id: 'eei_a', name: 'RookHub Server', maxThreads: 8, maxHash: 512 },
    { id: 'eei_b', name: 'RookHub PC', maxThreads: 30, maxHash: 4096 },
  ];

  it('refuses to compare the browser engine with itself when no second engine exists', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: [] });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));

    c.compareOn = true;
    c.onCompareToggle();

    // Ohne den Fix baute startCompare() hier eine zweite WASM-Instanz: zwei 7-MB-Kerne auf
    // demselben Prozessorkern, fuer zwei garantiert identische Linienlisten.
    expect((c as any).compareEngine).toBeUndefined();
    expect((c as any).__compareEngines.length).toBe(0);
    expect(c.compareOn).toBeFalse();                       // ehrlich abgeschaltet
    expect(localStorage.getItem('rookhub_analysis_compare')).toBe('0');
    c.ngOnDestroy();
  });

  it('builds the compare engine only once when restoring a colliding selection', async () => {
    localStorage.setItem('rookhub_analysis_engine_provider', 'eei_a');
    localStorage.setItem('rookhub_analysis_compare', '1');
    localStorage.setItem('rookhub_analysis_compare_engine', 'eei_a');   // dieselbe wie Haupt
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));

    // Vorher lief startCompare() zweimal: applyEngineSelection() baute eine Instanz, die
    // naechste Zeile in ngOnInit zerstoerte sie sofort wieder und baute eine zweite.
    expect((c as any).__compareEngines.length).toBe(1);
    expect((c as any).compareEngine).toBeDefined();
    c.ngOnDestroy();
  });

  it('clears the crash flag when the position changes', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    c.compareOn = true;
    c.onCompareToggle();

    (c as any).compareEngine['fatalError$'].next('crash');
    expect(c.compareCrashed).toBeTrue();

    (c as any).refresh();
    // Der Service setzt bei neuer FEN sein Crash-Budget selbst zurueck - die Karte darf dann
    // nicht weiter behaupten, die Engine sei abgestuerzt.
    expect(c.compareCrashed).toBeFalse();
    c.ngOnDestroy();
  });

  it('routes compare-engine telemetry into the same sink as the main engine', async () => {
    const c = makeComponent({ fen: START }, { loggedIn: true, engines: ENGINES });
    c.ngOnInit();
    await new Promise(r => setTimeout(r));
    const sink = jasmine.createSpy('reportEngineEvent');
    (c as any).engine.reportEngineEvent = sink;

    c.compareOn = true;
    c.onCompareToggle();
    (c as any).compareEngine.reportEngineEvent('crash', 'boom');

    // Von Hand gebaute Instanz: ohne Verdrahtung waeren ihre Abstuerze die einzigen,
    // die nirgends im Log auftauchen - ausgerechnet die mit dem hoechsten Speicherdruck.
    expect(sink).toHaveBeenCalledWith('compare_crash', 'boom');
    c.ngOnDestroy();
  });
});

// Suchtimer neben „Tiefe x/y": läuft ab Suchstart einer Stellung, über eine Fortsetzung nach
// Abriss hinweg (dieselbe Stellung, weiter „läuft"), und friert beim Ende der Suche ein.
describe('AnalysisComponent Suchtimer', () => {
  it('formatElapsed rendert m:ss', () => {
    expect(AnalysisComponent.formatElapsed(5)).toBe('0:05');
    expect(AnalysisComponent.formatElapsed(65)).toBe('1:05');
    expect(AnalysisComponent.formatElapsed(720)).toBe('12:00');
  });

  it('läuft ab Suchstart, über weitere Zeilen hinweg, und friert beim Ende ein', () => {
    const c = makeComponent({ fen: START });
    let now = 100_000;
    c.nowFn = () => now;
    c.ngOnInit();
    const st = (running: boolean, depth = 0) =>
      c.__engine.analysis$.next({ fen: c.currentFen, depth, lines: [], running, nodes: 0, nps: 0 });

    st(true);
    now += 30_000; c.updateClocks();
    expect(c.searchTime).toBe('0:30');

    st(true, 27);                       // neue Zeile derselben Suche (auch nach Fortsetzung) → läuft weiter
    now += 45_000; c.updateClocks();
    expect(c.searchTime).toBe('1:15');

    st(false, 27);                      // Suche beendet → eingefroren
    now += 60_000; c.updateClocks();
    expect(c.searchTime).toBe('1:15');

    st(true, 0);                        // neue Suche (z. B. nach Zug) → Timer beginnt von vorn
    now += 3_000; c.updateClocks();
    expect(c.searchTime).toBe('0:03');
    c.ngOnDestroy();
  });
});

// Bewertungsleiste: Jede neue Suche beginnt mit einem Zwischenstand OHNE Linien. Früher sprang die
// Leiste darauf bei jedem Zug auf 0.00 und erst mit der ersten Zeile (externe Engine: Sekunden
// später) wieder zurück — sichtbar unruhig. Sie hält jetzt den letzten Wert, bis die neue Suche
// etwas Belastbares liefert.
describe('AnalysisComponent eval bar holds its value between searches', () => {
  const line = (depth: number, score: number, scoreType: 'cp' | 'mate' = 'cp') =>
    ({ multipv: 1, depth, scoreType, score, evalText: `ev${score}@${depth}`, pvUci: ['e2e4'] });

  it('keeps the previous eval while the next position has no engine line yet', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e7e5' });
    c.ngOnInit();
    c.prev();
    (c as any).onEngineUpdate(c.currentFen, 20, [line(20, 80)]);
    const held = c.whiteHeight;
    expect(held).toBeGreaterThan(50);

    c.next();                                            // Zug → neue Suche
    c.running = true;
    (c as any).onEngineUpdate(c.currentFen, 0, []);      // gestartet, noch keine Zeile
    expect(c.whiteHeight).toBe(held);
    expect(c.evalText).toBe('ev80@20');
    c.ngOnDestroy();
  });

  it('ignores shallow evals of a running search and takes over once it is deep enough', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    (c as any).onEngineUpdate(c.currentFen, 20, [line(20, 30)]);
    c.running = true;

    (c as any).onEngineUpdate(c.currentFen, 3, [line(3, -250)]);   // Tiefe 3: Ausreißer
    expect(c.evalText).toBe('ev30@20');

    (c as any).onEngineUpdate(c.currentFen, 10, [line(10, 45)]);   // Schwelle erreicht
    expect(c.evalText).toBe('ev45@10');
    c.ngOnDestroy();
  });

  it('shows a mate immediately, even at low depth', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.running = true;
    (c as any).onEngineUpdate(c.currentFen, 4, [line(4, 2, 'mate')]);
    expect(c.whiteHeight).toBe(100);
    c.ngOnDestroy();
  });

  it('takes a shallow result once the search has ended', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.running = false;                                   // Suche beendet/gestoppt
    (c as any).onEngineUpdate(c.currentFen, 6, [line(6, -60)]);
    expect(c.evalText).toBe('ev-60@6');
    c.ngOnDestroy();
  });

  it('the settle depth stays below every selectable depth (a full search always reaches it)', () => {
    // Liegt die Schwelle über dem kleinsten Tiefenwert, übernähme die Leiste bei laufender Suche
    // nie etwas — sie erführe den Wert erst, wenn die Suche endet.
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.running = true;
    const minDepth = Math.min(...DEPTH_OPTIONS);
    (c as any).onEngineUpdate(c.currentFen, minDepth, [line(minDepth, 120)]);
    expect(c.evalText).toBe(`ev120@${minDepth}`);
    c.ngOnDestroy();
  });
});

describe('AnalysisComponent Verlauf + Sterne (0.603.0)', () => {
  beforeEach(() => jasmine.clock().install());
  afterEach(() => jasmine.clock().uninstall());

  const labels = (c: any) => c.starredList.map((n: any) => c.starLabel(n));

  it('speichert gedrosselt unter EINER Kennung — nur angemeldet, nicht die leere Grundstellung', () => {
    const c = makeComponent({}, { loggedIn: true });
    c.ngOnInit();
    jasmine.clock().tick(2000);
    expect(c.__history.save).not.toHaveBeenCalled();          // Grundstellung ohne Züge ist keine Analyse

    c.onMove({ orig: 'e2', dest: 'e4' });
    c.onMove({ orig: 'e7', dest: 'e5' });
    jasmine.clock().tick(1499);
    expect(c.__history.save).not.toHaveBeenCalled();
    jasmine.clock().tick(1);
    expect(c.__history.save).toHaveBeenCalledOnceWith({
      id: null, startFen: START, title: null, tree: { n: [{ p: -1, u: 'e2e4' }, { p: 0, u: 'e7e5' }] }, current: 1,
    });
    expect(c.historyId).toBe(41);

    c.onMove({ orig: 'g1', dest: 'f3' });
    jasmine.clock().tick(1500);
    expect(c.__history.save.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({
      id: 41, current: 2, tree: { n: [{ p: -1, u: 'e2e4' }, { p: 0, u: 'e7e5' }, { p: 1, u: 'g1f3' }] },
    }));
    c.ngOnDestroy();
  });

  it('abgemeldet wird nichts gespeichert, Sterne gehen trotzdem', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4' });
    c.ngOnInit();
    c.toggleStar();
    jasmine.clock().tick(5000);
    expect(c.__history.save).not.toHaveBeenCalled();
    expect(labels(c)).toEqual(['1.e4']);
    c.ngOnDestroy();
  });

  it('Sterne: umschalten, beschriften, springen — und ein anderer Zug wird eine Variante, die Sterne bleiben', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e7e5,g1f3' }, { loggedIn: true });
    c.ngOnInit();
    c.goTo(1); c.toggleStar();
    c.goTo(2); c.toggleStar();
    c.goTo(3); c.toggleStar(); c.toggleStar();                 // zweimal = wieder weg
    expect(labels(c)).toEqual(['1.e4', '1...e5']);
    expect(c.starLabel(c.root)).toBe('analysis.star.start');

    c.goTo(1);
    c.onMove({ orig: 'c7', dest: 'c5' });                      // ab 1.e4 ein anderer Zug → Variante, die Hauptlinie bleibt
    expect(c.line.map((n: any) => n.san)).toEqual(['e4', 'c5']);
    expect(c.root.children[0].children.map((n: any) => n.san)).toEqual(['e5', 'c5']);
    expect(labels(c)).toEqual(['1.e4', '1...e5']);
    c.goToNode(c.starredList[1]);                              // Sprung zurück in die Hauptlinie
    expect(c.line.map((n: any) => n.san)).toEqual(['e4', 'e5', 'Nf3']);
    expect(c.ply).toBe(2);
    jasmine.clock().tick(1500);
    expect(c.__history.save.calls.mostRecent().args[0].tree).toEqual({ n: [
      { p: -1, u: 'e2e4', s: true }, { p: 0, u: 'e7e5', s: true }, { p: 1, u: 'g1f3' }, { p: 0, u: 'c7c5' },
    ] });
    c.ngOnDestroy();
  });

  it('ein Eintrag aus dem Verlauf kommt mit Baum, Stand und Sternen aufs Brett und wird unter seiner Kennung weitergeführt', () => {
    const c = makeComponent({}, { loggedIn: true });
    c.ngOnInit();
    // 1.d4 d5 2.c4 (1...Nf6) — gestanden bei 1...Nf6, Sterne auf der Ausgangsstellung und 2.c4
    c.openHistoryEntry({ id: 7, startFen: START, moves: ['d2d4', 'd7d5', 'c2c4'], ply: 2, title: 'Carlsen – Nakamura',
      preview: '', moveCount: 3, nodeCount: 4, starCount: 2, current: 3, createdAt: '', updatedAt: '',
      tree: { s: true, n: [{ p: -1, u: 'd2d4' }, { p: 0, u: 'd7d5' }, { p: 1, u: 'c2c4', s: true }, { p: 0, u: 'g8f6', e: '+0.20' }] } });
    expect(c.line.map((n: any) => n.san)).toEqual(['d4', 'Nf6']);
    expect(c.ply).toBe(2);
    expect(c.currentNode.evalText).toBe('+0.20');
    expect(labels(c)).toEqual(['analysis.star.start', '2.c4']);
    jasmine.clock().tick(1500);
    expect(c.__history.save).not.toHaveBeenCalled();            // eben geladen = nichts Neues
    c.prev();
    jasmine.clock().tick(1500);
    expect(c.__history.save.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({ id: 7, current: 0, title: 'Carlsen – Nakamura' }));

    // Zurücksetzen = neue Analyse: ohne Kennung, ohne Sterne
    c.reset();
    expect(c.historyId).toBeNull();
    expect(c.starredList).toEqual([]);
    c.ngOnDestroy();
  });

  it('ein Eintrag aus der Liste (ohne Baum) wird erst geholt', () => {
    const c = makeComponent({}, { loggedIn: true });
    c.ngOnInit();
    const full = { id: 9, startFen: START, moves: ['e2e4'], ply: 1, title: null, preview: '', moveCount: 1, nodeCount: 1,
      starCount: 0, current: 0, createdAt: '', updatedAt: '', tree: { n: [{ p: -1, u: 'e2e4' }] } };
    c.__history.get.and.returnValue(of(full));
    c.openHistoryEntry({ ...full, tree: null });
    expect(c.__history.get).toHaveBeenCalledWith(9);
    expect(c.line.map((n: any) => n.san)).toEqual(['e4']);
    expect(c.historyId).toBe(9);
    c.ngOnDestroy();
  });

  it('ein geladenes PGN nimmt die Namen als Titel und die Varianten mit', () => {
    const c = makeComponent({}, { loggedIn: true });
    c.ngOnInit();
    c.pgnInput = '[White "Carlsen"]\n[Black "Nakamura"]\n\n1. e4 e5 (1... c5 2. Nf3) 2. Nf3 *';
    c.loadPgn();
    expect(c.line.map((n: any) => n.san)).toEqual(['e4', 'e5', 'Nf3']);
    expect(c.ply).toBe(3);
    jasmine.clock().tick(1500);
    expect(c.__history.save.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({
      title: 'Carlsen – Nakamura', current: 2,
      tree: { n: [{ p: -1, u: 'e2e4' }, { p: 0, u: 'e7e5' }, { p: 1, u: 'g1f3' }, { p: 0, u: 'c7c5' }, { p: 3, u: 'g1f3' }] },
    }));
    c.ngOnDestroy();
  });

  it('Verlassen der Seite schickt den letzten Stand sofort', () => {
    const c = makeComponent({}, { loggedIn: true });
    c.ngOnInit();
    c.onMove({ orig: 'e2', dest: 'e4' });
    c.ngOnDestroy();
    expect(c.__history.save).toHaveBeenCalledTimes(1);
  });
});

describe('AnalysisComponent Zugbaum (0.604.0)', () => {
  const sans = (nodes: any[]) => nodes.map((n: any) => n.san);

  function withVariation() {
    // 1.e4 e5 2.Nf3, dazu die Variante 1...c5 2.Nf3 — man steht am Ende der Variante
    const c = makeComponent({ fen: START, moves: 'e2e4,e7e5,g1f3' });
    c.ngOnInit();
    c.goTo(1);
    c.onMove({ orig: 'c7', dest: 'c5' });
    c.onMove({ orig: 'g1', dest: 'f3' });
    return c;
  }

  it('derselbe Zug geht in die vorhandene Fortsetzung, statt eine zweite anzulegen', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e7e5,g1f3' });
    c.ngOnInit();
    c.goTo(1);
    c.onMove({ orig: 'e7', dest: 'e5' });
    expect(c.root.children[0].children.length).toBe(1);
    expect(sans(c.line)).toEqual(['e4', 'e5', 'Nf3']);   // die Fortsetzung bleibt erreichbar
    expect(c.ply).toBe(2);
    c.ngOnDestroy();
  });

  it('Variante hochstufen und zur Hauptvariante machen lassen Brett und Stand stehen', () => {
    const c = withVariation();
    const here = c.currentNode;
    const v0 = c.treeVersion;
    c.onTreeAction({ kind: 'promote', node: here });
    expect(sans(c.root.children[0].children)).toEqual(['c5', 'e5']);
    expect(c.currentNode).toBe(here);
    expect(c.treeVersion).toBeGreaterThan(v0);
    c.onTreeAction({ kind: 'mainline', node: c.root.children[0].children[1] });   // 1...e5 wieder nach vorn
    expect(sans(c.root.children[0].children)).toEqual(['e5', 'c5']);
    expect(sans(c.line)).toEqual(['e4', 'c5', 'Nf3']);
    c.ngOnDestroy();
  });

  it('ab hier löschen: steht man darin, geht es beim Zug davor weiter', () => {
    const c = withVariation();
    const c5 = c.root.children[0].children[1];
    c.onTreeAction({ kind: 'delete', node: c5 });
    expect(sans(c.root.children[0].children)).toEqual(['e5']);
    expect(sans(c.line)).toEqual(['e4', 'e5', 'Nf3']);
    expect(c.ply).toBe(1);
    expect(c.currentFen).toBe(c.root.children[0].fen);

    // Steht man woanders, bleibt man dort
    c.goTo(3);
    c.onTreeAction({ kind: 'delete', node: c.root.children[0].children[0].children[0] });   // 2.Nf3 der Hauptlinie
    expect(sans(c.line)).toEqual(['e4', 'e5']);
    expect(c.ply).toBe(2);
    c.ngOnDestroy();
  });

  it('Stern über das Menü', () => {
    const c = withVariation();
    c.onTreeAction({ kind: 'star', node: c.root.children[0] });
    expect(c.starredList.map((n: any) => c.starLabel(n))).toEqual(['1.e4']);
    c.ngOnDestroy();
  });

  it('die Bewertung kommt an den Zug, sobald sie belastbar ist', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4' });
    c.ngOnInit();
    c.running = true;
    const line = (depth: number, score: number) => ({ multipv: 1, depth, scoreType: 'cp', score, evalText: (score > 0 ? '+' : '') + (score / 100).toFixed(2), pvUci: ['e7e5'] });
    (c as any).onEngineUpdate(c.currentFen, 4, [line(4, 90)]);
    expect(c.currentNode.evalText).toBeUndefined();          // flach: noch nicht
    const v0 = c.treeVersion;
    (c as any).onEngineUpdate(c.currentFen, 18, [line(18, 30)]);
    expect(c.currentNode.evalText).toBe('+0.30');
    expect(c.treeVersion).toBeGreaterThan(v0);
    c.ngOnDestroy();
  });

  it('Explorer/Repertoire-Züge aus der Mitte heraus werden eine Variante', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e7e5' });
    c.ngOnInit();
    c.goTo(1);
    c.playRepertoireMoves(['c5', 'Nf3', 'Zz9']);
    expect(sans(c.line)).toEqual(['e4', 'c5', 'Nf3']);
    expect(sans(c.root.children[0].children)).toEqual(['e5', 'c5']);
    c.ngOnDestroy();
  });
});

// Codereview UX-014: die Brett-Steuerknöpfe trugen nur matTooltip — der Text landet dort als aria-describedby, die
// Knöpfe hießen für Screenreader bloß „Schaltfläche" (axe button-name, critical, 6 Knoten). Gerendert wird die echte
// Vorlage; nur die schweren Kind-Komponenten (Brett, Explorer, Menü …) fallen weg, die Knopfreihe bleibt unverändert.
describe('AnalysisComponent Brett-Steuerknöpfe (zugänglicher Name)', () => {
  let fixture: ComponentFixture<AnalysisComponent>;

  beforeEach(async () => {
    const engine: any = {
      analysis$: new Subject(), engineFatalError$: new Subject(), remoteFallback$: new Subject(), remoteInterrupted$: new Subject(),
      setMultiPv: () => {}, setDepth: () => {}, setRemoteEngine: () => {},
      analyze: () => Promise.resolve(), stop: () => {}, destroy: () => {},
    };
    TestBed.overrideComponent(AnalysisComponent, {
      remove: { imports: [AnalysisBoardComponent, PositionSetupComponent, PositionRepertoiresComponent, HelpHintComponent,
        OpeningExplorerComponent, PositionMenuComponent, AnalysisMoveTreeComponent, MaiaSparringCardComponent] },
      add: { schemas: [NO_ERRORS_SCHEMA] },
    });
    await TestBed.configureTestingModule({
      imports: [AnalysisComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AnalysisEngineService, useValue: engine },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
        { provide: Router, useValue: { navigateByUrl: () => {} } },
        { provide: SnackbarService, useValue: { show: () => {}, warn: () => {} } },
        { provide: AuthService, useValue: { isLoggedIn: false } },
        { provide: ExternalEngineService, useValue: { listEngines: () => new Subject(), analyse: () => {} } },
        { provide: AnalysisHistoryService, useValue: { save: () => of({}), get: () => of({}), list: () => of([]) } },
        { provide: MatDialog, useValue: { open: () => {} } },
        // Kein echter Maia-Dienst: der baute beim ersten Start einen Worker samt 45-MB-Modell.
        { provide: MaiaEngineService, useValue: { chooseMove: () => Promise.resolve(null), release: () => {}, status: () => 'idle', prepare: () => Promise.resolve(false) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AnalysisComponent);
    fixture.detectChanges();   // kein whenStable: die Seite hält eigene Zeitgeber offen
  });

  afterEach(() => fixture.destroy());

  it('jeder Symbolknopf der Zugreihe hat ein aria-label mit dem Text seines Tooltips', () => {
    const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('.moves-card .controls > button'));
    // Anfang, Zurück, Vor, Ende, Stern, Brett drehen, Zurücksetzen (das ⋮-Menü ist eine eigene Komponente)
    expect(buttons.length).toBe(7);
    expect(buttons.map(b => b.getAttribute('aria-label'))).toEqual([
      'analysis.start', 'pgnViewer.nav.previous', 'pgnViewer.nav.next', 'pgnViewer.nav.last',
      'analysis.star.add', 'analysis.flip', 'analysis.reset',
    ]);
    const debugButtons = fixture.debugElement.queryAll(By.css('.moves-card .controls > button'));
    expect(debugButtons.map(d => d.injector.get(MatTooltip).message)).toEqual(buttons.map(b => b.getAttribute('aria-label')!));
  });

  it('während des Sparrings: Engine-Schalter gesperrt, „Engine pausiert" statt „Engine aus"', async () => {
    const c: any = fixture.componentInstance;
    c.sparring = { start: c.root, userColor: 'white', engineWasOn: true };
    c.engineOn = false;
    fixture.detectChanges();
    await new Promise(r => setTimeout(r));   // ngModel reicht disabled asynchron an den Schalter
    fixture.detectChanges();
    const card: HTMLElement = fixture.nativeElement.querySelector('.engine-card');
    expect(card.textContent).toContain('analysis.maia.enginePaused');
    expect(card.textContent).not.toContain('analysis.engineOff');
    expect(fixture.debugElement.query(By.directive(MatSlideToggle)).componentInstance.disabled).toBeTrue();
  });
});

describe('AnalysisComponent Sparring gegen Maia', () => {
  const flush = () => new Promise<void>(r => setTimeout(r));
  const AFTER_E4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';

  /** Komponente an der Grundstellung, Engine an, Sparring gestartet (der Nutzer spielt Weiß). */
  function sparringAtStart(params: Record<string, string | null> = { fen: START }) {
    const c = makeComponent(params);
    c.ngOnInit();
    c.engineOn = true;
    c.startSparring();
    return c;
  }

  it('Start schaltet die Engine aus, merkt den Zustand und schreibt NICHT in localStorage', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.engineOn = true;
    const stop = spyOn(c.__engine, 'stop').and.callThrough();
    const setItem = spyOn(Storage.prototype, 'setItem').and.callThrough();
    c.startSparring();
    expect(c.sparring).toEqual({ start: c.root, userColor: 'white', engineWasOn: true });
    expect(c.engineOn).toBeFalse();
    expect(c.orientation).toBe('white');
    expect(stop).toHaveBeenCalled();
    expect(c.shapes).toEqual([]);
    expect(setItem.calls.allArgs().filter(a => a[0] === 'rookhub_analysis_engine')).toEqual([]);
    c.ngOnDestroy();
  });

  it('startet nicht in einer Stellung, die zu Ende ist (Matt)', () => {
    const c = makeComponent({ fen: START, moves: 'f2f3,e7e5,g2g4,d8h4' });
    c.ngOnInit();
    c.startSparring();
    expect(c.sparring).toBeNull();
    c.ngOnDestroy();
  });

  it('eigener Zug → Maia antwortet: ihr Zug steht als Kind im Baum und auf dem Brett', async () => {
    const c = sparringAtStart();
    c.maiaElo = 1400;
    c.onMove({ orig: 'e2', dest: 'e4' });
    expect(c.maiaThinking).toBeTrue();
    expect(c.__maia.chooseMove).toHaveBeenCalledOnceWith(AFTER_E4, 1400);
    c.__maia.calls[0].resolve('e7e5');
    await flush();
    const e4 = c.root.children[0];
    expect(e4.children.map((n: any) => n.san)).toEqual(['e5']);
    expect(c.currentNode).toBe(e4.children[0]);
    expect(c.boardFen).toBe(e4.children[0].fen);
    expect(c.maiaThinking).toBeFalse();
    expect(c.maiaToMove).toBeFalse();   // jetzt ist der Nutzer wieder dran
    c.ngOnDestroy();
  });

  it('eine Antwort nach einer Navigation verfällt — Maia zieht danach nicht von selbst', async () => {
    const c = sparringAtStart();
    c.onMove({ orig: 'e2', dest: 'e4' });
    c.goTo(0);
    expect(c.maiaThinking).toBeFalse();
    c.__maia.calls[0].resolve('e7e5');
    await flush();
    expect(c.root.children[0].children).toEqual([]);
    expect(c.currentNode).toBe(c.root);
    expect(c.sparring).not.toBeNull();           // das Sparring bleibt aktiv
    expect(c.__maia.chooseMove).toHaveBeenCalledTimes(1);
    c.ngOnDestroy();
  });

  it('ein Zug für Maias Seite löst nichts aus', () => {
    // Nach 1.e4 steht Schwarz am Zug: der Nutzer spielt Schwarz, Weiß ist Maia.
    const c = makeComponent({ fen: START, moves: 'e2e4' });
    c.ngOnInit();
    c.startSparring();
    expect(c.sparring.userColor).toBe('black');
    c.goTo(0);                                    // zurück: Weiß (= Maia) am Zug, aber kein automatischer Zug
    expect(c.__maia.chooseMove).not.toHaveBeenCalled();
    c.onMove({ orig: 'd2', dest: 'd4' });         // der Nutzer zieht für Maia
    expect(c.__maia.chooseMove).not.toHaveBeenCalled();
    expect(c.maiaThinking).toBeFalse();
    c.ngOnDestroy();
  });

  it('„Maia zieht" fordert in Maias Zugrecht einen Zug an, sonst nicht', async () => {
    const c = makeComponent({ fen: START, moves: 'e2e4' });
    c.ngOnInit();
    c.startSparring();                            // Nutzer = Schwarz
    c.requestMaiaMove();                          // Schwarz am Zug → nichts
    expect(c.__maia.chooseMove).not.toHaveBeenCalled();
    c.goTo(0);
    expect(c.maiaToMove).toBeTrue();
    c.requestMaiaMove();
    expect(c.__maia.chooseMove).toHaveBeenCalledOnceWith(START, c.maiaElo);
    c.__maia.calls[0].resolve('d2d4');
    await flush();
    expect(c.root.children.map((n: any) => n.san)).toEqual(['e4', 'd4']);   // als Variante
    expect(c.currentNode.san).toBe('d4');
    c.ngOnDestroy();
  });

  it('Seite wechseln in Maias Zugrecht fordert sofort einen Zug', async () => {
    const c = sparringAtStart();
    c.switchSparringSides();
    expect(c.sparring.userColor).toBe('black');
    expect(c.orientation).toBe('black');
    expect(c.__maia.chooseMove).toHaveBeenCalledOnceWith(START, c.maiaElo);
    c.__maia.calls[0].resolve('e2e4');
    await flush();
    expect(c.currentNode.san).toBe('e4');
    c.ngOnDestroy();
  });

  it('„Nochmal" springt zur Ausgangsstellung — und ist dort Maia am Zug, zieht sie', async () => {
    const c = sparringAtStart();
    c.onMove({ orig: 'e2', dest: 'e4' });
    c.__maia.calls[0].resolve('e7e5');
    await flush();
    c.restartSparring();
    expect(c.currentNode).toBe(c.root);
    expect(c.__maia.chooseMove).toHaveBeenCalledTimes(1);   // Weiß = Nutzer am Zug

    c.switchSparringSides();                                 // jetzt Maia = Weiß → sofort ein Zug
    c.__maia.calls[1].resolve('d2d4');
    await flush();
    expect(c.currentNode.san).toBe('d4');
    c.restartSparring();
    expect(c.currentNode).toBe(c.root);
    expect(c.__maia.chooseMove).toHaveBeenCalledTimes(3);
    c.ngOnDestroy();
  });

  it('Beenden stellt engineOn wieder her (an bleibt an, aus bleibt aus)', () => {
    const c = sparringAtStart();
    c.stopSparring();
    expect(c.sparring).toBeNull();
    expect(c.engineOn).toBeTrue();
    expect(c.__engine.analyze).toHaveBeenCalledWith(c.currentFen);

    c.engineOn = false;
    c.startSparring();
    c.stopSparring();
    expect(c.engineOn).toBeFalse();
    c.ngOnDestroy();
  });

  it('Beenden lässt eine laufende Antwort verfallen', async () => {
    const c = sparringAtStart();
    c.onMove({ orig: 'e2', dest: 'e4' });
    c.stopSparring();
    c.__maia.calls[0].resolve('e7e5');
    await flush();
    expect(c.root.children[0].children).toEqual([]);
    expect(c.maiaThinking).toBeFalse();
    c.ngOnDestroy();
  });

  it('reset() und FEN laden beenden das Sparring', () => {
    const c = sparringAtStart();
    c.reset();
    expect(c.sparring).toBeNull();
    expect(c.engineOn).toBeTrue();

    c.startSparring();
    c.fenInput = '4k3/8/8/8/8/8/4P3/4K3 w - - 0 1';
    c.loadFen();
    expect(c.sparring).toBeNull();
    expect(c.engineOn).toBeTrue();
    c.ngOnDestroy();
  });

  it('Löschen der Ausgangsstellung beendet es, Löschen woanders nicht', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e7e5' });
    c.ngOnInit();
    c.engineOn = true;
    c.goTo(1);
    c.startSparring();                            // ab 1.e4, Nutzer = Schwarz
    const e4 = c.root.children[0];
    c.goTo(0);
    c.onMove({ orig: 'd2', dest: 'd4' });         // Variante 1.d4 — Zug für Maias Seite, keine Antwort
    c.onTreeAction({ kind: 'delete', node: c.root.children[1] });
    expect(c.sparring).not.toBeNull();

    c.goTo(2);
    c.onTreeAction({ kind: 'delete', node: e4 });
    expect(c.sparring).toBeNull();
    expect(c.engineOn).toBeTrue();
    c.ngOnDestroy();
  });

  it('Maia wandelt um: a7a8q wird richtig gespielt', async () => {
    const c = makeComponent({ fen: '8/P5k1/8/8/8/8/6K1/8 w - - 0 1' });
    c.ngOnInit();
    c.startSparring();
    c.switchSparringSides();                      // Maia = Weiß
    c.__maia.calls[0].resolve('a7a8q');
    await flush();
    expect(c.currentNode.san).toBe('a8=Q');
    expect(c.currentNode.uci).toBe('a7a8q');
    expect(c.currentFen.startsWith('Q7/6k1/')).toBeTrue();
    c.ngOnDestroy();
  });

  it('nach Matt/Patt keine Anfrage', () => {
    const c = makeComponent({ fen: START, moves: 'f2f3,e7e5,g2g4' });
    c.ngOnInit();
    c.startSparring();                            // Nutzer = Schwarz
    c.onMove({ orig: 'd8', dest: 'h4' });         // Qh4#
    expect(c.terminal).toBe('mate-black-wins');
    expect(c.__maia.chooseMove).not.toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('kein Zug (null) beendet nur das Überlegen', async () => {
    const c = sparringAtStart();
    c.onMove({ orig: 'e2', dest: 'e4' });
    c.__maia.calls[0].resolve(null);
    await flush();
    expect(c.maiaThinking).toBeFalse();
    expect(c.currentNode.san).toBe('e4');
    expect(c.__snackbar.warn).not.toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('chooseMove wirft → Snackbar, das Sparring bleibt', async () => {
    const c = sparringAtStart();
    c.onMove({ orig: 'e2', dest: 'e4' });
    c.__maia.calls[0].reject(new Error('worker gone'));
    await flush();
    expect(c.__snackbar.warn).toHaveBeenCalledWith('analysis.maia.moveFailed');
    expect(c.maiaThinking).toBeFalse();
    expect(c.sparring).not.toBeNull();
    c.ngOnDestroy();
  });

  it('Sitzung weg (Status error) → nach dem Fehlschlag im Hintergrund neu aufbauen', async () => {
    const c = sparringAtStart();
    c.onMove({ orig: 'e2', dest: 'e4' });
    c.__maia.statusValue = 'error';             // der Worker ist gestorben
    c.__maia.calls[0].reject(new Error('Maia failed'));
    await flush();
    expect(c.__snackbar.warn).toHaveBeenCalledWith('analysis.maia.moveFailed');
    expect(c.__maia.prepare).toHaveBeenCalledTimes(1);
    expect(c.sparring).not.toBeNull();
    c.ngOnDestroy();
  });

  it('nur diese eine Anfrage scheiterte (Status ready) → kein Neuaufbau', async () => {
    const c = sparringAtStart();
    c.onMove({ orig: 'e2', dest: 'e4' });
    c.__maia.calls[0].reject(new Error('boom'));
    await flush();
    expect(c.__snackbar.warn).toHaveBeenCalled();
    expect(c.__maia.prepare).not.toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('ein Zug aus Explorer/Repertoire zählt wie ein eigener: Maia antwortet', async () => {
    const c = sparringAtStart();
    c.playRepertoireMoves(['e4']);
    expect(c.__maia.chooseMove).toHaveBeenCalledOnceWith(AFTER_E4, c.maiaElo);
    c.__maia.calls[0].resolve('c7c5');
    await flush();
    expect(c.root.children[0].children.map((n: any) => n.san)).toEqual(['c5']);
    expect(c.currentNode.san).toBe('c5');
    c.ngOnDestroy();
  });

  it('Explorer/Repertoire-Zug für Maias Seite löst nichts aus', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4' });
    c.ngOnInit();
    c.startSparring();                            // Nutzer = Schwarz
    c.goTo(0);                                    // Weiß (= Maia) am Zug
    c.playRepertoireMoves(['d4']);                // der Nutzer zieht für Maia → danach ist er selbst dran
    expect(c.currentNode.san).toBe('d4');
    expect(c.__maia.chooseMove).not.toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('die Stärke: nur Werte der Auswahl, je Gerät gemerkt', () => {
    try { localStorage.setItem('rookhub_analysis_maia_elo', '2000'); } catch {}
    const c = makeComponent({ fen: START });
    expect(c.maiaElo).toBe(2000);
    c.onMaiaEloChange(1234);
    expect(c.maiaElo).toBe(2000);
    c.onMaiaEloChange(1200);
    expect(c.maiaElo).toBe(1200);
    expect(localStorage.getItem('rookhub_analysis_maia_elo')).toBe('1200');
    try { localStorage.removeItem('rookhub_analysis_maia_elo'); } catch {}
    c.ngOnInit();
    c.ngOnDestroy();
  });

  it('ngOnDestroy gibt Maia frei', () => {
    const c = sparringAtStart();
    c.ngOnDestroy();
    expect(c.__maia.release).toHaveBeenCalled();
  });
});

/** Die echte Vorlage mit Query-Parametern; die schweren Kind-Komponenten fallen weg (wie oben). Mit `lang` gelten die
 *  echten Texte aus `public/i18n` (Karma serviert sie), sonst stehen die Schlüssel da. */
async function renderAnalysis(params: Record<string, string> = {}, lang?: string): Promise<ComponentFixture<AnalysisComponent>> {
  const engine: any = {
    analysis$: new Subject(), engineFatalError$: new Subject(), remoteFallback$: new Subject(), remoteInterrupted$: new Subject(),
    setMultiPv: () => {}, setDepth: () => {}, setRemoteEngine: () => {},
    analyze: () => Promise.resolve(), stop: () => {}, destroy: () => {},
  };
  TestBed.overrideComponent(AnalysisComponent, {
    remove: { imports: [AnalysisBoardComponent, PositionSetupComponent, PositionRepertoiresComponent, HelpHintComponent,
      OpeningExplorerComponent, PositionMenuComponent, AnalysisMoveTreeComponent] },
    add: { schemas: [NO_ERRORS_SCHEMA] },
  });
  await TestBed.configureTestingModule({
    imports: [AnalysisComponent],
    providers: [
      provideNoopAnimations(),
      provideTranslateService({ fallbackLang: 'en' }),
      { provide: AnalysisEngineService, useValue: engine },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: (k: string) => params[k] ?? null } } } },
      { provide: Router, useValue: { navigateByUrl: () => {} } },
      { provide: SnackbarService, useValue: { show: () => {}, warn: () => {} } },
      { provide: AuthService, useValue: { isLoggedIn: false } },
      { provide: ExternalEngineService, useValue: { listEngines: () => new Subject(), analyse: () => {} } },
      { provide: AnalysisHistoryService, useValue: { save: () => of({}), get: () => of({}), list: () => of([]) } },
      { provide: MatDialog, useValue: { open: () => {} } },
    ],
  }).compileComponents();
  if (lang) {
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation(lang, await (await fetch(`/i18n/${lang}.json`)).json());
    translate.use(lang);
  }
  const fixture = TestBed.createComponent(AnalysisComponent);
  fixture.detectChanges();   // kein whenStable: die Seite hält eigene Zeitgeber offen
  return fixture;
}

// Codereview F4-012: ?from= setzen auch Partienliste, Partie, Favoriten und Kalkulation — der Knopf hieß trotzdem immer
// „Zurück zum Puzzle"; falsche FEN/PGN meldeten sich fest auf Englisch („Invalid FEN" + „OK").
describe('AnalysisComponent Zurück-Knopf + Meldungen (übersetzt)', () => {
  it('der Zurück-Knopf heißt neutral „Zurück", auch aus der Partienliste', async () => {
    const fixture = await renderAnalysis({ from: '/games' });
    const back = fixture.nativeElement.querySelector('.back-btn') as HTMLButtonElement;
    expect(back).not.toBeNull();
    expect(back.textContent).toContain('common.back');
    expect(back.textContent).not.toContain('backToPuzzle');
    fixture.destroy();
  });

  it('falsche FEN und falsches PGN melden sich über i18n-Keys, die Aktion ist „common.ok"', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    const show = jasmine.createSpy('show');
    c.snackbar = { show };
    c.fenInput = 'kein fen';
    c.loadFen();
    expect(show).toHaveBeenCalledWith('analysis.invalidFen', { action: 'common.ok', duration: 2500 });
    c.pgnInput = 'kein PGN';
    c.loadPgn();
    expect(show).toHaveBeenCalledWith('analysis.invalidPgn', { action: 'common.ok', duration: 2500 });
    c.ngOnDestroy();
  });
});

// Codereview UX-048: das 104-px-Auswahlfeld kürzte „Max. Tiefe" zu „Max. T", die FEN-Karte trug drei zweizeilige Knöpfe,
// und „FEN kopieren" stand dazu noch im ⋮-Menü derselben Seite. Gemessen wird mit der echten Schrift (Roboto, selbst
// ausgeliefert unter /fonts) — ohne sie misst der Browser mit einer Ersatzschrift.
describe('AnalysisComponent Seitenleiste: Beschriftungen passen (UX-048)', () => {
  let fonts: HTMLLinkElement;
  beforeAll(async () => {
    fonts = document.createElement('link');
    fonts.rel = 'stylesheet';
    fonts.href = '/fonts/fonts.css';
    const loaded = new Promise(r => { fonts.onload = r; fonts.onerror = r; });
    document.head.appendChild(fonts);
    await loaded;
    await Promise.all([document.fonts.load('16px Roboto'), document.fonts.load('500 14px Roboto')]);
  });
  afterAll(() => fonts.remove());

  it('die FEN-Karte zeigt nur „FEN laden" und „Stellung aufbauen" — Kopieren bleibt im ⋮-Menü', async () => {
    const fixture = await renderAnalysis();
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('.io-actions button')) as HTMLElement[];
    expect(buttons.map(b => b.querySelector('.mdc-button__label')!.textContent!.trim()))
      .toEqual(['analysis.loadFen', 'analysis.setup.button']);
    fixture.destroy();
  });

  for (const lang of ['en', 'de', 'hr', 'hu']) {
    it(`${lang}: Tiefe- und Linien-Feld zeigen ihre Beschriftung ungekürzt, die FEN-Knöpfe bleiben am Handy einzeilig`, async () => {
      const fixture = await renderAnalysis({}, lang);
      const el = fixture.nativeElement as HTMLElement;
      const side = el.querySelector('.side-col') as HTMLElement;
      side.style.flex = '0 0 366px';   // 390 px Handy minus Seitenrand
      side.style.width = '366px';
      fixture.detectChanges();
      await new Promise(r => setTimeout(r, 30));

      const labels = Array.from(el.querySelectorAll('.num-field .mdc-floating-label')) as HTMLElement[];
      expect(labels.length).toBe(2);
      for (const l of labels) {
        expect(l.scrollWidth).withContext(`„${l.textContent!.trim()}" gekürzt`).toBeLessThanOrEqual(l.clientWidth);
      }
      for (const b of Array.from(el.querySelectorAll('.io-actions button')) as HTMLElement[]) {
        const label = b.querySelector('.mdc-button__label') as HTMLElement;
        const cs = getComputedStyle(label);
        const line = parseFloat(cs.lineHeight) || parseFloat(cs.fontSize) * 1.25;
        expect(label.getBoundingClientRect().height).withContext(`„${label.textContent!.trim()}" zweizeilig`).toBeLessThan(line * 1.5);
      }
      fixture.destroy();
    });
  }
});
