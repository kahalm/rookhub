import { of, throwError } from 'rxjs';
import { RepertoireListComponent } from './repertoire-list.component';
import { saveRepertoireOffline, hasRepertoireOffline } from './repertoire-offline.util';
import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';

/**
 * Reiner Test des Such-Filters (filteredRepertoires) — Name + Beschreibung, case-insensitive.
 * Ohne TestBed: der Getter hängt nur an `repertoires` + `search`.
 */
describe('RepertoireListComponent search filter', () => {
  function make(): RepertoireListComponent {
    const comp = new RepertoireListComponent({} as any, {} as any, {} as any, {} as any, {} as any);
    comp.repertoires = [
      { id: 1, name: 'Sicilian Najdorf', description: 'Sharp lines', kind: 0, fileCount: 2, isPublic: false },
      { id: 2, name: 'London System', description: 'Solid setup', kind: 1, fileCount: 1, isPublic: false },
      { id: 3, name: 'Caro-Kann', description: 'vs e4 (najdorf-free)', kind: 0, fileCount: 1, isPublic: false },
    ] as any;
    return comp;
  }

  it('returns all repertoires when the search is empty', () => {
    const comp = make();
    expect(comp.filteredRepertoires.length).toBe(3);
  });

  it('matches on the name, case-insensitive', () => {
    const comp = make();
    comp.search = 'LONDON';
    expect(comp.filteredRepertoires.map(r => r.id)).toEqual([2]);
  });

  it('also matches on the description', () => {
    const comp = make();
    comp.search = 'najdorf';
    expect(comp.filteredRepertoires.map(r => r.id).sort()).toEqual([1, 3]);
  });

  it('returns nothing for a non-matching query', () => {
    const comp = make();
    comp.search = 'zzz';
    expect(comp.filteredRepertoires.length).toBe(0);
  });
});

/**
 * Offline-Verhalten der Liste: Download-Toggle (PGN + SR-Zustände + Intervalle cachen) und
 * Fallback auf heruntergeladene Repertoires, wenn der Server nicht erreichbar ist.
 */
describe('RepertoireListComponent offline', () => {
  const rep = (id: number, name: string): any =>
    ({ id, name, description: null, kind: 0, fileCount: 1, isPublic: false, useForExtension: false, createdAt: '', updatedAt: '', chessableCourseId: null });

  function clearOffline(): void {
    for (let i = localStorage.length - 1; i >= 0; i--) {
      const k = localStorage.key(i);
      if (k && k.startsWith(REPERTOIRE_OFFLINE_PREFIX)) localStorage.removeItem(k);
    }
  }
  beforeEach(clearOffline);
  afterEach(clearOffline);

  it('toggleOffline downloads pgn + states + config into the offline cache', () => {
    const repertoireService: any = { getPgnText: () => of('1. e4 *') };
    const training: any = {
      getLineStates: () => of([{ lineKey: 'k', level: 2, reps: 1, lapses: 0, dueAt: '2026-01-01T00:00:00Z', lastReviewedAt: null, inPool: true, paused: false }]),
      getConfig: () => of({ effective: [{ value: 4, unit: 'h' }], user: [], repertoire: null, source: 'default' }),
    };
    const comp = new RepertoireListComponent(repertoireService, training, {} as any, { info: () => {} } as any, { instant: (k: string) => k } as any);
    comp.toggleOffline(rep(7, 'Sizilianisch'));
    expect(hasRepertoireOffline(7)).toBeTrue();
    expect(comp.isOffline(rep(7, 'Sizilianisch'))).toBeTrue();
    // zweiter Toggle entfernt die Kopie wieder
    comp.toggleOffline(rep(7, 'Sizilianisch'));
    expect(hasRepertoireOffline(7)).toBeFalse();
  });

  it('falls back to downloaded repertoires when the list request fails (offline)', () => {
    saveRepertoireOffline({ meta: rep(3, 'Caro-Kann'), pgn: '1. e4 c6 *', states: [], config: null, savedAt: '2026-07-18T00:00:00Z' });
    const repertoireService: any = { list: () => throwError(() => new Error('offline')) };
    const comp = new RepertoireListComponent(repertoireService, {} as any, {} as any, { info: () => {} } as any, { instant: (k: string) => k } as any);
    comp.loadRepertoires();
    expect(comp.offlineList).toBeTrue();
    expect(comp.repertoires.map(r => r.id)).toEqual([3]);
  });

  it('keeps the plain error hint when nothing is downloaded', () => {
    const info = jasmine.createSpy('info');
    const repertoireService: any = { list: () => throwError(() => new Error('offline')) };
    const comp = new RepertoireListComponent(repertoireService, {} as any, {} as any, { info } as any, { instant: (k: string) => k } as any);
    comp.loadRepertoires();
    expect(comp.offlineList).toBeFalse();
    expect(comp.repertoires.length).toBe(0);
    expect(info).toHaveBeenCalled();
  });

  it('a successful load leaves offline mode again', () => {
    saveRepertoireOffline({ meta: rep(3, 'Caro-Kann'), pgn: '*', states: [], config: null, savedAt: '' });
    const svc: any = { list: jasmine.createSpy().and.returnValues(throwError(() => new Error('x')), of([rep(1, 'Live')])) };
    const comp = new RepertoireListComponent(svc, {} as any, {} as any, { info: () => {} } as any, { instant: (k: string) => k } as any);
    comp.loadRepertoires();
    expect(comp.offlineList).toBeTrue();
    comp.loadRepertoires();
    expect(comp.offlineList).toBeFalse();
    expect(comp.repertoires.map(r => r.id)).toEqual([1]);
  });
});
