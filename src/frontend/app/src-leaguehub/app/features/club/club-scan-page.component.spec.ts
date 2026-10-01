import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { ClubMatch, LeagueScanState } from '../../core/club.models';
import { ClubScanPageComponent } from './club-scan-page.component';

const PLY = (w: number, written: string, san: string, uci: string, extra = {}) =>
  ({ w, written, san, uci, match: 'exact', uncertain: false, ...extra });

const STATE: LeagueScanState = {
  scan: { id: 7, status: 'done', notationLanguage: 'de', createdAt: '2026-09-28T08:00:00Z', rounds: 1, moveCount: 4,
    uncertainCount: 1, unresolvedCount: 0 },
  notationLanguage: 'de',
  written: ['e4', 'e5', 'Sf3?', 'Sc6'],
  boxes: [null, null, null, null],
  plies: [
    PLY(0, 'e4', 'e4', 'e2e4'), PLY(1, 'e5', 'e5', 'e7e5'),
    PLY(2, 'Sf3?', 'Nf3', 'g1f3', { match: 'fuzzy', uncertain: true, options: [
      { san: 'Nf3', uci: 'g1f3', match: 'fuzzy', reach: 1, preview: ['Nc6'] },
      { san: 'Nh3', uci: 'g1h3', match: 'guess', reach: 1, preview: ['Nc6'] }] }),
    PLY(3, 'Sc6', 'Nc6', 'b8c6'),
  ],
  unresolved: [], unresolvedFrom: null,
  white: 'Oberschmid', black: 'Hengl', event: 'Simultan', date: '5.6.26', result: '0-1', ownerSide: 'white',
};
const MATCH = (blackLeague: boolean): ClubMatch => ({
  white: { league: true, ambiguous: false, name: 'Oberschmid, Patrik', fide: '900', club: true, candidates: [] },
  black: blackLeague ? { league: true, ambiguous: false, name: 'Hengl, Philip', fide: '222', club: false, candidates: [] }
    : { league: false, ambiguous: false, name: null, fide: null, club: false, candidates: [] },
});

