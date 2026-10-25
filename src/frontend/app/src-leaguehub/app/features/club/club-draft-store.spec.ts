import { fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ClubClient } from '../../core/club-api.service';
import { ClubDraft, ClubPreview } from '../../core/club.models';
import { ClubDraftStore, ClubDraftStoreDeps, SAVE_DEBOUNCE_MS } from './club-draft-store';
import { draftKeys } from './club-anon-keys';

const M = { league: true, ambiguous: false, name: 'Hengl, Philip', fide: '222', club: false, candidates: [] };
const PREVIEW: ClubPreview = { truncated: false, games: [
  { index: 1, year: 2024, result: '1-0', event: null, plies: 40, opening: '1.e4 c5', error: null, duplicate: false,
    white: { raw: 'Oberschmid', elo: null, match: { ...M, name: 'Oberschmid, Patrik', fide: '900', club: true }, owner: true, replace: true },
    black: { raw: 'Hengl', elo: null, match: M, owner: false, replace: false } },
] };
const DRAFT = (over: Partial<ClubDraft> = {}): ClubDraft => ({ ref: '7', id: 7, source: 'text', label: null, gameCount: 1, importedCount: 0,
  createdAt: '2026-09-28T18:00:00', updatedAt: '2026-09-28T18:00:00', viaShareLink: false, mine: true, ...over });

