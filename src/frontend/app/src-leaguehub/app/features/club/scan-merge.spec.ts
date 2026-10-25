import { HttpErrorResponse } from '@angular/common/http';
import { ScanRef } from '../../core/club.models';
import { ScanMerge, ScanMergeDeps } from './scan-merge';

const REF = (ref: string, status: string, extra = {}): ScanRef => ({ ref, scan: { id: Number(ref), status, notationLanguage: 'de',
  createdAt: '2026-10-10T08:00:00', rounds: 1, moveCount: 40, uncertainCount: 0, unresolvedCount: 0, ...extra } } as ScanRef);

describe('ScanMerge', () => {
  let deps: ScanMergeDeps;
  beforeEach(() => {
    deps = { scanRef: () => '7', scans: jasmine.createSpy('scans').and.resolveTo([REF('7', 'done'), REF('8', 'done'), REF('9', 'running')]),
      merge: jasmine.createSpy('merge').and.resolveTo(), confirm: jasmine.createSpy('confirm').and.resolveTo(true),
      reload: jasmine.createSpy('reload') };
  });

  it('zur Wahl stehen nur andere, nicht gerade gelesene Einlesungen', async () => {
    const m = new ScanMerge(deps);
    await m.openMerge();
    expect(m.mergeCandidates()!.map(c => c.ref)).toEqual(['8']);
  });

  it('Beschriftung: Nummer, Namen oder Dateiname, Halbzüge', () => {
    const m = new ScanMerge(deps);
    expect(m.candidateLabel(REF('8', 'done', { white: 'A', black: null }))).toBe('#8 · A – ? · 40 Halbzüge');
    expect(m.candidateLabel(REF('8', 'done', { fileName: 'b.jpg', moveCount: 0 }))).toBe('#8 · b.jpg');
  });

  it('nach Rückfrage zusammengeführt und neu geladen; ohne Ja nichts', async () => {
    const m = new ScanMerge(deps);
    (deps.confirm as jasmine.Spy).and.resolveTo(false);
    await m.merge(REF('8', 'done'));
    expect(deps.merge).not.toHaveBeenCalled();
    (deps.confirm as jasmine.Spy).and.resolveTo(true);
    m.mergeCandidates.set([REF('8', 'done')]);
    await m.merge(REF('8', 'done'));
    expect(deps.merge).toHaveBeenCalledWith('7', '8');
    expect(m.mergeCandidates()).toBeNull();
    expect(deps.reload).toHaveBeenCalled();
    expect(m.merging()).toBeFalse();
  });

  it('Absagen des Servers als Satz', async () => {
    const m = new ScanMerge(deps);
    (deps.merge as jasmine.Spy).and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'tooManyViews' } }));
    await m.merge(REF('8', 'done'));
    expect(m.mergeError()).toBe('Zu viele Fotos für diese Seite (höchstens vier).');
    (deps.merge as jasmine.Spy).and.rejectWith(new HttpErrorResponse({ status: 409, error: { reason: 'busy' } }));
    await m.merge(REF('8', 'done'));
    expect(m.mergeError()).toBe('Eine der beiden wird gerade gelesen — bitte kurz warten.');
    (deps.scans as jasmine.Spy).and.rejectWith(new Error('offline'));
    await m.openMerge();
    expect(m.mergeError()).toBe('Die Einlesungen ließen sich gerade nicht laden.');
  });
});
