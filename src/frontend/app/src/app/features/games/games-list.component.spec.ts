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

  // „Partie analysieren" in der Liste: die Liste trägt kein PGN, also erst die Partie nachladen, dann derselbe
  // Einwurf wie auf der geteilten Partie und der Punktepartie-Seite, danach dorthin.
  it('analyze: loads the PGN, posts it to the guess upload with the players as title, goes to the points-game page', async () => {
    const { fixture, http } = await setup();
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges(); // ngOnInit: Liste, Profil, Status
    http.expectOne(req => req.method === 'GET' && req.url.startsWith('/api/games') && !req.url.startsWith('/api/games/'))
      .flush([{ id: 4, source: 'lichess', white: 'a', black: 'b', result: '1-0', moveCount: 3, shareToken: 't', createdAt: '2026-07-16T00:00:00Z' }]);
    http.expectOne('/api/profile').flush({});
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).click();

    const pgn = '[White "a"]\n[Black "b"]\n\n1. e4 c5 0-1';
    http.expectOne('/api/games/4').flush({ id: 4, pgn });
    const post = http.expectOne({ method: 'POST', url: '/api/game-analyses/guess' });
    expect(post.request.body).toEqual({ pgn, title: 'a – b' });
    post.flush({ id: 9 });
    expect(navigate).toHaveBeenCalledWith(['/guess']);
    expect(fixture.componentInstance.analyzingId).toBeNull();
  });
});
