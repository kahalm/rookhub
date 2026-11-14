import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { ClubGameSummaryComponent } from './club-game-summary.component';
import { ClubGameSummaryService } from './club-game-summary.service';
import { GamesService } from '../games.service';
import { AuthService } from '../../../core/auth.service';
import { MenuService } from '../../../core/menu.service';
import { SnackbarService } from '../../../core/snackbar.service';
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

/** Wunsch 2026-11-14 („summary um den (+) knopf erweitern“): das Seitenbrett bekommt je Ansicht dieselbe Herkunft wie die Partieseite. */
describe('ClubGameSummaryComponent — Herkunft für den „+“-Knopf', () => {
  function render(mode: string | undefined, params: Record<string, string>) {
    const game = () => of({ pgn: '1. e4 e5 2. Nf3 Nc6', ownerSide: 'white' });
    const text = () => of({ markdown: 'Text.' });
    TestBed.configureTestingModule({
      imports: [ClubGameSummaryComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params), data: mode ? { mode } : {} } } },
        { provide: GamesService, useValue: { linkedClubGame: game, getShared: game, sharedClubGame: game, get: game, clubGame: game } },
        { provide: ClubGameSummaryService, useValue: { getLinked: text, getSharedOwn: text, getShared: text, getOwn: text, get: text,
          suggestions: () => of([]), exercises: () => of([]) } },
        { provide: AuthService, useValue: { isLoggedIn: false, currentUser: null, currentUser$: of(null) } },
        { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett' } },
      ],
    });
    const fixture = TestBed.createComponent(ClubGameSummaryComponent);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('app-summary-game-board .nav-mark button.mark')).not.toBeNull();
    return fixture.componentInstance.sideBoard()!.markSource();
  }

  it('Vereinspartie /club-games/:id: ihre Id', () => {
    expect(render(undefined, { id: '245' })).toEqual({ clubGameId: 245 });
  });

  it('LeagueHub-Teilen-Link /s/:token/club-games/:id: die Vereinspartie, wie auf der Partieseite', () => {
    expect(render('club-share', { token: 'abc', id: '245' })).toEqual({ clubGameId: 245 });
  });

  it('eigene Partie /games/:id: ihre Id', () => {
    expect(render('own', { id: '12' })).toEqual({ savedGameId: 12 });
  });

  it('Teilen-Link /g/:token: der Link', () => {
    expect(render('own-share', { token: 'tok' })).toEqual({ shareToken: 'tok' });
  });

  it('Link einer Vereinspartie /cg/:token: keine Partie als Herkunft (nur lesen), der Knopf bleibt', () => {
    expect(render('club-link', { token: 'tok' })).toEqual({});
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
          get: () => of({ markdown: md }), suggestions: () => of([{ id: 7, title: 'Buch', accessible: false, blocked: false }]),
          exercises: () => of([]) } },
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

