import { GameEvalPly, GameEvals } from './game-review.util';
import { SavedGame } from './games.service';
import { NO_CLASSIFIER } from './classifier.util';
import { byPlayedAt, classifierFromParam, classifierToParam, gameLabel, gameMistakes } from './mistakes-collection.util';

describe('mistakes-collection.util', () => {
  // Dieselbe Kurzpartie wie in mistakes.util.spec: 2.Nf3 (Weiß) und 2…Nc6 (Schwarz) gelten nach den Rechenwerten als Fehler.
  const PGN = '[Event "?"]\n\n1. e4 e5 2. Nf3 Nc6 *';
  const rows: GameEvalPly[] = [
    { ply: 0, depth: 20, cp: 20, bestUci: 'e2e4', playedUci: 'e2e4' },
    { ply: 1, depth: 20, cp: 20, bestUci: 'e7e5', playedUci: 'e7e5' },
    { ply: 2, depth: 20, cp: 30, bestUci: 'b1c3', playedUci: 'g1f3' },
    { ply: 3, depth: 20, cp: -250, bestUci: 'g8f6', playedUci: 'b8c6' },
  ];
  const evals: GameEvals = { status: 'done', analyzed: 4, total: 4, targetDepth: 20, plies: rows, final: { cp: 50 } };

  function game(over: Partial<SavedGame> = {}): SavedGame {
    return { id: 7, source: 'pgn', white: 'Huber', black: 'Maier', shareToken: 't', moveCount: 4,
      createdAt: '2026-10-01T10:00:00Z', playedAt: '2026-09-28T00:00:00Z', ...over };
  }

  it('nimmt nur die Seite des Besitzers, markiert die Partie und zählt alle Aufgaben', () => {
    const r = gameMistakes(game(), PGN, 'black', evals, false);
    expect(r.list.map(m => m.ply)).toEqual([3]);
    expect(r.total).toBe(1);
    expect(r.list[0].gameId).toBe(7);
    expect(r.list[0].gameLabel).toBe('Huber – Maier · 28.09.2026');
  });

  it('lässt ausgeblendete immer weg, gefundene nur ohne „auch gefundene"', () => {
    const solved = game({ mistakes: { total: 1, solved: 1, open: 0, solvedPlies: [2], lastTrainedAt: '' } });
    expect(gameMistakes(solved, PGN, 'white', evals, false).list).toEqual([]);
    expect(gameMistakes(solved, PGN, 'white', evals, true).list.map(m => m.ply)).toEqual([2]);

    const hidden = game({ mistakes: { total: 1, solved: 0, open: 0, solvedPlies: [], dismissedPlies: [2], lastTrainedAt: '' } });
    const r = gameMistakes(hidden, PGN, 'white', evals, true);
    expect(r.list).toEqual([]);
    expect(r.total).toBe(1);
  });

  it('ohne Bewertungen oder PGN keine Aufgaben', () => {
    expect(gameMistakes(game(), PGN, 'white', null, false)).toEqual({ list: [], total: 0 });
    expect(gameMistakes(game(), '', 'white', evals, false)).toEqual({ list: [], total: 0 });
  });

  it('Klassifizierer „ohne" geht als „-" in die Adresse und zurück', () => {
    expect(classifierToParam('')).toBeNull();
    expect(classifierToParam('Landesliga')).toBe('Landesliga');
    expect(classifierToParam(NO_CLASSIFIER)).toBe('-');
    expect(classifierFromParam('-')).toBe(NO_CLASSIFIER);
    expect(classifierFromParam(null)).toBe('');
    expect(classifierFromParam('2026/27')).toBe('2026/27');
  });

  it('Reihenfolge wie gespielt, ohne Datum nach dem Speichern; Kopfzeile ohne Datum', () => {
    const a = game({ id: 1, playedAt: '2026-10-05T00:00:00Z' });
    const b = game({ id: 2, playedAt: null, createdAt: '2026-09-01T00:00:00Z' });
    const c = game({ id: 3, playedAt: '2026-09-20T00:00:00Z' });
    expect([a, b, c].sort(byPlayedAt).map(g => g.id)).toEqual([2, 3, 1]);
    expect(gameLabel({ white: null, black: 'X', playedAt: null })).toBe('? – X');
  });
});
