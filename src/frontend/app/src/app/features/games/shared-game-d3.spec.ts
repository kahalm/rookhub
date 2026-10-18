import { TestBed } from '@angular/core/testing';
import { OverlayContainer } from '@angular/cdk/overlay';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SharedGameComponent } from './shared-game.component';

/**
 * Design-Runde d3 (Partien) auf der Partieseite — eigene Datei, weil shared-game.component.spec.ts über 1 000 Zeilen hat.
 */
describe('SharedGameComponent (Design-Runde d3)', () => {
  async function setup(own = false) {
    const snapshot = own
      ? { paramMap: convertToParamMap({ id: '4' }), data: { mode: 'own' } }
      : { paramMap: convertToParamMap({ token: 'tok' }), data: {} };
    await TestBed.configureTestingModule({
      imports: [SharedGameComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: true, has: () => false } },
        { provide: ActivatedRoute, useValue: { snapshot } },
      ],
    }).compileComponents();
    return { fixture: TestBed.createComponent(SharedGameComponent), http: TestBed.inject(HttpTestingController) };
  }

  const game = (extra: object = {}) => ({
    id: 4, source: 'lichess', white: 'Anna', black: 'Bert', result: '0-1', shareToken: 'tok4', moveCount: 2,
    pgn: '[White "Anna"]\n[Black "Bert"]\n\n1. e4 c5 0-1', createdAt: '2026-07-16T00:00:00Z', ownerSide: 'white', ...extra,
  });

  function flushEvals(http: HttpTestingController, url: string): void {
    const body = { status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [], final: null };
    http.expectOne(`${url}?book=0`).flush(body);
    http.match(url).forEach(r => r.flush(body));
  }

  // d3-rk-eigene-fehler-fremde-partie: Gäste und fremde Konten lasen „Eigene Fehler nachspielen“ — trainiert werden aber
  // die Fehler des Besitzers. Der Knopf nennt jetzt, wessen Fehler.
  it('on someone else’s shared game the mistakes button names whose mistakes it replays', async () => {
    const { fixture, http } = await setup();
    const t = TestBed.inject(TranslateService);
    t.setTranslation('en', { games: { mistakes: { button: 'Own ({{count}})', buttonSide: 'Mistakes of {{player}} ({{count}})' } } });
    t.use('en');
    fixture.detectChanges();
    const { id: _id, ...shared } = game();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(shared);
    fixture.detectChanges();
    http.match(() => true).forEach(r => { if (r.request.url.includes('/evals')) r.flush({ status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [], final: null }); });
    fixture.detectChanges();
    const cmp = fixture.componentInstance;
    const m = { ply: 1 } as never;
    cmp.mistakes.set({ white: [m, m], black: [] });
    fixture.detectChanges();
    expect(cmp.own).toBeFalse();
    expect((fixture.nativeElement as HTMLElement).querySelector('button.mistakes')!.textContent).toContain('Mistakes of Anna (2)');
    // Ohne Namen: die Farbe.
    expect(cmp.sidePlayer('black')).toBe('Bert');
    cmp.game = { ...cmp.game!, white: '?' };
    expect(cmp.sidePlayer('white')).toBe('games.review.white');
  });

  it('on the own game it stays “replay your mistakes”', async () => {
    const { fixture, http } = await setup(true);
    fixture.detectChanges();
    http.expectOne('/api/games/4').flush(game());
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    flushEvals(http, '/api/games/4/evals');
    fixture.detectChanges();
    const m = { ply: 1 } as never;
    fixture.componentInstance.mistakes.set({ white: [m], black: [] });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('button.mistakes')!.textContent).toContain('games.mistakes.button');
  });

  // d3-rk-partie-grund-neben-knopf-versetzt: mit dem Grund unter dem gesperrten Knopf stand der Knopf ~15 px höher als
  // „Teilen-Link kopieren“ und das ⋮ (Hülle mittig ausgerichtet). Karma läuft 1 400 px breit = PC-Regel.
  it('with the no-engine reason, the analyze button stands level with its neighbours', async () => {
    const { fixture, http } = await setup(true);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    http.expectOne('/api/games/4').flush(game());
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: false, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    flushEvals(http, '/api/games/4/evals');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.analyze-no-engine')).not.toBeNull();
    const btn = (el.querySelector('button.analyze') as HTMLElement).getBoundingClientRect();
    const share = (el.querySelector('button.share.wide-only') as HTMLElement).getBoundingClientRect();
    expect(Math.abs(btn.top - share.top)).toBeLessThanOrEqual(2);
    fixture.nativeElement.remove();
  });

  // d3-rk-spielmenue-reihenfolge-uneinheitlich: ⋮ der Partieseite nach DESIGN 5.5 und wie das ⋮ der Partienliste —
  // Öffnen/Bearbeiten → Engine → Aktionen → Teilen/Export.
  it('orders the ⋮ menu: open and edit first, share and export last', async () => {
    const { fixture, http } = await setup(true);
    fixture.detectChanges();
    http.expectOne('/api/games/4').flush(game({ sourceUrl: 'https://lichess.org/abc' }));
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    flushEvals(http, '/api/games/4/evals');
    fixture.detectChanges();
    ((fixture.nativeElement as HTMLElement).querySelector('.game-menu') as HTMLButtonElement).click();
    fixture.detectChanges();
    http.match('/api/engine/external').forEach(r => r.flush({ engines: [] }));
    const overlay = TestBed.inject(OverlayContainer).getContainerElement();
    const items = Array.from(overlay.querySelectorAll('.mat-mdc-menu-panel .mat-mdc-menu-item-text')).map(x => x.textContent!.trim());
    expect(items).toEqual(['games.openInAnalysis', 'games.openOriginal', 'games.edit.menu', 'games.live.engineMenu',
      'games.roast.menu', 'games.share', 'games.pgnCopy', 'common.downloadPgn', 'common.delete']);
  });

  // d4-rk-partie-menue-loeschen-fehlt-partieseite: „Löschen“ auch im ⋮ der Partieseite (rot, zuletzt) — Rückfrage, danach
  // zurück zur Liste.
  it('d4: deletes the own game from its page after confirming and returns to the list', async () => {
    const { fixture, http } = await setup(true);
    fixture.detectChanges();
    http.expectOne('/api/games/4').flush(game());
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    flushEvals(http, '/api/games/4/evals');
    const ask = spyOn(TestBed.inject(ConfirmService), 'ask').and.returnValue(of(true));
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture.componentInstance.remove();
    expect(ask).toHaveBeenCalledWith('games.deleteConfirm');
    http.expectOne(r => r.method === 'DELETE' && r.url === '/api/games/4').flush(null);
    expect(nav).toHaveBeenCalledWith(['/games']);
  });

  // d4-rk-engine-hinweis-jargon: ohne Engine nannte der Tooltip „eingeworfene Partien“ (Punktepartie-Text).
  it('d4: without an engine the analyse tooltip uses the game page reason, not the point-game text', async () => {
    const { fixture, http } = await setup(true);
    fixture.detectChanges();
    http.expectOne('/api/games/4').flush(game());
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: false, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    flushEvals(http, '/api/games/4/evals');
    expect(fixture.componentInstance.analyzeTooltip()).toBe('games.analyzeNoEngine');
  });
});
