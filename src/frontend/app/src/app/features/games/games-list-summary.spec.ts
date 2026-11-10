import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { GamesListComponent, parseMine, summaryLabelKey } from './games-list.component';
import { SavedGameSummaryState } from './games.service';

/** Partienliste 0.954.0: Zusammenfassungs-Symbol je Zeile, Turnier unter den Namen, „Nur meine Partien“ ↔ `?mine=1` ↔ Aufruf. */
describe('GamesListComponent — Zusammenfassung, Turnier, Nur meine', () => {
  const game = (id: number, extra: object = {}) => ({
    id, source: 'pgn', white: 'a', black: 'b', result: '1-0', moveCount: 3, shareToken: 't' + id, createdAt: '2026-07-16T00:00:00Z', ...extra,
  });
  const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.startsWith('/api/games?');
  const isCounts = (r: { method: string; url: string }) => r.method === 'GET' && r.url.startsWith('/api/games/kind-counts');

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
      games: {
        onlyMine: 'Only my games', onlyMineEmpty: 'None of yours',
        summary: { open: 'Summary', requested: 'Summary requested …', running: 'Summary is being written …' },
      },
    });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, GamesListComponent);
    return { harness, http, el: harness.routeNativeElement as HTMLElement };
  }

  function answerRest(http: HttpTestingController, counts = { all: 3, online: 0, otb: 3 }): void {
    http.match(isCounts).forEach(r => r.flush(counts));
    http.match('/api/game-analyses/guess/status')
      .forEach(r => r.flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 }));
  }

  it('parseMine / summaryLabelKey', () => {
    expect(parseMine('1')).toBeTrue();
    expect(parseMine('true')).toBeTrue();
    expect(parseMine(null)).toBeFalse();
    expect(parseMine('0')).toBeFalse();
    const s = (status: SavedGameSummaryState['status']) => ({ status, link: '/x' });
    expect(summaryLabelKey(s('done'))).toBe('games.summary.open');
    expect(summaryLabelKey(s('requested'))).toBe('games.summary.requested');
    expect(summaryLabelKey(s('running'))).toBe('games.summary.running');
  });

  it('drei Zustände: fertig = Link mit Beschriftung, angefordert/läuft = Sanduhr ohne Knopf, keine = leerer Platz', async () => {
    const { harness, http, el } = await open('/games?kind=otb');
    http.expectOne(isList).flush([
      game(1, { summary: { status: 'done', link: '/club-games/38/summary' } }),
      game(2, { summary: { status: 'requested', link: '/games/2/summary' } }),
      game(3, { summary: { status: 'running', link: '/games/3/summary' } }),
      game(4),
    ]);
    answerRest(http);
    harness.detectChanges();
    await harness.fixture.whenStable();

    const rows = Array.from(el.querySelectorAll<HTMLElement>('.row.game'));
    const done = rows[0].querySelector<HTMLAnchorElement>('a.summary-done')!;
    expect(done.getAttribute('href')).toBe('/club-games/38/summary');
    expect(done.getAttribute('aria-label')).toBe('Summary');
    expect(done.textContent).toContain('article');

    const wait = rows[1].querySelector<HTMLElement>('.summary-wait')!;
    expect(wait.textContent).toContain('hourglass_top');
    expect(wait.getAttribute('aria-label')).toBe('Summary requested …');
    expect(rows[1].querySelector('a.summary-done')).toBeNull();
    expect(rows[2].querySelector('.summary-wait')!.getAttribute('aria-label')).toBe('Summary is being written …');

    expect(rows[3].querySelector('.summary-done, .summary-wait')).toBeNull();
    expect(rows[3].querySelector('.summary-none')).not.toBeNull();
  });

  it('Turnier steht unter den Namen, nur wenn der Server eins liefert', async () => {
    const { harness, http, el } = await open('/games?kind=otb');
    http.expectOne(isList).flush([game(1, { event: 'TMM Landesliga 2025/2026 · R5' }), game(2)]);
    answerRest(http);
    harness.detectChanges();
    await harness.fixture.whenStable();
    const rows = Array.from(el.querySelectorAll<HTMLElement>('.row.game'));
    expect(rows[0].querySelector('.players .event')?.textContent?.trim()).toBe('TMM Landesliga 2025/2026 · R5');
    expect(rows[1].querySelector('.event')).toBeNull();
  });

  it('„Nur meine“: Häkchen schreibt ?mine=1 (Lasche bleibt), lädt Liste und Zahlen mit mine=true; aus entfernt es wieder', async () => {
    const { harness, http, el } = await open('/games?kind=otb');
    const first = http.expectOne(isList);
    expect(first.request.url).toBe('/api/games?take=200&kind=otb');
    first.flush([game(1)]);
    expect(http.match(isCounts).map(r => { r.flush({ all: 3, online: 0, otb: 3 }); return r.request.url; })).toEqual(['/api/games/kind-counts']);
    answerRest(http);
    harness.detectChanges();
    await harness.fixture.whenStable();
    const router = TestBed.inject(Router);

    const box = el.querySelector<HTMLInputElement>('.only-mine input[type=checkbox]')!;
    expect(box.checked).toBeFalse();
    box.click();
    await harness.fixture.whenStable();
    expect(router.url).toBe('/games?kind=otb&mine=1');
    expect(http.expectOne(isList).request.url).toBe('/api/games?take=200&kind=otb&mine=true');
    expect(http.expectOne(isCounts).request.url).toBe('/api/games/kind-counts?mine=true');
    harness.detectChanges();
    expect(el.querySelector<HTMLInputElement>('.only-mine input[type=checkbox]')!.checked).toBeTrue();

    el.querySelector<HTMLInputElement>('.only-mine input[type=checkbox]')!.click();
    await harness.fixture.whenStable();
    expect(router.url).toBe('/games?kind=otb');
    expect(http.expectOne(isList).request.url).toBe('/api/games?take=200&kind=otb');
    expect(http.expectOne(isCounts).request.url).toBe('/api/games/kind-counts');
  });

  it('?mine=1 in der Adresse: Häkchen gesetzt, erster Aufruf schon gefiltert, Laschenwechsel behält es', async () => {
    const { harness, http, el } = await open('/games?mine=1');
    expect(http.expectOne(isList).request.url).toBe('/api/games?take=200&mine=true');
    expect(http.expectOne(isCounts).request.url).toBe('/api/games/kind-counts?mine=true');
    answerRest(http);
    harness.detectChanges();
    await harness.fixture.whenStable();
    // ohne Partien keine Laschen — eine Partie nachliefern
    const comp = harness.routeDebugElement!.componentInstance as GamesListComponent;
    comp.counts.set({ all: 1, online: 1, otb: 0 });
    harness.detectChanges();
    await harness.fixture.whenStable();
    expect(el.querySelector<HTMLInputElement>('.only-mine input[type=checkbox]')!.checked).toBeTrue();

    el.querySelector<HTMLAnchorElement>('a.kind-tab[data-kind="online"]')!.click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/games?mine=1&kind=online');
    expect(http.expectOne(isList).request.url).toBe('/api/games?take=200&kind=online&mine=true');
    http.expectNone(isCounts);   // die Zahlen hängen nicht an der Lasche
  });

  it('„Nur meine“ ohne Treffer in „Alle“: Satz statt Leerkarte, Häkchen bleibt zum Abwählen', async () => {
    const { harness, http, el } = await open('/games?mine=1');
    http.expectOne(isList).flush([]);
    answerRest(http, { all: 0, online: 0, otb: 0 });
    harness.detectChanges();
    await harness.fixture.whenStable();
    expect(el.querySelector('.kind-empty')?.textContent?.trim()).toBe('None of yours');
    expect(el.querySelector('.empty-state')).toBeNull();
    expect(el.querySelector('.only-mine input[type=checkbox]')).not.toBeNull();
  });
});
