import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from '@rh/core/auth.service';
import { PrepApiService } from './prep-api.service';
import { PrepCardApi, toPlayerCard } from './prep-card-api';
import { PrepCardJson } from './prep.models';

const CARD: PrepCardJson = {
  id: 42, fide: null, name: 'Huber, Franz', n: 3, games: 3, loaded: 3, limited: false, limit: 500, max: 3000, since: null,
  twin: null, twinIncluded: false, src: { Mega: 3 }, recent: [],
  accounts: [{ id: 7, site: 'lichess', user: 'huberf', url: 'https://lichess.org/@/huberf', conf: 'sicher', comment: 'Notiz' }],
};

describe('PrepCardApi (Schnittstelle der Spielerkarte für die Spielervorbereitung)', () => {
  let http: HttpTestingController;
  let api: PrepCardApi;
  let perms: Set<string>;

  beforeEach(() => {
    perms = new Set();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), PrepCardApi,
        { provide: AuthService, useValue: { has: (p: string) => perms.has(p) } }],
    });
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(PrepCardApi);
  });

  afterEach(() => http.verify());

  it('macht aus der Prep-Karte die Form der Liga-Karte: key = Id, ohne FIDE-ID kein FIDE-Link, Konten ohne Id', () => {
    const c = toPlayerCard(CARD);
    expect(c.key).toBe('42');
    expect(c.fide).toBe('');
    expect(c.accounts).toEqual([{ site: 'lichess', user: 'huberf', url: 'https://lichess.org/@/huberf', conf: 'sicher', comment: 'Notiz' }]);
    expect(toPlayerCard({ ...CARD, fide: '1503014' }).fide).toBe('1503014');
  });

  it('Trainingslinien: über /api/prep mit Grenze/Zwilling der Seite; der Trainer bekommt prep:<Id> samt all/twin', async () => {
    api.options.set({ all: true, twin: false });
    const p = api.trainingLines('42', { repertoire: 7, color: 'w', filter: { source: 'board', speeds: [], years: null, withUnsure: false } });
    const req = http.expectOne(r => r.url === '/api/prep/player/42/training-lines');
    expect(req.request.params.get('repertoire')).toBe('7');
    expect(req.request.params.get('color')).toBe('w');
    expect(req.request.params.get('source')).toBe('board');
    expect(req.request.params.get('all')).toBe('true');
    expect(req.request.params.has('twin')).toBeFalse();
    req.flush({ repertoires: [], repertoire: 7, color: 'w', colors: ['w'], games: 0, total: 0, lines: [], more: 0 });
    expect((await p).repertoire).toBe(7);
    expect(api.trainerParams('42')).toEqual({ opponent: 'prep:42', all: 'true' });
    api.options.set({ all: false, twin: true });
    expect(api.trainerParams('42')).toEqual({ opponent: 'prep:42', twin: 'true' });
  });

  it('fragt mit den Schaltern der Seite und merkt sich, was geladen ist', async () => {
    api.options.set({ all: true, twin: true });
    const p = api.card('42');
    const req = http.expectOne(r => r.url === '/api/prep/player/42');
    expect(req.request.params.get('all')).toBe('true');
    expect(req.request.params.get('twin')).toBe('true');
    req.flush({ ...CARD, limited: true, since: '2015.10.11' });
    const card = await p;
    expect(card.key).toBe('42');
    expect(api.scope()).toEqual(jasmine.objectContaining({ id: 42, limited: true, since: '2015.10.11', max: 3000 }));
  });

  it('Baum, Profil, letzte Partien und PGN gehen an /api/prep mit Filter und Schaltern', async () => {
    api.options.set({ all: false, twin: true });
    const filter = { source: 'both' as const, speeds: ['blitz'], years: 3, withUnsure: true };
    void api.tree('42', 's', ['e4', 'c5'], null, filter);
    let req = http.expectOne(r => r.url === '/api/prep/player/42/tree');
    expect(req.request.params.get('color')).toBe('s');
    expect(req.request.params.get('line')).toBe('e4 c5');
    expect(req.request.params.get('source')).toBe('both');
    expect(req.request.params.get('speeds')).toBe('blitz');
    expect(req.request.params.get('years')).toBe('3');
    expect(req.request.params.get('unsure')).toBe('true');
    expect(req.request.params.get('twin')).toBe('true');
    expect(req.request.params.has('all')).toBeFalse();
    req.flush({});
    void api.profile('42', null, filter);
    http.expectOne(r => r.url === '/api/prep/player/42/profile' && r.params.get('source') === 'both').flush({});
    void api.recent('42', null, 'w');
    req = http.expectOne(r => r.url === '/api/prep/player/42/recent');
    expect(req.request.params.get('color')).toBe('w');
    req.flush({ fide: '', games: [] });
    void api.pgn('42');
    req = http.expectOne(r => r.url === '/api/prep/player/42/pgn');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['x']));
  });

  it('Konten pflegt nur LeagueHub; „auch unsichere Konten" nur mit prep.manage', () => {
    expect(api.accountsEditable).toBeFalse();
    expect(api.unsureAllowed()).toBeFalse();
    perms.add('prep.view');
    expect(api.unsureAllowed()).toBeFalse();
    perms.add('prep.manage');
    expect(api.unsureAllowed()).toBeTrue();
  });

  it('Suche: /api/prep/players?q=', async () => {
    const p = TestBed.inject(PrepApiService).search('Carlsen, M');
    const req = http.expectOne(r => r.url === '/api/prep/players');
    expect(req.request.params.get('q')).toBe('Carlsen, M');
    req.flush({ items: [{ id: 1, name: 'Carlsen, Magnus', fide: '1503014', games: 530, firstYear: 2001, lastYear: 2026, maxElo: 2882 }] });
    expect((await p)[0].name).toBe('Carlsen, Magnus');
  });
});
