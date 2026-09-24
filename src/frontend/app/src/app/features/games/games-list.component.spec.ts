import { TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ANALYSIS_POLL_MS, GamesListComponent } from './games-list.component';

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
    // Danach holt die Liste den Stand — der Knopf wird zum Fortschritt.
    http.expectOne(req => req.method === 'GET' && req.url.startsWith('/api/games?')).flush([]);
  });

  const game = (analysis: object | null) => ({
    id: 4, source: 'lichess', white: 'a', black: 'b', result: '1-0', moveCount: 40, shareToken: 't',
    createdAt: '2026-07-16T00:00:00Z', analysis,
  });
  const listRequest = (req: { method: string; url: string }) =>
    req.method === 'GET' && req.url.startsWith('/api/games') && !req.url.startsWith('/api/games/');

  // Gewünscht 2026-09-24: „anstelle des Icons die % stehen, die schon analysiert sind (und sich aktualisieren)".
  it('running analysis: shows the percentage instead of the analyze button and refreshes every 10 s while it runs', fakeAsync(async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne(listRequest).flush([game({ status: 'running', analyzed: 10, total: 40 })]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 1, maxGames: 5 });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('button.analyze')).toBeNull();
    expect(el.querySelector('.progress')!.textContent!.trim()).toBe('25 %');
    expect(el.querySelector('.accuracy')).toBeNull();

    tick(ANALYSIS_POLL_MS);
    http.expectOne(listRequest).flush([game({ status: 'running', analyzed: 30, total: 40 })]);
    fixture.detectChanges();
    expect(el.querySelector('.progress')!.textContent!.trim()).toBe('75 %');

    // Fertig: kein Nachfragen mehr, kein Knopf, die Genauigkeit beider Seiten bei den Partie-Daten.
    tick(ANALYSIS_POLL_MS);
    http.expectOne(listRequest).flush([game({ status: 'done', analyzed: 40, total: 40, accuracyWhite: 87.4, accuracyBlack: 71.6 })]);
    fixture.detectChanges();
    expect(el.querySelector('.progress')).toBeNull();
    expect(el.querySelector('button.analyze')).toBeNull();
    expect(el.querySelector('.accuracy')!.textContent!.replace(/\s+/g, ' ').trim()).toBe('♔ 87 % · ♚ 72 %');
    tick(ANALYSIS_POLL_MS);
    http.expectNone(listRequest);
    discardPeriodicTasks();
  }));

  it('no or failed analysis: the analyze button stays; a side without a rated move shows a dash', fakeAsync(async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne(listRequest).flush([
      game(null),
      { ...game({ status: 'failed', analyzed: 3, total: 40 }), id: 5 },
      { ...game({ status: 'done', analyzed: 1, total: 1, accuracyWhite: 100, accuracyBlack: null }), id: 6 },
    ]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('button.analyze').length).toBe(2);
    expect(el.querySelectorAll('.progress').length).toBe(0);
    expect(el.querySelector('.accuracy')!.textContent!.replace(/\s+/g, ' ').trim()).toBe('♔ 100 % · ♚ —');
    tick(ANALYSIS_POLL_MS);
    http.expectNone(listRequest);   // nichts läuft → kein Nachfragen
    discardPeriodicTasks();
  }));

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
