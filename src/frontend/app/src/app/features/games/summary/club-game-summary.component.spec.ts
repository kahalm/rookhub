import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { ClubGameSummaryComponent } from './club-game-summary.component';
import { ClubGameSummaryService } from './club-game-summary.service';
import { GamesService } from '../games.service';
import { AuthService } from '../../../core/auth.service';
import { PreferencesService } from '../../../core/preferences.service';

/**
 * UI-Sweep 10.11.2026: `/cg/<ungültig>/summary` zeigte „konnte nicht geladen werden“ + „Wiederholen“. Ein 404 heißt
 * „den Link gibt es nicht“ — Wiederholen brächte dasselbe. Andere Fehler (5xx, Netz) behalten den Knopf.
 */
describe('ClubGameSummaryComponent — ungültiger Link', () => {
  const err = (status: number) => throwError(() => new HttpErrorResponse({ status }));

  function render(mode: string, params: Record<string, string>, status: number) {
    const games = {
      linkedClubGame: () => err(status), getShared: () => err(status), sharedClubGame: () => err(status),
      get: () => err(status), clubGame: () => err(status),
    };
    const summaries = {
      getLinked: () => of({}), getSharedOwn: () => of({}), getShared: () => of({}), getOwn: () => of({}), get: () => of({}),
    };
    TestBed.configureTestingModule({
      imports: [ClubGameSummaryComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params), data: { mode } } } },
        { provide: GamesService, useValue: games },
        { provide: ClubGameSummaryService, useValue: summaries },
        { provide: AuthService, useValue: { isLoggedIn: false } },
        { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett' } },
      ],
    });
    const fixture = TestBed.createComponent(ClubGameSummaryComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('Vereinspartie-Link /cg/:token: 404 → „Link gibt es nicht“ ohne Wiederholen', () => {
    const el = render('club-link', { token: 'kaputt' }, 404);
    expect(el.querySelector('.empty-state.not-found p')?.textContent).toContain('games.shared.notFound');
    expect(el.querySelector('app-load-error')).toBeNull();
    expect(el.querySelector('.gate-actions a.to-home')?.getAttribute('href')).toBe('/');
  });

  it('Teilen-Link /s/:token/club-games/:id: 404 → derselbe Zustand', () => {
    const el = render('club-share', { token: 'kaputt', id: '5' }, 404);
    expect(el.querySelector('.empty-state.not-found')).not.toBeNull();
    expect(el.querySelector('app-load-error')).toBeNull();
  });

  it('eigene Partie /games/:id: 404 → „Partie gibt es nicht“ mit Weg zu „Meine Partien“', () => {
    const el = render('own', { id: '5' }, 404);
    expect(el.querySelector('.empty-state.not-found p')?.textContent).toContain('games.notFound');
    expect(el.querySelector('.gate-actions a.to-games')?.getAttribute('href')).toBe('/games');
  });

  it('Serverfehler (500) behält „Wiederholen“', () => {
    const el = render('club-link', { token: 'x' }, 500);
    expect(el.querySelector('.empty-state.not-found')).toBeNull();
    expect(el.querySelector('app-load-error')).not.toBeNull();
  });

  it('Netzfehler (0) behält „Wiederholen“', () => {
    const el = render('own-share', { token: 'x' }, 0);
    expect(el.querySelector('app-load-error')).not.toBeNull();
  });
});
