import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, flushMicrotasks } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { LeagueApiService } from '../../core/league-api.service';
import { ClubGame, ClubGameAnalysis } from '../../core/club.models';
import { ClubGamesPageComponent } from './club-games-page.component';
import { of } from 'rxjs';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';

const G = (id: number, extra: Partial<ClubGame> = {}): ClubGame => ({
  id, year: 2024, white: 'Schwaz', black: 'Hengl, Philip', whiteFide: null, blackFide: '222', whiteElo: null, blackElo: 2172,
  result: '1-0', event: null, plies: 22, opening: '1.e4 c5 2.Nf3 d6', anonymized: true, canDelete: false, ...extra,
});

describe('ClubGamesPageComponent', () => {
  let fixture: ComponentFixture<ClubGamesPageComponent>;
  let api: jasmine.SpyObj<ClubClient>;
  /** Rückfrage (ConfirmService) — Vorgabe „ja", je Test umstellbar. */
  let confirmAsk: jasmine.Spy;
  let perms: Set<string>;

  beforeEach(() => {
    confirmAsk = jasmine.createSpy('ask').and.returnValue(of(true));
    perms = new Set(['league.view', 'league.contribute']);
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['list', 'deleteGame', 'pgn', 'updateGame', 'players', 'game', 'evalsUrl']);
    api.evalsUrl.and.callFake((id: number) => `/api/league/club/games/${id}/evals`);
    api.players.and.resolveTo([]);
    api.list.and.resolveTo({ total: 2, page: 1, pageSize: 50, items: [G(1), G(2, { white: 'Oberschmid, Patrik', whiteFide: '900', anonymized: false, canDelete: true })] });
  });

  function create(routeData: Record<string, unknown> = {}): HTMLElement {
    TestBed.configureTestingModule({
      imports: [ClubGamesPageComponent],
      providers: [{ provide: ConfirmService, useValue: { ask: (...a: unknown[]) => confirmAsk(...a) } }, 
        provideRouter([]),
        provideTranslateService({ fallbackLang: 'de' }), provideHttpClient(), provideHttpClientTesting(),
        { provide: ClubApiService, useValue: { client: () => api } },
        { provide: LeagueApiService, useValue: jasmine.createSpyObj('LeagueApiService', ['card', 'pgn']) },
        { provide: AuthService, useValue: { has: (p: string) => perms.has(p), currentUser: { username: 'patrik' } } },
        { provide: ActivatedRoute, useValue: { snapshot: { data: routeData } } },
      ],
    });
    fixture = TestBed.createComponent(ClubGamesPageComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('„Meine Partien" (0.652.0): fragt nur die eigenen ab, eigener Titel; dafür reicht beitragen', fakeAsync(() => {
    perms = new Set(['league.contribute']);
    const el = create({ mine: true });
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.list).toHaveBeenCalledWith(null, null, 1, true);
    expect(el.querySelector('.club-intro h2')?.textContent).toBe('Meine Partien');
    expect(el.textContent).toContain('jederzeit bearbeiten');
    expect(el.querySelector('lh-access-gate')).toBeNull();
  }));

  it('die Vereinspartien fragen alle ab', fakeAsync(() => {
    create();
    flushMicrotasks();
    expect(api.list).toHaveBeenCalledWith(null, null, 1, false);
  }));

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
    const rows = el.querySelectorAll('tbody tr.game');
    expect(rows[0].querySelector('.pl-nofide')?.textContent).toContain('ohne FIDE-ID');
    expect(rows[0].textContent).not.toContain('✎');
    expect(rows[1].querySelector('.pl.unknown')?.textContent).toContain('✎');
  }));

  it('zeigt die Partien: Jahr, Namen (Ligaspieler anklickbar), Eröffnung deutsch, Löschen nur wo erlaubt', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const rows = el.querySelectorAll('tbody tr.game');
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

  // UX-035: bei 390 px lag die Aktionsspalte (nowrap) 177 px rechts außerhalb — „Bearbeiten“/„Löschen“ nur durch
  // unsichtbares Querwischen erreichbar. Unter 640 px Containerbreite stehen sie jetzt in einer eigenen Zeile darunter.
  it('am Handy (390 px) stehen die Aktionen in einer eigenen Zeile, die Tabelle läuft nicht quer über (UX-035)', async () => {
    api.list.and.resolveTo({ total: 1, page: 1, pageSize: 50, items: [
      G(7, { white: 'Mustermann-Huber, Maximilian', whiteFide: '900', whiteElo: 1860, anonymized: false, canDelete: true,
        analysis: { status: 'done', analyzed: 22, total: 22, accuracyWhite: 87.4, accuracyBlack: 71.6 } })] });
    const el = create();
    el.style.display = 'block';
    el.style.width = '390px';
    await fixture.whenStable();
    fixture.detectChanges();
    const scroll = el.querySelector('.club-scroll') as HTMLElement;
    void scroll.offsetWidth;                      // Layout anstoßen, damit Barlow angefordert wird …
    await (document as unknown as { fonts: { ready: Promise<unknown> } }).fonts.ready;   // … und gemessen wird mit ihr
    const visible = (e: Element | null) => !!e && getComputedStyle(e).display !== 'none';
    const game = el.querySelector('tbody tr.game') as HTMLElement;
    const acts = el.querySelector('tbody tr.acts-row') as HTMLElement;
    expect(visible(game.querySelector('td.acts'))).toBeFalse();
    expect(visible(acts)).toBeTrue();
    const labels = Array.from(acts.querySelectorAll('.btn-link')).map(b => b.textContent?.trim());
    expect(labels).toEqual(['Nachspielen', 'Bearbeiten', 'Löschen']);
    expect(scroll.scrollWidth).toBeLessThanOrEqual(scroll.clientWidth);
    // breit wie bisher: Aktionen in der Spalte, keine eigene Zeile
    el.style.width = '900px';
    expect(visible(game.querySelector('td.acts'))).toBeTrue();
    expect(visible(acts)).toBeFalse();
  });

  it('Löschen fragt nach und nimmt die Zeile heraus', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    api.deleteGame.and.resolveTo({});
    (Array.from(el.querySelectorAll('tbody tr.game')[1].querySelectorAll('.btn-link')).find(b => b.textContent?.trim() === 'Löschen') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.deleteGame).toHaveBeenCalledWith(2);
    expect(el.querySelectorAll('tbody tr.game').length).toBe(1);
    expect(el.textContent).toContain('1 Partie');
  }));

  it('Suche fragt mit dem Begriff, ohne Treffer ein Leerzustand', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    api.list.and.resolveTo({ total: 0, page: 1, pageSize: 50, items: [] });
    fixture.componentInstance.search(' Hengl ');
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.list).toHaveBeenCalledWith(null, 'Hengl', 1, false);
    expect(el.textContent).toContain('Nichts gefunden');
  }));

  // UX-034 (b): früher „Lade …" neben „Fehler 500." und kein Weg weiter.
  it('erster Abruf scheitert: kein „Lade …", sondern Fehlerkarte mit „Erneut versuchen"', fakeAsync(() => {
    api.list.and.returnValues(
      Promise.reject(new HttpErrorResponse({ status: 500 })),
      Promise.resolve({ total: 1, page: 1, pageSize: 50, items: [G(1)] }),
    );
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).not.toContain('Lade …');
    expect(el.textContent).not.toContain('Fehler 500.');
    const gate = el.querySelector('section.gate')!;
    expect(gate.textContent).toContain('Vereinspartien nicht geladen');
    expect(gate.textContent).toContain('Der Server hatte ein Problem (500)');
    expect(el.querySelector('.stand .update-msg')?.textContent?.trim()).toBe('');   // nicht doppelt
    (gate.querySelector('button') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.list).toHaveBeenCalledTimes(2);
    expect(el.querySelector('section.gate')).toBeNull();
    expect(el.querySelectorAll('tbody tr.game').length).toBe(1);
    expect(el.textContent).toContain('1 Partie');
  }));

  it('PGN-Download, den der Browser nicht annimmt, sagt es — nicht stumm nichts (F8-006)', fakeAsync(() => {
    create();
    flushMicrotasks();
    api.pgn.and.resolveTo(new Blob(['1. e4 *'], { type: 'application/x-chess-pgn' }));
    spyOn(URL, 'createObjectURL').and.throwError('gesperrt');
    void fixture.componentInstance.download();
    flushMicrotasks();
    expect(api.pgn).toHaveBeenCalledWith(null, null);
    expect(fixture.componentInstance.error()).toBe('Die PGN-Datei konnte nicht geladen werden.');
    expect(fixture.componentInstance.downloading()).toBeFalse();
  }));

  it('ohne Freischaltung kein Abruf', () => {
    perms = new Set();
    const el = create();
    expect(el.textContent).toContain('Nicht freigeschaltet');
    expect(el.textContent).toContain('Angemeldet als patrik.');
    // UX-033: mit nächstem Schritt statt nur einem Satz.
    expect(el.querySelector('lh-access-gate')?.textContent).toContain('Freischaltung anfragen');
    expect(el.querySelector('lh-access-gate')?.textContent).toContain('Mit anderem Konto anmelden');
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
    const cell = (r: number) => el.querySelectorAll('tbody tr.game')[r].querySelectorAll('td')[6].textContent?.trim();
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
