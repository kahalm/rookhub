import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import {
  DEFAULT_EXPLORER_SETTINGS, ExplorerAnalysisRequest, ExplorerAnalysisResult, ExplorerSources, RepertoireExplorerService,
  RepertoireHole, clampThreshold, fitToLocal, formatPath, holeMoveLabel, nextRoundDelayMs, positionAfterHole, readExplorerSettings,
  saveExplorerSettings,
} from './repertoire-explorer.service';

function result(extra: Partial<ExplorerAnalysisResult> = {}): ExplorerAnalysisResult {
  return {
    complete: false, positionsAnalyzed: 0, positionsPending: 0, rateLimited: false, retryAfterSeconds: null,
    tokenMissing: false, tokenInvalid: false, fetchFailed: false, holes: [], lineFrequencies: null, ...extra,
  };
}

const REQ: ExplorerAnalysisRequest = {
  color: 'b', chapterColors: { Sizilianisch: 'b' }, source: 'online', database: 'lichess', ratings: [1800], speeds: ['blitz'],
  thresholdPercent: 1, includeHoles: true, includeLineFrequencies: false,
};

describe('repertoire-explorer helpers', () => {
  afterEach(() => localStorage.removeItem('rookhub_explorer_settings'));

  it('nextRoundDelayMs: stops when done or blocked, waits out the rate limit, stops without progress', () => {
    expect(nextRoundDelayMs(result({ complete: true }), null)).toBeNull();
    expect(nextRoundDelayMs(result({ tokenMissing: true }), null)).toBeNull();
    expect(nextRoundDelayMs(result({ tokenInvalid: true }), null)).toBeNull();
    expect(nextRoundDelayMs(result({ fetchFailed: true }), null)).toBeNull();
    expect(nextRoundDelayMs(result({ rateLimited: true, retryAfterSeconds: 30 }), null)).toBe(31_000);
    expect(nextRoundDelayMs(result({ positionsAnalyzed: 5 }), null)).toBe(0);
    expect(nextRoundDelayMs(result({ positionsAnalyzed: 9 }), result({ positionsAnalyzed: 5 }))).toBe(0);
    // Keine neue Stellung, obwohl nicht gebremst: sonst liefe die Schleife endlos gegen eine Wand.
    expect(nextRoundDelayMs(result({ positionsAnalyzed: 5 }), result({ positionsAnalyzed: 5 }))).toBeNull();
    // Nach der Drossel zählt der Vergleich nicht — die gebremste Runde konnte nichts holen.
    expect(nextRoundDelayMs(result({ positionsAnalyzed: 5 }), result({ positionsAnalyzed: 5, rateLimited: true }))).toBe(0);
  });

  it('formatPath numbers moves from the start position or a FEN', () => {
    expect(formatPath(null, ['e4', 'c5', 'Nf3'])).toBe('1. e4 c5 2. Nf3');
    expect(formatPath('rnbqkb1r/pppppppp/5n2/8/3P4/8/PPP1PPPP/RNBQKBNR w KQkq - 1 2', ['c4', 'e6'])).toBe('2. c4 e6');
    expect(formatPath('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1', ['c5', 'Nf3'])).toBe('1… c5 2. Nf3');
    expect(formatPath(null, [])).toBe('');
  });

  it('labels the missing move with its number and shows the position after it', () => {
    const hole = {
      fen: 'rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2', san: 'Nc3',
    } as RepertoireHole;
    expect(holeMoveLabel(hole)).toBe('2. Nc3');
    expect(holeMoveLabel({ ...hole, fen: 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1', san: 'e5' })).toBe('1… e5');

    const view = positionAfterHole(hole)!;
    expect(view.lastMove).toEqual(['b1', 'c3']);
    expect(view.fen.startsWith('rnbqkbnr/pp1ppppp/8/2p5/4P3/2N5/PPPP1PPP/R1BQKBNR b')).toBeTrue();
    expect(positionAfterHole({ ...hole, san: 'Ke5' })).toBeNull();
  });

  it('settings: defaults, round trip, junk falls back', () => {
    expect(readExplorerSettings()).toEqual(DEFAULT_EXPLORER_SETTINGS);
    expect(DEFAULT_EXPLORER_SETTINGS.source).toBe('local');
    expect(DEFAULT_EXPLORER_SETTINGS.database).toBe('masters');
    // Gespeichert vor der Quellen-Wahl (ohne Feld): Vorgabe, nicht stillschweigend online.
    localStorage.setItem('rookhub_explorer_settings', JSON.stringify({ database: 'lichess', ratings: [2000], speeds: ['rapid'] }));
    expect(readExplorerSettings().source).toBe('local');
    expect(readExplorerSettings().database).toBe('lichess');
    saveExplorerSettings({ source: 'local', database: 'masters', ratings: [2200], speeds: ['rapid'], thresholdPercent: 2.5 });
    expect(readExplorerSettings()).toEqual({ source: 'local', database: 'masters', ratings: [2200], speeds: ['rapid'], thresholdPercent: 2.5 });

    localStorage.setItem('rookhub_explorer_settings', JSON.stringify({ ratings: [1700], speeds: ['hyper'], thresholdPercent: 999 }));
    const s = readExplorerSettings();
    expect(s.ratings).toEqual(DEFAULT_EXPLORER_SETTINGS.ratings);
    expect(s.speeds).toEqual(DEFAULT_EXPLORER_SETTINGS.speeds);
    expect(s.thresholdPercent).toBe(50);

    localStorage.setItem('rookhub_explorer_settings', '{kaputt');
    expect(readExplorerSettings()).toEqual(DEFAULT_EXPLORER_SETTINGS);
  });

  it('fitToLocal drops what the local explorer has no games for, and falls back to its defaults', () => {
    const src: ExplorerSources = { online: true, local: true, localRatings: [1600, 1800, 2000, 2200, 2500], localSpeeds: ['blitz', 'rapid', 'classical', 'correspondence'] };
    const kept = fitToLocal({ ...DEFAULT_EXPLORER_SETTINGS, ratings: [1400, 2200], speeds: ['bullet', 'rapid'] }, src);
    expect(kept.ratings).toEqual([2200]);
    expect(kept.speeds).toEqual(['rapid']);
    const fallback = fitToLocal({ ...DEFAULT_EXPLORER_SETTINGS, ratings: [1000], speeds: ['bullet'] }, src);
    expect(fallback.ratings).toEqual([1600, 1800, 2000]);
    expect(fallback.speeds).toEqual(['blitz', 'rapid', 'classical']);
  });

  it('clampThreshold keeps 0.1 … 50 with one decimal', () => {
    expect(clampThreshold(0)).toBe(0.1);
    expect(clampThreshold(1.26)).toBe(1.3);
    expect(clampThreshold(80)).toBe(50);
  });
});

describe('RepertoireExplorerService.run', () => {
  let http: HttpTestingController;
  let service: RepertoireExplorerService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(RepertoireExplorerService);
  });

  afterEach(() => http.verify());

  it('asks again until the server says complete, and emits every round', fakeAsync(() => {
    const seen: ExplorerAnalysisResult[] = [];
    let done = false;
    service.run(7, REQ).subscribe({ next: r => seen.push(r), complete: () => done = true });

    const first = http.expectOne('/api/repertoires/7/explorer-analysis');
    expect(first.request.method).toBe('POST');
    expect(first.request.body).toEqual(REQ);
    first.flush(result({ positionsAnalyzed: 10, positionsPending: 5 }));
    tick();

    http.expectOne('/api/repertoires/7/explorer-analysis').flush(result({ positionsAnalyzed: 15, complete: true }));
    tick();

    expect(seen.map(r => r.positionsAnalyzed)).toEqual([10, 15]);
    expect(done).toBeTrue();
  }));

  it('effectiveSettings: a remembered local source becomes online when the server has none', () => {
    saveExplorerSettings({ ...DEFAULT_EXPLORER_SETTINGS, source: 'local' });
    let seen: string | undefined;
    service.effectiveSettings().subscribe(s => seen = s.source);
    http.expectOne('/api/repertoires/explorer/sources').flush({ online: true, local: false, localRatings: [], localSpeeds: [] });
    expect(seen).toBe('online');

    // Nur einmal je Sitzung gefragt.
    service.effectiveSettings().subscribe(s => seen = s.source);
    http.expectNone('/api/repertoires/explorer/sources');
    localStorage.removeItem('rookhub_explorer_settings');
  });

  it('waits out a rate limit before the next round', fakeAsync(() => {
    service.run(7, REQ).subscribe();
    http.expectOne('/api/repertoires/7/explorer-analysis').flush(result({ rateLimited: true, retryAfterSeconds: 2 }));

    tick(2_500);
    http.expectNone('/api/repertoires/7/explorer-analysis');
    tick(600);
    http.expectOne('/api/repertoires/7/explorer-analysis').flush(result({ complete: true }));
  }));
});
