import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { GuessBoardComponent } from './guess-board.component';
import { GuessIntroMove, GuessSession } from './guess.service';

function session(over: Partial<GuessSession> = {}): GuessSession {
  return {
    id: 3, gameAnalysisId: 1, title: 'A – B', white: 'A', black: 'B',
    guessWhite: true, startPly: 8, status: 'running',
    points: 0, maxPoints: 0, movesPlayed: 0, gameMoveHits: 0, secondsSpent: 0,
    position: { ply: 8, moveNumber: 5, whiteToMove: true, fen: 'fen-8', lastMoveUci: 'e7e5' },
    totalGuesses: 10, startFen: null, intro: [], ...over,
  };
}

describe('GuessBoardComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GuessBoardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '3' }) } } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(over: Partial<GuessSession> = {}) {
    const fixture = TestBed.createComponent(GuessBoardComponent);
    fixture.detectChanges();
    http.expectOne('/api/guess-sessions/3').flush(session(over));
    return fixture;
  }

  it('zeigt die zu ratende Stellung und hebt den Zug DAVOR hervor', () => {
    const c = load().componentInstance;
    expect(c.boardFen).toBe('fen-8');
    expect(c.lastMove).toEqual(['e7', 'e5']);   // der Gegenzug, nicht der gesuchte
    expect(c.canGuess).toBeTrue();
  });

  it('schickt den geratenen Zug und uebernimmt die Rueckmeldung', () => {
    const fixture = load();
    const c = fixture.componentInstance;

    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'egal' });
    const req = http.expectOne('/api/guess-sessions/3/guess');
    expect(req.request.body.uci).toBe('g1f3');
    req.flush({
      grade: 'gameMove', points: 5, playedSan: 'Nf3', gameMoveSan: 'Nf3', gameMoveUci: 'g1f3',
      replySan: 'Nc6', replyUci: 'b8c6', diffCp: 0, evalText: '+0.30',
      session: session({ points: 5, maxPoints: 10, movesPlayed: 1, gameMoveHits: 1,
        position: { ply: 10, moveNumber: 6, whiteToMove: true, fen: 'fen-10', lastMoveUci: 'b8c6' } }),
    });

    expect(c.last!.points).toBe(5);
    expect(c.boardFen).toBe('fen-10');           // Brett steht auf der nächsten Aufgabe
    expect(c.session!.points).toBe(5);
  });

  it('schickt bei einer Umwandlung die Figur mit', () => {
    // Das Brett meldet nur from/to; die Figur steht nur im SAN. Ohne sie ginge „e7e8" zum Server —
    // dort kein legaler Zug, und JEDE Umwandlung waere mit 400 abgeprallt.
    const c = load().componentInstance;
    c.onMove({ from: 'e7', to: 'e8', san: 'e8=Q+', fen: 'egal' });
    expect(http.expectOne('/api/guess-sessions/3/guess').request.body.uci).toBe('e7e8q');
  });

  it('passen schickt einen leeren Zug (0 Punkte, keine Strafe)', () => {
    const c = load().componentInstance;
    c.skip();
    const req = http.expectOne('/api/guess-sessions/3/guess');
    expect(req.request.body.uci).toBeNull();
    req.flush({
      grade: null, points: 0, playedSan: null, gameMoveSan: 'Nf3', gameMoveUci: 'g1f3',
      replySan: null, replyUci: null, diffCp: null, evalText: null,
      session: session({ movesPlayed: 1 }),
    });
    expect(c.last!.grade).toBeNull();
  });

  it('holt am Ende den Rueckblick und sperrt das Brett', () => {
    const fixture = load();
    const c = fixture.componentInstance;

    c.skip();
    http.expectOne('/api/guess-sessions/3/guess').flush({
      grade: 'gameMove', points: 5, playedSan: 'Nf3', gameMoveSan: 'Nf3', gameMoveUci: 'g1f3',
      replySan: null, replyUci: null, diffCp: 0, evalText: null,
      session: session({ status: 'done', position: null, points: 5, maxPoints: 10, movesPlayed: 1 }),
    });
    http.expectOne('/api/guess-sessions/3/review').flush([
      { ply: 8, moveNumber: 5, white: true, gameSan: 'Nf3', playedSan: 'Nf3', grade: 'gameMove', points: 5,
        diffCp: 0, secondsSpent: 9, bestSan: 'Nf3', bestEval: '+0.30', gameEval: '+0.30' },
    ]);

    expect(c.review.length).toBe(1);
    expect(c.canGuess).toBeFalse();   // beendet → keine Eingabe mehr
  });

  it('meldet einen Fehler beim Werten sichtbar', () => {
    const c = load().componentInstance;
    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'egal' });
    http.expectOne('/api/guess-sessions/3/guess')
      .flush({ message: 'Dieser Zug ist in der Stellung nicht möglich.' }, { status: 400, statusText: 'Bad Request' });

    // Der Nutzer hat gerade gezogen — kein stiller Fehlschlag, und das Brett bleibt bedienbar.
    expect(c.busy).toBeFalse();
    expect(c.canGuess).toBeTrue();
    expect(c.boardFen).withContext('nicht gewerteter Zug bleibt nicht stehen').toBe('fen-8');
  });
});

