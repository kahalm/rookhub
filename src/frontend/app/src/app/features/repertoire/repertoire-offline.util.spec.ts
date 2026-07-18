import {
  saveRepertoireOffline, getRepertoireOffline, hasRepertoireOffline, removeRepertoireOffline,
  cachedRepertoires, refreshRepertoireOffline, updateRepertoireOfflineStates, OfflineRepertoire,
} from './repertoire-offline.util';
import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';
import { LineStateDto } from './repertoire-training.service';

describe('repertoire-offline.util', () => {
  const st = (lineKey: string, level = 1): LineStateDto =>
    ({ lineKey, level, reps: 1, lapses: 0, dueAt: '2026-07-18T00:00:00Z', lastReviewedAt: null, inPool: true, paused: false });

  const entry = (id: number, name: string): OfflineRepertoire => ({
    meta: { id, name, description: null, kind: 0, fileCount: 1, isPublic: false, useForExtension: false, createdAt: '', updatedAt: '', chessableCourseId: null } as any,
    pgn: '1. e4 *',
    states: [st('a')],
    config: [{ value: 4, unit: 'h' }],
    savedAt: '2026-07-18T00:00:00Z',
  });

  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('save / has / get / remove roundtrip', () => {
    expect(hasRepertoireOffline(5)).toBeFalse();
    expect(saveRepertoireOffline(entry(5, 'Najdorf'))).toBeTrue();
    expect(hasRepertoireOffline(5)).toBeTrue();
    const loaded = getRepertoireOffline(5)!;
    expect(loaded.meta.name).toBe('Najdorf');
    expect(loaded.pgn).toBe('1. e4 *');
    expect(loaded.states.length).toBe(1);
    removeRepertoireOffline(5);
    expect(getRepertoireOffline(5)).toBeNull();
  });

  it('cachedRepertoires lists all metas sorted by name, skipping corrupt entries', () => {
    saveRepertoireOffline(entry(1, 'Zulu'));
    saveRepertoireOffline(entry(2, 'Alpha'));
    localStorage.setItem(REPERTOIRE_OFFLINE_PREFIX + '99', '{kaputt');
    expect(cachedRepertoires().map(m => m.name)).toEqual(['Alpha', 'Zulu']);
  });

  it('refreshRepertoireOffline replaces pgn + states but keeps meta/config; no-op without copy', () => {
    refreshRepertoireOffline(5, '1. d4 *', []);           // keine Kopie → nichts anlegen
    expect(getRepertoireOffline(5)).toBeNull();
    saveRepertoireOffline(entry(5, 'Najdorf'));
    refreshRepertoireOffline(5, '1. d4 *', [st('b', 3)]);
    const loaded = getRepertoireOffline(5)!;
    expect(loaded.pgn).toBe('1. d4 *');
    expect(loaded.states[0].lineKey).toBe('b');
    expect(loaded.meta.name).toBe('Najdorf');
    expect(loaded.config?.length).toBe(1);
  });

  it('updateRepertoireOfflineStates only swaps the states', () => {
    saveRepertoireOffline(entry(5, 'Najdorf'));
    updateRepertoireOfflineStates(5, [st('x'), st('y')]);
    const loaded = getRepertoireOffline(5)!;
    expect(loaded.states.map(s => s.lineKey)).toEqual(['x', 'y']);
    expect(loaded.pgn).toBe('1. e4 *');
  });
});
