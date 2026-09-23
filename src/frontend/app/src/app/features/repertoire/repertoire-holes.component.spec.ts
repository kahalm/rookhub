import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { RepertoireHolesComponent, HoleBoardView } from './repertoire-holes.component';
import { ExplorerAnalysisResult, ExplorerSources, RepertoireExplorerService, RepertoireHole } from './repertoire-explorer.service';
import { ParsedGame, parsePgnText } from '../../shared/pgn-viewer/pgn-parser';

const PGN_BLACK = [
  '[Event "Rep"]', '[White "Sizilianisch"]', '[Black "Schwarz gegen e4"]', '', '1. e4 c5 2. Nf3 d6 *', '',
].join('\n');
const PGN_MIXED = PGN_BLACK + '\n' + [
  '[Event "Rep"]', '[White "Londoner"]', '[Black "Weiss mit d4"]', '', '1. d4 d5 2. Bf4 *', '',
].join('\n');

const HOLE: RepertoireHole = {
  color: 'b', fen: 'rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2', startFen: null,
  path: ['e4', 'c5'], san: 'Nc3', uci: 'b1c3', share: 0.3, games: 300, positionGames: 1000,
  frequency: 0.15, opening: 'Sicilian Defense: Closed', eco: 'B23',
};

function result(extra: Partial<ExplorerAnalysisResult> = {}): ExplorerAnalysisResult {
  return {
    complete: true, positionsAnalyzed: 2, positionsPending: 0, rateLimited: false, retryAfterSeconds: null,
    tokenMissing: false, tokenInvalid: false, fetchFailed: false, holes: [HOLE], lineFrequencies: null, ...extra,
  };
}

function games(pgn: string): ParsedGame[] { return parsePgnText(pgn); }

const ONLINE_ONLY: ExplorerSources = { online: true, local: false, localRatings: [], localSpeeds: [] };
const WITH_LOCAL: ExplorerSources = {
  online: true, local: true, localRatings: [1600, 1800, 2000, 2200, 2500], localSpeeds: ['blitz', 'rapid', 'classical', 'correspondence'],
};

