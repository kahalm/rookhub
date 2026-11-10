import { TestBed } from '@angular/core/testing';
import { OverlayContainer } from '@angular/cdk/overlay';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SharedGameComponent } from './shared-game.component';

/**
 * UI-Sweep 2026-11-10 (Design-Runde d10) auf der Partieseite: Kopfknöpfe einer analysierten Partie (eine Hauptaktion,
 * Original/Teilen im ⋮) und „Ohne Engine“ am Handy im ⋮ — auch ohne Anmeldung.
 */
describe('SharedGameComponent (Design-Runde d10)', () => {
  async function setup(own: boolean, loggedIn = true) {
    const snapshot = own
      ? { paramMap: convertToParamMap({ id: '4' }), data: { mode: 'own' } }
      : { paramMap: convertToParamMap({ token: 'tok' }), data: {} };
    await TestBed.configureTestingModule({
      imports: [SharedGameComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn, has: () => false } },
        { provide: ActivatedRoute, useValue: { snapshot } },
      ],
    }).compileComponents();
    return { fixture: TestBed.createComponent(SharedGameComponent), http: TestBed.inject(HttpTestingController) };
  }

  const game = (extra: object = {}) => ({
    id: 4, source: 'lichess', white: 'Anna', black: 'Bert', result: '0-1', shareToken: 'tok4', moveCount: 2,
    sourceUrl: 'https://lichess.org/abc',
    pgn: '[White "Anna"]\n[Black "Bert"]\n\n1. e4 c5 0-1', createdAt: '2026-07-16T00:00:00Z', ownerSide: 'white', ...extra,
  });
  const none = { status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [], final: null };

  async function ownGame() {
    const { fixture, http } = await setup(true);
    fixture.detectChanges();
    http.expectOne('/api/games/4').flush(game());
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    http.match(r => r.url.includes('/evals')).forEach(r => r.flush(none));
    fixture.detectChanges();
    const m = { ply: 1 } as never;
    fixture.componentInstance.mistakes.set({ white: [m, m], black: [] });
    return { fixture, http };
  }

  // d10-rk-partiekopf-fuenf-sekundaerknoepfe: fünf gleichrangige Rahmen-/Textknöpfe, keine Hauptaktion (DESIGN 2/5.4).
  it('analysed game: one primary button (replay mistakes), at most three secondary ones, original and share only in the ⋮', async () => {
    const { fixture } = await ownGame();
    fixture.componentInstance.reviewStatus.set('done');
    fixture.detectChanges();
    const actions = (fixture.nativeElement as HTMLElement).querySelector('.header-actions') as HTMLElement;
    const primary = actions.querySelectorAll('.mat-mdc-unelevated-button');
    expect(primary.length).toBe(1);
    expect(primary[0].classList).toContain('mistakes');
    expect(actions.querySelectorAll('.mat-mdc-outlined-button, .mat-mdc-button').length).toBeLessThanOrEqual(3);
    expect(actions.querySelector('a.original')).toBeNull();
    expect(actions.querySelector('button.share')).toBeNull();
  });

  it('not yet analysed: “Analyse game” stays the primary button, replaying mistakes is secondary', async () => {
    const { fixture } = await ownGame();
    fixture.detectChanges();
    const actions = (fixture.nativeElement as HTMLElement).querySelector('.header-actions') as HTMLElement;
    expect(actions.querySelector('button.analyze')!.classList).toContain('mat-mdc-unelevated-button');
    expect(actions.querySelector('button.mistakes')!.classList).toContain('mat-mdc-outlined-button');
  });

  // d10-rk-partie-handy-ohne-engine-eigene-zeile: am Handy steht „Ohne Engine“ im ⋮ — dort auch für Gäste.
  it('“Without engine” is a wide-only header button and is in the ⋮ even without signing in', async () => {
    const { fixture, http } = await setup(false, false);
    fixture.detectChanges();
    const { id: _id, ...shared } = game();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(shared);
    fixture.detectChanges();
    http.match(r => r.url.includes('/evals')).forEach(r => r.flush(none));
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.header-actions > button.blind-btn')!.classList).toContain('wide-only');
    (el.querySelector('.game-menu') as HTMLButtonElement).click();
    fixture.detectChanges();
    http.match('/api/engine/external').forEach(r => r.flush({ engines: [] }));
    const overlay = TestBed.inject(OverlayContainer).getContainerElement();
    expect(overlay.querySelector('button.without-engine')).not.toBeNull();
    expect(overlay.querySelector('a.original-item')).not.toBeNull();
  });
});
