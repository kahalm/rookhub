import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { LeagueApiService } from '../../core/league-api.service';
import { League, LeagueIndex } from '../../core/league.models';
import { LeaguePageComponent } from './league-page.component';

const INDEX: LeagueIndex = { season: '2026/27', generated: '27.09.2026 21:00', leagues: [{ tnr: 10, name: 'Landesliga' }, { tnr: 20, name: '1. Klasse Ost' }] };

function league(tnr: number, teams: string[]): League {
  return {
    tnr, name: tnr === 10 ? 'Landesliga' : '1. Klasse Ost', season: '2026/27', level: 1, boards: 6, teams, source: '',
    rounds: [
      { round: 1, date: 'Sa 03.10.2026', played: true, open: false },
      { round: 2, date: 'So 04.10.2026', played: false, open: true },
      { round: 3, date: 'Sa 07.11.2026', played: false, open: false },
    ],
    fixtures: Object.fromEntries(teams.map(t => [t, {
      '1': { opp: 'X', home: true, status: 'played', boards: [], roster: [] },
      '2': { opp: `Gegner von ${t}`, home: false, status: 'open', boards: [], roster: [] },
      '3': { opp: 'Y', home: true, status: 'locked', unlock_after: 2 },
    }])),
  };
}

describe('LeaguePageComponent', () => {
  let fixture: ComponentFixture<LeaguePageComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;
  let router: jasmine.SpyObj<Router>;
  let perms: Set<string>;
  let query: Record<string, string>;

  beforeEach(() => {
    localStorage.removeItem('leaguehub');
    perms = new Set(['league.view', 'league.manage']);
    query = {};
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['index', 'league', 'clearCache', 'startUpdate', 'updateStatus', 'createShare', 'deleteShare', 'card', 'pgn']);
    api.index.and.resolveTo(INDEX);
    api.league.and.callFake(async (tnr: number) => league(tnr, tnr === 10 ? ['Kufstein', 'Schwaz', 'Wörgl'] : ['Absam', 'Hall']));
    api.updateStatus.and.resolveTo({ running: false, started: null, finished: null, ok: null, message: null });
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
  });

  function create(): HTMLElement {
    TestBed.configureTestingModule({
      imports: [LeaguePageComponent],
      providers: [
        { provide: LeagueApiService, useValue: api },
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
        { provide: AuthService, useValue: { has: (p: string) => perms.has(p), currentUser: { username: 'patrik' } } },
      ],
    });
    fixture = TestBed.createComponent(LeaguePageComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const settle = async () => { for (let i = 0; i < 6; i++) { await fixture.whenStable(); fixture.detectChanges(); } };

  it('ohne league.view: nur der Hinweis, keine Abfrage', async () => {
    perms = new Set();
    const el = create();
    await settle();
    expect(el.textContent).toContain('Nicht freigeschaltet');
    expect(el.textContent).toContain('patrik');
    expect(api.index).not.toHaveBeenCalled();
  });

  it('wählt ohne Vorgabe die erste Liga, die erste offene Runde und Schwaz', async () => {
    const el = create();
    await settle();
    const c = fixture.componentInstance;
    expect(c.tnr()).toBe(10);
    expect(c.round()).toBe(2);
    expect(c.team()).toBe('Schwaz');
    expect(el.querySelector('lh-fixture .match')?.textContent).toContain('Gegner von Schwaz');
    expect(router.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({
      queryParams: { liga: 10, runde: 2, verein: 'Schwaz' }, replaceUrl: true }));
    expect(JSON.parse(localStorage.getItem('leaguehub')!)).toEqual({ liga: 10, verein: 'Schwaz' });
    expect(el.textContent).toContain('Stand der Daten: 27.09.2026 21:00');
  });

  it('nimmt Liga, Runde und Verein aus der Adresse; ein unbekannter Verein fällt zurück', async () => {
    query = { liga: '20', runde: '3', verein: 'Hall' };
    create();
    await settle();
    const c = fixture.componentInstance;
    expect([c.tnr(), c.round(), c.team()]).toEqual([20, 3, 'Hall']);

    await c.pickLeague(10);   // Hall gibt es in der Landesliga nicht, Schwaz schon
    await settle();
    expect([c.tnr(), c.round(), c.team()]).toEqual([10, 2, 'Schwaz']);
  });

  it('ohne league.manage kein Knopf „Daten aktualisieren" und kein Teilen-Link', async () => {
    perms = new Set(['league.view']);
    const el = create();
    await settle();
    expect(el.textContent).not.toContain('Daten aktualisieren');
    expect(el.textContent).not.toContain('Link teilen');
    expect(api.updateStatus).not.toHaveBeenCalled();
  });

  it('Aktualisieren: fragt nach, bis der Lauf fertig ist, und lädt dann frisch', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    api.startUpdate.and.resolveTo({ running: true, started: 'x', finished: null, ok: null, message: null });
    api.updateStatus.and.returnValues(
      Promise.resolve({ running: true, started: 'x', finished: null, ok: null, message: null }),
      Promise.resolve({ running: false, started: 'x', finished: 'y', ok: true, message: '4 Ligen neu geholt' }),
    );
    void fixture.componentInstance.startUpdate();
    flushMicrotasks();
    fixture.detectChanges();
    expect(fixture.componentInstance.updating()).toBeTrue();
    expect(el.querySelector('.stand button')?.hasAttribute('disabled')).toBeTrue();
    tick(4000); flushMicrotasks();
    expect(api.clearCache).not.toHaveBeenCalled();
    tick(4000); flushMicrotasks();
    fixture.detectChanges();
    expect(api.clearCache).toHaveBeenCalled();
    expect(api.league).toHaveBeenCalledWith(10, true);
    expect(fixture.componentInstance.updating()).toBeFalse();
    expect(el.querySelector('.update-msg')?.textContent).toContain('4 Ligen neu geholt');
  }));

  it('Aktualisieren zu früh: Hinweis statt Fehlerseite', async () => {
    api.startUpdate.and.rejectWith(new HttpErrorResponse({ status: 429 }));
    const el = create();
    await settle();
    await fixture.componentInstance.startUpdate();
    fixture.detectChanges();
    expect(el.querySelector('.update-msg.err')?.textContent).toContain('in zwei Minuten');
    expect(fixture.componentInstance.updating()).toBeFalse();
  });

  it('läuft beim Öffnen schon ein Lauf, wird nachgefragt — bis die Seite geschlossen wird', fakeAsync(() => {
    api.updateStatus.and.resolveTo({ running: true, started: 'x', finished: null, ok: null, message: null });
    create();
    flushMicrotasks();
    expect(fixture.componentInstance.updating()).toBeTrue();
    tick(4000); flushMicrotasks();
    const calls = api.updateStatus.calls.count();
    expect(calls).toBeGreaterThanOrEqual(2);
    fixture.destroy();
    tick(20000); flushMicrotasks();
    expect(api.updateStatus.calls.count()).toBe(calls);
  }));
});