describe('ClubScanPageComponent', () => {
  let fixture: ComponentFixture<ClubScanPageComponent>;
  let api: jasmine.SpyObj<ClubClient>;
  let params: Record<string, string>;
  let clubApi: { client: () => ClubClient; addToMyGames: jasmine.Spy };
  let loggedIn: boolean;
  let perms: string[] | null;

  beforeEach(() => {
    perms = null;   // null = alle Rechte
    params = { id: '7' };
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['scan', 'photo', 'match', 'players', 'resolve', 'addGame', 'discard']);
    api.scan.and.resolveTo(structuredClone(STATE));
    api.photo.and.rejectWith(new Error('kein Foto'));
    api.match.and.resolveTo(MATCH(true));
    api.players.and.resolveTo([]);
    clubApi = { client: () => api, addToMyGames: jasmine.createSpy('addToMyGames') };
    loggedIn = true;
  });

  function create(): HTMLElement {
    TestBed.configureTestingModule({
      imports: [ClubScanPageComponent],
      providers: [
        provideRouter([]),
        provideTranslateService({ fallbackLang: 'de' }),
        { provide: ClubApiService, useValue: clubApi },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params) } } },
        { provide: AuthService, useValue: { has: (p: string) => !perms || perms.includes(p), currentUser: { username: 'patrik' }, get isLoggedIn() { return loggedIn; } } },
      ],
    });
    fixture = TestBed.createComponent(ClubScanPageComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('übernimmt Kopfdaten und springt auf die unsichere Stelle; die Vorschau zeigt „Schwaz" statt meines Namens', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.s.cursor()).toBe(2);
    expect(c.year()).toBe(2026);
    expect(c.result()).toBe('0-1');
    expect(el.querySelector('.where')?.textContent).toContain('Sf3');       // deutsche Figuren
    expect(el.querySelector('.preview')?.textContent).toContain('2026 · Schwaz – Hengl, Philip · 0-1');
    expect(el.textContent).toContain('Ligaspieler: Hengl, Philip');
    expect(el.textContent).not.toContain('Kein Ligaspieler erkannt');
  }));

  it('am Handy (390 px): oben der Ausschnitt der Zeile, die Lesarten und „Stimmt so", darunter das Brett', async () => {
    const canvas = document.createElement('canvas');
    canvas.width = 400;
    canvas.height = 600;
    const blob: Blob = await new Promise(r => canvas.toBlob(b => r(b!), 'image/png'));
    const st = structuredClone(STATE);
    st.boxes = [null, null, [100, 300, 400, 340], null];
    api.scan.and.resolveTo(st);
    api.photo.and.resolveTo(blob);
    const el = create();
    el.style.display = 'block';
    el.style.width = '390px';
    await fixture.whenStable();
    fixture.detectChanges();
    const img = el.querySelector('.scan-photo img') as HTMLImageElement;
    if (!img.complete || !img.naturalWidth) await new Promise(r => img.addEventListener('load', r, { once: true }));
    img.dispatchEvent(new Event('load'));
    fixture.detectChanges();
    const check = el.querySelector('.scan-check') as HTMLElement, board = el.querySelector('.scan-board') as HTMLElement;
    expect(check.querySelector('.crop-frame')).not.toBeNull();                        // die Zeile des Formulars
    expect(check.textContent).toContain('Mögliche Lesarten');
    expect(check.textContent).toContain('Stimmt so');
    expect(check.getBoundingClientRect().bottom).toBeLessThanOrEqual(board.getBoundingClientRect().top);
    el.style.width = '1300px';                                                       // breit: Foto | Brett | Prüfen
    expect(check.getBoundingClientRect().left).toBeGreaterThan(board.getBoundingClientRect().left);
  });

  // UX-036: am Handy lag „In die Vereins-Datenbank übernehmen" bei 2 750 von 3 022 px — hinter Zugliste und GANZEM Foto,
  // dessen Ausschnitt oben schon steht. Jetzt ist das ganze Foto am Handy eingeklappt, am PC steht es wie bisher.
  it('am Handy (390 px) ist das ganze Foto eingeklappt, „Ganzes Foto zeigen" klappt es auf; breit steht es immer da (UX-036)', async () => {
    const canvas = document.createElement('canvas');
    canvas.width = 400;
    canvas.height = 600;
    const blob: Blob = await new Promise(r => canvas.toBlob(b => r(b!), 'image/png'));
    api.photo.and.resolveTo(blob);
    const el = create();
    el.style.display = 'block';
    el.style.width = '390px';
    await fixture.whenStable();
    fixture.detectChanges();
    const shown = (sel: string) => { const e = el.querySelector(sel); return !!e && getComputedStyle(e).display !== 'none'; };
    expect(shown('.scan-photo .photo-scroll')).toBeFalse();
    expect(shown('.scan-photo .photo-zoom')).toBeFalse();
    const toggle = el.querySelector('.scan-photo .photo-toggle button') as HTMLButtonElement;
    expect(shown('.scan-photo .photo-toggle')).toBeTrue();
    expect(toggle.textContent?.trim()).toBe('Ganzes Foto zeigen');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    fixture.detectChanges();
    expect(shown('.scan-photo .photo-scroll')).toBeTrue();
    expect(toggle.textContent?.trim()).toBe('Ganzes Foto ausblenden');
    toggle.click();
    fixture.detectChanges();
    el.style.width = '1300px';                                                    // PC: Foto | Brett | Prüfen, ohne Schalter
    expect(shown('.scan-photo .photo-scroll')).toBeTrue();
    expect(shown('.scan-photo .photo-toggle')).toBeFalse();
  });

  it('„Weiter zu Namen & Übernehmen" oben und am Ende der Partie führen zum Speicherteil (UX-036)', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const panel = el.querySelector('section.save-panel') as HTMLElement;
    const scroll = spyOn(panel, 'scrollIntoView');
    const top = el.querySelector('.club-intro button.to-save') as HTMLButtonElement;
    expect(top.textContent).toContain('Weiter zu Namen & Übernehmen');
    top.click();
    expect(scroll).toHaveBeenCalledTimes(1);
    expect(el.querySelector('.scan-check button.to-save')).toBeNull();            // mitten in der Partie: Prüfen geht vor
    const c = fixture.componentInstance;
    c.s.go(c.s.legalCount());                                                     // Ende der Partie (ohne Abschlussdialog)
    fixture.detectChanges();
    const end = el.querySelector('.scan-check button.to-save') as HTMLButtonElement;
    expect(end.textContent?.trim()).toBe('Weiter zu den Namen');
    end.click();
    expect(scroll).toHaveBeenCalledTimes(2);
    tick(1000);
  }));

  it('nach der letzten unsicheren Stelle kommt der Hinweis mit „Übernehmen" und „Weiter bearbeiten"', fakeAsync(() => {
    const st = structuredClone(STATE);
    st.boxes = [null, null, [100, 300, 400, 340], null];
    api.scan.and.resolveTo(st);
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const dlg = el.querySelector('dialog.done-dlg') as HTMLDialogElement;
    expect(dlg.open).toBeFalse();
    expect(fixture.componentInstance.s.mark()).toEqual({ left: 10, top: 30, width: 30, height: 4, page: 1, uncertain: true });
    (Array.from(el.querySelectorAll('.scan-check button')).find(b => b.textContent?.trim() === 'Stimmt so') as HTMLButtonElement).click();
    fixture.detectChanges();
    flushMicrotasks();
    fixture.detectChanges();
    expect(dlg.open).toBeTrue();
    expect(dlg.textContent).toContain('Alle unsicheren Stellen geprüft');
    (Array.from(dlg.querySelectorAll('button')).find(b => b.textContent?.includes('Weiter bearbeiten')) as HTMLButtonElement).click();
    expect(dlg.open).toBeFalse();
    tick(1000);
  }));

  it('eine andere Lesart wählen lässt den Rest neu lesen', fakeAsync(() => {
    create();
    flushMicrotasks();
    const c = fixture.componentInstance;
    api.resolve.and.returnValue(of({ plies: [PLY(3, 'Sc6', 'Nc6', 'b8c6')], unresolved: [] }));
    c.s.choose(STATE.plies[2].options![1]);
    expect(api.resolve).toHaveBeenCalledWith('7', ['e4', 'e5', 'Nh3'], 3);
    expect(c.s.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nh3', 'Nc6']);
  }));

  // UX-070: „Zug löschen" steht abgesetzt neben „Stimmt so" und lässt sich zurücknehmen.
  it('„Zug löschen" ist abgesetzt; danach holt „Rückgängig" den Zug samt Rest zurück', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    const buttons = () => Array.from(el.querySelectorAll<HTMLButtonElement>('.scan-check button'));
    const del = buttons().find(b => b.textContent?.trim() === 'Zug löschen')!;
    const ok = buttons().find(b => b.textContent?.trim() === 'Stimmt so')!;
    expect(del.className).toContain('del');
    expect(del.className).not.toContain('btn-sec');
    expect(ok.className).toContain('btn-sec');
    expect(el.querySelector('.undo-line')).toBeNull();

    api.resolve.and.returnValue(of({ plies: [PLY(3, 'Sc6', 'Nc6', 'b8c6')], unresolved: [] }));
    del.click();
    fixture.detectChanges();
    expect(c.s.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nc6']);
    const undo = el.querySelector<HTMLButtonElement>('.undo-line button')!;
    expect(undo.textContent?.trim()).toBe('Rückgängig');

    undo.click();
    fixture.detectChanges();
    expect(c.s.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6']);
    expect(c.s.cursor()).toBe(2);
    expect(el.querySelector('.undo-line')).toBeNull();
  }));

  it('ersetzt standardmäßig Schwaz-Spieler und die eigene Seite; eingreifen geht', fakeAsync(() => {
    api.match.and.resolveTo(MATCH(false));
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.replace('white')()).toBeTrue();                             // Oberschmid: Schwaz UND ich
    expect(el.textContent).toContain('bleibt kein bekannter Gegner übrig');
    c.setReplace('white', false);                                         // dann bleibe ich als Ligaspieler stehen
    fixture.detectChanges();
    expect(el.textContent).not.toContain('bleibt kein Gegner');
    c.setOwner('black');                                                  // „angefasst" — die Wahl bleibt
    expect(c.replace('white')()).toBeFalse();
    expect(c.replace('black')()).toBeTrue();
  }));

  it('„Meintest du …": ein ähnlicher Ligaspieler unter einem unerkannten Namen, ein Klick setzt ihn (0.596.0)', fakeAsync(() => {
    const philip = { name: 'Hengl, Philip', fide: '222', teams: ['Absam'], club: false };
    const m = MATCH(false);
    m.black = { ...m.black, similar: [philip] };
    api.match.and.resolveTo(m);
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const chip = el.querySelector('.quick-pick button') as HTMLButtonElement;
    expect(chip.textContent!.trim()).toBe('Hengl, Philip');
    chip.click();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.name('black')()).toBe('Hengl, Philip');
    expect(c.matchText('black')).toBe('Ligaspieler: Hengl, Philip');
    expect(el.querySelector('.quick-pick')).toBeNull();
    tick(1000);
  }));

  it('ein Treffer der Spielersuche (auch aus der Megabase) setzt Namen und FIDE-ID', fakeAsync(() => {
    create();
    flushMicrotasks();
    const c = fixture.componentInstance;
    c.pickPerson('black', { name: 'Hengl, Peter', fide: '777', teams: [], club: false, league: false, source: 'mega' });
    expect(c.name('black')()).toBe('Hengl, Peter');
    expect(c.matchText('black')).toBe('nicht in Liga — aus der Megabase: Hengl, Peter (FIDE 777)');
    expect(c.problem()).toBeNull();
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    api.addGame.and.resolveTo({ id: 13, anonymized: true });
    void c.save();
    flushMicrotasks();
    expect(api.addGame).toHaveBeenCalledWith(jasmine.objectContaining({ black: 'Hengl, Peter', blackFide: '777' }), '7');
    tick(1000);
  }));

  it('Übernehmen schickt Züge, Seiten und Einlesung; danach bleibt die Seite mit PGN und „Meine Partien" stehen', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    api.addGame.and.resolveTo({ id: 11, anonymized: true });
    void fixture.componentInstance.save();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.addGame).toHaveBeenCalledWith(jasmine.objectContaining({
      moves: ['e4', 'e5', 'Nf3', 'Nc6'], white: 'Oberschmid', black: 'Hengl', year: 2026, result: '0-1',
      whiteReplace: true, blackReplace: false,
    }), '7');
    expect(router.navigate).not.toHaveBeenCalled();
    expect(el.textContent).toContain('In die Vereins-Datenbank übernommen.');
    expect(el.querySelector('.save-panel .btn-pri')).toBeNull();                          // kein zweites Mal

    // PGN mit den Namen wie im Formular (nicht „Schwaz"), als Text kopieren
    const written: string[] = [];
    spyOn(navigator.clipboard, 'writeText').and.callFake(async (t: string) => { written.push(t); });
    (Array.from(el.querySelectorAll('button')).find(b => b.textContent?.trim() === 'PGN kopieren') as HTMLButtonElement).click();
    flushMicrotasks();
    expect(written[0]).toContain('[White "Oberschmid"]');
    expect(written[0]).toContain('1. e4 e5 2. Nf3 Nc6 0-1');

    clubApi.addToMyGames.and.resolveTo({ imported: 1, duplicates: 0, ids: [42] });
    (Array.from(el.querySelectorAll('button')).find(b => b.textContent?.includes('Zu meinen Partien')) as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(clubApi.addToMyGames).toHaveBeenCalledWith(jasmine.stringContaining('[Black "Hengl"]'));
    expect(el.textContent).toContain('In deinen Partien gespeichert.');
    tick(1000);
  }));

  it('ohne Anmeldung gibt es „Zu meinen Partien" nicht', fakeAsync(() => {
    loggedIn = false;
    params = { token: 'TOK', key: 'geheim' };
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).toContain('PGN herunterladen');
    expect(el.textContent).not.toContain('Zu meinen Partien');
    tick(1000);
  }));

  it('über einen Teilen-Link: Schlüssel statt Nummer, zurück auf die Hochladeseite des Links', fakeAsync(() => {
    params = { token: 'TOK', key: 'geheim' };
    create();
    flushMicrotasks();
    expect(api.scan).toHaveBeenCalledWith('geheim');
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    api.addGame.and.resolveTo({ id: 12, anonymized: true });
    void fixture.componentInstance.save();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.addGame).toHaveBeenCalledWith(jasmine.any(Object), 'geheim');
    expect(router.navigate).not.toHaveBeenCalled();
    const back = (fixture.nativeElement as HTMLElement).querySelector('.ok-text a') as HTMLAnchorElement;
    expect(back.getAttribute('href')).toContain('/s/TOK/hochladen');                      // zurück auf die Hochladeseite des Links
    tick(1000);
  }));

  // UX-034 (a): früher bei JEDEM Fehler außer 404 still alle 3 s nachfragen und „Lade …" — ohne Meldung, ohne Rückweg.
  it('Serverfehler (500): gleich Klartext mit „Neu laden" und „← Deine Formulare", kein stilles Nachfragen', fakeAsync(() => {
    api.scan.and.rejectWith(new HttpErrorResponse({ status: 500 }));
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).not.toContain('Lade …');
    const gate = el.querySelector('section.gate')!;
    expect(gate.textContent).toContain('Das Formular lässt sich gerade nicht laden');
    expect(gate.textContent).toContain('Der Server hatte ein Problem (500)');
    expect(gate.querySelector('a')?.getAttribute('href')).toBe('/verein/neu?art=formular');
    tick(9000);
    flushMicrotasks();
    expect(api.scan).toHaveBeenCalledTimes(1);

    api.scan.and.resolveTo(structuredClone(STATE));
    (gate.querySelector('button') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.scan).toHaveBeenCalledTimes(2);
    expect(el.querySelector('section.gate')).toBeNull();
    expect(el.textContent).toContain('Partieformular prüfen');
  }));

  it('vorübergehend nicht erreichbar: nach drei Fehlversuchen ein Hinweis, das Nachfragen läuft weiter und heilt', fakeAsync(() => {
    api.scan.and.rejectWith(new HttpErrorResponse({ status: 502 }));
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.querySelector('section.gate')).toBeNull();          // ein Aussetzer bleibt still
    tick(3000); flushMicrotasks();
    tick(3000); flushMicrotasks();
    fixture.detectChanges();
    expect(api.scan).toHaveBeenCalledTimes(3);
    expect(el.querySelector('section.gate')?.textContent).toContain('nicht erreichbar');
    expect(el.querySelector('section.gate')?.textContent).toContain('im Hintergrund weiter');

    api.scan.and.resolveTo(structuredClone(STATE));
    tick(3000); flushMicrotasks();
    fixture.detectChanges();
    expect(el.querySelector('section.gate')).toBeNull();
    expect(el.textContent).toContain('Partieformular prüfen');
  }));

  // UX-033: die Formular-Sperre hatte weder „Angemeldet als" noch einen Weg weiter.
  it('ohne Beitragsrecht: „Angemeldet als", fehlendes Recht, Anfrage und zurück zu den Vereinspartien — kein Abruf', () => {
    perms = ['league.view'];
    const el = create();
    const gate = el.querySelector('lh-access-gate')!;
    expect(gate.textContent).toContain('Angemeldet als patrik.');
    expect(gate.textContent).toContain('Zum Einlesen von Partieformularen fehlt dir noch die Freigabe');
    expect(gate.textContent).toContain('Freischaltung anfragen');
    expect(Array.from(gate.querySelectorAll('a')).find(a => a.textContent?.includes('Zu den Vereinspartien'))?.getAttribute('href')).toBe('/verein');
    expect(api.scan).not.toHaveBeenCalled();
  });

  it('ganz ohne Recht: der allgemeine Satz, ohne Rückweg zu den Vereinspartien', () => {
    perms = [];
    const el = create();
    const gate = el.querySelector('lh-access-gate')!;
    expect(gate.textContent).toContain('Partieformulare einlesen dürfen Admins und die Vereinsgruppe von SK Schwaz.');
    expect(gate.textContent).toContain('Angemeldet als patrik.');
    expect(gate.textContent).not.toContain('Zu den Vereinspartien');
  });
});