/**
 * Ein Zug, der NICHT der Partiezug war, bleibt auf dem Brett stehen; darunter steht, was die Partie
 * gespielt hat, und erst „Weiter" holt die naechste Aufgabe. Der Abstand zum Partiezug wird nur bei
 * einem SCHLECHTEN Zug genannt — bei gleichwertig/besser genuegt der Partiezug selbst.
 */
describe('GuessBoardComponent Zug stehen lassen', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  const FEN8 = 'rn1qkbnr/ppp2ppp/3p4/4P3/4P3/5b2/PPP2PPP/RNBQKB1R w KQkq - 0 5';
  // Wie der Server es liefert: je Zug die Stellung DANACH — die des letzten ist die Aufgabe selbst
  // (im Backend als `dto.Position.Fen == dto.Intro[^1].Fen` festgenagelt).
  const INTRO: GuessIntroMove[] = [
    { ply: 6, moveNumber: 4, white: true, san: 'dxe5', uci: 'd4e5', fen: 'nach-dxe5' },
    { ply: 7, moveNumber: 4, white: false, san: 'Bxf3', uci: 'g4f3', fen: FEN8 },
  ];
  const FEN10 = 'rn1qkbnr/ppp2ppp/8/4p3/4P3/5Q2/PPP2PPP/RNB1KB1R w KQkq - 0 6';
  let http: HttpTestingController;

  function base(over: Partial<GuessSession> = {}): GuessSession {
    return {
      id: 3, gameAnalysisId: 1, title: 'A - B', white: 'A', black: 'B',
      guessWhite: true, startPly: 8, status: 'running',
      points: 0, maxPoints: 0, movesPlayed: 0, gameMoveHits: 0, secondsSpent: 0,
      position: { ply: 8, moveNumber: 5, whiteToMove: true, fen: FEN8, lastMoveUci: 'c8g4' },
      totalGuesses: 10, startFen: START, intro: INTRO, ...over,
    };
  }
  const nextSession = base({
    points: 0, maxPoints: 10, movesPlayed: 1,
    position: { ply: 10, moveNumber: 6, whiteToMove: true, fen: FEN10, lastMoveUci: 'd6e5' },
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GuessBoardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '3' }) } } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  /** Laedt und geht auf die Aufgabe — beim Oeffnen steht das Brett jetzt auf der Grundstellung. */
  function load() {
    const c = loadAtStart();
    c.browse(null);
    return c;
  }

  function loadAtStart() {
    const fixture = TestBed.createComponent(GuessBoardComponent);
    fixture.detectChanges();
    http.expectOne('/api/guess-sessions/3').flush(base());
    return fixture.componentInstance;
  }

  function guess(c: any, from: string, to: string, san: string, res: Partial<any>) {
    c.onMove({ from, to, san, fen: 'egal' });
    http.expectOne('/api/guess-sessions/3/guess').flush({
      grade: 'worse', points: 0, playedSan: san, gameMoveSan: 'Qxf3', gameMoveUci: 'd1f3',
      replySan: 'dxe5', replyUci: 'd6e5', diffCp: -120, evalText: '+1.83',
      session: nextSession, ...res,
    });
  }

  it('anderer Zug: bleibt stehen, naechste Aufgabe wartet hinter Weiter', () => {
    const c = load();
    guess(c, 'f1', 'c4', 'Bc4', {});

    expect(c.holding).withContext('haelt').toBeTrue();
    expect(c.canGuess).withContext('kein zweiter Zug, solange gehalten').toBeFalse();
    // Brett zeigt MEINEN Zug: Laeufer auf c4, f1 leer — NICHT die naechste Aufgabe.
    expect(c.boardFen).toContain('2B1P3');
    expect(c.boardFen).not.toBe(FEN10);
    expect(c.lastMove).toEqual(['f1', 'c4']);

    c.continueGame();
    expect(c.holding).toBeFalse();
    expect(c.boardFen).withContext('jetzt die naechste Aufgabe').toBe(FEN10);
    expect(c.canGuess).toBeTrue();
  });

  it('Partiezug: rueckt sofort vor, nichts wird gehalten', () => {
    const c = load();
    guess(c, 'd1', 'f3', 'Qxf3', { grade: 'gameMove', points: 5, playedSan: 'Qxf3', diffCp: 0 });
    expect(c.holding).toBeFalse();
    expect(c.boardFen).toBe(FEN10);
  });

  it('Passen: rueckt sofort vor', () => {
    const c = load();
    c.skip();
    http.expectOne('/api/guess-sessions/3/guess').flush({
      grade: null, points: 0, playedSan: null, gameMoveSan: 'Qxf3', gameMoveUci: 'd1f3',
      replySan: 'dxe5', replyUci: 'd6e5', diffCp: null, evalText: null, session: nextSession,
    });
    expect(c.holding).toBeFalse();
    expect(c.boardFen).toBe(FEN10);
  });

  it('Abstand nur bei schlecht und bei deutlich besser', () => {
    const c1 = load();
    guess(c1, 'f1', 'c4', 'Bc4', { grade: 'muchWorse', points: -2, diffCp: -240 });
    expect(c1.showsDelta).toBeTrue();
    expect(c1.evalDelta).toBe('-2.40');

    // Deutlich besser: die Zahl sagt, wie viel man gefunden hat — mit Vorzeichen.
    const c2 = load();
    guess(c2, 'f1', 'c4', 'Bc4', { grade: 'clearlyBetter', points: 10, diffCp: 60 });
    expect(c2.showsDelta).toBeTrue();
    expect(c2.evalDelta).toBe('+0.60');

    // Dazwischen sagt die Zahl nichts, was die Stufe nicht schon sagt.
    const c3 = load();
    guess(c3, 'f1', 'c4', 'Bc4', { grade: 'similar', points: 2, diffCp: -5 });
    expect(c3.showsDelta).toBeFalse();
    expect(c3.evalDelta).toBeNull();

    const c4 = load();
    guess(c4, 'f1', 'c4', 'Bc4', { grade: 'better', points: 8, diffCp: 15 });
    expect(c4.evalDelta).withContext('knapp besser: nur der Partiezug').toBeNull();
  });

  it('die Eroeffnung laesst sich durchklicken, das Brett bleibt dabei gesperrt', () => {
    const c = load();
    expect(c.viewFen).withContext('zeigt zunaechst die Aufgabe').toBe(FEN8);
    expect(c.canGuess).toBeTrue();

    c.browse(0);                                  // 4.dxe5 anschauen — eine Stellung VOR der Aufgabe
    expect(c.viewFen).toBe('nach-dxe5');
    expect(c.viewLastMove).toEqual(['d4', 'e5']);
    expect(c.canGuess).withContext('kein Raten in einer alten Stellung').toBeFalse();

    c.browse(-1);                                 // Grundstellung
    expect(c.viewFen).toBe(START);
    expect(c.viewLastMove).withContext('davor wurde nichts gezogen').toBeUndefined();

    c.browse(null);                               // zurueck zur Aufgabe
    expect(c.browsing).toBeFalse();
    expect(c.viewFen).toBe(FEN8);
    expect(c.canGuess).toBeTrue();
  });

  it('waehrend des Durchklickens wird kein Zug angenommen', () => {
    // Sonst wuerde ein Zug in einer ALTEN Stellung als Rateversuch fuer die aktuelle Aufgabe gewertet.
    const c = load();
    c.browse(0);
    c.onMove({ from: 'd1', to: 'f3', san: 'Qxf3', fen: 'egal' });
    http.expectNone('/api/guess-sessions/3/guess');

    c.skip();                                   // auch Passen muss waehrenddessen wirkungslos sein
    http.expectNone('/api/guess-sessions/3/guess');

    c.browse(null);
    guess(c, 'd1', 'f3', 'Qxf3', { grade: 'gameMove', points: 5, playedSan: 'Qxf3', diffCp: 0 });
    expect(c.browsing).withContext('nach dem Zug steht die naechste Aufgabe').toBeFalse();
    expect(c.viewFen).toBe(FEN10);
  });

  it('startet bei Zug 1, nicht bei der ersten Aufgabe', () => {
    const c = loadAtStart();
    expect(c.browseIndex).withContext('Grundstellung').toBe(-1);
    expect(c.viewFen).toBe(START);
    expect(c.canGuess).withContext('erst durchklicken').toBeFalse();

    c.browse(0);                                   // 4.dxe5 — noch nicht die Aufgabe
    expect(c.canGuess).toBeFalse();

    c.browse(INTRO.length - 1);                    // letzter Eroeffnungszug = die Aufgabe
    expect(c.viewFen).withContext('das ist die Aufgabenstellung').toBe(FEN8);
    expect(c.browsing).toBeFalse();
    expect(c.canGuess).withContext('ab hier darf gezogen werden').toBeTrue();
  });

  it('der letzte Zug bleibt auf dem Brett stehen', () => {
    // Ohne naechste Aufgabe fasste `apply` das Brett gar nicht an — die Figur sprang zurueck und
    // das Brett zeigte die Stellung VOR dem Schlusszug.
    const c = load();
    c.onMove({ from: 'd1', to: 'f3', san: 'Qxf3', fen: 'egal' });
    http.expectOne('/api/guess-sessions/3/guess').flush({
      grade: 'onlyMove', points: 8, playedSan: 'Qxf3', gameMoveSan: 'Qxf3', gameMoveUci: 'd1f3',
      replySan: null, replyUci: null, diffCp: 0, evalText: null,
      session: base({ status: 'done', position: null, points: 8, maxPoints: 10, movesPlayed: 1 }),
    });
    http.expectOne('/api/guess-sessions/3/review').flush([]);

    expect(c.session!.status).toBe('done');
    expect(c.boardFen).withContext('Dame steht auf f3').toContain('5Q2');
    expect(c.boardFen).not.toBe(FEN8);
    expect(c.lastMove).toEqual(['d1', 'f3']);
    expect(c.canGuess).toBeFalse();
  });

  it('das Info-Zeichen erscheint nur, wo es etwas Besseres gab', () => {
    const c = load();
    const row = (over: any) => ({
      ply: 8, moveNumber: 5, white: true, gameSan: 'Qxf3', playedSan: 'Qxf3', grade: 'gameMove',
      points: 5, diffCp: 0, secondsSpent: 9, bestSan: 'Qxf3', bestEval: '+1.75', gameEval: '+1.75', ...over,
    });

    expect(c.hasBetter(row({}))).withContext('Partiezug war der beste').toBeFalse();
    expect(c.hasBetter(row({ bestSan: 'Bc4' }))).toBeTrue();
    expect(c.hasBetter(row({ bestSan: null }))).withContext('keine Kandidatenliste').toBeFalse();

    // Eigener Zug abweichend -> beide Zeilen; eigener Zug = Partiezug -> nur der beste.
    const both = c.infoText(row({ bestSan: 'Bc4', bestEval: '+2.40', playedSan: 'Nc3' }));
    expect(both).toContain('guess.info.gameMove');
    expect(both).toContain('guess.info.bestMove');

    const only = c.infoText(row({ bestSan: 'Bc4', bestEval: '+2.40' }));
    expect(only).not.toContain('guess.info.gameMove');
    expect(only).toContain('guess.info.bestMove');
  });

  it('der eigene Zug steht sofort, nicht erst mit der Antwort', () => {
    // Sonst setzt das Sperren des Bretts (busy) es kurz auf die alte Stellung zurueck und die
    // Figur zuckt: hin, zurueck, wieder hin.
    const c = load();
    c.onMove({ from: 'f1', to: 'c4', san: 'Bc4', fen: 'egal' });
    expect(c.boardFen).withContext('Laeufer schon auf c4').toContain('2B1P3');
    expect(c.lastMove).toEqual(['f1', 'c4']);
    http.expectOne('/api/guess-sessions/3/guess').flush({
      grade: 'worse', points: 0, playedSan: 'Bc4', gameMoveSan: 'Qxf3', gameMoveUci: 'd1f3',
      replySan: 'dxe5', replyUci: 'd6e5', diffCp: -120, evalText: '+1.83', session: nextSession,
    });
    expect(c.holding).toBeTrue();
    expect(c.boardFen).withContext('bleibt dort stehen').toContain('2B1P3');
  });

  it('nach dem ersten Zug ist der letzte Eroeffnungszug wieder nur Ansicht', () => {
    // Der Kurzschluss „letzter Introzug == Aufgabe" gilt NUR, solange nichts geraten wurde. Ohne
    // diese Bedingung koennte man dort in einer alten Stellung ziehen — gewertet gegen die aktuelle.
    const c = load();
    guess(c, 'd1', 'f3', 'Qxf3', { grade: 'gameMove', points: 5, playedSan: 'Qxf3', diffCp: 0 });

    c.browse(INTRO.length - 1);
    expect(c.browsing).withContext('jetzt eine alte Stellung').toBeTrue();
    expect(c.canGuess).toBeFalse();
  });

  it('vor und zurueck blaettern durch die Eroeffnung', () => {
    const c = loadAtStart();
    expect(c.atStart).toBeTrue();
    expect(c.atTask).toBeFalse();

    c.step(1);
    expect(c.browseIndex).toBe(0);
    c.step(1);
    expect(c.browseIndex).toBe(INTRO.length - 1);
    expect(c.atTask).withContext('letzter Introzug ist die erste Aufgabe').toBeTrue();

    c.step(-1);
    expect(c.browseIndex).toBe(0);
    c.step(-5);
    expect(c.browseIndex).withContext('nicht vor die Grundstellung').toBe(-1);
  });

  it('die Zugliste steht untereinander, je Zeile Weiss und Schwarz', () => {
    const c = loadAtStart();
    expect(c.introRows.length).toBe(1);              // 4.dxe5 Bxf3 = eine Zeile
    expect(c.introRows[0].no).toBe(4);
    expect(c.introRows[0].w).toBe('dxe5');
    expect(c.introRows[0].b).toBe('Bxf3');
    expect(c.introRows[0].wIdx).toBe(0);
    expect(c.introRows[0].bIdx).toBe(1);
  });
});
