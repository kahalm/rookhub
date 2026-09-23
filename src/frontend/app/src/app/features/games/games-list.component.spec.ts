import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { GamesListComponent } from './games-list.component';

describe('GamesListComponent', () => {
  async function setup() {
    await TestBed.configureTestingModule({
      imports: [GamesListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    return { fixture: TestBed.createComponent(GamesListComponent), http: TestBed.inject(HttpTestingController) };
  }

  it('creates (template AOT-compiles + DI resolves)', async () => {
    const { fixture } = await setup();
    expect(fixture.componentInstance).toBeTruthy();
  });

  // „Partie analysieren" in der Liste (seit 0.512.0): kein PGN mehr nachladen — der Server hat es —, und man
  // bleibt auf der Seite; die Kurve steht danach im Nachspiel-Dialog. Vorher ging es auf /guess, wo eine
  // solche Analyse gar nicht mehr erscheint.
  it('analyze: posts to the game\'s analyze endpoint, loads no PGN and stays on the page', async () => {
    const { fixture, http } = await setup();
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges(); // ngOnInit: Liste, Profil, Status
    http.expectOne(req => req.method === 'GET' && req.url.startsWith('/api/games') && !req.url.startsWith('/api/games/'))
      .flush([{ id: 4, source: 'lichess', white: 'a', black: 'b', result: '1-0', moveCount: 3, shareToken: 't', createdAt: '2026-07-16T00:00:00Z' }]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).click();

    http.expectNone('/api/games/4');
    const post = http.expectOne({ method: 'POST', url: '/api/games/4/analyze' });
    post.flush({ analysis: { id: 9 }, reused: true });
    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.componentInstance.analyzingId).toBeNull();
  });

  it('opens a game as a page: name and play button link to /games/:id (no dialog since 0.513.0)', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne(req => req.method === 'GET' && req.url.startsWith('/api/games') && !req.url.startsWith('/api/games/'))
      .flush([{ id: 4, source: 'lichess', white: 'a', black: 'b', result: '1-0', moveCount: 3, shareToken: 't', createdAt: '2026-07-16T00:00:00Z' }]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();

    const links = Array.from(fixture.nativeElement.querySelectorAll('a[href="/games/4"]')) as HTMLAnchorElement[];
    expect(links.length).toBe(2);   // Spielernamen + Abspiel-Knopf
    http.expectNone('/api/games/4');
  });
});