describe('RepertoireHolesComponent', () => {
  afterEach(() => {
    localStorage.removeItem('rookhub_explorer_settings');
    localStorage.removeItem('rookhub_rep_train_chaptercolor_5');
  });

  function make(pgn: string, explorer: any, sources: ExplorerSources = ONLINE_ONLY): RepertoireHolesComponent {
    explorer.sources ??= () => of(sources);
    const c = new RepertoireHolesComponent(explorer);
    c.repertoireId = 5;
    c.games = games(pgn);
    c.ngOnInit();
    c.ngOnChanges();
    return c;
  }

  it('detects the side from the chapters and sends chapter colours with the request', () => {
    const explorer = { run: jasmine.createSpy('run').and.returnValue(of(result())) };
    const c = make(PGN_BLACK, explorer);

    expect(c.colorsPresent()).toEqual(['b']);
    expect(c.color()).toBe('b');

    c.start();

    const [id, req] = explorer.run.calls.mostRecent().args;
    expect(id).toBe(5);
    expect(req.color).toBe('b');
    expect(req.chapterColors).toEqual({ 'Schwarz gegen e4': 'b' });
    expect(req.includeHoles).toBeTrue();
    expect(req.thresholdPercent).toBe(1);
    expect(c.result()!.holes.length).toBe(1);
    expect(c.running()).toBeFalse();
  });

  it('offers both colours for a mixed repertoire; switching colour clears the result', () => {
    const c = make(PGN_MIXED, { run: () => of(result()) });
    expect(c.colorsPresent()).toEqual(['w', 'b']);

    c.start();
    expect(c.result()).not.toBeNull();
    c.setColor(c.color() === 'w' ? 'b' : 'w');
    expect(c.result()).toBeNull();
  });

  it('the threshold is adjustable, clamped on blur and remembered', () => {
    const explorer = { run: jasmine.createSpy('run').and.returnValue(of(result())) };
    const c = make(PGN_BLACK, explorer);

    c.setThreshold('3.5');
    c.start();
    expect(explorer.run.calls.mostRecent().args[1].thresholdPercent).toBe(3.5);

    c.setThreshold(0.01);
    c.normalizeThreshold();
    expect(c.settings().thresholdPercent).toBe(0.1);
    expect(JSON.parse(localStorage.getItem('rookhub_explorer_settings')!).thresholdPercent).toBe(0.1);
  });

  it('cannot start without a rating or a speed for the Lichess database', () => {
    const c = make(PGN_BLACK, { run: () => of(result()) });
    c.setDatabase('lichess');
    for (const r of [...c.settings().ratings]) c.toggleRating(r);
    expect(c.canStart()).toBeFalse();
    c.setDatabase('masters');
    expect(c.canStart()).toBeTrue();
  });

  it('selecting a hole shows the position after the missing move', () => {
    const c = make(PGN_BLACK, { run: () => of(result()) });
    const views: (HoleBoardView | null)[] = [];
    c.holeSelected.subscribe(v => views.push(v));
    c.start();

    c.select(c.result()!.holes[0]);

    expect(views[views.length - 1]!.lastMove).toEqual(['b1', 'c3']);   // davor: null beim Start
    expect(c.selectedKey()).toBe(c.keyOf(HOLE));
    expect(c.label(HOLE)).toBe('2. Nc3');
    expect(c.path(HOLE)).toBe('1. e4 c5');
  });

  it('a failed request ends the run and says so', () => {
    const c = make(PGN_BLACK, { run: () => throwError(() => new Error('down')) });
    c.start();
    expect(c.running()).toBeFalse();
    expect(c.result()!.fetchFailed).toBeTrue();
  });

  it('offers the local source only when the server has one', () => {
    expect(make(PGN_BLACK, { run: () => of(result()) }).sources()!.local).toBeFalse();
    expect(make(PGN_BLACK, { run: () => of(result()) }, WITH_LOCAL).sources()!.local).toBeTrue();
  });

  it('switching to local keeps only ratings and speeds the local explorer has, and asks it', () => {
    const run = jasmine.createSpy('run').and.returnValue(of(result()));
    localStorage.setItem('rookhub_explorer_settings', JSON.stringify({ ratings: [1200, 1800], speeds: ['bullet', 'blitz'] }));
    const c = make(PGN_BLACK, { run }, WITH_LOCAL);

    c.setSource('local');

    expect(c.settings().ratings).toEqual([1800]);
    expect(c.settings().speeds).toEqual(['blitz']);
    expect(c.ratings()).toEqual([1600, 1800, 2000, 2200, 2500]);
    c.start();
    expect(run.calls.mostRecent().args[1].source).toBe('local');
  });

  it('a remembered local source falls back to online where there is none — without saving that', () => {
    localStorage.setItem('rookhub_explorer_settings', JSON.stringify({ source: 'local' }));
    const c = make(PGN_BLACK, { run: () => of(result()) });
    expect(c.settings().source).toBe('online');
    expect(JSON.parse(localStorage.getItem('rookhub_explorer_settings')!).source).toBe('local');
  });

  it('by default it asks the local explorer for master games', () => {
    const run = jasmine.createSpy('run').and.returnValue(of(result()));
    const c = make(PGN_BLACK, { run }, WITH_LOCAL);
    c.start();
    const req = run.calls.mostRecent().args[1];
    expect(req.source).toBe('local');
    expect(req.database).toBe('masters');
  });

  it('without a local explorer the default becomes online masters', () => {
    const run = jasmine.createSpy('run').and.returnValue(of(result()));
    const c = make(PGN_BLACK, { run });
    c.start();
    const req = run.calls.mostRecent().args[1];
    expect(req.source).toBe('online');
    expect(req.database).toBe('masters');
  });

  it('renders the hole list', async () => {
    await TestBed.configureTestingModule({
      imports: [RepertoireHolesComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: RepertoireExplorerService, useValue: { run: () => of(result()), sources: () => of(WITH_LOCAL) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RepertoireHolesComponent);
    fixture.componentRef.setInput('repertoireId', 5);
    fixture.componentRef.setInput('games', games(PGN_BLACK));
    fixture.detectChanges();

    fixture.componentInstance.start();
    fixture.detectChanges();

    const items = fixture.nativeElement.querySelectorAll('.hole');
    expect(items.length).toBe(1);
    expect(items[0].textContent).toContain('2. Nc3');
    expect(items[0].textContent).toContain('B23 Sicilian Defense: Closed');
    // Mit lokaler Quelle steht der Umschalter über der Auswahl.
    expect(fixture.nativeElement.querySelectorAll('mat-button-toggle-group').length).toBe(2);
  });
});
