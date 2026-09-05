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