describe('ClubDraftStore', () => {
  let api: jasmine.SpyObj<ClubClient>;
  let deps: ClubDraftStoreDeps;
  let confirm: jasmine.Spy;

  beforeEach(() => {
    localStorage.removeItem('lh-anon-drafts');
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['preview', 'createDraft', 'drafts', 'draft', 'saveDraft', 'deleteDraft']);
    api.createDraft.and.callFake(async () => DRAFT());
    api.drafts.and.resolveTo([]);
    api.saveDraft.and.resolveTo();
    api.deleteDraft.and.resolveTo();
    api.preview.and.resolveTo(PREVIEW);
    confirm = jasmine.createSpy('confirm').and.resolveTo(true);
    deps = { client: api, share: null, isManager: false, allDrafts: jasmine.createSpy('allDrafts').and.resolveTo([]),
      savedGame: jasmine.createSpy('savedGame'), confirm };
  });
  afterEach(() => localStorage.removeItem('lh-anon-drafts'));

  it('startPreview legt den Entwurf an und öffnet die Übersicht; derselbe Text legt keinen zweiten an', fakeAsync(() => {
    const s = new ClubDraftStore(deps);
    s.pgn.set('1. e4 *');
    void s.startPreview();
    flushMicrotasks();
    expect(api.createDraft).toHaveBeenCalledOnceWith('1. e4 *', 'text', null);
    expect(api.preview).toHaveBeenCalledWith('1. e4 *', 7);
    expect(s.review()).not.toBeNull();
    expect(s.previewing()).toBeFalse();
    void s.startPreview();
    flushMicrotasks();
    expect(api.createDraft).toHaveBeenCalledTimes(1);
  }));

  it('Herkunft gilt nur für den unveränderten Text', fakeAsync(() => {
    const s = new ClubDraftStore(deps);
    s.pgn.set('A');
    s.loaded('A', 'datei', 'x.pgn');
    void s.startPreview();
    flushMicrotasks();
    expect(api.createDraft).toHaveBeenCalledWith('A', 'datei', 'x.pgn');
    s.pgn.set('B');
    void s.startPreview();
    flushMicrotasks();
    expect(api.deleteDraft).toHaveBeenCalledWith('7');                    // der alte Entwurf geht
    expect(api.createDraft).toHaveBeenCalledWith('B', 'text', null);
  }));

  it('ohne Konto: Schlüssel des Entwurfs gemerkt, Vorschau ohne Entwurfs-Id', fakeAsync(() => {
    api.createDraft.and.callFake(async () => ({ ...DRAFT(), key: 'geheim' }));
    const s = new ClubDraftStore({ ...deps, share: 'TOK' });
    s.pgn.set('1. e4 *');
    void s.startPreview();
    flushMicrotasks();
    expect(draftKeys('TOK')).toEqual(['geheim']);
    expect(api.preview).toHaveBeenCalledWith('1. e4 *', null);
  }));

  it('Fehler der Vorschau als Satz: abgelaufener Link, fehlendes Recht, Absage des Servers', fakeAsync(() => {
    const s = new ClubDraftStore({ ...deps, share: 'TOK' });
    s.pgn.set('x');
    api.preview.and.rejectWith(new HttpErrorResponse({ status: 404 }));
    void s.startPreview();
    flushMicrotasks();
    expect(s.importError()).toBe('Dieser Link ist abgelaufen.');
    const t = new ClubDraftStore(deps);
    t.pgn.set('x');
    api.preview.and.rejectWith(new HttpErrorResponse({ status: 403 }));
    void t.startPreview();
    flushMicrotasks();
    expect(t.importError()).toBe('Dafür fehlt dir die Berechtigung (Vereinsmitglieder).');
  }));

  it('scheduleSave speichert gedrosselt den Stand der Übersicht; dispose setzt eine wartende Speicherung sofort ab', fakeAsync(() => {
    const s = new ClubDraftStore(deps);
    s.pgn.set('1. e4 *');
    void s.startPreview();
    flushMicrotasks();
    s.scheduleSave();
    tick(SAVE_DEBOUNCE_MS - 1);
    expect(api.saveDraft).not.toHaveBeenCalled();
    tick(1);
    expect(api.saveDraft).toHaveBeenCalledOnceWith('7', { state: jasmine.any(String) });
    s.scheduleSave();
    s.dispose();
    expect(api.saveDraft).toHaveBeenCalledTimes(2);
    tick(SAVE_DEBOUNCE_MS);
    expect(api.saveDraft).toHaveBeenCalledTimes(2);
  }));

  it('done: Ergebnis steht, Text und Entwurf gehen, Liste neu geladen', fakeAsync(() => {
    const s = new ClubDraftStore(deps);
    s.pgn.set('1. e4 *');
    void s.startPreview();
    flushMicrotasks();
    s.done({ added: 1, anonymized: 0, duplicates: 0, truncated: false, ids: [5], failed: [] });
    flushMicrotasks();
    expect(s.result()?.added).toBe(1);
    expect(s.review()).toBeNull();
    expect(s.pgn()).toBe('');
    expect(s.draft()).toBeNull();
    expect(api.deleteDraft).toHaveBeenCalledWith('7');
    expect(api.drafts).toHaveBeenCalled();
  }));

  it('Verwalter: fremde Listen geladen, Fortsetzen nennt den Einreicher, Verwerfen fragt mit Namen', fakeAsync(() => {
    const foreign = DRAFT({ ref: '12', id: 12, mine: false, owner: 'hans' });
    (deps.allDrafts as jasmine.Spy).and.resolveTo([foreign, DRAFT({ ref: '13', mine: true })]);
    api.draft.and.resolveTo({ ...foreign, pgn: '1. d4 *', imported: [1], state: JSON.stringify({ v: 1, replaceClub: true, games: [] }) });
    const s = new ClubDraftStore({ ...deps, isManager: true });
    void s.loadDrafts();
    flushMicrotasks();
    expect(s.othersDrafts().map(d => d.ref)).toEqual(['12']);
    void s.resume(foreign);
    flushMicrotasks();
    expect(s.resumedFrom()).toBe('hans');
    expect(s.pgn()).toBe('1. d4 *');
    expect(api.preview).toHaveBeenCalledWith('1. d4 *', 12);
    s.onSaved([2]);
    expect(api.saveDraft).toHaveBeenCalledWith('12', { imported: [1, 2] });
    confirm.and.resolveTo(false);
    void s.discardDraft(foreign);
    flushMicrotasks();
    expect(confirm.calls.mostRecent().args[0]).toContain('von hans');
    expect(api.deleteDraft).not.toHaveBeenCalled();
  }));

  it('loadSavedGame: Partie aus RookHub gleich in die Übersicht, Fehler als Satz', fakeAsync(() => {
    (deps.savedGame as jasmine.Spy).and.resolveTo({ pgn: '1. e4 *' });
    const s = new ClubDraftStore(deps);
    void s.loadSavedGame(33);
    flushMicrotasks();
    expect(api.createDraft).toHaveBeenCalledWith('1. e4 *', 'rookhub', 'Partie 33 aus RookHub');
    (deps.savedGame as jasmine.Spy).and.rejectWith(new Error('404'));
    const t = new ClubDraftStore(deps);
    void t.loadSavedGame(34);
    flushMicrotasks();
    expect(t.importError()).toContain('Die Partie aus RookHub ließ sich nicht laden');
  }));

  it('draftTitle/draftState', () => {
    const s = new ClubDraftStore(deps);
    expect(s.draftTitle(DRAFT({ source: 'chessbase', gameCount: 3 }))).toBe('ChessBase (3 Partien)');
    expect(s.draftTitle(DRAFT({ label: 'liga.pgn' }))).toBe('liga.pgn (1 Partie)');
    expect(s.draftState(DRAFT())).toBe('noch nichts importiert');
    expect(s.draftState(DRAFT({ importedCount: 1, gameCount: 2 }))).toBe('1 von 2 importiert');
  });

  it('busy, solange ein Weg läuft', () => {
    const s = new ClubDraftStore(deps);
    expect(s.busy()).toBeFalse();
    s.readingDb.set(true);
    expect(s.busy()).toBeTrue();
  });
});