/** Wunsch 2026-11-14: „Die Partie“ als ein Text — Partiezüge im Fließtext, Varianten in Klammern; alte Texte behalten die Zugliste. */
describe('ClubGameSummaryComponent — verschmolzener Abschnitt „Die Partie“', () => {
  const merged = [
    '# A – B', '', 'Kopf.', '', '## Die Partie', '',
    'Die Partie begann spanisch: 1.e4 e5 2.Sf3 Sc6 3.Lb5 a6! (statt 3...Sf6 4.0-0 mit der Berliner Mauer, 0,0) und Schluss (0,0).', '',
    '## Was man mitnehmen kann', '', '- Nach 2.Sf3 entwickeln.',
  ].join('\n');
  const old = ['# A – B', '', 'Kopf.', '', '## Zusammenfassung', '', 'Nach 1.e4 e5 stand es gleich.', '', '## Fazit', '', 'x'].join('\n');

  function render(md: string) {
    TestBed.configureTestingModule({
      imports: [ClubGameSummaryComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '244' }), data: {} } } },
        { provide: GamesService, useValue: { clubGame: () => of({ pgn: '1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1/2-1/2', ownerSide: 'white', result: '1/2-1/2' }) } },
        { provide: ClubGameSummaryService, useValue: { get: () => of({ markdown: md }), suggestions: () => of([]), exercises: () => of([]) } },
        { provide: AuthService, useValue: { isLoggedIn: false, currentUser$: of(null) } },
        { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett' } },
      ],
    });
    const fixture = TestBed.createComponent(ClubGameSummaryComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('verschmolzen: ein Fließtext, Partiezüge fett, Variante in eigener Klammer-Spanne, keine eingesetzte Zugliste', () => {
    const el = render(merged).nativeElement as HTMLElement;
    expect(el.querySelector('.game-moves')).toBeNull();
    expect(Array.from(el.querySelectorAll('h2')).map(h => h.textContent)).toEqual(['Die Partie', 'Was man mitnehmen kann']);
    const p = el.querySelector('p.game-text')!;
    expect(p.textContent).toBe('Die Partie begann spanisch: 1.e4 e5 2.Sf3 Sc6 3.Lb5 a6!½–½ (statt 3...Sf6 4.0-0 mit der Berliner Mauer, 0,0) und Schluss (0,0).');
    expect(Array.from(p.querySelectorAll('button.gt-mv.gt-game')).map(b => b.textContent)).toEqual(['1.e4', 'e5', '2.Sf3', 'Sc6', '3.Lb5', 'a6!']);
    const variation = p.querySelector('.gt-var')!;
    expect(variation.textContent).toBe('(statt 3...Sf6 4.0-0 mit der Berliner Mauer, 0,0)');
    expect(Array.from(variation.querySelectorAll('button.gt-mv')).map(b => b.className.includes('gt-game'))).toEqual([false, false]);
    expect(p.querySelectorAll('.gt-var').length).toBe(1);
    expect(p.querySelector('button.gt-good')!.textContent).toBe('a6!');
    expect(p.querySelector('.gt-result')!.textContent).toBe('½–½');
    // übrige Abschnitte wie bisher: Zug-Knopf im Listenpunkt
    expect(el.querySelector('li button.mv')!.textContent).toBe('2.Sf3');
  });

  it('Klick: Partiezug springt als Halbzug (und wird hervorgehoben), Variantenzug als Zugfolge', () => {
    const fixture = render(merged);
    const el = fixture.nativeElement as HTMLElement;
    const calls: unknown[] = [];
    spyOn(fixture.componentInstance, 'showOnBoard').and.callFake(j => { calls.push(j); });
    (el.querySelectorAll('button.gt-mv.gt-game')[2] as HTMLButtonElement).click();
    (el.querySelector('.gt-var button.gt-mv') as HTMLButtonElement).click();
    expect(calls[0]).toEqual({ ply: 3 });
    const v = calls[1] as { line: { san: string }[]; at: number };
    expect(v.at).toBe(6);
    expect(v.line.map(m => m.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5', 'Nf6', 'O-O']);
  });

  it('alte Zusammenfassung ohne den Abschnitt: Zugliste wie bisher hinter „Zusammenfassung“, kein verschmolzener Text', () => {
    const el = render(old).nativeElement as HTMLElement;
    expect(el.querySelector('p.game-text')).toBeNull();
    expect(el.querySelectorAll('.game-moves button.gm').length).toBe(6);
    expect(Array.from(el.querySelectorAll('h2')).map(h => h.textContent)).toEqual(['Zusammenfassung', 'games.summary.theGame', 'Fazit']);
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
        { provide: MenuService, useValue: { visible$: of(new Set<string>()) } },   // angemeldet liest die Seite die Menü-Sichtbarkeit
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

/** Empfohlene Übungen (0.960.0): Abschnitt nur mit Stellen; ohne (Partie ohne Mattbild) oder bei Fehler gar keiner. */
describe('ClubGameSummaryComponent — Empfohlene Übungen', () => {
  const group = { ply: 28, white: true, reason: 'missedMate', mateIn: 4, alsoPlies: [], exercises: [
    { id: 11, fen: '6k1/p4p1p/5P2/6Q1/8/8/8/K7 b - - 0 1', moves: ['a7a6', 'g5g7'], rating: 1500, themes: ['mateIn1'], match: 'strict' }] };

  function render(exercises: () => unknown, data: Record<string, string> = {}, params: Record<string, string> = { id: '5' }) {
    const calls: unknown[] = [];
    TestBed.configureTestingModule({
      imports: [ClubGameSummaryComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params), data } } },
        { provide: GamesService, useValue: { clubGame: () => of({ pgn: '1. e4 e5', ownerSide: 'white' }), getShared: () => of({ pgn: '1. e4 e5' }) } },
        { provide: ClubGameSummaryService, useValue: {
          get: () => of({ markdown: '# Titel\n\nText.' }), getSharedOwn: () => of({ markdown: '# Titel' }),
          exercises: (src: unknown) => { calls.push(src); return exercises(); } } },
        { provide: AuthService, useValue: { isLoggedIn: false, currentUser$: of(null) } },
        { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett' } },
      ],
    });
    const fixture = TestBed.createComponent(ClubGameSummaryComponent);
    fixture.detectChanges();
    return { el: fixture.nativeElement as HTMLElement, calls };
  }

  it('mit Stellen: Abschnitt nach dem Text, Karten mit Brett', () => {
    const { el, calls } = render(() => of([group]));
    expect(calls).toEqual([{ kind: 'club', id: 5 }]);
    const sec = el.querySelector('app-summary-exercises')!;
    expect(sec).not.toBeNull();
    expect(sec.querySelectorAll('.card app-chess-board').length).toBe(1);
    expect(sec.querySelector('h3')?.textContent).toContain('15.');
  });

  it('Partie ohne Mattbild (keine Stellen): kein Abschnitt, kein Platzhalter', () => {
    const { el } = render(() => of([]));
    expect(el.querySelector('app-summary-exercises')).toBeNull();
    expect(el.textContent).not.toContain('games.summary.exercises');
  });

  it('Fehler beim Laden: still kein Abschnitt', () => {
    const { el } = render(() => throwError(() => new HttpErrorResponse({ status: 500 })));
    expect(el.querySelector('app-summary-exercises')).toBeNull();
    expect(el.querySelector('app-load-error')).toBeNull();
  });

  it('Teilen-Link der eigenen Partie fragt über denselben Weg', () => {
    const { calls } = render(() => of([]), { mode: 'own-share' }, { token: 'abc' });
    expect(calls).toEqual([{ kind: 'own-share', token: 'abc' }]);
  });
});

/** „Taktiken trainieren“ (Wunsch 2026-11-14): unter den Empfohlenen Übungen, nur angemeldet; legt das Kapitel an und springt in den Kurs-Löser. */
describe('ClubGameSummaryComponent — „Taktiken trainieren“', () => {
  const group = { ply: 36, white: false, reason: 'allowedTactic', theme: 'fork', mateIn: null, alsoPlies: [], exercises: [
    { id: 11, fen: 'r3k3/p7/8/3N4/8/8/8/4K3 b - - 0 1', moves: ['a7a6', 'd5c7', 'e8d7', 'c7a8'], rating: 1500, themes: ['fork'], match: 'strict' }] };

  function render(opts: { loggedIn: boolean; menu?: string[]; train?: () => unknown; data?: Record<string, string>; params?: Record<string, string> }) {
    const calls: unknown[] = [];
    const warn = jasmine.createSpy('warn');
    TestBed.configureTestingModule({
      imports: [ClubGameSummaryComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(opts.params ?? { id: '247' }), data: opts.data ?? {} } } },
        { provide: GamesService, useValue: { clubGame: () => of({ pgn: '1. e4 e5', ownerSide: 'white' }), linkedClubGame: () => of({ pgn: '1. e4 e5' }) } },
        { provide: ClubGameSummaryService, useValue: {
          get: () => of({ markdown: '# Titel\n\nText.' }), getLinked: () => of({ markdown: '# Titel' }),
          exercises: () => of([group]),
          trainTactics: (src: unknown, lang: string) => { calls.push([src, lang]); return (opts.train ?? (() => of({ bookId: 9, chapterIndex: 2, chapter: 'x', created: true, lines: 1 })))(); } } },
        { provide: AuthService, useValue: { isLoggedIn: opts.loggedIn, currentUser$: of(null) } },
        { provide: MenuService, useValue: { visible$: of(new Set(opts.menu ?? ['courses'])) } },
        { provide: SnackbarService, useValue: { warn, info: () => undefined } },
        { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett' } },
      ],
    });
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(ClubGameSummaryComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el, calls, navigate, warn, button: () => el.querySelector('app-summary-exercises .train-btn') as HTMLButtonElement | null };
  }

  it('angemeldet: Knopf unter dem Abschnitt; Klick legt das Kapitel an und öffnet es sequenziell im Kurs-Löser', () => {
    const { button, calls, navigate } = render({ loggedIn: true });
    expect(button()).not.toBeNull();
    button()!.click();
    expect(calls).toEqual([[{ kind: 'club', id: 247 }, 'en']]);
    expect(navigate).toHaveBeenCalledOnceWith(['/courses', 9, 'chapter', 2, 'sequential']);
  });

  it('ohne Anmeldung (auch über einen Teilen-Link): kein Knopf', () => {
    expect(render({ loggedIn: false }).button()).toBeNull();
    TestBed.resetTestingModule();
    expect(render({ loggedIn: false, data: { mode: 'club-link' }, params: { token: 'abc' } }).button()).toBeNull();
  });

  it('angemeldet über den Link einer Vereinspartie: derselbe Weg wie die Übungen', () => {
    const { button, calls } = render({ loggedIn: true, data: { mode: 'club-link' }, params: { token: 'abc' } });
    button()!.click();
    expect(calls).toEqual([[{ kind: 'club-link', token: 'abc' }, 'en']]);
  });

  it('angemeldet, aber ohne den Menüpunkt „Kurse“: kein Knopf (der Löser wiese ab)', () => {
    expect(render({ loggedIn: true, menu: ['puzzles'] }).button()).toBeNull();
  });

  it('Kapitel ohne abfragbare Linie: auf die Kursseite', () => {
    const { button, navigate } = render({ loggedIn: true, train: () => of({ bookId: 9, chapterIndex: null, chapter: null, created: false, lines: 1 }) });
    button()!.click();
    expect(navigate).toHaveBeenCalledOnceWith(['/courses', 9]);
  });

  it('Fehler: Hinweis, kein Sprung, Knopf wieder frei', () => {
    const { fixture, button, navigate, warn } = render({ loggedIn: true, train: () => throwError(() => new HttpErrorResponse({ status: 400 })) });
    button()!.click();
    fixture.detectChanges();
    expect(warn).toHaveBeenCalledOnceWith('games.summary.exercises.trainFailed');
    expect(navigate).not.toHaveBeenCalled();
    expect(button()!.disabled).toBeFalse();
  });
});

describe('ClubGameSummaryService.trainTactics', () => {
  it('schickt die Partie auf dem Weg der Seite und die Sprache', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(ClubGameSummaryService).trainTactics({ kind: 'club-share', token: 't', id: 5 }, 'de').subscribe();
    const req = http.expectOne('/api/courses/personal-tactics/from-game');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ kind: 'club-share', token: 't', id: 5, lang: 'de' });
    req.flush({ bookId: 1, chapterIndex: 0, chapter: 'x', created: true, lines: 3 });
    http.verify();
  });
});
