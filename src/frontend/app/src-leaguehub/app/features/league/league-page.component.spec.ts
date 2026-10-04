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
      '2': { opp: `Gegner von ${t}`, home: false, status: 'open', boards: [], roster: [
        { rb: 1, n: 'Gast, Gerd', elo: 1800, p: 90, prev: '', cur: '', fide: `${t}-1`, g: 5, acc: [] },
        { rb: 2, n: 'Ohne, Fide', elo: null, p: 40, prev: '', cur: '', fide: null, g: 0, acc: [] },
      ] },
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
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['index', 'league', 'clearCache', 'startUpdate', 'updateStatus', 'createShare', 'deleteShare', 'card', 'pgn', 'sources', 'forecastStats']);
    api.forecastStats.and.resolveTo({ season: null, total: { fixtures: 0, players: 0, boards: 0, of: 0 }, rounds: [], leagues: [] });
    api.index.and.resolveTo(INDEX);
    api.sources.and.callFake(async (_token: string | null = null, fides: string[] = [], tnr: number | null = null) => ({
      board: [{ key: 'Lumbra', label: 'Lumbra', games: 34838 }, { key: 'Mega', label: 'ChessBase-Megabase', games: 18839 }], boardTotal: 53677,
      online: [{ key: 'lichess', label: 'Lichess', games: 667881 }, { key: 'chess.com', label: 'chess.com', games: 29528 }], onlineTotal: 697409,
      countedAt: '2026-10-01T13:00:00Z',
      league: tnr ? { players: 177, board: { Lumbra: 29982, Mega: 12311 }, boardTotal: 42293,
        online: { lichess: { games: 345890, accounts: 54 } }, onlineTotal: 345890, onlineAccounts: 54 } : undefined,
      opponent: fides.length ? { players: fides.length, board: { Lumbra: 187 }, boardTotal: 187, online: {}, onlineTotal: 0, onlineAccounts: 0 } : undefined,
    }));
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
    // UX-033: früher eine Sackgasse (auch direkt nach der Registrierung) — jetzt Anfrage und Kontowechsel.
    const gate = el.querySelector('lh-access-gate')!;
    expect(gate.textContent).toContain('LeagueHub sehen Admins und die Vereinsgruppe von SK Schwaz.');
    expect(gate.textContent).toContain('Freischaltung anfragen');
    expect(gate.textContent).toContain('Mit anderem Konto anmelden');
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
    // Partien je Quelle (0.626.0) als Tabelle mit Liga und Begegnung (0.628.0): die Liga + die Meldeliste des Gegners, ohne leere FIDE-IDs.
    expect(api.sources).toHaveBeenCalledWith(null, ['Schwaz-1'], 10);
    // seit 0.650.0 hinter dem (i) „Partien"
    (el.querySelector('button[aria-label="Partien je Quelle"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const rows = Array.from(el.querySelectorAll('.src-tbl tbody tr')).map(r => Array.from(r.children).map(c => c.textContent!.trim()));
    expect(rows).toEqual([
      ['Brett', '53.677', '42.293', '187'], ['Lumbra', '34.838', '29.982', '187'], ['ChessBase-Megabase', '18.839', '12.311', '0'],
      ['Online', '697.409', '345.890', '–'], ['Lichess', '667.881', '345.890', '–'], ['chess.com', '29.528', '–', '–'],
    ]);
    const head = el.querySelector('.src-tbl thead')!.textContent!;
    expect(head).toContain('Landesliga · 177 Spieler');
    expect(head).toContain('Gegner von Schwaz · 1 Spieler');
    // Anderer Verein → andere Meldeliste → neu geholt.
    c.pickTeam('Wörgl');
    await settle();
    expect(api.sources).toHaveBeenCalledWith(null, ['Wörgl-1'], 10);
    expect(el.querySelector('.src-tbl thead')!.textContent).toContain('Gegner von Wörgl');
  });

  it('ohne Zählung (Fehler) fehlt nur die Tabelle', async () => {
    api.sources.and.rejectWith(new Error('x'));
    const el = create();
    await settle();
    expect(el.querySelector('.src-tbl')).toBeNull();
    expect(el.querySelector('lh-fixture')).not.toBeNull();
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

  // F7-010: Funkloch beim Ligawechsel — früher ersetzte „Daten nicht geladen" die ganze Seite bis zum Browser-Reload.
  it('Ligawechsel scheitert: Auswahl bleibt, Fehler an Stelle der Begegnung, „Erneut versuchen" lädt nach', async () => {
    const el = create();
    await settle();
    const c = fixture.componentInstance;
    let fail = true;
    api.league.and.callFake(async (tnr: number) => {
      if (tnr === 20 && fail) throw new HttpErrorResponse({ status: 0 });
      return league(tnr, tnr === 10 ? ['Kufstein', 'Schwaz', 'Wörgl'] : ['Absam', 'Hall']);
    });

    await c.pickLeague(20);
    await settle();
    expect(el.textContent).not.toContain('Daten nicht geladen');
    expect(el.querySelectorAll('form.pick select').length).toBe(3);
    expect(el.textContent).toContain('Liga nicht geladen');
    expect(el.textContent).toContain('nicht erreichbar');
    const retry = Array.from(el.querySelectorAll('button')).find(b => b.textContent?.includes('Erneut versuchen'));
    expect(retry).toBeTruthy();

    fail = false;
    retry!.click();
    await settle();
    expect(el.textContent).not.toContain('Liga nicht geladen');
    expect([c.tnr(), c.round(), c.team()]).toEqual([20, 2, 'Absam']);
    expect(el.querySelector('lh-fixture .match')?.textContent).toContain('Gegner von Absam');
  });

  it('Ligawechsel scheitert: eine andere Liga lässt sich trotzdem wählen', async () => {
    const el = create();
    await settle();
    const c = fixture.componentInstance;
    api.league.and.callFake(async (tnr: number) => {
      if (tnr === 20) throw new HttpErrorResponse({ status: 500 });
      return league(tnr, ['Kufstein', 'Schwaz', 'Wörgl']);
    });
    await c.pickLeague(20);
    await settle();
    expect(el.textContent).toContain('Liga nicht geladen');
    await c.pickLeague(10);
    await settle();
    expect(el.textContent).not.toContain('Liga nicht geladen');
    expect(el.querySelector('lh-fixture .match')?.textContent).toContain('Gegner von Schwaz');
  });

  // UX-034 (d): kam der Bestand nicht, stand nur „Fehler 500." da — ohne Knopf, bis zum Browser-Reload.
  it('Bestand kommt nicht: Klartext und „Neu laden" holt ihn noch einmal', async () => {
    api.index.and.returnValues(Promise.reject(new HttpErrorResponse({ status: 500 })), Promise.resolve(INDEX));
    const el = create();
    await settle();
    expect(el.textContent).toContain('Daten nicht geladen');
    expect(el.textContent).toContain('Der Server hatte ein Problem (500). Bitte gleich noch einmal versuchen.');
    expect(el.textContent).not.toContain('Fehler 500.');
    const btn = Array.from(el.querySelectorAll('.gate button')).find(b => b.textContent?.includes('Neu laden')) as HTMLButtonElement;
    expect(btn).toBeTruthy();
    btn.click();
    await settle();
    expect(api.index).toHaveBeenCalledTimes(2);
    expect(el.textContent).not.toContain('Daten nicht geladen');
    expect(el.querySelectorAll('form.pick select').length).toBe(3);
    expect(el.querySelector('lh-fixture .match')?.textContent).toContain('Gegner von Schwaz');
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
