import { categoryTabs, clipRange, filterNotes, groupByLesson, noteGameLink, notebookGameKey } from './training-notebook.util';
import { TrainingNotebookNote } from './training-videos.service';

export function nbNote(p: Partial<TrainingNotebookNote>): TrainingNotebookNote {
  return {
    id: 1, lessonId: 1, date: '2026-10-27', startSec: 600, category: 'plan', text: 'Text', quote: null, gameKey: 'g1',
    gameWhite: 'Ich', gameBlack: 'Gegner', gameKind: 'own', rookhubGameId: null, hasVideo: true, lessonDurationSec: 3600, ...p,
  };
}

describe('training-notebook.util', () => {
  it('Reiter in fester Reihenfolge mit Anzahl, nur belegte; Unbekanntes zählt als „allgemein“', () => {
    const tabs = categoryTabs([nbNote({ category: 'plan' }), nbNote({ category: 'schwaeche' }), nbNote({ category: 'plan' }),
      nbNote({ category: 'endspiel' }), nbNote({ category: 'quatsch' })]);
    expect(tabs).toEqual([
      { category: 'schwaeche', count: 1 }, { category: 'plan', count: 2 }, { category: 'endspiel', count: 1 },
      { category: 'allgemein', count: 1 },
    ]);
  });

  it('filtert nach Art, Partie (stundenübergreifend) und Suchtreffern', () => {
    const a = nbNote({ id: 1, rookhubGameId: 5 });
    const b = nbNote({ id: 2, lessonId: 2, rookhubGameId: 5 });
    const c = nbNote({ id: 3, gameKind: 'lesson', gameWhite: 'Martinovic', gameBlack: 'Kozul' });
    const d = nbNote({ id: 4, gameKey: null, gameKind: null });
    const all = [a, b, c, d];
    expect(filterNotes(all, { kind: 'lesson', game: null, ids: null }).map(n => n.id)).toEqual([3]);
    expect(filterNotes(all, { kind: 'all', game: notebookGameKey(a), ids: null }).map(n => n.id)).toEqual([1, 2]);
    expect(filterNotes(all, { kind: 'all', game: null, ids: new Set([2, 4]) }).map(n => n.id)).toEqual([2, 4]);
    expect(notebookGameKey(d)).toBeNull();
    expect(notebookGameKey(c)).toBe(notebookGameKey(nbNote({ lessonId: 9, gameKey: 'g2', gameKind: 'lesson', gameWhite: 'Martinovic', gameBlack: 'Kozul' })));
  });

  it('gruppiert eine Kategorie je Stunde, neueste zuerst', () => {
    const groups = groupByLesson([
      nbNote({ id: 1, lessonId: 1, date: '2025-05-20' }), nbNote({ id: 2, lessonId: 2, date: '2026-01-15' }),
      nbNote({ id: 3, lessonId: 1, date: '2025-05-20' }), nbNote({ id: 4, lessonId: 2, category: 'zeit' }),
    ], 'plan');
    expect(groups.map(g => [g.lessonId, g.notes.map(n => n.id)])).toEqual([[2, [2]], [1, [1, 3]]]);
  });

  it('Ausschnitt 5 s vor bis 60 s nach der Notiz, nie über das Stundenende; ohne Zeit keiner', () => {
    expect(clipRange({ startSec: 600, lessonDurationSec: 3600 })).toEqual({ start: 595, end: 660 });
    expect(clipRange({ startSec: 2, lessonDurationSec: null })).toEqual({ start: 0, end: 62 });
    expect(clipRange({ startSec: 3590, lessonDurationSec: 3600 })).toEqual({ start: 3585, end: 3600 });
    expect(clipRange({ startSec: null, lessonDurationSec: 3600 })).toBeNull();
  });

  it('Partie-Link: eigene RookHub-Partie, sonst die Stunde an der Notizzeit', () => {
    expect(noteGameLink({ rookhubGameId: 7, gameKind: 'own', lessonId: 3, startSec: 90 })).toEqual({ link: ['/games', 7], query: null });
    expect(noteGameLink({ rookhubGameId: null, gameKind: 'lesson', lessonId: 3, startSec: 90 })).toEqual({ link: ['/training-videos', 3], query: { t: 90 } });
  });
});
