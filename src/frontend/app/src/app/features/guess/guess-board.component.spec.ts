import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { GuessBoardComponent } from './guess-board.component';
import { GuessHistoryMove, GuessSession } from './guess.service';
import { AuthService } from '../../core/auth.service';

function session(over: Partial<GuessSession> = {}): GuessSession {
  return {
    id: 3, gameAnalysisId: 1, title: 'A – B', white: 'A', black: 'B',
    guessWhite: true, startPly: 8, status: 'running',
    points: 0, maxPoints: 0, movesPlayed: 0, gameMoveHits: 0, secondsSpent: 0,
    position: { ply: 8, moveNumber: 5, whiteToMove: true, fen: 'fen-8', lastMoveUci: 'e7e5' },
    totalGuesses: 10, startFen: null, history: [], ...over,
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
        // Diese Suite beschreibt den ANGEMELDETEN Fall: ohne den Stub gilt der Nutzer als
        // abgemeldet, und der GuessService greift dann zu `…/anonymous` samt Sitzungskennung
        // (der anonyme Zweig hat eine eigene Suite in guess.service.spec.ts).
        { provide: AuthService, useValue: { isLoggedIn: true } },
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
  const HISTORY: GuessHistoryMove[] = [
    { ply: 6, moveNumber: 4, white: true, san: 'dxe5', uci: 'd4e5', fen: 'nach-dxe5' },
    { ply: 7, moveNumber: 4, white: false, san: 'Bxf3', uci: 'g4f3', fen: FEN8 },
  ];
  const FEN9 = 'rn1qkbnr/ppp2ppp/3p4/4P3/4P3/5Q2/PPP2PPP/RNB1KB1R b KQkq - 0 5';   // nach 5.Qxf3
  const FEN10 = 'rn1qkbnr/ppp2ppp/8/4p3/4P3/5Q2/PPP2PPP/RNB1KB1R w KQkq - 0 6';   // nach 5...dxe5
  /** Der Verlauf, den der Server NACH dem Rateversuch liefert: die eben gespielten Halbzuege
   *  stehen mit drin — daraus baut das Brett seine Schrittfolge. */
  const PLAYED: GuessHistoryMove[] = [
    { ply: 8, moveNumber: 5, white: true, san: 'Qxf3', uci: 'd1f3', fen: FEN9 },
    { ply: 9, moveNumber: 5, white: false, san: 'dxe5', uci: 'd6e5', fen: FEN10 },
  ];
  let http: HttpTestingController;

  function base(over: Partial<GuessSession> = {}): GuessSession {
    return {
      id: 3, gameAnalysisId: 1, title: 'A - B', white: 'A', black: 'B',
      guessWhite: true, startPly: 8, status: 'running',
      points: 0, maxPoints: 0, movesPlayed: 0, gameMoveHits: 0, secondsSpent: 0,
      position: { ply: 8, moveNumber: 5, whiteToMove: true, fen: FEN8, lastMoveUci: 'c8g4' },
      totalGuesses: 10, startFen: START, history: HISTORY, ...over,
    };
  }
  const nextSession = base({
    points: 0, maxPoints: 10, movesPlayed: 1,
    position: { ply: 10, moveNumber: 6, whiteToMove: true, fen: FEN10, lastMoveUci: 'd6e5' },
    history: [...HISTORY, ...PLAYED],
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GuessBoardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '3' }) } } },
        { provide: AuthService, useValue: { isLoggedIn: true } },   // angemeldeter Fall, siehe oben
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  /** Laedt und geht auf die Aufgabe — beim Oeffnen steht das Brett jetzt auf der Grundstellung. */
  function load(over: Partial<GuessSession> = {}) {
    const c = loadAtStart(over);
    c.browse(null);
    return c;
  }

  function loadAtStart(over: Partial<GuessSession> = {}) {
    const fixture = TestBed.createComponent(GuessBoardComponent);
    fixture.detectChanges();
    http.expectOne('/api/guess-sessions/3').flush(base(over));
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

  it('anderer Zug: bleibt stehen, naechste Aufgabe wartet hinter Weiter', fakeAsync(() => {
    const c = load();
    guess(c, 'f1', 'c4', 'Bc4', {});

    expect(c.holding).withContext('haelt').toBeTrue();
    expect(c.canGuess).withContext('kein zweiter Zug, solange gehalten').toBeFalse();
    // Brett zeigt MEINEN Zug: Laeufer auf c4, f1 leer — NICHT die naechste Aufgabe.
    expect(c.boardFen).toContain('2B1P3');
    expect(c.boardFen).not.toBe(FEN10);
    expect(c.lastMove).toEqual(['f1', 'c4']);

    // „Weiter" zeigt zuerst den PARTIEZUG — das ist die Korrektur, die man sehen will — und
    // erst nach der Pause die Antwort des Gegners (= die naechste Aufgabe). Beides im selben
    // Bild waere zwei Halbzuege auf einmal, und man saehe nie, was gespielt wurde.
    c.continueGame();
    expect(c.holding).toBeFalse();
    expect(c.boardFen).withContext('erst der Partiezug Qxf3').toBe(FEN9);
    expect(c.lastMove).toEqual(['d1', 'f3']);
    expect(c.canGuess).withContext('waehrend der Schrittfolge gesperrt').toBeFalse();

    tick(1000);
    expect(c.boardFen).withContext('dann die Antwort').toBe(FEN10);
    tick(500);
    expect(c.canGuess).withContext('jetzt ist wieder der Nutzer dran').toBeTrue();
  }));

  it('Partiezug: der eigene Zug steht, die Antwort kommt erst nach der Pause', fakeAsync(() => {
    const c = load();
    guess(c, 'd1', 'f3', 'Qxf3', { grade: 'gameMove', points: 5, playedSan: 'Qxf3', diffCp: 0 });
    expect(c.holding).toBeFalse();
    expect(c.boardFen).withContext('noch ohne die Antwort dxe5').toBe(FEN9);
    expect(c.lastMove).toEqual(['d1', 'f3']);

    tick(1000);
    expect(c.boardFen).toBe(FEN10);
    tick(500);
  }));

  it('Passen: erst der Partiezug, dann die Antwort', fakeAsync(() => {
    const c = load();
    c.skip();
    http.expectOne('/api/guess-sessions/3/guess').flush({
      grade: null, points: 0, playedSan: null, gameMoveSan: 'Qxf3', gameMoveUci: 'd1f3',
      replySan: 'dxe5', replyUci: 'd6e5', diffCp: null, evalText: null, session: nextSession,
    });
    expect(c.holding).toBeFalse();
    expect(c.boardFen).withContext('der Partiezug steht zuerst').toBe(FEN9);
    expect(c.lastMove).toEqual(['d1', 'f3']);

    tick(1000);
    expect(c.boardFen).toBe(FEN10);
    tick(500);
  }));

  /** Ohne dazugekommene Halbzuege gibt es nichts abzuspielen — dann sofort die naechste Aufgabe. */
  it('ohne neue Halbzuege geht es ohne Pause weiter', () => {
    const c = load();
    c.skip();
    http.expectOne('/api/guess-sessions/3/guess').flush({
      grade: null, points: 0, playedSan: null, gameMoveSan: 'Qxf3', gameMoveUci: 'd1f3',
      replySan: null, replyUci: null, diffCp: null, evalText: null,
      session: base({ points: 0, maxPoints: 10, movesPlayed: 1, history: HISTORY,
                      position: { ply: 10, moveNumber: 6, whiteToMove: true, fen: FEN10, lastMoveUci: 'd6e5' } }),
    });
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

  it('waehrend des Durchklickens wird kein Zug angenommen', fakeAsync(() => {
    // Sonst wuerde ein Zug in einer ALTEN Stellung als Rateversuch fuer die aktuelle Aufgabe gewertet.
    const c = load();
    c.browse(0);
    c.onMove({ from: 'd1', to: 'f3', san: 'Qxf3', fen: 'egal' });
    http.expectNone('/api/guess-sessions/3/guess');

    c.skip();                                   // auch Passen muss waehrenddessen wirkungslos sein
    http.expectNone('/api/guess-sessions/3/guess');

    c.browse(null);
    guess(c, 'd1', 'f3', 'Qxf3', { grade: 'gameMove', points: 5, playedSan: 'Qxf3', diffCp: 0 });
    tick(1000);                                 // erst der Partiezug, dann die Antwort
    expect(c.browsing).withContext('nach dem Zug steht die naechste Aufgabe').toBeFalse();
    expect(c.viewFen).toBe(FEN10);
  }));

  it('startet bei Zug 1, nicht bei der ersten Aufgabe', () => {
    const c = loadAtStart();
    expect(c.browseIndex).withContext('Grundstellung').toBe(-1);
    expect(c.viewFen).toBe(START);
    expect(c.canGuess).withContext('erst durchklicken').toBeFalse();

    c.browse(0);                                   // 4.dxe5 — noch nicht die Aufgabe
    expect(c.canGuess).toBeFalse();

    c.browse(HISTORY.length - 1);                    // letzter Eroeffnungszug = die Aufgabe
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

  it('die Pfeile fuehren nie ueber die Aufgabe hinaus', () => {
    // Der Verlauf endet beim Zug VOR der Aufgabe — ein Schritt weiter waere die Loesung.
    const c = loadAtStart();
    for (let i = 0; i < 10; i++) c.step(1);
    expect(c.atTask).toBeTrue();
    expect(c.viewFen).withContext('die Aufgabe, nicht die Stellung danach').toBe(FEN8);
    expect(c.browseIndex === null || c.browseIndex === HISTORY.length - 1).toBeTrue();
  });

  it('vor und zurueck blaettern durch den Verlauf', () => {
    const c = loadAtStart();
    expect(c.atStart).toBeTrue();
    expect(c.atTask).toBeFalse();

    c.step(1);
    expect(c.browseIndex).toBe(0);
    c.step(1);
    // Am Ende des Verlaufs steht die Aufgabe — dafuer ist `null` die kanonische Form.
    expect(c.browseIndex).toBeNull();
    expect(c.atTask).toBeTrue();

    c.step(-1);
    expect(c.browseIndex).toBe(0);
    c.step(-5);
    expect(c.browseIndex).withContext('nicht vor die Grundstellung').toBe(-1);
  });

  it('die Zugliste steht untereinander, je Zeile Weiss und Schwarz', () => {
    const c = loadAtStart();
    expect(c.historyRows.length).toBe(1);              // 4.dxe5 Bxf3 = eine Zeile
    expect(c.historyRows[0].no).toBe(4);
    expect(c.historyRows[0].w).toBe('dxe5');
    expect(c.historyRows[0].b).toBe('Bxf3');
    expect(c.historyRows[0].wIdx).toBe(0);
    expect(c.historyRows[0].bIdx).toBe(1);
  });

  /**
   * Die Kommentare einer Meisterpartie sind der Grund, sie zu spielen — in der Zugliste muessen die
   * kommentierten Zuege deshalb erkennbar und wieder anklickbar sein.
   */
  it('markiert kommentierte Zuege und zeigt den Kommentar beim Anklicken', () => {
    const c = load({
      history: [
        { ...HISTORY[0], comment: 'der Schluesselzug' },
        { ...HISTORY[1] },
      ],
    });

    const rows = c.historyRows;
    expect(rows[0].wNoted).withContext('4.dxe5 ist kommentiert').toBeTrue();
    expect(rows[0].bNoted).withContext('4...Bxf3 nicht').toBeFalse();

    // Auf der Aufgabe selbst gibt es nichts zu zeigen — dort waere ein Kommentar die Loesung.
    c.browse(null);
    expect(c.browsedComment).toBeNull();

    c.browse(0);
    expect(c.browsedComment).toEqual({ move: '4.dxe5', text: 'der Schluesselzug' });

    c.browse(1);
    expect(c.browsedComment).withContext('dieser Zug hat keinen').toBeNull();
  });

  /**
   * Ein Kommentar am eben gespielten Partiezug ist der Grund, die Partie zu spielen — da darf das
   * Brett nicht nach einer Sekunde weiterspringen. Es haelt an, zeigt den Text, und erst „Weiter"
   * spielt die Antwort des Gegners.
   */
  it('haelt am kommentierten Partiezug an, statt weiterzuspringen', fakeAsync(() => {
    const c = load();
    const withNote = {
      ...nextSession,
      history: [...HISTORY, { ply: 8, moveNumber: 5, white: true, san: 'Qxf3', uci: 'd1f3',
                              fen: 'nach-Qxf3', comment: 'der entscheidende Zug' }],
    };
    guess(c, 'd1', 'f3', 'Qxf3', { grade: 'gameMove', points: 5, playedSan: 'Qxf3', diffCp: 0,
                                   session: withNote });

    expect(c.holding).withContext('haelt am Kommentar').toBeTrue();
    expect(c.holdingNote).toBeTrue();
    expect(c.browsedComment).toEqual({ move: '5.Qxf3', text: 'der entscheidende Zug' });

    tick(2000);                                  // keine Zeitschranke raeumt das weg
    expect(c.holding).withContext('immer noch da').toBeTrue();
    expect(c.boardFen).not.toBe(FEN10);

    c.continueGame();
    expect(c.holdingNote).toBeFalse();
    expect(c.boardFen).withContext('jetzt die Antwort').toBe(FEN10);
  }));

  /** Der Kommentar zur ANTWORT des Gegners blockiert nicht — er steht bei der neuen Aufgabe. */
  it('zeigt den Kommentar zur Antwort bei der naechsten Aufgabe', fakeAsync(() => {
    const c = load();
    const withReplyNote = {
      ...nextSession,
      history: [...HISTORY,
        { ply: 8, moveNumber: 5, white: true, san: 'Qxf3', uci: 'd1f3', fen: 'nach-Qxf3' },
        { ply: 9, moveNumber: 5, white: false, san: 'dxe5', uci: 'd6e5', fen: FEN10,
          comment: 'und jetzt steht Schwarz besser' }],
    };
    guess(c, 'd1', 'f3', 'Qxf3', { grade: 'gameMove', points: 5, playedSan: 'Qxf3', diffCp: 0,
                                   session: withReplyNote });
    expect(c.holdingNote).withContext('kein Halt, der Zug selbst ist unkommentiert').toBeFalse();

    tick(1000);
    expect(c.boardFen).toBe(FEN10);
    expect(c.browsedComment).toEqual({ move: '5…dxe5', text: 'und jetzt steht Schwarz besser' });
  }));

  /**
   * Wird eine Stellung uebersprungen (die Engine fuehrt den Partiezug nicht unter ihren
   * Kandidaten), kommen MEHR als zwei Halbzuege dazu. Frueher sprang das Brett in einem Satz
   * darueber — gemeldet als „nach Bxe7 spielt er sofort 3 Zuege". Jetzt laeuft jeder einzeln.
   */
  it('spielt uebersprungene Zuege einzeln ab und markiert sie', fakeAsync(() => {
    const FEN11 = 'rn1qkbnr/ppp2ppp/8/4p3/4P3/5Q2/PPP2PPP/RNB1KB1R b KQkq - 1 6';
    const FEN12 = 'rn1qkb1r/ppp2ppp/5n2/4p3/4P3/5Q2/PPP2PPP/RNB1KB1R w KQkq - 2 7';
    const skippy = base({
      points: 5, maxPoints: 10, movesPlayed: 1,
      position: { ply: 12, moveNumber: 7, whiteToMove: true, fen: FEN12, lastMoveUci: 'g8f6' },
      history: [...HISTORY, ...PLAYED,
        // 6.Qf3-f4 wurde NICHT abgefragt: nicht wertbar.
        { ply: 10, moveNumber: 6, white: true, san: 'Qf4', uci: 'f3f4', fen: FEN11,
          skipped: 'notScorable' },
        { ply: 11, moveNumber: 6, white: false, san: 'Nf6', uci: 'g8f6', fen: FEN12 }],
    });
    const c = load();
    guess(c, 'd1', 'f3', 'Qxf3', { grade: 'gameMove', points: 5, playedSan: 'Qxf3', diffCp: 0,
                                   session: skippy });

    expect(c.boardFen).withContext('1. Schritt: der Partiezug').toBe(FEN9);
    tick(1000);
    expect(c.boardFen).withContext('2. Schritt: die Antwort').toBe(FEN10);
    tick(500);
    expect(c.boardFen).withContext('3. Schritt: der uebersprungene Zug').toBe(FEN11);
    tick(500);
    expect(c.boardFen).withContext('4. Schritt: und die Antwort darauf').toBe(FEN12);
    tick(500);
    expect(c.canGuess).withContext('erst jetzt ist der Nutzer dran').toBeTrue();

    // In der Zugliste ist der uebersprungene Zug als solcher erkennbar — samt Begruendung.
    const row = c.historyRows.find(r => r.w === 'Qf4')!;
    expect(row.wSkipped).toBe('notScorable');
    c.browse(c.session!.history.findIndex((h: GuessHistoryMove) => h.ply === 10));
    expect(c.browsedSkip).withContext('sagt, warum nicht gefragt wurde').toBeTruthy();
  }));
});
