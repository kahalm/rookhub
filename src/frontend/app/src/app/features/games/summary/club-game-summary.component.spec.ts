import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
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

/** Partie 243 (10.11.2026): Züge in der Literatur-Begründung klickbar, kein Leerzeichen zwischen „(“ und einem Zug-Knopf. */
describe('ClubGameSummaryComponent — Zug-Knöpfe im Text', () => {
  const md = [
    '- Erst den Turm aktivieren (2.Nf3 vor Lxd5). Danach 2...Sc6.', '',
    '## Empfohlene Literatur', '', '```book', 'id: 7', 'why: Mit 2.Nf3 und 2...Sc6 sofort entwickelt.', '```',
  ].join('\n');

  function render() {
    TestBed.configureTestingModule({
      imports: [ClubGameSummaryComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '243' }), data: {} } } },
        { provide: GamesService, useValue: { clubGame: () => of({ pgn: '1. e4 e5 2. Nf3 Nc6 3. Bb5 a6', ownerSide: 'white' }) } },
        { provide: ClubGameSummaryService, useValue: {
          get: () => of({ markdown: md }), suggestions: () => of([{ id: 7, title: 'Buch', accessible: false, blocked: false }]) } },
        { provide: AuthService, useValue: { isLoggedIn: false, currentUser$: of(null) } },
        { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett' } },
      ],
    });
    const fixture = TestBed.createComponent(ClubGameSummaryComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('Listenpunkt: Text klebt am Knopf, der Zug ohne Nummer bleibt Text', () => {
    const li = render().querySelector('li')!;
    expect(Array.from(li.querySelectorAll('button.mv')).map(b => b.textContent)).toEqual(['2.Nf3', '2...Sc6']);
    expect(li.textContent).toBe('Erst den Turm aktivieren (2.Nf3 vor Lxd5). Danach 2...Sc6.');
  });

  it('Begründung des Buchs mit Zug-Knöpfen', () => {
    const why = render().querySelector('.book-why')!;
    expect(Array.from(why.querySelectorAll('button.mv')).map(b => b.textContent)).toEqual(['2.Nf3', '2...Sc6']);
    expect(why.textContent).toBe('Mit 2.Nf3 und 2...Sc6 sofort entwickelt.');
  });
});

/** UI-Sweep 2026-11-10 (d10): Sperrkarte bei 403, Kartenpolster und Leerzustand mit Anfordern als Hauptknopf. */
describe('ClubGameSummaryComponent — Design-Runde d10', () => {
  const err = (status: number) => throwError(() => new HttpErrorResponse({ status }));

  function render(mode: string, params: Record<string, string>, games: object, summary: object = {}) {
    TestBed.configureTestingModule({
      imports: [ClubGameSummaryComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params), data: { mode } } } },
        { provide: GamesService, useValue: games },
        { provide: ClubGameSummaryService, useValue: { get: () => of(summary), getOwn: () => of(summary) } },
        { provide: AuthService, useValue: { isLoggedIn: true, isAdmin: false } },
        { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett' } },
      ],
    });
    const fixture = TestBed.createComponent(ClubGameSummaryComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    return fixture;
  }

  // d10-rk-zusammenfassung-ladefehler-sackgasse
  it('Vereinspartie ohne Zugang (403): Sperrkarte mit Grund und Weg zur Startseite, kein „Wiederholen“', () => {
    const f = render('club', { id: '5' }, { clubGame: () => err(403) });
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('app-load-error')).toBeNull();
    expect(el.querySelector('.empty-state mat-icon')?.textContent).toContain('lock');
    expect(el.querySelector('.empty-state p')?.textContent).toContain('games.clubNoAccess');
    const home = el.querySelector('.gate-actions a.to-home')!;
    expect(home.getAttribute('href')).toBe('/');
    expect(home.classList).toContain('mat-mdc-unelevated-button');
    f.nativeElement.remove();
  });

  it('Serverfehler (500) bei der Vereinspartie: weiter app-load-error', () => {
    const f = render('club', { id: '5' }, { clubGame: () => err(500) });
    expect((f.nativeElement as HTMLElement).querySelector('app-load-error')).not.toBeNull();
    f.nativeElement.remove();
  });

  // d10-rk-zusammenfassung-karte-ohne-polster + d10-rk-zusammenfassung-anfordern-textknopf
  it('ohne Zusammenfassung: Karte mit Polster, Leersatz in .empty-state, Anfordern als Hauptknopf darunter', () => {
    const game = { id: 5, white: 'Weiss, Erfunden', black: 'Schwarz, Erfunden', result: '1-0', pgn: '1. e4 e5 1-0' };
    const f = render('own', { id: '5' }, { get: () => of(game) }, { status: 'none', markdown: null });
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const card = el.querySelector('.summary-card') as HTMLElement;
    expect(parseFloat(getComputedStyle(card).paddingLeft)).toBeGreaterThanOrEqual(12);
    expect(parseFloat(getComputedStyle(card).paddingTop)).toBeGreaterThanOrEqual(12);
    const empty = el.querySelector('.summary-card .empty-state') as HTMLElement;
    expect(empty.textContent).toContain('games.summary.none');
    expect(getComputedStyle(empty).textAlign).toBe('center');
    const req = empty.querySelector('button.summary-request')!;
    expect(req.classList).toContain('mat-mdc-unelevated-button');
    // Im Kopf steht dann nur „Zur Partie“.
    expect(el.querySelector('.head-actions .summary-request')).toBeNull();
    f.nativeElement.remove();
  });
});
