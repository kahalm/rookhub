import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialog } from '@angular/material/dialog';
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
    http.expectOne('/api/profile').flush({});
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).click();

    http.expectNone('/api/games/4');
    const post = http.expectOne({ method: 'POST', url: '/api/games/4/analyze' });
    post.flush({ analysis: { id: 9 }, reused: true });
    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.componentInstance.analyzingId).toBeNull();
  });

  it('replay: the dialog gets the graph and the analyse button of THIS game', async () => {
    const { fixture, http } = await setup();
    // Die Instanz der KOMPONENTE: MatDialogModule in den Standalone-Imports kann einen eigenen MatDialog
    // mitbringen, TestBed.inject läge dann daneben.
    const dialog = (fixture.componentInstance as unknown as { dialog: MatDialog }).dialog;
    const open = spyOn(dialog, 'open').and.returnValue({} as never);
    fixture.detectChanges();
    http.expectOne(req => req.method === 'GET' && req.url.startsWith('/api/games') && !req.url.startsWith('/api/games/'))
      .flush([{ id: 4, source: 'lichess', white: 'a', black: 'b', result: '1-0', moveCount: 3, shareToken: 't', createdAt: '2026-07-16T00:00:00Z' }]);
    http.expectOne('/api/profile').flush({});
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });

    fixture.componentInstance.replay({ id: 4, source: 'lichess', shareToken: 't', moveCount: 3, createdAt: '' });
    http.expectOne('/api/games/4').flush({ id: 4, pgn: '1. e4 c5 *' });

    const data = open.calls.mostRecent().args[1]!.data as { evalsUrl: string; analyzeUrl: string };
    expect(data.evalsUrl).toBe('/api/games/4/evals');
    expect(data.analyzeUrl).toBe('/api/games/4/analyze');
  });
});
