import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { CLUB_STORAGE_KEY, ClubContextService, LeagueMe, leagueClubInterceptor, needsClub, ownsTeam, pickClub } from './club-context.service';

const SCHWAZ = { id: 1, name: 'SK Testdorf', anonName: 'Testdorf', teamPrefix: 'Testdorf', source: null };
const WEILER = { id: 2, name: 'SK Weiler', anonName: 'Weiler', teamPrefix: 'SK Weiler', source: 'ligamanager' };

describe('club-context (Vereine als Mandanten, 0.698.0)', () => {
  it('ownsTeam: Anfang an einer Wortgrenze, ohne Groß/klein (Spiegel von LeagueClub.OwnsTeam)', () => {
    expect(ownsTeam('Schwaz', 'Schwaz')).toBeTrue();
    expect(ownsTeam('Schwaz', 'Schwaz 2')).toBeTrue();
    expect(ownsTeam('SK Weilheim', 'sk weilheim 1')).toBeTrue();
    expect(ownsTeam('SK Weilheim', 'SK Weilheimer 1')).toBeFalse();
    expect(ownsTeam('Schwaz', 'Schwazer SK')).toBeFalse();
    expect(ownsTeam('', 'Schwaz')).toBeFalse();
    expect(ownsTeam(undefined, 'Schwaz')).toBeFalse();
  });

  it('pickClub: gemerkt (wenn erlaubt) vor dem Server vor dem ersten', () => {
    const me: LeagueMe = { clubs: [SCHWAZ, WEILER], current: null };
    expect(pickClub(me, 2)?.id).toBe(2);
    expect(pickClub(me, 99)?.id).toBe(1);                               // gemerkt, aber nicht (mehr) erlaubt
    expect(pickClub({ ...me, current: 2 }, null)?.id).toBe(2);
    expect(pickClub({ clubs: [], current: null }, 1)).toBeNull();
  });

  it('needsClub: alle LeagueHub-Aufrufe außer /me und den Teilen-Links', () => {
    expect(needsClub('/api/league/index')).toBeTrue();
    expect(needsClub('/api/league/club/games?page=2')).toBeTrue();
    expect(needsClub('/api/league/me')).toBeFalse();
    expect(needsClub('/api/league/s/abc/club/games')).toBeFalse();
    expect(needsClub('/api/games/1')).toBeFalse();
  });

  describe('Dienst + Interceptor', () => {
    let http: HttpTestingController;
    let client: HttpClient;
    let user: { userId: number } | null;

    beforeEach(() => {
      localStorage.removeItem(CLUB_STORAGE_KEY);
      user = { userId: 7 };
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(withInterceptors([leagueClubInterceptor])),
          provideHttpClientTesting(),
          { provide: AuthService, useValue: { get currentUser() { return user; } } },
        ],
      });
      http = TestBed.inject(HttpTestingController);
      client = TestBed.inject(HttpClient);
    });

    afterEach(() => {
      http.verify();
      localStorage.removeItem(CLUB_STORAGE_KEY);
    });

    it('hängt ?club= an (einmal /me für alle Aufrufe), nicht an /me und Teilen-Links', async () => {
      const a = firstValueFrom(client.get('/api/league/index'));
      const b = firstValueFrom(client.get('/api/league/club/games'));
      http.expectOne('/api/league/me').flush({ clubs: [SCHWAZ, WEILER], current: 2 } satisfies LeagueMe);
      http.expectOne('/api/league/index?club=2').flush({});
      http.expectOne('/api/league/club/games?club=2').flush({});
      await Promise.all([a, b]);
      const c = firstValueFrom(client.get('/api/league/s/tok/club/players'));
      http.expectOne('/api/league/s/tok/club/players').flush([]);
      await c;
      const ctx = TestBed.inject(ClubContextService);
      expect(ctx.anonName()).toBe('Weiler');
      expect(ctx.clubs().length).toBe(2);
    });

    it('der gemerkte Verein gilt; ein Wechsel wird gemerkt', async () => {
      localStorage.setItem(CLUB_STORAGE_KEY, '1');
      const ctx = TestBed.inject(ClubContextService);
      const id = firstValueFrom(ctx.ensure());
      http.expectOne('/api/league/me').flush({ clubs: [SCHWAZ, WEILER], current: 2 } satisfies LeagueMe);
      expect(await id).toBe(1);
      ctx.select(2);
      expect(localStorage.getItem(CLUB_STORAGE_KEY)).toBe('2');
      expect(ctx.current()?.name).toBe('SK Weiler');
    });

    it('ohne Anmeldung: kein /me, der Aufruf geht ohne Verein', async () => {
      user = null;
      const r = firstValueFrom(client.get('/api/league/index'));
      http.expectOne('/api/league/index').flush({});
      await r;
    });

    it('der Verein eines Teilen-Links schlägt den angemeldeten', () => {
      const ctx = TestBed.inject(ClubContextService);
      ctx.useShareClub({ id: 2, name: 'SK Weiler', anonName: 'Weiler' });
      expect(ctx.anonName()).toBe('Weiler');
      ctx.useShare('tok');
      http.expectOne('/api/league/s/tok').flush({ club: { id: 1, name: 'SK Testdorf', anonName: 'Testdorf' } });
      expect(ctx.clubName()).toBe('SK Testdorf');
    });
  });
});
