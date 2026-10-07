import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LeagueApiService } from './league-api.service';

describe('LeagueApiService.trainingLines (2026-10-07)', () => {
  let http: HttpTestingController;
  let api: LeagueApiService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(LeagueApiService);
  });

  afterEach(() => http.verify());

  it('fragt angemeldet unter /api/league/player/{fide} — es gibt keine Fassung über einen Teilen-Link', async () => {
    const p = api.trainingLines('1606921', { repertoire: 3, color: null, chapterColors: null,
      filter: { source: 'both', speeds: ['blitz'], years: null, withUnsure: true } });
    const req = http.expectOne(r => r.url === '/api/league/player/1606921/training-lines');
    expect(req.request.params.get('repertoire')).toBe('3');
    expect(req.request.params.has('color')).toBeFalse();
    expect(req.request.params.get('source')).toBe('both');
    expect(req.request.params.get('speeds')).toBe('blitz');
    expect(req.request.params.get('unsure')).toBe('true');
    req.flush({ repertoires: [{ id: 3, name: 'R' }], repertoire: 3, color: 'b', colors: ['b'], games: 4, total: 0, lines: [], more: 0 });
    expect((await p).color).toBe('b');
  });
});
