import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { of, Subject } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { HandoffService } from '../../core/handoff.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GamesService } from '../games/games.service';
import { AnalysisGameActionsComponent } from './analysis-game-actions.component';
import { GameDialogResult } from './analysis-game-dialog.component';
import { GameDetails } from './analysis-game-pgn';
import { AnalysisNode, createRoot, playSan } from './analysis-tree';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const DETAILS: GameDetails = { white: 'A', black: 'B', result: '1-0', date: '', event: '', round: '' };

describe('AnalysisGameActionsComponent', () => {
  let opened: Subject<GameDialogResult | undefined>;
  let dialog: { open: jasmine.Spy };
  let games: { importPgn: jasmine.Spy };
  let snackbar: { show: jasmine.Spy; warn: jasmine.Spy };
  let handoff: { leagueHubUrl: string | null; jumpToLeagueHub: jasmine.Spy };
  let onAction: Subject<void>;

  function setup(opts: { loggedIn?: boolean; contribute?: boolean; root?: AnalysisNode; clubEntry?: boolean } = {}) {
    TestBed.configureTestingModule({
      imports: [AnalysisGameActionsComponent],
      providers: [provideTranslateService(), provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: { isLoggedIn: opts.loggedIn ?? true, has: (p: string) => p === 'league.contribute' && (opts.contribute ?? false) } },
        { provide: HandoffService, useValue: handoff },
        { provide: GamesService, useValue: games },
        { provide: MatDialog, useValue: dialog },
        { provide: SnackbarService, useValue: snackbar },
      ],
    });
    const fixture = TestBed.createComponent(AnalysisGameActionsComponent);
    let root = opts.root;
    if (!root) { root = createRoot(START); playSan(root, 'e4'); }
    fixture.componentRef.setInput('root', root);
    fixture.componentRef.setInput('clubEntry', opts.clubEntry ?? false);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, http: TestBed.inject(HttpTestingController) };
  }

  const labels = (el: HTMLElement) => Array.from(el.querySelectorAll('button')).map(b => b.textContent!.trim());

  beforeEach(() => {
    opened = new Subject();
    dialog = { open: jasmine.createSpy('open').and.callFake(() => ({ afterClosed: () => opened })) };
    games = { importPgn: jasmine.createSpy('importPgn').and.returnValue(of({ imported: 1, duplicates: 0, truncated: false, ids: [9], failed: [] })) };
    onAction = new Subject();
    snackbar = { show: jasmine.createSpy('show').and.returnValue({ onAction: () => onAction }), warn: jasmine.createSpy('warn') };
    handoff = { leagueHubUrl: 'https://leaguehub.example', jumpToLeagueHub: jasmine.createSpy('jump').and.resolveTo() };
  });

  it('Gast: nur Herunterladen', () => {
    const { el } = setup({ loggedIn: false });
    expect(labels(el).length).toBe(1);
    expect(labels(el)[0]).toContain('common.downloadPgn');
  });

  it('angemeldet ohne Recht: Herunterladen und Speichern, kein Verein', () => {
    const { el } = setup();
    expect(labels(el).some(l => l.includes('gameActions.save'))).toBeTrue();
    expect(labels(el).some(l => l.includes('gameActions.club'))).toBeFalse();
  });

  it('mit league.contribute: Vereinsdatenbank sichtbar, ohne LeagueHub nicht', () => {
    expect(labels(setup({ contribute: true }).el).some(l => l.includes('gameActions.club'))).toBeTrue();
    TestBed.resetTestingModule();
    handoff.leagueHubUrl = null;
    expect(labels(setup({ contribute: true }).el).some(l => l.includes('gameActions.club'))).toBeFalse();
  });

  it('leerer Baum: Knöpfe gesperrt mit Begründung', () => {
    const { el } = setup({ root: createRoot(START) });
    expect(el.querySelector('button')!.disabled).toBeTrue();
    expect(el.textContent).toContain('analysis.gameActions.needMoves');
  });

  it('aus abweichender Stellung: Vereinsdatenbank gesperrt, Begründung', () => {
    const root = createRoot('4k3/8/8/8/8/8/4P3/4K3 w - - 0 1');
    playSan(root, 'e4');
    const { el } = setup({ contribute: true, root });
    const club = Array.from(el.querySelectorAll('button')).find(b => b.textContent!.includes('gameActions.club'))!;
    expect(club.disabled).toBeTrue();
    expect(el.textContent).toContain('analysis.gameActions.clubNeedsStart');
  });

  it('Aufrufmodus aus LeagueHub stellt die Übernahme nach vorn', () => {
    const { el } = setup({ contribute: true, clubEntry: true });
    expect(labels(el)[0]).toContain('gameActions.club');
    expect(el.querySelector('.entry')!.textContent).toContain('entryHint');
  });

  it('Speichern: Hauptlinie an den Import, Link zur Partie', async () => {
    const root = createRoot(START);
    const e4 = playSan(root, 'e4')!;
    playSan(root, 'd4');
    expect(e4).toBeTruthy();
    const { fixture } = setup({ root });
    const run = fixture.componentInstance.run('save');
    opened.next({ details: DETAILS, clubId: null });
    opened.complete();
    await run;
    expect(games.importPgn.calls.mostRecent().args[0]).not.toContain('d4');
    expect(snackbar.show).toHaveBeenCalled();
    expect(snackbar.show.calls.mostRecent().args[0]).toContain('saved');
  });

  it('Duplikat wird als solches gemeldet', async () => {
    games.importPgn.and.returnValue(of({ imported: 0, duplicates: 1, truncated: false, ids: [3], failed: [] }));
    const { fixture } = setup();
    const run = fixture.componentInstance.run('save');
    opened.next({ details: DETAILS, clubId: null });
    opened.complete();
    await run;
    expect(snackbar.show.calls.mostRecent().args[0]).toContain('duplicate');
  });

  it('Vereinsdatenbank: Entwurf mit Verein, Sprung nur mit Nummer', async () => {
    const { fixture, http } = setup({ contribute: true });
    fixture.componentRef.setInput('clubId', 2);
    const run = fixture.componentInstance.run('club');
    http.expectOne('/api/league/me').flush({ clubs: [{ id: 1, name: 'X' }, { id: 2, name: 'Y' }], current: 1 });
    await Promise.resolve(); await Promise.resolve();
    expect(dialog.open.calls.mostRecent().args[1].data.clubId).toBe(2);
    opened.next({ details: DETAILS, clubId: 2 });
    opened.complete();
    await new Promise(r => setTimeout(r));
    const req = http.expectOne(r => r.url === '/api/league/club/drafts' && r.params.get('club') === '2');
    expect(req.request.body.source).toBe('rookhub');
    expect(req.request.body.pgn).toContain('[White "A"]');
    req.flush({ id: 77 });
    await run;
    expect(handoff.jumpToLeagueHub).toHaveBeenCalledOnceWith('verein/neu?entwurf=77&club=2');
    expect(handoff.jumpToLeagueHub.calls.mostRecent().args[0]).not.toContain('e4');
  });

  it('Deckel der offenen Listen wird gesagt', async () => {
    const { fixture, http } = setup({ contribute: true });
    const run = fixture.componentInstance.run('club');
    http.expectOne('/api/league/me').flush({ clubs: [{ id: 1, name: 'X' }], current: 1 });
    await Promise.resolve(); await Promise.resolve();
    opened.next({ details: DETAILS, clubId: 1 });
    opened.complete();
    await new Promise(r => setTimeout(r));
    http.expectOne(r => r.url === '/api/league/club/drafts').flush({ reason: 'tooManyDrafts' }, { status: 400, statusText: 'Bad' });
    await run;
    expect(snackbar.warn.calls.mostRecent().args[0]).toContain('tooManyDrafts');
    expect(handoff.jumpToLeagueHub).not.toHaveBeenCalled();
  });
});
