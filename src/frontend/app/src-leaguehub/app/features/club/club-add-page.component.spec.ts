import { provideTestClub } from '../../core/club-context.testing';
import { ComponentFixture, TestBed, fakeAsync, flush, flushMicrotasks, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { ClubPreview, ScoresheetScan } from '../../core/club.models';
import { ClubAddPageComponent, anonKeys } from './club-add-page.component';
import { of } from 'rxjs';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';

const SCAN = (status: ScoresheetScan['status']): ScoresheetScan => ({
  id: 7, status, notationLanguage: 'de', createdAt: '2026-09-28T08:00:00Z', rounds: 1, moveCount: 40, uncertainCount: 2,
  unresolvedCount: 0, white: 'Didi', black: 'Hengl',
});
const M = { league: true, ambiguous: false, name: 'Hengl, Philip', fide: '222', club: false, candidates: [] };
const PREVIEW: ClubPreview = { truncated: false, games: [
  { index: 1, year: 2024, result: '1-0', event: null, plies: 40, opening: '1.e4 c5', error: null, duplicate: false,
    white: { raw: 'Oberschmid', elo: null, match: { ...M, name: 'Oberschmid, Patrik', fide: '900', club: true }, owner: true, replace: true },
    black: { raw: 'Hengl', elo: null, match: M, owner: false, replace: false } },
] };

describe('ClubAddPageComponent', () => {
  let fixture: ComponentFixture<ClubAddPageComponent>;
  let api: jasmine.SpyObj<ClubClient>;
  /** Rückfrage (ConfirmService) — Vorgabe „ja", je Test umstellbar. */
  let confirmAsk: jasmine.Spy;
  let service: { client: jasmine.Spy; savedGame: jasmine.Spy; openScans: jasmine.Spy; allDrafts: jasmine.Spy };
  let query: Record<string, string>;
  let params: Record<string, string>;

  beforeEach(() => {
    confirmAsk = jasmine.createSpy('ask').and.returnValue(of(true));
    localStorage.removeItem('lh-anon-scans');
    query = {};
    params = {};
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['preview', 'importPgn', 'scans', 'scoresheetStatus', 'upload', 'discard', 'players', 'match', 'lichess',
      'createDraft', 'drafts', 'draft', 'saveDraft', 'deleteDraft', 'chessBase']);
    api.scans.and.resolveTo([]);
    api.drafts.and.resolveTo([]);
    api.createDraft.and.callFake(async () => ({ ref: '7', id: 7, source: 'text', label: null, gameCount: 1, importedCount: 0,
      createdAt: '2026-09-28T18:00:00', updatedAt: '2026-09-28T18:00:00', viaShareLink: false, mine: true }));
    api.saveDraft.and.resolveTo();
    api.deleteDraft.and.resolveTo();
    api.scoresheetStatus.and.resolveTo({ available: true, dailyLimit: 10, usedToday: 0, languages: [{ code: 'de', name: 'Deutsch', pieces: 'KDTLS' }] });
    service = { client: jasmine.createSpy('client').and.returnValue(api), savedGame: jasmine.createSpy('savedGame'),
      openScans: jasmine.createSpy('openScans').and.resolveTo([]), allDrafts: jasmine.createSpy('allDrafts').and.resolveTo([]) };
  });

  function create(perms: boolean | string[] = true): HTMLElement {
    const has = (p: string) => Array.isArray(perms) ? perms.includes(p) : perms;
    TestBed.configureTestingModule({
      imports: [ClubAddPageComponent],
      providers: [provideTestClub(), { provide: ConfirmService, useValue: { ask: (...a: unknown[]) => confirmAsk(...a) } }, 
        provideRouter([]),
        { provide: ClubApiService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query), paramMap: convertToParamMap(params) } } },
        { provide: AuthService, useValue: { has, currentUser: perms ? { username: 'patrik' } : null } },
      ],
    });
    fixture = TestBed.createComponent(ClubAddPageComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('PGN: erst die Übersicht, gespeichert wird erst mit „Importieren"', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    expect(service.client).toHaveBeenCalledWith(null);
    expect((el.querySelector('.anon-toggle input') as HTMLInputElement).checked).toBeTrue();
    api.preview.and.resolveTo(PREVIEW);
    fixture.componentInstance.pgn.set('[White "x"]\n1. e4 *');
    fixture.detectChanges();
    (el.querySelector('.btn-pri') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.createDraft).toHaveBeenCalledWith('[White "x"]\n1. e4 *', 'text', null);   // gleich online abgelegt (0.595.0)
    expect(api.preview).toHaveBeenCalledWith('[White "x"]\n1. e4 *', 7);
    expect(api.importPgn).not.toHaveBeenCalled();
    expect(el.querySelector('.review-table')?.textContent).toContain('Hengl, Philip');

    api.importPgn.and.resolveTo({ added: 1, anonymized: 1, duplicates: 0, truncated: false, ids: [5],
      failed: [] });
    (el.querySelector('lh-club-import-review .btn-pri') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.importPgn).toHaveBeenCalledWith('[White "x"]\n1. e4 *',
      [{ index: 1, white: { name: null, fide: '900', replace: true }, black: { name: null, fide: '222', replace: false }, leagueGameId: 0 }], 7);
    expect(el.querySelector('.result')?.textContent).toContain('1 Partie übernommen (1 mit „Testdorf“).');
    expect(api.deleteDraft).toHaveBeenCalledWith('7');                  // fertig importiert → Entwurf samt Rohtext weg
  }));

  it('Entwürfe (0.595.0): Korrekturen landen gedrosselt im Entwurf; offene Listen stehen da und lassen sich fortsetzen', fakeAsync(() => {
    const D = { ref: '9', id: 9, source: 'datei', label: 'liga.pgn', gameCount: 2, importedCount: 1,
      createdAt: '2026-09-28T17:00:00', updatedAt: '2026-09-28T17:30:00', viaShareLink: false, mine: true };
    api.drafts.and.resolveTo([D]);
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const item = el.querySelector('.scan-list li')!;
    expect(item.textContent).toContain('liga.pgn (2 Partien)');
    expect(item.textContent).toContain('1 von 2 importiert');

    api.draft.and.resolveTo({ ...D, pgn: '[White "x"]\n1. e4 *', imported: [1],
      state: JSON.stringify({ v: 1, replaceClub: true, games: [] }) });
    api.preview.and.resolveTo(PREVIEW);
    (Array.from(item.querySelectorAll('button')).find(b => b.textContent?.includes('Weiter')) as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.preview).toHaveBeenCalledWith('[White "x"]\n1. e4 *', 9);     // für den Einreicher gelesen
    expect(api.createDraft).not.toHaveBeenCalled();                         // derselbe Entwurf, kein neuer
    expect(el.querySelector('.draft-note')).not.toBeNull();

    fixture.componentInstance.review()!.toggleInclude(1);                   // eine Korrektur …
    fixture.detectChanges();
    tick(1600);                                                              // … gedrosselt gespeichert
    expect(api.saveDraft).toHaveBeenCalledWith('9', { state: jasmine.any(String) });
    fixture.componentInstance.onSaved([2]);
    expect(api.saveDraft).toHaveBeenCalledWith('9', { imported: [1, 2] }); // frühere + neue Portion
    flush();
  }));

  // Codereview W3 F7-004: „Verwerfen" neben „Importieren" löschte den Entwurf samt Korrekturen ohne Rückfrage — beim
  // Fertigstellen auch den eines anderen. Jetzt heißt der Knopf „Schließen" und der Entwurf bleibt liegen.
  function closeButton(el: HTMLElement): HTMLButtonElement {
    return Array.from(el.querySelectorAll('lh-club-import-review button')).filter(b => b.textContent?.trim() === 'Schließen').pop() as HTMLButtonElement;
  }

  it('„Schließen" in der Übersicht behält den Entwurf und speichert die letzte Korrektur', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    api.preview.and.resolveTo(PREVIEW);
    fixture.componentInstance.pgn.set('[White "x"]\n1. e4 *');
    fixture.detectChanges();
    (el.querySelector('.btn-pri') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(Array.from(el.querySelectorAll('lh-club-import-review button')).some(b => b.textContent?.trim() === 'Verwerfen')).toBeFalse();

    fixture.componentInstance.review()!.toggleInclude(1);                   // Korrektur, Drossel läuft noch …
    fixture.detectChanges();
    api.drafts.calls.reset();
    const confirmSpy = confirmAsk;
    closeButton(el).click();                                                // … und sofort schließen
    flushMicrotasks();
    fixture.detectChanges();

    expect(api.deleteDraft).not.toHaveBeenCalled();
    expect(confirmSpy).not.toHaveBeenCalled();
    expect(api.saveDraft).toHaveBeenCalledWith('7', { state: jasmine.any(String) });
    expect(fixture.componentInstance.review()).toBeNull();
    expect(fixture.componentInstance.pgn()).withContext('Text liegt im Entwurf, kein zweiter beim erneuten Prüfen').toBe('');
    expect(api.drafts).toHaveBeenCalled();                                  // Liste neu geladen — dort steht der Entwurf
    tick(2000);
    expect(api.saveDraft).toHaveBeenCalledTimes(1);                        // keine zweite, verspätete Speicherung
  }));

  it('„Schließen" ohne Online-Entwurf fragt, weil die Korrekturen sonst verloren gehen', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    api.preview.and.resolveTo(PREVIEW);
    api.createDraft.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'tooManyDrafts' } }));
    fixture.componentInstance.pgn.set('[White "x"]\n1. e4 *');
    fixture.detectChanges();
    (el.querySelector('.btn-pri') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    const confirmSpy = confirmAsk.and.returnValue(of(false));
    closeButton(el).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(confirmSpy).toHaveBeenCalled();
    expect(fixture.componentInstance.review()).not.toBeNull();              // abgelehnt → Übersicht bleibt offen
    expect(api.deleteDraft).not.toHaveBeenCalled();

    confirmSpy.and.returnValue(of(true));
    closeButton(el).click();
    flushMicrotasks();
    expect(fixture.componentInstance.review()).toBeNull();
    expect(fixture.componentInstance.pgn()).withContext('ohne Entwurf bleibt wenigstens der Text im Feld').toBe('[White "x"]\n1. e4 *');
  }));

  it('Verwerfen einer fremden Liste nennt in der Rückfrage, von wem sie ist', fakeAsync(() => {
    const D = { ref: '12', id: 12, source: 'datei', label: 'liga.pgn', gameCount: 200, importedCount: 0,
      createdAt: '2026-09-28T17:00:00', updatedAt: '2026-09-28T17:30:00', owner: 'hans', viaShareLink: false, mine: false };
    service.allDrafts.and.resolveTo([D]);
    const confirmSpy = confirmAsk.and.returnValue(of(false));
    const el = create(['league.contribute', 'league.manage']);
    flushMicrotasks();
    fixture.detectChanges();
    (Array.from(el.querySelectorAll('.scan-list button')).find(b => b.textContent?.trim() === 'Verwerfen') as HTMLButtonElement).click();
    flushMicrotasks();
    expect(confirmSpy.calls.mostRecent().args[0]).toContain('von hans');
    expect(api.deleteDraft).not.toHaveBeenCalled();
  }));

  it('Formular angemeldet: 10 je Tag, Foto hochladen, nachfragen bis gelesen', fakeAsync(() => {
    query = { art: 'formular' };
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).toContain('10 je 24 Stunden (heute: 0 von 10)');
    api.upload.and.resolveTo({ ref: '7', scan: SCAN('pending') });
    api.scans.and.resolveTo([{ ref: '7', scan: SCAN('done') }]);
    const file = new File(['x'], 'bogen.jpg', { type: 'image/jpeg' });
    fixture.componentInstance.photo.set(file);
    fixture.componentInstance.side.set('white');
    void fixture.componentInstance.upload();
    flushMicrotasks();
    expect(api.upload).toHaveBeenCalledWith([file], 'auto', 'white');
    tick(3000);
    flushMicrotasks();
    fixture.detectChanges();
    const link = el.querySelector('.scan-list a') as HTMLAnchorElement;
    expect(link.textContent).toContain('Prüfen und übernehmen');
    expect(link.getAttribute('href')).toBe('/verein/formular/7');
  }));

  // UX-034 (c): früher „Lade …" für immer und darunter „Hochladen hat nicht geklappt (HTTP 500)" — ohne Upload.
  it('Formular: scheitert der Status-Abruf, sagt die Seite das (kein Upload-Fehler) und bietet „Neu laden"', fakeAsync(() => {
    query = { art: 'formular' };
    api.scoresheetStatus.and.rejectWith(new HttpErrorResponse({ status: 500 }));
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const panel = el.querySelector('section.panel')!;
    expect(panel.textContent).toContain('Ob Einlesen gerade geht, ließ sich nicht prüfen.');
    expect(panel.textContent).toContain('Der Server hatte ein Problem (500)');
    expect(panel.textContent).not.toContain('Hochladen hat nicht geklappt');
    expect(panel.textContent).not.toContain('Lade …');
    api.scoresheetStatus.and.resolveTo({ available: true, dailyLimit: 10, usedToday: 0, languages: [] });
    const retry = Array.from(panel.querySelectorAll('button')).find(b => b.textContent?.includes('Neu laden')) as HTMLButtonElement;
    retry.click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).toContain('10 je 24 Stunden (heute: 0 von 10)');
    expect(el.textContent).not.toContain('ließ sich nicht prüfen');
    expect(el.querySelector('input[type="file"]')).not.toBeNull();
  }));

  it('Formular über mehrere Blätter: „+ Seite 2/3“ und alle Fotos in einer Einlesung', fakeAsync(() => {
    query = { art: 'formular' };
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.querySelector('.add-page')).toBeNull();                       // erst nach dem ersten Foto
    const c = fixture.componentInstance;
    const p1 = new File(['1'], 's1.jpg', { type: 'image/jpeg' });
    const p2 = new File(['2'], 's2.jpg', { type: 'image/jpeg' });
    const p3 = new File(['3'], 's3.jpg', { type: 'image/jpeg' });
    c.photo.set(p1);
    fixture.detectChanges();
    (el.querySelector('.add-page') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelectorAll('.extra-photo').length).toBe(1);
    expect(el.querySelector('.add-page')).toBeNull();                       // erst wenn Seite 2 ein Foto hat
    c.extraPhotos.set([p2]);
    fixture.detectChanges();
    expect(el.querySelector('.add-page')?.textContent).toContain('Seite 3');
    c.addExtra();
    c.extraPhotos.set([p2, p3]);
    fixture.detectChanges();
    expect(el.querySelector('.add-page')).toBeNull();                       // höchstens drei
    api.upload.and.resolveTo({ ref: '7', scan: SCAN('pending') });
    void c.upload();
    flushMicrotasks();
    expect(api.upload).toHaveBeenCalledWith([p1, p2, p3], 'auto', 'auto');
    expect(c.extraPhotos()).toEqual([]);
    flush();
  }));

  it('über einen Teilen-Link: ohne Anmeldung, Schlüssel gemerkt, Korrektur unter /s/…', fakeAsync(() => {
    params = { token: 'TOK' };
    query = { art: 'formular' };
    const el = create(false);
    flushMicrotasks();
    fixture.detectChanges();
    expect(service.client).toHaveBeenCalledWith('TOK');
    expect(el.textContent).not.toContain('Nicht freigeschaltet');
    api.upload.and.resolveTo({ ref: 'geheim', scan: SCAN('done') });
    fixture.componentInstance.photo.set(new File(['x'], 'b.jpg', { type: 'image/jpeg' }));
    void fixture.componentInstance.upload();
    flushMicrotasks();
    fixture.detectChanges();
    expect(anonKeys('TOK')).toEqual(['geheim']);
    expect((el.querySelector('.scan-list a') as HTMLAnchorElement).getAttribute('href')).toBe('/s/TOK/formular/geheim');
    tick(3000);
  }));

  it('der Formular-Hinweis nennt Anthropic und verlinkt den Datenschutz (A6-008)', fakeAsync(() => {
    params = { token: 'TOK' };
    query = { art: 'formular' };
    const el = create(false);
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).toContain('Anthropic (USA)');
    expect(el.querySelector('a[href="/privacy"]')).not.toBeNull();
  }));

  it('eine öffentliche Lichess-Studie laden führt direkt in die Übersicht; Absagen stehen als Satz da', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    api.lichess.and.resolveTo('[Event "Studie: Kapitel 1"]\n1. e4 *');
    api.preview.and.resolveTo(PREVIEW);
    fixture.componentInstance.studyUrl.set('https://lichess.org/study/AbCdEf12');
    void fixture.componentInstance.loadStudy();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.lichess).toHaveBeenCalledWith('https://lichess.org/study/AbCdEf12');
    expect(api.preview).toHaveBeenCalledWith('[Event "Studie: Kapitel 1"]\n1. e4 *', 7);
    expect(api.createDraft).toHaveBeenCalledWith('[Event "Studie: Kapitel 1"]\n1. e4 *', 'lichess', jasmine.stringContaining('lichess.org'));
    expect(el.querySelector('.review-table')).not.toBeNull();

    fixture.componentInstance.review.set(null);
    api.lichess.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'lichessNotFound' } }));
    void fixture.componentInstance.loadStudy();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).toContain('nicht öffentlich');
  }));

  it('ChessBase-Datenbank: nur die nötigen Dateien gepackt hoch, mehr als 500 Partien werden zu offenen Listen', async () => {
    create();
    const game = (i: number) => `[Event "Liga"]\n[White "W${i}"]\n[Black "B${i}"]\n[Result "1-0"]\n\n1. e4 e5 1-0`;
    const pgn = Array.from({ length: 1100 }, (_, i) => game(i + 1)).join('\n\n') + '\n\n';
    api.chessBase.and.resolveTo({ format: '2cbh', name: 'Verein', pgn, games: 1100, converted: 1100, deleted: 0, truncated: false, skippedCount: 0, skipped: [] });
    api.preview.and.resolveTo(PREVIEW);
    const files = ['Verein.2cbh', 'Verein.2cbg', 'Verein.2lid', 'Verein.2cba', 'Verein.ini'].map(n => new File(['abc'], n));
    const input = { files, value: 'C:\\fakepath\\Verein.2cbh' };
    await fixture.componentInstance.pickChessBase({ target: input } as unknown as Event);
    for (let i = 0; i < 5; i++) await new Promise(r => setTimeout(r));
    fixture.detectChanges();

    expect(input.value).toBe('');
    expect(api.chessBase.calls.mostRecent().args[0].map(f => f.name)).toEqual(['Verein.2cbh.gz', 'Verein.2cbg.gz', 'Verein.2lid.gz']);
    const part1 = api.preview.calls.mostRecent().args[0];
    expect(part1.match(/\[Event /g)!.length).toBe(500);
    expect(api.createDraft.calls.allArgs().map(a => [a[1], a[2]])).toEqual([
      ['chessbase', 'Verein.2cbh (Teil 1 von 3)'], ['chessbase', 'Verein.2cbh (Teil 2 von 3)'], ['chessbase', 'Verein.2cbh (Teil 3 von 3)']]);
    expect(api.createDraft.calls.argsFor(2)[0].match(/\[Event /g)!.length).toBe(100);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.db-note')?.textContent).toContain('Verein: 1.100 Partien gelesen.');
    expect(el.querySelector('.portion-note')?.textContent).toContain('in 3 Pakete');
    expect(el.querySelector('.portion-note')?.textContent).toContain('unter „Deine offenen Listen“');
  });

  it('eine PGN-Datei mit mehr als 500 Partien wird ganz gelesen und in Paketen angeboten (0.598.1)', async () => {
    const el = create();
    const game = (i: number) => `[White "W${i}"]\n[Black "B${i}"]\n[Result "1-0"]\n\n1. e4 {Kommentar\n[%clk 0:01:00]} e5 1-0`;
    const pgn = Array.from({ length: 1001 }, (_, i) => game(i + 1)).join('\n\n');
    api.preview.and.resolveTo(PREVIEW);
    await fixture.componentInstance.pickFile({ target: { files: [new File([pgn], 'Verein.pgn')] } } as unknown as Event);
    for (let i = 0; i < 5; i++) await new Promise(r => setTimeout(r));
    fixture.detectChanges();

    expect(api.preview.calls.mostRecent().args[0].match(/\[White /g)!.length).toBe(500);
    expect(api.createDraft.calls.allArgs().map(a => [a[1], a[2]])).toEqual([
      ['datei', 'Verein.pgn (Teil 1 von 3)'], ['datei', 'Verein.pgn (Teil 2 von 3)'], ['datei', 'Verein.pgn (Teil 3 von 3)']]);
    expect(api.createDraft.calls.argsFor(2)[0].match(/\[White /g)!.length).toBe(1);
    expect(el.querySelector('.portion-note')?.textContent).toContain('in 3 Pakete');
    expect(api.drafts).toHaveBeenCalled();
  });

  // UX-037: die Wahl einer PGN-Datei füllte nur das Textfeld (am Handy unter dem Rand) — sichtbar passierte nichts, und
  // dieselbe Datei noch einmal zu wählen löste kein change aus. ChessBase und Lichess starteten die Übersicht selbst.
  it('eine PGN-Datei wählen führt gleich in die Übersicht und leert das Feld für eine erneute Wahl (UX-037)', async () => {
    const el = create();
    api.preview.and.resolveTo(PREVIEW);
    const input = { files: [new File(['[White "x"]\n1. e4 *'], 'Mannschaft.pgn')], value: 'C:\\fakepath\\Mannschaft.pgn' };
    await fixture.componentInstance.pickFile({ target: input } as unknown as Event);
    fixture.detectChanges();
    expect(input.value).toBe('');
    expect(api.createDraft).toHaveBeenCalledWith('[White "x"]\n1. e4 *', 'datei', 'Mannschaft.pgn');
    expect(api.preview).toHaveBeenCalledWith('[White "x"]\n1. e4 *', 7);
    expect(el.querySelector('.review-table')?.textContent).toContain('Hengl, Philip');
    expect(api.importPgn).not.toHaveBeenCalled();                            // gespeichert wird erst mit „Importieren"
  });

  // UX-037-Nacharbeit: seit die Wahl selbst die Vorschau startet, darf eine zweite Wahl (oder ein anderer Weg) während der
  // laufenden Vorschau keine zweite daneben starten — sonst lagen Pakete doppelt als offene Listen da.
  it('während die Datei gelesen wird, sind die Wege gesperrt und eine zweite Wahl startet keine zweite Vorschau (UX-037)', async () => {
    const el = create();
    const game = (i: number) => `[White "W${i}"]\n[Black "B${i}"]\n[Result "1-0"]\n\n1. e4 e5 1-0`;
    const file = new File([Array.from({ length: 1001 }, (_, i) => game(i + 1)).join('\n\n')], 'Verein.pgn');
    let answer!: (p: ClubPreview) => void;
    api.preview.and.returnValue(new Promise<ClubPreview>(r => answer = r));
    const first = fixture.componentInstance.pickFile({ target: { files: [file], value: 'C:\\fakepath\\Verein.pgn' } } as unknown as Event);
    for (let i = 0; i < 50 && !api.preview.calls.count(); i++) await new Promise(r => setTimeout(r));
    fixture.detectChanges();
    expect(api.preview).toHaveBeenCalledTimes(1);                           // die Vorschau läuft noch
    const pgnField = el.querySelector('input[type=file][accept^=".pgn"]') as HTMLInputElement;
    expect(pgnField.disabled).toBeTrue();
    expect(pgnField.closest('label')?.textContent).toContain('Lese die Datei …');   // am Feld, nicht nur am Knopf weiter unten
    expect((el.querySelector('input[type=file][multiple]') as HTMLInputElement).disabled).toBeTrue();
    expect((el.querySelector('.btn-pri') as HTMLButtonElement).disabled).toBeTrue();

    const again = { files: [file], value: 'C:\\fakepath\\Verein.pgn' };
    void fixture.componentInstance.pickFile({ target: again } as unknown as Event);    // kommt trotzdem eine Wahl durch …
    fixture.componentInstance.studyUrl.set('https://lichess.org/study/AbCdEf12');
    void fixture.componentInstance.loadStudy();                             // … oder Enter im Studien-Feld
    for (let i = 0; i < 5; i++) await new Promise(r => setTimeout(r));
    expect(again.value).toBe('');
    expect(api.lichess).not.toHaveBeenCalled();
    expect(api.createDraft).toHaveBeenCalledTimes(1);

    answer(PREVIEW);
    await first;
    for (let i = 0; i < 5; i++) await new Promise(r => setTimeout(r));
    fixture.detectChanges();
    expect(api.preview).toHaveBeenCalledTimes(1);
    expect(api.createDraft.calls.allArgs().map(a => [a[1], a[2]])).toEqual([
      ['datei', 'Verein.pgn (Teil 1 von 3)'], ['datei', 'Verein.pgn (Teil 2 von 3)'], ['datei', 'Verein.pgn (Teil 3 von 3)']]);
    expect(fixture.componentInstance.busy()).toBeFalse();
    expect(el.querySelector('.review-table')).not.toBeNull();
  });

  it('nach dem Hochladen eines Fotos ist das Dateifeld wieder leer (UX-037)', fakeAsync(() => {
    query = { art: 'formular' };
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const input = el.querySelector('input[type=file][accept^="image"]') as HTMLInputElement;
    const file = new File(['x'], 'bogen.jpg', { type: 'image/jpeg' });
    const dt = new DataTransfer();
    dt.items.add(file);
    input.files = dt.files;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(input.value).toContain('bogen.jpg');
    expect(fixture.componentInstance.photo()).toBe(file);
    api.upload.and.resolveTo({ ref: '7', scan: SCAN('pending') });
    void fixture.componentInstance.upload();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.upload).toHaveBeenCalled();
    expect(input.value).toBe('');
    expect(fixture.componentInstance.photo()).toBeNull();
    fixture.destroy();
    flush();
  }));

  // UX-071: axe meldete aria-allowed-attr (critical) — role="tab" mit aria-pressed, ohne tabpanel und Pfeiltasten.
  it('„PGN-Datei | Partieformular" ist ein Umschalter wie die übrigen: role=group, nur aria-pressed, keine tab-Rollen (UX-071)', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const group = el.querySelector('.club-kind') as HTMLElement;
    expect(group.getAttribute('role')).toBe('group');
    expect(group.getAttribute('aria-label')).toBe('Art');
    expect(el.querySelector('[role="tablist"], [role="tab"], [aria-selected]')).toBeNull();
    const [pgn, sheet] = Array.from(group.querySelectorAll('button')) as HTMLButtonElement[];
    expect([pgn.getAttribute('aria-pressed'), sheet.getAttribute('aria-pressed')]).toEqual(['true', 'false']);
    sheet.click();
    flushMicrotasks();
    fixture.detectChanges();
    expect([pgn.getAttribute('aria-pressed'), sheet.getAttribute('aria-pressed')]).toEqual(['false', 'true']);
    fixture.destroy();
    flush();
  }));

  it('am Deckel der offenen Listen sagt die Seite, wie viele Pakete fehlen', async () => {
    const el = create();
    const pgn = Array.from({ length: 1600 }, (_, i) => `[White "W${i}"]\n\n1. d4 *`).join('\n\n');
    api.preview.and.resolveTo(PREVIEW);
    let n = 0;
    api.createDraft.and.callFake(async () => {
      if (++n > 2) throw new HttpErrorResponse({ status: 400, error: { reason: 'tooManyDrafts' } });
      return { ref: String(n), id: n, source: 'text', label: null, gameCount: 500, importedCount: 0,
        createdAt: '2026-09-29T10:00:00', updatedAt: '2026-09-29T10:00:00', viaShareLink: false, mine: true };
    });
    fixture.componentInstance.pgn.set(pgn);
    fixture.detectChanges();
    await fixture.componentInstance.startPreview();
    for (let i = 0; i < 5; i++) await new Promise(r => setTimeout(r));
    fixture.detectChanges();
    expect(api.createDraft.calls.argsFor(0)[2]).toBe('Teil 1 von 4');
    expect(el.querySelector('.portion-note')?.textContent).toContain('2 weitere konnten nicht abgelegt werden');
  });

  it('ChessBase: ohne Kopfdatei wird gar nicht erst hochgeladen, Absagen des Servers stehen als Satz da', async () => {
    const el = create();
    await fixture.componentInstance.pickChessBase({ target: { files: [new File(['x'], 'a.2cbg')], value: '' } } as unknown as Event);
    fixture.detectChanges();
    expect(api.chessBase).not.toHaveBeenCalled();
    expect(el.querySelector('.update-msg')?.textContent).toContain('.cbh oder .2cbh');

    api.chessBase.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'missingFile', message: 'Es fehlt: a.2lid.' } }));
    await fixture.componentInstance.pickChessBase({ target: { files: [new File(['x'], 'a.2cbh')], value: '' } } as unknown as Event);
    fixture.detectChanges();
    expect(el.querySelector('.update-msg')?.textContent).toContain('Es fehlt: a.2lid.');
    expect(api.preview).not.toHaveBeenCalled();
  });

  it('eine Partie aus RookHub (?partie=33) geht gleich in die Übersicht', fakeAsync(() => {
    query = { partie: '33' };
    service.savedGame.and.resolveTo({ pgn: '[White "Oberschmid"]\n1. e4 *', white: 'Oberschmid', black: 'Hengl' });
    api.preview.and.resolveTo(PREVIEW);
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(service.savedGame).toHaveBeenCalledWith(33);
    expect(api.preview).toHaveBeenCalledWith('[White "Oberschmid"]\n1. e4 *', 7);
    expect(el.querySelector('.review-table')).not.toBeNull();
  }));

  it('eine Partie aus RookHub, die sich nicht laden lässt, sagt es', fakeAsync(() => {
    query = { partie: '33' };
    service.savedGame.and.rejectWith(new Error('404'));
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.preview).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Die Partie aus RookHub ließ sich nicht laden');
  }));

  it('ein Formular, das gerade gelesen wird, zeigt eine mitlaufende Uhr ab dem Hochladen — fertig steht sie still', fakeAsync(() => {
    query = { art: 'formular' };
    const up = new Date(Date.now() - 30_000).toISOString().replace('Z', '');   // wie aus der Datenbank: ohne Zone
    const scan = { id: 3, status: 'running', notationLanguage: 'de', createdAt: up, rounds: 0, moveCount: 0, uncertainCount: 0,
      unresolvedCount: 0, white: 'Oberschmid', black: 'Hengl' };
    api.scans.and.resolveTo([{ ref: '3', scan }] as never);
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.querySelector('.scan-clock')?.textContent).toContain('0:30');
    expect(el.textContent).toContain('ein paar Minuten');
    tick(2000);
    fixture.detectChanges();
    expect(el.querySelector('.scan-clock')?.textContent).toContain('0:32');
    api.scans.and.resolveTo([{ ref: '3', scan: { ...scan, status: 'done', moveCount: 40 } }] as never);
    tick(1000);                                                                   // nächster Abruf (alle 3 s)
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.querySelector('.scan-clock')).toBeNull();
  }));

  it('Verwalter sehen offene Formulare anderer (auch über Teilen-Links) und können sie prüfen oder verwerfen', fakeAsync(() => {
    query = { art: 'formular' };
    const done = (id: number, white: string) => ({ id, status: 'done', notationLanguage: 'de', createdAt: '2026-09-27T18:00:00', rounds: 1,
      moveCount: 40, uncertainCount: 0, unresolvedCount: 0, white, black: 'Hengl' });
    service.openScans.and.resolveTo([
      { scan: done(8, 'Fremd'), viaShareLink: true, mine: false },
      { scan: done(9, 'Meins'), viaShareLink: false, mine: true },
    ]);
    api.discard.and.resolveTo();
    const el = create(['league.contribute', 'league.manage']);
    flushMicrotasks();
    fixture.detectChanges();
    const text = el.textContent ?? '';
    expect(text).toContain('Offene Formulare anderer');
    expect(text).toContain('Fremd – Hengl');
    expect(text).toContain('über Teilen-Link');
    expect(text).not.toContain('Meins – Hengl');                                     // die eigenen stehen oben
    expect((el.querySelector('a[href="/verein/formular/8"]'))).not.toBeNull();
    (Array.from(el.querySelectorAll('button')).filter(b => b.textContent?.trim() === 'Verwerfen').pop() as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.discard).toHaveBeenCalledWith('8');
    expect(el.textContent).not.toContain('Fremd – Hengl');
  }));

  it('„Deine Formulare": ein fertig gelesenes lässt sich verwerfen — erst nach Rückfrage (0.659.2)', fakeAsync(() => {
    query = { art: 'formular' };
    const scan = { id: 4, status: 'done', notationLanguage: 'de', createdAt: '2026-10-05T08:00:00', rounds: 1,
      moveCount: 40, uncertainCount: 0, unresolvedCount: 0, white: 'Oberschmid', black: 'Hengl' };
    api.scans.and.resolveTo([{ ref: '4', scan }] as never);
    api.discard.and.resolveTo();
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const btn = () => el.querySelector('button[aria-label="Formular Oberschmid – Hengl verwerfen"]') as HTMLButtonElement | null;
    expect(el.querySelector('a[href="/verein/formular/4"]')).not.toBeNull();       // Prüfen und übernehmen bleibt
    confirmAsk.and.returnValue(of(false));
    btn()!.click();
    flushMicrotasks();
    expect(api.discard).not.toHaveBeenCalled();                                      // Nein → bleibt
    confirmAsk.and.returnValue(of(true));
    btn()!.click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(confirmAsk.calls.mostRecent().args[0]).toContain('Formular Oberschmid – Hengl verwerfen?');
    expect(api.discard).toHaveBeenCalledWith('4');
    expect(btn()).toBeNull();
  }));

  // Gemeldet 2026-09-28: nach einem Abruf, der nicht durchkam, fragte die Seite nie wieder nach — „wird gelesen" blieb stehen.
  it('ein Abruf, der nicht durchkommt, beendet das Nachfragen nicht', fakeAsync(() => {
    query = { art: 'formular' };
    const scan = { id: 3, status: 'running', notationLanguage: 'de', createdAt: new Date().toISOString(), rounds: 0,
      moveCount: 0, uncertainCount: 0, unresolvedCount: 0, white: 'Oberschmid', black: 'Hengl' };
    api.scans.and.resolveTo([{ ref: '3', scan }] as never);
    const el = create();
    flushMicrotasks();
    api.scans.and.rejectWith(new Error('offline'));
    tick(3000);
    flushMicrotasks();
    api.scans.and.resolveTo([{ ref: '3', scan: { ...scan, status: 'done', moveCount: 40 } }] as never);
    tick(3000);
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).toContain('gelesen: 20 Züge');
    expect(el.querySelector('.scan-clock')).toBeNull();
  }));

  it('ohne Verwalter-Recht fragt die Seite die fremden Formulare gar nicht ab', fakeAsync(() => {
    query = { art: 'formular' };
    create(['league.contribute']);
    flushMicrotasks();
    expect(service.openScans).not.toHaveBeenCalled();
  }));

  it('angemeldet ohne Recht: nicht freigeschaltet', () => {
    const el = create(false);
    expect(el.textContent).toContain('Nicht freigeschaltet');
    expect(el.textContent).toContain('Partien hinzufügen dürfen Admins und die Vereinsgruppen der teilnehmenden Vereine.');
    expect(el.textContent).toContain('Freischaltung anfragen');
  });

  // UX-033: wer nur lesen darf, gehört zur Lesegruppe — der Satz „die Vereinsgruppe darf das" führte in die Irre.
  it('nur Leserecht: nennt das fehlende Beitragsrecht und führt zu den Vereinspartien', () => {
    const el = create(['league.view']);
    expect(el.textContent).toContain('Du kannst die Vereinspartien lesen. Zum Hinzufügen fehlt dir noch die Freigabe');
    expect(el.textContent).not.toContain('Partien hinzufügen dürfen Admins');
    expect(el.textContent).toContain('Angemeldet als patrik.');
    const back = Array.from(el.querySelectorAll('lh-access-gate a')).find(a => a.textContent?.includes('Zu den Vereinspartien'));
    expect(back?.getAttribute('href')).toBe('/verein');
    expect(api.scans).not.toHaveBeenCalled();
  });
});
