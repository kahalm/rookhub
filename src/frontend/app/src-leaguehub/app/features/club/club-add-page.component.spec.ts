import { ComponentFixture, TestBed, fakeAsync, flush, flushMicrotasks, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { ClubPreview, ScoresheetScan } from '../../core/club.models';
import { ClubAddPageComponent, anonKeys } from './club-add-page.component';

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
  let service: { client: jasmine.Spy; savedGame: jasmine.Spy; openScans: jasmine.Spy; allDrafts: jasmine.Spy };
  let query: Record<string, string>;
  let params: Record<string, string>;

  beforeEach(() => {
    localStorage.removeItem('lh-anon-scans');
    query = {};
    params = {};
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['preview', 'importPgn', 'scans', 'scoresheetStatus', 'upload', 'discard', 'players', 'match', 'lichess',
      'createDraft', 'drafts', 'draft', 'saveDraft', 'deleteDraft']);
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
      providers: [
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
      [{ index: 1, white: { name: null, fide: '900', replace: true }, black: { name: null, fide: '222', replace: false } }], 7);
    expect(el.querySelector('.result')?.textContent).toContain('1 Partie übernommen (1 mit „Schwaz“).');
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
    expect(api.upload).toHaveBeenCalledWith(file, 'auto', 'white');
    tick(3000);
    flushMicrotasks();
    fixture.detectChanges();
    const link = el.querySelector('.scan-list a') as HTMLAnchorElement;
    expect(link.textContent).toContain('Prüfen und übernehmen');
    expect(link.getAttribute('href')).toBe('/verein/formular/7');
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
    expect(el.textContent).toContain('Sekunden pro Zug');
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
    spyOn(window, 'confirm').and.returnValue(true);
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
    expect(create(false).textContent).toContain('Nicht freigeschaltet');
  });
});
