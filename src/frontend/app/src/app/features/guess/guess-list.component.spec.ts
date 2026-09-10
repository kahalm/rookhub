import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { GuessListComponent } from './guess-list.component';
import { GameAnalysis } from '../analysis/game-analysis.service';
import { AuthService } from '../../core/auth.service';

function analysis(over: Partial<GameAnalysis> = {}): GameAnalysis {
  return {
    id: 1, title: 'A – B', white: 'A', black: 'B', result: '1-0', event: null,
    targetDepth: 20, multiPv: 5, engineId: 'eei_x', status: 'running',
    plyCount: 40, analyzedPlies: 0, lastError: null, isPublic: false, annotated: false,
    createdAt: '2026-09-10T10:00:00Z', finishedAt: null, ...over,
  };
}

/**
 * Die Punktepartie-Übersicht mit dem EINWURF: eine eigene Partie hineinwerfen, ohne Tiefe und ohne
 * Linienzahl — beides setzt der Server. Geprüft wird deshalb vor allem, was NICHT mitgeschickt wird.
 */
describe('GuessListComponent', () => {
  let http: HttpTestingController;

  /** Angemeldet: nur dann gibt es eigene Analysen und den Einwurf. */
  function setup(loggedIn = true) {
    TestBed.configureTestingModule({
      imports: [GuessListComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(GuessListComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  /** Ohne diesen Abruf wüsste die Seite nicht, ob überhaupt eine Engine bereitsteht. Der
   *  Filter-Zustand (`view-state`) hängt am Konto und kommt bei jedem Aufbau mit. */
  function flushInitial(own: GameAnalysis[] = [], status: Partial<{ engineAvailable: boolean; ownEngine: boolean; openGames: number; maxGames: number }> = {}) {
    http.expectOne('/api/view-state/guess.list').flush({ annotatedOnly: false });
    http.expectOne('/api/game-analyses/public').flush([]);
    http.expectOne('/api/game-analyses').flush(own);
    http.expectOne('/api/game-analyses/guess/status').flush({
      engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5, ...status,
    });
    http.expectOne('/api/guess-sessions').flush([]);
  }

  it('wirft ein PGN OHNE Tiefe und Linienzahl ein — die setzt der Server', () => {
    const fixture = setup();
    flushInitial();

    fixture.componentInstance.pgn = '1. e4 e5';
    fixture.componentInstance.upload();

    const req = http.expectOne(r => r.method === 'POST' && r.url === '/api/game-analyses/guess');
    expect(req.request.body.pgn).toBe('1. e4 e5');
    // Die eiserne Regel dieses Wegs: kein Regler, also auch kein Feld.
    expect(req.request.body.targetDepth).toBeUndefined();
    expect(req.request.body.multiPv).toBeUndefined();
    req.flush(analysis({ id: 7 }));

    // Danach frischt die Seite Liste und Kontingent auf.
    http.expectOne('/api/game-analyses').flush([analysis({ id: 7 })]);
    http.expectOne('/api/game-analyses/guess/status').flush({
      engineAvailable: true, ownEngine: false, openGames: 1, maxGames: 5,
    });
    expect(fixture.componentInstance.pgn).toBe('');
    expect(fixture.componentInstance.uploading).toBeFalse();
  });

  it('behält das PGN, wenn der Server absagt', () => {
    const fixture = setup();
    flushInitial();

    fixture.componentInstance.pgn = 'kein PGN';
    fixture.componentInstance.upload();
    http.expectOne('/api/game-analyses/guess')
      .flush({ reason: 'invalid-pgn' }, { status: 400, statusText: 'Bad Request' });

    // Eingabe stehen lassen: der Nutzer soll sie korrigieren können, nicht neu suchen.
    expect(fixture.componentInstance.pgn).toBe('kein PGN');
    expect(fixture.componentInstance.uploading).toBeFalse();
  });

  /** Eine gerade eingeworfene Partie hat NULL gerechnete Stellungen. Stünde sie deshalb nicht in
   *  der Liste, hielte der Nutzer den Einwurf für verloren und wiederholte ihn. */
  it('zeigt auch die noch rechnende Partie, aber ungespielt', () => {
    const fixture = setup();
    flushInitial([analysis({ id: 3, analyzedPlies: 0, status: 'pending' })]);

    expect(fixture.componentInstance.ownGames.length).toBe(1);
    expect(fixture.componentInstance.percent(fixture.componentInstance.ownGames[0])).toBe(0);

    fixture.componentInstance.ownGames = [analysis({ analyzedPlies: 10, plyCount: 40 })];
    expect(fixture.componentInstance.percent(fixture.componentInstance.ownGames[0])).toBe(25);
  });

  it('fragt ohne Anmeldung weder eigene Partien noch das Kontingent ab', () => {
    setup(false);
    http.expectOne('/api/game-analyses/public').flush([]);
    // Ohne Konto laeuft alles ueber die anonyme Sitzung — und der Einwurf gar nicht.
    http.expectOne(r => r.url === '/api/guess-sessions/anonymous').flush([]);
    http.expectNone('/api/game-analyses/guess/status');
    http.expectNone('/api/game-analyses');
  });
});
