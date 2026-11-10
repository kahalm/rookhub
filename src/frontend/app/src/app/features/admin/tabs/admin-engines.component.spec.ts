import { ComponentFixture, TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import {
  AdminEnginesComponent, ENGINE_OVERVIEW_POLL_MS, ENGINE_OVERVIEW_URL, EngineOverview, EngineOverviewEngine,
  EngineThroughputWindow, backgroundRows, engineBackgroundUrl, problemCount,
} from './admin-engines.component';
import { SnackbarService } from '../../../core/snackbar.service';

const W = (p: Partial<EngineThroughputWindow> = {}): EngineThroughputWindow => ({
  runs: 0, done: 0, failed: 0, interrupted: 0, retried: 0, unavailable: 0, timeouts: 0, errors: 0, busySeconds: 0,
  avgJobSeconds: null, kiloNodesPerSecond: null, ...p,
});

function engine(id: string, p: Partial<EngineOverviewEngine> = {}): EngineOverviewEngine {
  return {
    id, name: null, source: 'rookhub', registered: true, ownerUserId: 990101, ownerName: 'ov-owner', online: true,
    lastSeenAt: '2026-11-10T11:59:50Z', registeredAt: '2026-11-07T12:00:00Z', maxThreads: 4, maxHash: 256, variants: ['chess'],
    backgroundFor: [], houseEngine: false, explicitOnly: false, state: 'idle', liveStreams: 0, providerPolling: 1,
    brokerQueued: 0, current: null, waitingQueued: 0, waitingPaused: 0, oldestWaitingSince: null,
    throughput: { hour: W(), day: W(), week: W() }, broker: null, ...p,
  };
}

const overview: EngineOverview = {
  generatedAt: '2026-11-10T12:00:00Z', serverStartedAt: '2026-11-10T08:00:00Z', bucketMinutes: 10,
  pause: { paused: false, forcedByConfig: false, since: null, by: null },
  queue: {
    queued: 2, paused: 1, running: 1, oldestWaitingSince: '2026-11-10T07:00:00Z',
    byKind: [{ kind: 'league', queued: 1, paused: 0, running: 1, oldestWaitingSince: '2026-11-10T09:00:00Z' }],
    gameAnalysesPending: 3, gameAnalysesRunning: 1,
  },
  totals: { hour: W({ done: 5, avgJobSeconds: 42, kiloNodesPerSecond: 500 }), day: W({ done: 15 }), week: W({ done: 16, failed: 1 }) },
  engines: [
    engine('rhe_ovA000000001', {
      name: 'Server 1', state: 'working', houseEngine: true,
      backgroundFor: [{ userId: 990101, name: 'ov-owner' }, { userId: 990102, name: null }],
      current: {
        jobId: 990201, ownerUserId: 990101, ownerName: 'ov-owner', title: null, fen: '8/8/8/8/8/8/8/K6k w - - 0 1',
        kind: 'league', background: true, batch: true, targetDepth: 30, targetNodes: null, multiPv: 3, reachedDepth: 12,
        depth: 22, nps: 900_000, nodes: 0, seconds: 60, runningSince: '2026-11-10T11:59:40Z', gameAnalysisId: 990301, ply: 17,
        gameTitle: 'Weiß – Schwarz',
      },
      waitingQueued: 2,
      throughput: { hour: W({ done: 5 }), day: W({ done: 15, unavailable: 2, failed: 1, busySeconds: 43_200 }), week: W({ done: 16 }) },
    }),
    engine('eei_ovLichess01', { source: 'lichess', registered: false, online: null, state: 'unknown', maxThreads: null, maxHash: null }),
    engine('rhe_ovC000000003', { name: 'Alter PC', online: false, state: 'offline' }),
  ],
};

describe('AdminEnginesComponent', () => {
  let fixture: ComponentFixture<AdminEnginesComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (id: string) => el().querySelector(`[data-testid="${id}"]`);
  const all = (id: string) => Array.from(el().querySelectorAll(`[data-testid="${id}"]`));

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminEnginesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' })],
    });
    fixture = TestBed.createComponent(AdminEnginesComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function loaded(data: EngineOverview = overview): Promise<void> {
    fixture.detectChanges();
    http.expectOne(ENGINE_OVERVIEW_URL).flush(data);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('zeigt Engines (offline ausgeblendet), laufenden Auftrag, Hintergrund-Zuordnung und Warteschlange', async () => {
    await loaded();
    const cards = all('engine');
    expect(cards.length).toBe(2);                               // die Offline-Engine ist eingeklappt
    expect(cards[0].textContent).toContain('Server 1');
    expect(cards[0].querySelector('[data-testid="engine-state"]')!.textContent).toContain('admin.engines.state.working');
    expect(cards[0].textContent).toContain('admin.engines.house');
    expect(cards[0].querySelector('[data-testid="engine-background"]')!.textContent).toContain('admin.engines.bgInList');
    const cur = cards[0].querySelector('[data-testid="engine-current"]')!;
    expect(cur.textContent).toContain('admin.engines.kind.league');
    expect(cur.textContent).toContain('Weiß – Schwarz');
    expect(cur.textContent).toContain('900 kN/s');
    expect(cards[0].querySelector('[data-testid="engine-waiting"]')).not.toBeNull();
    expect(cards[0].querySelector('[data-testid="engine-throughput"]')!.textContent).toContain('admin.engines.problemsDay');
    expect(cards[1].textContent).toContain('admin.engines.source.lichess');
    expect(cards[1].querySelector('[data-testid="engine-current"]')).toBeNull();

    expect(all('queue-kind').length).toBe(1);
    expect(all('queue-kind')[0].textContent).toContain('admin.engines.kind.league');
    expect(all('throughput-row').length).toBe(3);
    expect(all('throughput-row')[0].textContent).toContain('500 kN/s');
    expect(q('pause-state')!.textContent).toContain('admin.engines.runningNow');
    expect(q('pause-link')!.getAttribute('href')).toBe('/admin?tab=jobs');
  });

  it('blendet Offline-Engines per Knopf ein', async () => {
    await loaded();
    (q('toggle-offline') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(all('engine').length).toBe(3);
    expect(all('engine')[2].querySelector('[data-testid="engine-state"]')!.textContent).toContain('admin.engines.state.offline');
  });

  it('zeigt die Pause der Hintergrundjobs an', async () => {
    await loaded({ ...overview, pause: { paused: true, forcedByConfig: false, since: '2026-11-10T10:00:00Z', by: 'kahalm' } });
    expect(q('pause-state')!.textContent).toContain('admin.engines.pausedNow');
    expect(q('pause-state')!.classList).toContain('paused');
  });

  it('leer: kein Fehler, sondern Leerzustand', async () => {
    await loaded({ ...overview, engines: [], queue: { ...overview.queue, byKind: [] } });
    expect(q('engines-empty')).not.toBeNull();
    expect(q('queue-empty')).not.toBeNull();
    expect(el().querySelector('app-load-error')).toBeNull();
  });

  it('erstes Laden gescheitert → Fehlerzustand mit Wiederholen', async () => {
    fixture.detectChanges();
    http.expectOne(ENGINE_OVERVIEW_URL).flush('x', { status: 500, statusText: 'err' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el().querySelector('app-load-error')).not.toBeNull();
  });

  it('frischt im Takt auf, nur bei sichtbarem Tab; ein gescheitertes Auffrischen lässt den Stand stehen', fakeAsync(() => {
    let hidden = false;
    spyOnProperty(document, 'visibilityState', 'get').and.callFake(() => (hidden ? 'hidden' : 'visible') as DocumentVisibilityState);
    fixture.detectChanges();
    http.expectOne(ENGINE_OVERVIEW_URL).flush(overview);
    tick();

    tick(ENGINE_OVERVIEW_POLL_MS);
    http.expectOne(ENGINE_OVERVIEW_URL).flush('x', { status: 503, statusText: 'down' });
    tick();
    fixture.detectChanges();
    expect(fixture.componentInstance.stale()).toBeTrue();
    expect(fixture.componentInstance.data()).not.toBeNull();
    expect(el().querySelector('app-load-error')).toBeNull();

    hidden = true;
    tick(ENGINE_OVERVIEW_POLL_MS * 2);
    http.expectNone(ENGINE_OVERVIEW_URL);                       // versteckt: kein Abruf

    hidden = false;
    document.dispatchEvent(new Event('visibilitychange'));     // zurück: sofort auffrischen
    http.expectOne(ENGINE_OVERVIEW_URL).flush(overview);
    tick();
    expect(fixture.componentInstance.stale()).toBeFalse();

    fixture.destroy();
    tick(ENGINE_OVERVIEW_POLL_MS);
    http.expectNone(ENGINE_OVERVIEW_URL);                       // nach dem Verlassen ruht der Takt
    discardPeriodicTasks();
  }));

  it('Probleme = gescheitert + kein Provider + Zeitüberschreitung + Fehler (nicht unterbrochen)', () => {
    expect(problemCount(W({ failed: 1, unavailable: 2, timeouts: 3, errors: 4, interrupted: 9, retried: 9 }))).toBe(10);
  });

  // ── Hintergrund-Zuordnung umschalten (0.951.0) ───────────────────────────────────────────────────────

  const tick0 = () => new Promise<void>(r => setTimeout(r));

  it('Hintergrund: je Konto der Liste ein „entfernen“, beim Besitzer ohne Eintrag ein „aufnehmen“, fremdes Konto markiert', async () => {
    await loaded();
    const [a, lichess] = all('engine');
    const rows = Array.from(a.querySelectorAll('[data-testid="bg-row"]'));
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('admin.engines.bgOwner');                    // Besitzer zuerst
    expect(rows[1].textContent).toContain('admin.engines.bgForeign');                  // fremdes Konto
    expect(a.querySelectorAll('[data-testid="bg-remove"]').length).toBe(2);
    expect(a.querySelector('[data-testid="bg-add"]')).toBeNull();
    // Lichess-Engine mit Besitzer, aber in keiner Liste: „aufnehmen“ beim Besitzer, der Zustand steht als Text da.
    expect(lichess.querySelector('[data-testid="bg-add"]')!.textContent).toContain('admin.engines.bgAdd');
    expect(lichess.querySelector('[data-testid="bg-row"]')!.textContent).toContain('admin.engines.bgNotInList');
  });

  it('backgroundRows: ohne eindeutigen Besitzer kein „aufnehmen“, Lichess-Kennung in der einzigen Liste nur „entfernen“', () => {
    expect(backgroundRows(engine('eei_x', { registered: false, ownerUserId: null, backgroundFor: [] }))).toEqual([]);
    const two = backgroundRows(engine('eei_x', {
      registered: false, ownerUserId: null, backgroundFor: [{ userId: 1, name: 'a' }, { userId: 2, name: null }],
    }));
    expect(two.map(r => [r.userId, r.name, r.inList, r.owner, r.foreign])).toEqual([[1, 'a', true, false, false], [2, '#2', true, false, false]]);
    const single = backgroundRows(engine('eei_x', { registered: false, ownerUserId: 1, backgroundFor: [{ userId: 1, name: 'a' }] }));
    expect(single.map(r => [r.inList, r.owner])).toEqual([[true, true]]);
  });

  it('Hintergrund entfernen: PUT ans Konto der Zeile, Knöpfe gesperrt bis zur Antwort, danach neu geladen', async () => {
    const snack = TestBed.inject(SnackbarService);
    spyOn(snack, 'quick');
    await loaded();
    (all('engine')[0].querySelectorAll('[data-testid="bg-remove"]')[1] as HTMLButtonElement).click();
    fixture.detectChanges();

    const req = http.expectOne(engineBackgroundUrl('rhe_ovA000000001'));
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ userId: 990102, enabled: false });
    const buttons = Array.from(el().querySelectorAll<HTMLButtonElement>('[data-testid="bg-remove"], [data-testid="bg-add"]'));
    expect(buttons.length).toBeGreaterThan(0);
    expect(buttons.every(b => b.disabled)).toBeTrue();
    expect(fixture.componentInstance.saving()).toBe('rhe_ovA000000001|990102');

    req.flush({ engineId: 'rhe_ovA000000001', userId: 990102, enabled: false, changed: true, backgroundEngineIds: [] });
    await tick0();
    const after = structuredClone(overview);
    after.engines[0].backgroundFor = [{ userId: 990101, name: 'ov-owner' }];
    http.expectOne(ENGINE_OVERVIEW_URL).flush(after);
    await tick0();
    fixture.detectChanges();

    expect(snack.quick).toHaveBeenCalled();
    expect(fixture.componentInstance.saving()).toBeNull();
    expect(all('engine')[0].querySelectorAll('[data-testid="bg-row"]').length).toBe(1);
    expect((all('engine')[0].querySelector('[data-testid="bg-remove"]') as HTMLButtonElement).disabled).toBeFalse();
  });

  it('Hintergrund aufnehmen: volle Liste → Snackbar mit der Meldung, Stand bleibt, Knöpfe wieder frei', async () => {
    const snack = TestBed.inject(SnackbarService);
    spyOn(snack, 'show');
    await loaded();
    (all('engine')[1].querySelector('[data-testid="bg-add"]') as HTMLButtonElement).click();
    const req = http.expectOne(engineBackgroundUrl('eei_ovLichess01'));
    expect(req.request.body).toEqual({ userId: 990101, enabled: true });
    req.flush({ message: 'At most 32 background engines', code: 'background_list_full' }, { status: 400, statusText: 'Bad Request' });
    await tick0();
    fixture.detectChanges();

    expect(snack.show).toHaveBeenCalledWith('At most 32 background engines');   // ohne geladene Sprachdatei: message
    http.expectNone(ENGINE_OVERVIEW_URL);
    expect(fixture.componentInstance.saving()).toBeNull();
    expect((all('engine')[1].querySelector('[data-testid="bg-add"]') as HTMLButtonElement).disabled).toBeFalse();
  });

  it('Handybreite: die Seite läuft nicht über (Tabellen scrollen in ihrem Kasten)', async () => {
    (fixture.nativeElement as HTMLElement).style.display = 'block';
    (fixture.nativeElement as HTMLElement).style.width = '390px';
    await loaded();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.scrollWidth).toBeLessThanOrEqual(host.clientWidth + 1);
  });
});
