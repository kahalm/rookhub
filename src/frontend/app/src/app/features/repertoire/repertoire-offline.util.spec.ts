import {
  saveRepertoireOffline, getRepertoireOffline, hasRepertoireOffline, removeRepertoireOffline,
  cachedRepertoires, refreshRepertoireOffline, updateRepertoireOfflineSettings, updateRepertoireOfflineStates, OfflineRepertoire,
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

  it('refreshRepertoireOffline mit Einstellungen: Intervalle und Reihenfolge kommen im selben Schreibvorgang mit', () => {
    saveRepertoireOffline(entry(5, 'Najdorf'));
    refreshRepertoireOffline(5, '1. d4 *', [st('b')], { config: [{ value: 2, unit: 'd' }, { value: 1, unit: 'w' }], lineOrder: 'mostTrained' });
    const loaded = getRepertoireOffline(5)!;
    expect(loaded.pgn).toBe('1. d4 *');
    expect(loaded.config).toEqual([{ value: 2, unit: 'd' }, { value: 1, unit: 'w' }]);
    expect(loaded.lineOrder).toBe('mostTrained');
    // nur ein Feld übergeben → das andere bleibt; „nichts gewählt“ (null) ist ein Wert und überschreibt
    refreshRepertoireOffline(5, '1. c4 *', [], { lineOrder: null });
    expect(getRepertoireOffline(5)!.config?.length).toBe(2);
    expect(getRepertoireOffline(5)!.lineOrder).toBeNull();
  });

  it('eine Kopie von vor 0.996.0 hat keine Reihenfolge — lesbar, Feld fehlt', () => {
    saveRepertoireOffline(entry(5, 'Najdorf'));
    expect(getRepertoireOffline(5)!.lineOrder).toBeUndefined();
  });

  it('updateRepertoireOfflineSettings zieht nur die übergebenen Felder nach; ohne Kopie oder ohne Felder nichts', () => {
    updateRepertoireOfflineSettings(5, { lineOrder: 'random' });   // keine Kopie → nichts anlegen
    expect(getRepertoireOffline(5)).toBeNull();
    saveRepertoireOffline(entry(5, 'Najdorf'));
    const before = localStorage.getItem(REPERTOIRE_OFFLINE_PREFIX + '5');
    updateRepertoireOfflineSettings(5, {});
    expect(localStorage.getItem(REPERTOIRE_OFFLINE_PREFIX + '5')).toBe(before);
    updateRepertoireOfflineSettings(5, { lineOrder: 'random' });
    const loaded = getRepertoireOffline(5)!;
    expect(loaded.lineOrder).toBe('random');
    expect(loaded.config?.length).toBe(1);          // Intervalle unberührt
    expect(loaded.pgn).toBe('1. e4 *');
    expect(loaded.states.length).toBe(1);
    expect(loaded.savedAt).toBe('2026-07-18T00:00:00Z');
    updateRepertoireOfflineSettings(5, { config: [{ value: 9, unit: 'h' }] });
    expect(getRepertoireOffline(5)!.config).toEqual([{ value: 9, unit: 'h' }]);
    expect(getRepertoireOffline(5)!.lineOrder).toBe('random');
  });

  it('updateRepertoireOfflineStates only swaps the states', () => {
    saveRepertoireOffline(entry(5, 'Najdorf'));
    updateRepertoireOfflineStates(5, [st('x'), st('y')]);
    const loaded = getRepertoireOffline(5)!;
    expect(loaded.states.map(s => s.lineKey)).toEqual(['x', 'y']);
    expect(loaded.pgn).toBe('1. e4 *');
  });
});
