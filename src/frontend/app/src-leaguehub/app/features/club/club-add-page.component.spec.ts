import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
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
  let service: { client: jasmine.Spy };
  let query: Record<string, string>;
  let params: Record<string, string>;

  beforeEach(() => {
    localStorage.removeItem('lh-anon-scans');
    query = {};
    params = {};
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['preview', 'importPgn', 'scans', 'scoresheetStatus', 'upload', 'discard', 'players', 'match', 'lichess']);
    api.scans.and.resolveTo([]);
    api.scoresheetStatus.and.resolveTo({ available: true, dailyLimit: 10, usedToday: 0, languages: [{ code: 'de', name: 'Deutsch', pieces: 'KDTLS' }] });
    service = { client: jasmine.createSpy('client').and.returnValue(api) };
  });

  function create(perms = true): HTMLElement {
    TestBed.configureTestingModule({
      imports: [ClubAddPageComponent],
      providers: [
        provideRouter([]),
        { provide: ClubApiService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query), paramMap: convertToParamMap(params) } } },
        { provide: AuthService, useValue: { has: () => perms, currentUser: perms ? { username: 'patrik' } : null } },
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
    expect(api.preview).toHaveBeenCalledWith('[White "x"]\n1. e4 *');
    expect(api.importPgn).not.toHaveBeenCalled();
    expect(el.querySelector('.review-table')?.textContent).toContain('Hengl, Philip');

    api.importPgn.and.resolveTo({ added: 1, anonymized: 1, duplicates: 0, truncated: false, ids: [5],
      failed: [] });
    (el.querySelector('lh-club-import-review .btn-pri') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.importPgn).toHaveBeenCalledWith('[White "x"]\n1. e4 *',
      [{ index: 1, white: { name: null, fide: '900', replace: true }, black: { name: null, fide: '222', replace: false } }]);
    expect(el.querySelector('.result')?.textContent).toContain('1 Partie übernommen (1 mit „Schwaz“).');
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
    expect(api.preview).toHaveBeenCalledWith('[Event "Studie: Kapitel 1"]\n1. e4 *');
    expect(el.querySelector('.review-table')).not.toBeNull();

    fixture.componentInstance.review.set(null);
    api.lichess.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'lichessNotFound' } }));
    void fixture.componentInstance.loadStudy();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).toContain('nicht öffentlich');
  }));

  it('angemeldet ohne Recht: nicht freigeschaltet', () => {
    expect(create(false).textContent).toContain('Nicht freigeschaltet');
  });
});
