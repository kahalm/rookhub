import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, flushMicrotasks } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { LeagueApiService } from '../../core/league-api.service';
import { ClubGame, ClubGameAnalysis } from '../../core/club.models';
import { ClubGamesPageComponent } from './club-games-page.component';

const G = (id: number, extra: Partial<ClubGame> = {}): ClubGame => ({
  id, year: 2024, white: 'Schwaz', black: 'Hengl, Philip', whiteFide: null, blackFide: '222', whiteElo: null, blackElo: 2172,
  result: '1-0', event: null, plies: 22, opening: '1.e4 c5 2.Nf3 d6', anonymized: true, canDelete: false, ...extra,
});

describe('ClubGamesPageComponent', () => {
  let fixture: ComponentFixture<ClubGamesPageComponent>;
  let api: jasmine.SpyObj<ClubClient>;
  let perms: Set<string>;

  beforeEach(() => {
    perms = new Set(['league.view', 'league.contribute']);
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['list', 'deleteGame', 'pgn', 'updateGame', 'players', 'game', 'evalsUrl']);
    api.evalsUrl.and.callFake((id: number) => `/api/league/club/games/${id}/evals`);
    api.players.and.resolveTo([]);
    api.list.and.resolveTo({ total: 2, page: 1, pageSize: 50, items: [G(1), G(2, { white: 'Oberschmid, Patrik', whiteFide: '900', anonymized: false, canDelete: true })] });
  });

  function create(): HTMLElement {
    TestBed.configureTestingModule({
      imports: [ClubGamesPageComponent],
      providers: [
        provideRouter([]),
        provideTranslateService({ fallbackLang: 'de' }), provideHttpClient(), provideHttpClientTesting(),
        { provide: ClubApiService, useValue: { client: () => api } },
        { provide: LeagueApiService, useValue: jasmine.createSpyObj('LeagueApiService', ['card', 'pgn']) },
        { provide: AuthService, useValue: { has: (p: string) => perms.has(p), currentUser: { username: 'patrik' } } },
      ],
    });
    fixture = TestBed.createComponent(ClubGamesPageComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('„Analyse" gibt das ganze PGN mit (?pgn=), eine überlange Partie nur die Züge (0.592.0)', fakeAsync(() => {
    create();
    flushMicrotasks();
    const c = fixture.componentInstance as any;
    c.rookHub = 'https://rookhub.example';
    const g = G(1, { uci: 'e2e4 e7e5', pgn: '[White "Schwaz"]\n[Black "Hengl, Philip"]\n\n1. e4 e5 *\n' });
    expect(c.analysisUrl(g)).toBe('https://rookhub.example/analysis?pgn=' + encodeURIComponent(g.pgn!));
    expect(c.analysisUrl(G(2, { uci: 'e2e4', pgn: 'x'.repeat(7000) }))).toBe('https://rookhub.example/analysis?moves=e2e4');
    expect(c.analysisUrl(G(3, { uci: 'd2d4' }))).toBe('https://rookhub.example/analysis?moves=d2d4');   // ältere API ohne PGN
  }));

  it('Ligaspieler ohne FIDE-ID: kein Bleistift, sondern „ohne FIDE-ID"; unbekannte Namen behalten ihn (0.594.0)', fakeAsync(() => {
    api.list.and.resolveTo({ total: 2, page: 1, pageSize: 50, items: [
      G(1, { black: 'Kinsiz, Atlas', blackFide: null, blackInRoster: true, canDelete: true }),
      G(2, { black: 'Niemand, Kennt', blackFide: null, canDelete: true }),
    ] });
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const rows = el.querySelectorAll('tbody tr');
    expect(rows[0].querySelector('.pl-nofide')?.textContent).toContain('ohne FIDE-ID');
    expect(rows[0].textContent).not.toContain('✎');
    expect(rows[1].querySelector('.pl.unknown')?.textContent).toContain('✎');
  }));

  it('zeigt die Partien: Jahr, Namen (Ligaspieler anklickbar), Eröffnung deutsch, Löschen nur wo erlaubt', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const rows = el.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('2024');
    expect(rows[0].querySelector('.anon')?.textContent).toBe('Schwaz');
    expect(rows[0].querySelector('button.pl')?.textContent).toContain('Hengl, Philip');
    expect(rows[0].textContent).toContain('1.e4 c5 2.Sf3 d6');
    expect(rows[0].textContent).not.toContain('Löschen');
    expect(rows[1].textContent).toContain('Löschen');
    expect(el.textContent).toContain('2 Partien');
    expect(el.querySelector('a.btn-pri')?.textContent).toContain('Partien hinzufügen');
  }));

  // Wunsch 2026-09-28: „die Spieler sollen alle klickbar sein (Kinsiz, Atlas ist nicht klickbar), Ergebnis anpassbar".
  it('ein Name ohne FIDE-ID öffnet die Korrektur; Spieler wählen und Ergebnis ändern speichert, „Schwaz" bleibt', fakeAsync(() => {
    api.list.and.resolveTo({ total: 1, page: 1, pageSize: 50,
      items: [G(53, { black: 'Kinsiz, Atlas', blackFide: null, blackElo: null, canDelete: true })] });
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const unknown = el.querySelector('button.pl.unknown') as HTMLButtonElement;
    expect(unknown.textContent).toContain('Kinsiz, Atlas');
    unknown.click();
    fixture.detectChanges();
    const editRow = el.querySelector('tr.edit-row') as HTMLElement;
    expect(editRow.textContent).toContain('bleibt anonym');                        // Weiß = Schwaz: kein Suchfeld
    expect(editRow.querySelectorAll('lh-player-search').length).toBe(1);
    const c = fixture.componentInstance;
    c.picked('black', { name: 'Kinsiz, Onur', fide: '6301517', teams: [], club: false, league: false, source: 'mega' });
    c.setResult('0-1');
    api.updateGame.and.resolveTo(G(53, { black: 'Kinsiz, Onur', blackFide: '6301517', result: '0-1', canDelete: true }));
    void c.save();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.updateGame).toHaveBeenCalledWith(53, { white: null, black: { name: 'Kinsiz, Onur', fide: '6301517', replace: false }, result: '0-1' });
    // ein Spieler von Schwaz: „ersetzen" ist vorgewählt
    c.edit(G(53, { black: 'Kinsiz, Atlas', blackFide: null, canDelete: true }));
    c.picked('black', { name: 'Oberschmid, Patrik', fide: '1693034', teams: ['Schwaz'], club: true });
    expect(c.editing()?.black?.replace).toBeTrue();
    c.editing.set(null);
    const row = el.querySelector('tbody tr') as HTMLElement;
    expect(row.querySelector('button.pl:not(.unknown)')?.textContent).toContain('Kinsiz, Onur');   // jetzt mit Karte
    expect(row.textContent).toContain('0–1');
    expect(el.querySelector('tr.edit-row')).toBeNull();
  }));

  it('Löschen fragt nach und nimmt die Zeile heraus', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    spyOn(window, 'confirm').and.returnValue(true);
    api.deleteGame.and.resolveTo({});
    (Array.from(el.querySelectorAll('tbody tr')[1].querySelectorAll('.btn-link')).find(b => b.textContent?.trim() === 'Löschen') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.deleteGame).toHaveBeenCalledWith(2);
    expect(el.querySelectorAll('tbody tr').length).toBe(1);
    expect(el.textContent).toContain('1 Partie');
  }));

  it('Suche fragt mit dem Begriff, ohne Treffer ein Leerzustand', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    api.list.and.resolveTo({ total: 0, page: 1, pageSize: 50, items: [] });
    fixture.componentInstance.search(' Hengl ');
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.list).toHaveBeenCalledWith(null, 'Hengl', 1);
    expect(el.textContent).toContain('Nichts gefunden');
  }));

  it('ohne Freischaltung kein Abruf', () => {
    perms = new Set();
    const el = create();
    expect(el.textContent).toContain('Nicht freigeschaltet');
    expect(api.list).not.toHaveBeenCalled();
  });
  // Wunsch 2026-09-28: „die Partien sollen allen aus dem Verein zur Verfügung stehen" — auch wer nur lesen darf.
  it('Spalte „Analyse": Genauigkeit bzw. Fortschritt; „Nachspielen" klappt Brett und Rückblick auf', fakeAsync(() => {
    perms = new Set(['league.view']);
    const done: ClubGameAnalysis = { status: 'done', analyzed: 22, total: 22, accuracyWhite: 87.4, accuracyBlack: 71.6 };
    api.list.and.resolveTo({ total: 3, page: 1, pageSize: 50, items: [
      G(1, { analysis: done }),
      G(2, { analysis: { status: 'running', analyzed: 5, total: 22, accuracyWhite: null, accuracyBlack: null } }),
      G(3)] });
    api.game.and.resolveTo({ ...G(1, { analysis: done }),
      pgn: '[White "Schwaz"]\n[Black "Hengl, Philip"]\n[Result "1-0"]\n\n1. e4 c5 2. Nf3 d6 1-0\n' });
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const cell = (r: number) => el.querySelectorAll('tbody tr')[r].querySelectorAll('td')[6].textContent?.trim();
    expect(cell(0)).toBe('87 · 72');
    expect(cell(1)).toBe('22 %');
    expect(cell(2)).toBe('–');

    const btn = el.querySelector('tbody tr button[aria-expanded]') as HTMLButtonElement;
    expect(btn.textContent).toContain('Nachspielen');
    btn.click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.game).toHaveBeenCalledWith(1);
    expect(el.querySelector('tr.edit-row lh-game-replay app-game-review')).not.toBeNull();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/league/club/games/1/evals').flush({ status: 'done', analyzed: 4, total: 4, targetDepth: 20,
      analysisId: 9, plies: [], final: null, bookPlies: [], refining: false, refined: 0 });
    fixture.detectChanges();
    http.expectNone(r => r.url.includes('/explanations'));

    btn.click();                                    // zuklappen
    fixture.detectChanges();
    expect(el.querySelector('tr.edit-row')).toBeNull();
    discardPeriodicTasks();
    flush();
  }));

  it('Partie ohne Analyse: nachspielen ohne Rückblick, mit Hinweis', fakeAsync(() => {
    api.list.and.resolveTo({ total: 1, page: 1, pageSize: 50, items: [G(4)] });
    api.game.and.resolveTo({ ...G(4), pgn: '[White "A"]\n[Black "B"]\n[Result "*"]\n\n1. d4 d5 *\n' });
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    (el.querySelector('tbody tr button[aria-expanded]') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.querySelector('tr.edit-row lh-game-replay')).not.toBeNull();
    expect(el.querySelector('app-game-review')).toBeNull();
    expect(el.querySelector('tr.edit-row')?.textContent).toContain('Noch nicht analysiert');
    TestBed.inject(HttpTestingController).verify();
    flush();
  }));
});
