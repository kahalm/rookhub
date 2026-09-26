import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SimilarGames } from './games.service';
import { SimilarGamesComponent } from './similar-games.component';

describe('SimilarGamesComponent', () => {
  const url = '/api/games/4/similar';

  function setup(loggedIn = true) {
    TestBed.configureTestingModule({
      imports: [SimilarGamesComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
      ],
    });
    const fixture = TestBed.createComponent(SimilarGamesComponent);
    fixture.componentRef.setInput('url', url);
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
  }

  const game = (id: number, gameAnalysisId: number | null) => ({
    id, white: 'Marshall', black: 'Capablanca', whiteElo: null, blackElo: null, result: '0-1', event: 'New York',
    playedOn: '1918-10-23', eco: null, plyCount: 70, annotator: 'Alekhine', commentedPlies: 30, commentChars: 4000,
    languages: 'en', score: 90, sourceTitle: null, inPool: gameAnalysisId != null, requested: false, gameAnalysisId,
  });
  const data = (gameAnalysisId: number | null): SimilarGames => ({
    opening: 'Ruy Lopez', sharedPlies: 13, sharedLine: '1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.O-O Be7 6.Re1 b5 7.Bb3',
    items: [{ game: game(7, gameAnalysisId), sharedPlies: 13, lastSharedMove: '7.Bb3', masterMove: '7...O-O', gameMove: '7...d6' }],
  });

  function open(fixture: { nativeElement: HTMLElement; detectChanges(): void }) {
    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
  }

  it('loads only when opened, once, and names where the master turned off', () => {
    const { fixture, http, el } = setup();
    http.expectNone(url);

    open(fixture);
    http.expectOne(url).flush(data(12));
    fixture.detectChanges();
    expect(el.querySelector('.row strong')!.textContent).toContain('Marshall – Capablanca');
    expect(fixture.componentInstance.branch(data(12).items[0])).toBe('games.similar.branch');
    expect(el.querySelector('button.play')).not.toBeNull();

    open(fixture);   // wieder auf- und zuklappen lädt nicht erneut
    http.expectNone(url);
  });

  it('play starts a guess session on the analysis and goes there', () => {
    const { fixture, http, el } = setup();
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    open(fixture);
    http.expectOne(url).flush(data(12));
    fixture.detectChanges();

    (el.querySelector('button.play') as HTMLButtonElement).click();
    const req = http.expectOne(r => r.method === 'POST' && r.url.startsWith('/api/guess-sessions'));
    expect(req.request.body.gameAnalysisId).toBe(12);
    req.flush({ id: 55 });
    expect(nav).toHaveBeenCalledWith(['/guess', 55]);
  });

  it('request (signed in) turns the row into play; without an account it asks to sign in', () => {
    const { fixture, http, el } = setup();
    open(fixture);
    http.expectOne(url).flush(data(null));
    fixture.detectChanges();

    (el.querySelector('button.request') as HTMLButtonElement).click();
    http.expectOne({ method: 'POST', url: '/api/library-games/7/request' }).flush({ analysis: { id: 99 }, alreadyPlayable: false });
    fixture.detectChanges();
    expect(el.querySelector('button.request')).toBeNull();
    expect(el.querySelector('button.play')).not.toBeNull();

    TestBed.resetTestingModule();
    const anon = setup(false);
    open(anon.fixture);
    anon.http.expectOne(url).flush(data(null));
    anon.fixture.detectChanges();
    expect(anon.el.querySelector('button.request')).toBeNull();
    expect(anon.el.querySelector('button.login')).not.toBeNull();
  });

  it('says so when nothing matched', () => {
    const { fixture, http, el } = setup();
    open(fixture);
    http.expectOne(url).flush({ opening: null, sharedPlies: 0, sharedLine: null, items: [] });
    fixture.detectChanges();
    expect(el.textContent).toContain('games.similar.none');
  });
});
