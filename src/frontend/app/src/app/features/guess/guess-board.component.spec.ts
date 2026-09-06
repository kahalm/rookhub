import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { GuessBoardComponent } from './guess-board.component';
import { GuessSession } from './guess.service';

function session(over: Partial<GuessSession> = {}): GuessSession {
  return {
    id: 3, gameAnalysisId: 1, title: 'A – B', white: 'A', black: 'B',
    guessWhite: true, startPly: 8, status: 'running',
    points: 0, maxPoints: 0, movesPlayed: 0, gameMoveHits: 0, secondsSpent: 0,
    position: { ply: 8, moveNumber: 5, whiteToMove: true, fen: 'fen-8', lastMoveUci: 'e7e5' },
    totalGuesses: 10, ...over,
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
      { ply: 8, moveNumber: 5, white: true, gameSan: 'Nf3', playedSan: 'Nf3', grade: 'gameMove', points: 5, diffCp: 0, secondsSpent: 9 },
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
  });
});

/**
 * Ein Zug, der NICHT der Partiezug war, bleibt auf dem Brett stehen; darunter steht, was die Partie
 * gespielt hat, und erst „Weiter" holt die naechste Aufgabe. Der Abstand zum Partiezug wird nur bei
 * einem SCHLECHTEN Zug genannt — bei gleichwertig/besser genuegt der Partiezug selbst.
 */
describe('GuessBoardComponent Zug stehen lassen', () => {
  const FEN8 = 'rn1qkbnr/ppp2ppp/3p4/4P3/4P3/5b2/PPP2PPP/RNBQKB1R w KQkq - 0 5';
  const FEN10 = 'rn1qkbnr/ppp2ppp/8/4p3/4P3/5Q2/PPP2PPP/RNB1KB1R w KQkq - 0 6';
  let http: HttpTestingController;

  function base(over: Partial<GuessSession> = {}): GuessSession {
    return {
      id: 3, gameAnalysisId: 1, title: 'A - B', white: 'A', black: 'B',
      guessWhite: true, startPly: 8, status: 'running',
      points: 0, maxPoints: 0, movesPlayed: 0, gameMoveHits: 0, secondsSpent: 0,
      position: { ply: 8, moveNumber: 5, whiteToMove: true, fen: FEN8, lastMoveUci: 'c8g4' },
      totalGuesses: 10, ...over,
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

  function load() {
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

  it('schlechter Zug nennt den Abstand, gleichwertiger nicht', () => {
    const c1 = load();
    guess(c1, 'f1', 'c4', 'Bc4', { grade: 'muchWorse', points: -2, diffCp: -240 });
    expect(c1.isPoor).toBeTrue();
    expect(c1.evalDelta).toBe('-2.40');

    const c2 = load();
    guess(c2, 'f1', 'c4', 'Bc4', { grade: 'similar', points: 2, diffCp: -5 });
    expect(c2.isPoor).toBeFalse();
    expect(c2.evalDelta).withContext('gut oder besser: nur der Partiezug').toBeNull();

    const c3 = load();
    guess(c3, 'f1', 'c4', 'Bc4', { grade: 'clearlyBetter', points: 10, diffCp: 60 });
    expect(c3.evalDelta).toBeNull();
  });
});
