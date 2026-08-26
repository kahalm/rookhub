import { Subject } from 'rxjs';
import { AnalysisComponent, DEPTH_OPTIONS } from './analysis.component';
import { AnalysisEngineService } from './analysis-engine.service';

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
  const snackBar: any = { open: () => {} };
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
  const c: any = new AnalysisComponent(engine, route, snackBar, router, auth, externalEngines, cdr, translate, opts.locale ?? 'de');
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

  it('formats millions as MN/s and thousands as kN/s', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.engineOn = true;

    (c as any).onEngineUpdate(c.currentFen, 20, [], 8234567, 3450000);
    expect(c.speedHint).toContain('MN/s');
    expect(c.speedHint).toContain('3,5');            // de-DE: Komma als Dezimaltrenner

    (c as any).onEngineUpdate(c.currentFen, 20, [], 90000, 45000);
    expect(c.speedHint).toContain('kN/s');
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
