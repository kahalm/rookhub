import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '../../core/league-api.service';
import { LineupsApiService } from '../../core/lineups';
import { SharePageComponent } from './share-page.component';
import { AuthService } from '@rh/core/auth.service';
import { Router } from '@angular/router';

describe('SharePageComponent', () => {
  let api: jasmine.SpyObj<LeagueApiService>;
  let lineups: jasmine.SpyObj<LineupsApiService>;
  /** Anmeldestand (0.846.4): abgelaufener Link → angemeldet zur Prognose. */
  let auth: { isLoggedIn: boolean; has: () => boolean };

  function create() {
    TestBed.configureTestingModule({
      imports: [SharePageComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'de' }),
        { provide: LeagueApiService, useValue: api },
        { provide: LineupsApiService, useValue: lineups },
        { provide: AuthService, useValue: auth },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ token: 'TOKEN123' }), queryParamMap: convertToParamMap({}) } } },
      ],
    });
    const f = TestBed.createComponent(SharePageComponent);
    f.detectChanges();
    return f;
  }

  beforeEach(() => {
    auth = { isLoggedIn: false, has: () => false };
    lineups = jasmine.createSpyObj<LineupsApiService>('LineupsApiService', ['lineups', 'saveMoves', 'deleteMoves', 'clubGame', 'clubGameMarks', 'fixturePgn', 'sharedPgn']);
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['shared', 'card', 'pgn', 'createShare', 'deleteShare', 'sources', 'forecastStats', 'fixtureGames', 'shareTarget']);
    // 0.838.2: auch eine offene Runde holt über den Link die (vorläufigen) Paarungen
    api.fixtureGames.and.resolveTo([]);
    api.forecastStats.and.resolveTo({ season: null, total: { fixtures: 0, top1: 0, top2: 0, top3: 0, of: 0 }, rounds: [], leagues: [] });
    api.sources.and.resolveTo({
      board: [{ key: 'Lumbra', label: 'Lumbra', games: 34838 }], boardTotal: 34838,
      online: [{ key: 'chess.com', label: 'chess.com', games: 29522 }], onlineTotal: 29522, countedAt: '2026-10-01T14:30:00Z',
      league: { players: 226, board: { Lumbra: 12106 }, boardTotal: 12106, online: { 'chess.com': { games: 13132, accounts: 25 } }, onlineTotal: 13132, onlineAccounts: 25 },
      opponent: { players: 11, board: { Lumbra: 187 }, boardTotal: 187, online: { 'chess.com': { games: 12, accounts: 1 } }, onlineTotal: 12, onlineAccounts: 1 },
    });
  });

  it('zeigt die geteilte Begegnung ohne „Link teilen"', async () => {
    api.shared.and.resolveTo({
      league: 'Landesliga', season: '2026/27', round: 2, team: 'Schwaz/', generated: '27.09.2026 21:00', expires: '2026-10-11',
      fixture: { opp: 'Wörgl', home: true, status: 'open', boards: [], roster: [] },
    });
    const f = create();
    await f.whenStable();
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(api.shared).toHaveBeenCalledWith('TOKEN123');
    expect(el.textContent).toContain('Link gültig bis 11.10.2026');
    expect(el.querySelector('.match')?.textContent).toContain('Wörgl');
    expect(el.textContent).toContain('Auf WhatsApp teilen');
    expect(el.textContent).not.toContain('Link teilen');
    // Ganz oben die Aufforderung, Partien hochzuladen — ohne Anmeldung, beide Wege.
    const cta = el.querySelector('section.cta')!;
    expect(el.firstElementChild).toBe(cta);
    // UI-Sweep 2026-10-11 (d2-l-teilen-cta): kurzer Titel, Erklärtext hinter (i)
    expect(cta.querySelector('h2')?.textContent).toBe('In der Runde gespielt? Partien hochladen');
    expect(cta.textContent).not.toContain('ohne Anmeldung');
    (cta.querySelector('.chk-btn') as HTMLButtonElement).click();
    f.detectChanges();
    expect(cta.querySelector('.cta-info')?.textContent).toContain('ohne Anmeldung');
    expect(el.querySelector('.share-stand')?.textContent).toContain('Nur zum Ansehen');
    const links = Array.from(cta.querySelectorAll('a')).map(a => a.getAttribute('href'));
    expect(links).toEqual(['/s/TOKEN123/hochladen?art=formular', '/s/TOKEN123/hochladen']);
    expect(TestBed.inject(Title).getTitle()).toBe('Schwaz – Runde 2 | LeagueHub');
    // Partien je Quelle wie auf der Startseite — über den Link (0.627.0).
    await f.whenStable();
    f.detectChanges();
    expect(api.sources).toHaveBeenCalledWith('TOKEN123');
    // seit 0.650.0 hinter dem (i) „Partien"
    (el.querySelector('button[aria-label="Partien je Quelle"]') as HTMLButtonElement).click();
    f.detectChanges();
    // Liga und Gegner bestimmt über den Link der Server — die Seite schickt nichts mit.
    const rows = Array.from(el.querySelectorAll('.src-tbl tbody tr')).map(r => Array.from(r.children).map(c => c.textContent!.trim()));
    expect(rows).toEqual([['Brett', '34.838', '12.106', '187'], ['Lumbra', '34.838', '12.106', '187'],
      ['Online', '29.522', '13.132', '12'], ['chess.com', '29.522', '13.132', '12']]);
    const head = el.querySelector('.src-tbl thead')!.textContent!;
    expect(head).toContain('Landesliga · 226 Spieler');
    expect(head).toContain('Wörgl · 11 Spieler');
  });

  // 0.838.0, Wunsch 2026-10-25: „die Aufstellung für die Runde soll auch bei den anonymen Links verfügbar sein".
  it('„Alle Aufstellungen der Runde" klappt die Aufstellungen über den Link auf (nur lesen)', async () => {
    api.shared.and.resolveTo({
      league: 'Landesliga', season: '2026/27', round: 2, team: 'Schwaz/', generated: '27.09.2026 21:00', expires: '2026-10-11',
      fixture: { opp: 'Wörgl', home: true, status: 'open', boards: [], roster: [] },
    });
    lineups.lineups.and.resolveTo({ tnr: 4711, round: 2, date: '2026-10-11', canEdit: false, matches: [
      { matchNo: 1, home: 'Schwaz/', away: 'Wörgl', homePts: null, awayPts: null, own: true, boards: [
        { board: 1, homePlayer: 'Ackermann, Anna', homeTitle: null, homeElo: 2001, awayPlayer: 'Brunner, Bert', awayTitle: null, awayElo: 1950,
          homeColor: 'w', result: '', forfeit: 0, moves: 'e4 c5', canEditMoves: false } ] } ] });
    const f = create();
    await f.whenStable();
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('lh-round-lineups')).toBeNull();
    const toggle = el.querySelector('.share-lineups button') as HTMLButtonElement;
    expect(toggle.textContent).toContain('Alle Aufstellungen der Runde 2');
    toggle.click();
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    expect(lineups.lineups).toHaveBeenCalledWith(null, null, 'TOKEN123');
    expect(el.querySelector('lh-round-lineups .lu-match')?.textContent).toContain('Ackermann, Anna');
    expect(el.querySelector('.bm-moves')?.textContent).toContain('1.e4 c5');
    expect(el.querySelector('.bm-edit')).toBeNull();
    expect(toggle.textContent).toContain('Aufstellungen ausblenden');
  });

  it('abgelaufen oder widerrufen (404): „Link ungültig"', async () => {
    api.shared.and.rejectWith(new HttpErrorResponse({ status: 404 }));
    const f = create();
    await f.whenStable();
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Link ungültig');
    expect(el.querySelector('.gate button')).toBeNull();
    expect(el.querySelector('.gate a')).toBeNull();          // ohne Anmeldung kein „Zu den Prognosen"
    expect(api.shareTarget).not.toHaveBeenCalled();
    expect(api.sources).not.toHaveBeenCalled();
  });

  // 0.846.4, Wunsch 2026-10-26: „wenn man eingeloggt ist und ein Link abgelaufen ist, aber das sowieso sehen darf, sollte die
  // Seite auch aufgehen" — angemeldet fragt die Seite das Ziel des Links ab und leitet zur Prognose der Begegnung.
  it('abgelaufen, aber angemeldet: leitet zur Prognose der Begegnung weiter', async () => {
    auth.isLoggedIn = true;
    api.shared.and.rejectWith(new HttpErrorResponse({ status: 404 }));
    api.shareTarget.and.resolveTo({ tnr: 1479345, round: 3, team: 'Schwaz/', expires: '2026-10-20', expired: true });
    const f = create();
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    await f.whenStable();
    f.detectChanges();
    expect(api.shareTarget).toHaveBeenCalledWith('TOKEN123');
    expect(navigate).toHaveBeenCalledWith(['/'], { queryParams: { liga: 1479345, runde: 3, verein: 'Schwaz/' }, replaceUrl: true });
    expect((f.nativeElement as HTMLElement).textContent).not.toContain('Link ungültig');
  });

  it('widerrufen, aber angemeldet: „Link ungültig" mit „Zu den Prognosen"', async () => {
    auth.isLoggedIn = true;
    api.shared.and.rejectWith(new HttpErrorResponse({ status: 404 }));
    api.shareTarget.and.rejectWith(new HttpErrorResponse({ status: 404 }));
    const f = create();
    await f.whenStable();
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Link ungültig');
    expect(el.querySelector('.gate a')?.textContent).toContain('Zu den Prognosen');
    expect(el.querySelector('.gate a')?.getAttribute('href')).toBe('/');
  });

  // F7-009: 429 (viele Handys im selben Vereins-WLAN) oder Funkloch heißt NICHT, dass der Link tot ist.
  for (const [status, hint] of [[429, 'in einer Minute'], [0, 'nicht erreichbar'], [502, 'HTTP 502']] as const) {
    it(`vorübergehender Fehler ${status}: kein „Link ungültig", sondern Hinweis und „Neu laden"`, async () => {
      api.shared.and.rejectWith(new HttpErrorResponse({ status }));
      const f = create();
      await f.whenStable();
      f.detectChanges();
      const el = f.nativeElement as HTMLElement;
      expect(el.textContent).not.toContain('Link ungültig');
      expect(el.textContent).not.toContain('widerrufen');
      expect(el.textContent).toContain(hint);
      expect(el.querySelector('.gate button')?.textContent).toContain('Neu laden');
    });
  }

  it('„Neu laden" holt die Begegnung noch einmal', async () => {
    api.shared.and.returnValues(
      Promise.reject(new HttpErrorResponse({ status: 429 })),
      Promise.resolve({
        league: 'Landesliga', season: '2026/27', round: 2, team: 'Schwaz/', generated: '27.09.2026 21:00', expires: '2026-10-11',
        fixture: { opp: 'Wörgl', home: true, status: 'open', boards: [], roster: [] },
      }),
    );
    const f = create();
    await f.whenStable();
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    (el.querySelector('.gate button') as HTMLButtonElement).click();
    await f.whenStable();
    f.detectChanges();
    expect(api.shared).toHaveBeenCalledTimes(2);
    expect(el.querySelector('.gate')).toBeNull();
    expect(el.querySelector('.match')?.textContent).toContain('Wörgl');
  });
});
