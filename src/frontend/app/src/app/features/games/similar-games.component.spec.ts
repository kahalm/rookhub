import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
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
        { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
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

  // 0.567.0: nach dem Anfordern Fortschritt statt sofort „Spielen" — gespielt wird erst, wenn die Partie fertig gerechnet ist.
  it('request (signed in) shows the progress until the analysis is done, then play; without an account it asks to sign in', fakeAsync(() => {
    const { fixture, http, el } = setup();
    open(fixture);
    http.expectOne(url).flush(data(null));
    fixture.detectChanges();

    (el.querySelector('button.request') as HTMLButtonElement).click();
    const analysis = (status: string, analyzedPlies: number) => ({ id: 99, status, plyCount: 70, analyzedPlies });
    http.expectOne({ method: 'POST', url: '/api/library-games/7/request' }).flush({ analysis: analysis('pending', 0), alreadyPlayable: false });
    http.expectOne(r => r.method === 'GET' && r.url === '/api/game-analyses').flush([analysis('running', 35)]);
    fixture.detectChanges();
    expect(el.querySelector('button.request')).toBeNull();
    expect(el.querySelector('button.play')).toBeNull();
    expect(el.querySelector('.computing')).not.toBeNull();
    expect(fixture.componentInstance.percent(fixture.componentInstance.data()!.items[0])).toBe(50);

    tick(SimilarGamesComponent.PollMs);
    http.expectOne(r => r.method === 'GET' && r.url === '/api/game-analyses').flush([analysis('done', 70)]);
    fixture.detectChanges();
    expect(el.querySelector('.computing')).toBeNull();
    expect(el.querySelector('button.play')).not.toBeNull();
    tick(SimilarGamesComponent.PollMs);
    http.expectNone(r => r.url === '/api/game-analyses');   // fertig → Ruhe

    TestBed.resetTestingModule();
    const anon = setup(false);
    open(anon.fixture);
    anon.http.expectOne(url).flush(data(null));
    anon.fixture.detectChanges();
    expect(anon.el.querySelector('button.request')).toBeNull();
    expect(anon.el.querySelector('button.login')).not.toBeNull();
    expect(anon.el.querySelector('button.view')).toBeNull();   // Anschauen nur angemeldet
  }));

  it('an already requested game that is still computing shows its progress right after loading', () => {
    const { fixture, http, el } = setup();
    open(fixture);
    const d = data(99);
    d.items[0].game.inPool = false;
    d.items[0].game.requested = true;
    http.expectOne(url).flush(d);
    http.expectOne(r => r.url === '/api/game-analyses').flush([{ id: 99, status: 'failed', plyCount: 70, analyzedPlies: 3 }]);
    fixture.detectChanges();
    expect(el.querySelector('button.request')!.textContent).toContain('games.similar.retry');
  });

  it('view loads the annotated game and opens it in the replay dialog', async () => {
    const { fixture, http, el } = setup();
    open(fixture);
    http.expectOne(url).flush(data(12));
    fixture.detectChanges();

    (el.querySelector('button.view') as HTMLButtonElement).click();
    http.expectOne(r => r.method === 'GET' && r.url === '/api/library-games/7/view').flush({ id: 7, pgn: '[Event "x"]\n\n1. e4 *', language: 'de', languages: ['de', 'en'] });
    const dialog = TestBed.inject(MatDialog) as jasmine.SpyObj<MatDialog>;
    // Der Dialog wird erst beim Klick nachgeladen (dynamischer Import) — kurz warten, bis er da ist.
    for (let i = 0; i < 100 && !dialog.open.calls.count(); i++) await new Promise(r => setTimeout(r, 20));
    expect(dialog.open).toHaveBeenCalled();
    expect((dialog.open.calls.mostRecent().args[1] as { data: { pgn: string } }).data.pgn).toContain('1. e4');
  });

  it('says so when nothing matched', () => {
    const { fixture, http, el } = setup();
    open(fixture);
    http.expectOne(url).flush({ opening: null, sharedPlies: 0, sharedLine: null, items: [] });
    fixture.detectChanges();
    expect(el.textContent).toContain('games.similar.none');
  });
});
