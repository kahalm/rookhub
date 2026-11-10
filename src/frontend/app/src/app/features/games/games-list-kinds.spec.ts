import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { GamesListComponent, parseGameKind } from './games-list.component';

/** Laschen Alle / Online / Am Brett (Wunsch 10.11.2026): Lasche ↔ Adresse (`?kind=`) ↔ Aufruf `GET /api/games?kind=`. */
describe('GamesListComponent — Laschen', () => {
  const game = (id: number, source = 'lichess') => ({
    id, source, white: 'a', black: 'b', result: '1-0', moveCount: 3, shareToken: 't' + id, createdAt: '2026-07-16T00:00:00Z',
  });
  const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.startsWith('/api/games?');

  async function open(url: string) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideRouter([{ path: 'games', component: GamesListComponent }]),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    });
    TestBed.inject(TranslateService).use('en');
    TestBed.inject(TranslateService).setTranslation('en', {
      games: { kind: { all: 'All', online: 'Online', otb: 'Over the board', emptyOnline: 'No online', emptyOtb: 'No otb', label: 'Kind' } },
    });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, GamesListComponent);
    return { harness, http, el: harness.routeNativeElement as HTMLElement };
  }

  function answerStatus(http: HttpTestingController): void {
    http.match('/api/game-analyses/guess/status')
      .forEach(r => r.flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 }));
  }

  const tabs = (el: HTMLElement) => Array.from(el.querySelectorAll<HTMLAnchorElement>('a.kind-tab'));

  it('parseGameKind: known values pass, anything else is „all“', () => {
    expect(parseGameKind('online')).toBe('online');
    expect(parseGameKind('otb')).toBe('otb');
    expect(parseGameKind('all')).toBe('all');
    expect(parseGameKind('bogus')).toBe('all');
    expect(parseGameKind(null)).toBe('all');
  });

  it('without ?kind= asks for all games (no kind parameter), shows three tabs with counts, „All“ active', async () => {
    const { harness, http, el } = await open('/games');
    const list: TestRequest = http.expectOne(isList);
    expect(list.request.url).toBe('/api/games?take=200');
    list.flush([game(1), game(2, 'scoresheet')]);
    http.expectOne('/api/games/kind-counts').flush({ all: 2, online: 1, otb: 1 });
    answerStatus(http);
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(tabs(el).map(a => a.textContent!.trim().replace(/\s+/g, ' '))).toEqual(['All (2)', 'Online (1)', 'Over the board (1)']);
    expect(tabs(el).map(a => a.classList.contains('mdc-tab--active'))).toEqual([true, false, false]);
  });

  it('?kind=otb opens the OTB tab and passes kind=otb to the server', async () => {
    const { harness, http, el } = await open('/games?kind=otb');
    const list = http.expectOne(isList);
    expect(list.request.url).toBe('/api/games?take=200&kind=otb');
    list.flush([game(2, 'scoresheet')]);
    http.expectOne('/api/games/kind-counts').flush({ all: 2, online: 1, otb: 1 });
    answerStatus(http);
    harness.detectChanges();
    await harness.fixture.whenStable();
    expect(el.querySelector('a.kind-tab.mdc-tab--active')!.getAttribute('data-kind')).toBe('otb');
  });

  it('clicking a tab writes ?kind= into the URL and reloads the list; „All“ removes the parameter again', async () => {
    const { harness, http, el } = await open('/games');
    http.expectOne(isList).flush([game(1)]);
    http.expectOne('/api/games/kind-counts').flush({ all: 1, online: 1, otb: 0 });
    answerStatus(http);
    harness.detectChanges();
    await harness.fixture.whenStable();
    const router = TestBed.inject(Router);

    tabs(el).find(a => a.dataset['kind'] === 'online')!.click();
    await harness.fixture.whenStable();
    expect(router.url).toBe('/games?kind=online');
    const online = http.expectOne(isList);
    expect(online.request.url).toBe('/api/games?take=200&kind=online');
    online.flush([game(1)]);
    harness.detectChanges();

    tabs(el).find(a => a.dataset['kind'] === 'all')!.click();
    await harness.fixture.whenStable();
    expect(router.url).toBe('/games');
    expect(http.expectOne(isList).request.url).toBe('/api/games?take=200');
  });

  it('an empty tab shows its own sentence, keeps the tabs and not the account-wide empty state', async () => {
    const { harness, http, el } = await open('/games?kind=online');
    http.expectOne(isList).flush([]);
    http.expectOne('/api/games/kind-counts').flush({ all: 3, online: 0, otb: 3 });
    answerStatus(http);
    harness.detectChanges();
    await harness.fixture.whenStable();
    expect(el.querySelector('.kind-empty')?.textContent?.trim()).toBe('No online');
    expect(el.querySelector('.empty-state')).toBeNull();
    expect(tabs(el).length).toBe(3);
  });

  it('search runs within the tab: q and kind travel together', async () => {
    const { harness, http } = await open('/games?kind=online');
    http.expectOne(isList).flush([game(1)]);
    http.expectOne('/api/games/kind-counts').flush({ all: 1, online: 1, otb: 0 });
    answerStatus(http);
    harness.detectChanges();
    const comp = harness.routeDebugElement!.componentInstance as GamesListComponent;
    comp.onSearch('carlsen');
    await new Promise(r => setTimeout(r, 350));
    expect(http.expectOne(isList).request.url).toBe('/api/games?take=500&q=carlsen&kind=online');
  });
});
