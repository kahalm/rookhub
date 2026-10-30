import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Subject, of } from 'rxjs';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { SnackbarService } from '../../core/snackbar.service';
import { GameActionsService } from './game-actions.service';
import { GameRoastDialogComponent } from './game-roast-dialog.component';

describe('GameActionsService', () => {
  let actions: GameActionsService;
  let http: HttpTestingController;
  let confirmAnswer = true;
  let snack: jasmine.SpyObj<SnackbarService>;

  beforeEach(() => {
    confirmAnswer = true;
    snack = jasmine.createSpyObj<SnackbarService>('SnackbarService', ['copy', 'warn', 'success', 'info']);
    TestBed.configureTestingModule({
      providers: [
        GameActionsService, provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ConfirmService, useValue: { ask: () => of(confirmAnswer) } },
        { provide: SnackbarService, useValue: snack },
      ],
    });
    actions = TestBed.inject(GameActionsService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('kopiert PGN und Teilen-Links; ohne Inhalt geschieht nichts', async () => {
    const written: string[] = [];
    spyOn(navigator.clipboard, 'writeText').and.callFake((t: string) => { written.push(t); return Promise.resolve(); });
    actions.copyPgn(null);
    actions.copyPgn('1. e4');
    actions.copyShareLink(null);
    actions.copyShareLink('tok');
    actions.copyTrainingLink(null);
    actions.copyTrainingLink(actions.trainingShareUrl('tok', 'black'));
    await Promise.resolve();
    expect(written).toEqual(['1. e4', `${window.location.origin}/g/tok`, `${window.location.origin}/g/tok?train=black`]);
    expect(actions.trainingShareUrl(null, 'white')).toBeNull();
    expect(snack.copy).toHaveBeenCalledTimes(3);
  });

  it('nennt die PGN-Datei nach Spielern und Datum und haengt ein Zeilenende an', () => {
    const names: string[] = [];
    let blob: Blob | null = null;
    spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) { names.push(this.download); });
    const create = spyOn(URL, 'createObjectURL').and.callFake((b: Blob | MediaSource) => { blob = b as Blob; return 'blob:x'; });
    actions.downloadPgn({ pgn: '1. e4', white: 'a', black: 'b', playedAt: '2026-07-16T10:00:00Z' });
    actions.downloadPgn({ pgn: '' });
    expect(names).toEqual(['a_b_2026_07_16.pgn']);
    expect(create).toHaveBeenCalledTimes(1);
    expect(blob!.size).toBe('1. e4\n'.length);
  });

  it('loescht erst nach der Rueckfrage und meldet den Erfolg', () => {
    const done: boolean[] = [];
    confirmAnswer = false;
    actions.deleteGame(7).subscribe(v => done.push(v));
    http.expectNone('/api/games/7');

    confirmAnswer = true;
    actions.deleteGame(7).subscribe(v => done.push(v));
    http.expectOne({ url: '/api/games/7', method: 'DELETE' }).flush(null);
    expect(done).toEqual([true]);

    actions.deleteGame(8).subscribe(v => done.push(v));
    http.expectOne({ url: '/api/games/8', method: 'DELETE' }).flush('x', { status: 500, statusText: 'err' });
    expect(done).toEqual([true]);
    expect(snack.warn).toHaveBeenCalledWith('games.deleteError');
  });

  it('widerruft den Teilen-Link nach Rückfrage und liefert das neue Token (A2-006)', () => {
    const got: string[] = [];
    confirmAnswer = false;
    actions.revokeShareLink(7).subscribe(t => got.push(t));
    http.expectNone('/api/games/7/share');

    confirmAnswer = true;
    actions.revokeShareLink(7).subscribe(t => got.push(t));
    http.expectOne({ url: '/api/games/7/share', method: 'DELETE' }).flush({ shareToken: 'neu' });
    expect(got).toEqual(['neu']);
    expect(snack.success).toHaveBeenCalledWith('games.revokeShareDone');

    actions.revokeShareLink(8).subscribe(t => got.push(t));
    http.expectOne({ url: '/api/games/8/share', method: 'DELETE' }).flush('x', { status: 404, statusText: 'nf' });
    expect(got).toEqual(['neu']);
    expect(snack.warn).toHaveBeenCalledWith('games.revokeShareError');
  });

  it('oeffnet das Analysebrett mit dem ganzen PGN', () => {
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    actions.openInAnalysis(null, '/g/x');
    actions.openInAnalysis('1. e4', '/g/x');
    expect(nav).toHaveBeenCalledOnceWith(['/analysis'], { state: { pgn: '1. e4' }, queryParams: { from: '/g/x' } });
  });

  it('Listen-Varianten laden das PGN nach; scheitert das, meldet „games.loadError"', async () => {
    const detail = { id: 4, white: 'a', black: 'b', playedAt: '2026-07-16T10:00:00Z', pgn: '1. d4' };
    const written: string[] = [];
    spyOn(navigator.clipboard, 'writeText').and.callFake((t: string) => { written.push(t); return Promise.resolve(); });
    const names: string[] = [];
    spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) { names.push(this.download); });
    spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    actions.copyPgnOf(4);
    http.expectOne({ method: 'GET', url: '/api/games/4' }).flush(detail);
    actions.downloadPgnOf(4);
    http.expectOne({ method: 'GET', url: '/api/games/4' }).flush(detail);
    actions.openInAnalysisOf(4, '/games');
    http.expectOne({ method: 'GET', url: '/api/games/4' }).flush(detail);
    await Promise.resolve();
    expect(written).toEqual(['1. d4']);
    expect(names).toEqual(['a_b_2026_07_16.pgn']);
    expect(nav).toHaveBeenCalledOnceWith(['/analysis'], { state: { pgn: '1. d4' }, queryParams: { from: '/games' } });

    actions.copyPgnOf(5);
    http.expectOne('/api/games/5').flush('x', { status: 404, statusText: 'nf' });
    expect(snack.warn).toHaveBeenCalledOnceWith('games.loadError');
  });

  it('Roast-Dialog bekommt Teilen-Link und Kennung', () => {
    const open = spyOn(TestBed.inject(MatDialog), 'open').and.returnValue({} as never);
    actions.roast(3, 'tok');
    expect(open.calls.mostRecent().args[0]).toBe(GameRoastDialogComponent);
    expect(open.calls.mostRecent().args[1]!.data).toEqual({ gameId: 3, shareUrl: `${window.location.origin}/g/tok`, shareToken: 'tok', canAnalyze: false });
  });

  it('d9: Roast ohne Analyse — „Partie analysieren“ im Dialog startet die Analyse des Aufrufers', () => {
    const closed = new Subject<string | undefined>();
    const open = spyOn(TestBed.inject(MatDialog), 'open').and.returnValue({ afterClosed: () => closed } as never);
    let analyzed = 0;
    actions.roast(3, null, () => analyzed++);
    expect(open.calls.mostRecent().args[1]!.data).toEqual(jasmine.objectContaining({ canAnalyze: true }));
    closed.next('analyze');
    expect(analyzed).toBe(1);
    closed.next(undefined);
    expect(analyzed).toBe(1);
  });

  it('Lc0: meldet Start bzw. Fehlschlag', () => {
    let started = 0;
    actions.startLc0(null, () => started++);
    actions.startLc0('1. e4', () => started++);
    const req = http.expectOne(r => r.method === 'POST');
    req.flush({});
    expect(started).toBe(1);
    expect(snack.success).toHaveBeenCalled();
  });

  it('Lc0: abbrechen löscht, neu starten reiht ein — Erfolg ruft zurück, Fehler meldet', () => {
    let done = 0;
    actions.cancelLc0(7, () => done++);
    http.expectOne(r => r.method === 'DELETE' && r.url.endsWith('/7')).flush(null);
    actions.restartLc0(7, () => done++);
    http.expectOne(r => r.method === 'POST' && r.url.endsWith('/7/restart')).flush({});
    expect(done).toBe(2);
    actions.cancelLc0(8, () => done++);
    http.expectOne(r => r.url.endsWith('/8')).flush('x', { status: 500, statusText: 'err' });
    expect(done).toBe(2);
    expect(snack.warn).toHaveBeenCalled();
  });
});
