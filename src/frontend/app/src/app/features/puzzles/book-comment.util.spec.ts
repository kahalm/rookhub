import {
  commentForPlyPlayed, latestCommentUpTo, displayComment, buildCommentLines, hasTrailingSolutionComment,
  CommentLinesState,
} from './book-comment.util';

describe('book-comment.util', () => {
  const MC = { '-1': 'Intro', '0': 'Zug 0', '2': 'Zug 2' };

  describe('commentForPlyPlayed', () => {
    it('liefert den Kommentar zum Ply (−1 = Einleitung)', () => {
      expect(commentForPlyPlayed(MC, -1)).toBe('Intro');
      expect(commentForPlyPlayed(MC, 0)).toBe('Zug 0');
      expect(commentForPlyPlayed(MC, 2)).toBe('Zug 2');
    });
    it('liefert null für einen Ply ohne Kommentar bzw. ohne Map', () => {
      expect(commentForPlyPlayed(MC, 1)).toBeNull();
      expect(commentForPlyPlayed(null, 0)).toBeNull();
      expect(commentForPlyPlayed(undefined, 0)).toBeNull();
    });
  });

  describe('latestCommentUpTo', () => {
    it('geht rückwärts bis zum zuletzt kommentierten Zug', () => {
      expect(latestCommentUpTo(MC, 0, 3)).toBe('Zug 2');   // 3,ohne → 2 hat einen
      expect(latestCommentUpTo(MC, 0, 1)).toBe('Zug 0');   // 1 ohne → 0 hat einen
    });
    it('liefert null, wenn im Bereich keiner kommentiert ist', () => {
      expect(latestCommentUpTo(MC, 3, 5)).toBeNull();
      expect(latestCommentUpTo(null, 0, 5)).toBeNull();
    });
  });

  describe('displayComment', () => {
    it('außerhalb des Reviews die Einleitung', () => {
      expect(displayComment(false, 5, 'ignoriert', 'Einleitung')).toBe('Einleitung');
      expect(displayComment(false, 0, null, undefined)).toBeNull();
    });
    it('im Review bevorzugt den Zug-Kommentar; Einleitung nur bei reviewIndex 0', () => {
      expect(displayComment(true, 0, 'Zug', 'Einleitung')).toBe('Zug');
      expect(displayComment(true, 0, null, 'Einleitung')).toBe('Einleitung');   // Fallback vor 1. Zug
      expect(displayComment(true, 1, null, 'Einleitung')).toBeNull();           // ab Zug 1 kein Intro-Fallback
      expect(displayComment(true, 3, 'Zug', 'Einleitung')).toBe('Zug');
    });
  });

  describe('buildCommentLines', () => {
    const base: CommentLinesState = {
      reviewMode: false, reviewIndex: 0, moveComment: null, puzzleComment: 'Einleitung',
      moveComments: { '0': 'Guter erster Zug', '2': 'Springer raus' },
      onSolutionPath: true, moveIndex: 0, solving: true, startPly: 0,
    };

    it('stapelt gespielte Zug-Kommentare (kein Intro-Rückfall)', () => {
      expect(buildCommentLines({ ...base, moveIndex: 0 })).toEqual(['Einleitung']);
      expect(buildCommentLines({ ...base, moveIndex: 1 })).toEqual(['Guter erster Zug']);
      expect(buildCommentLines({ ...base, moveIndex: 2 })).toEqual(['Guter erster Zug']);
      expect(buildCommentLines({ ...base, moveIndex: 3 })).toEqual(['Guter erster Zug', 'Springer raus']);
      expect(buildCommentLines({ ...base, moveIndex: 4 })).toEqual(['Guter erster Zug', 'Springer raus']);
    });

    it('off-path (Fehlzug) → leer, kein Intro-Rückfall', () => {
      expect(buildCommentLines({ ...base, moveIndex: 3, onSolutionPath: false })).toEqual([]);
    });

    it('Mid-Line (startPly ≥ 1) zeigt Kommentare nicht zu früh', () => {
      const mid: CommentLinesState = { ...base, startPly: 2, moveComments: { '3': 'Springer-Kommentar' } };
      expect(buildCommentLines({ ...mid, moveIndex: 3 })).toEqual([]);
      expect(buildCommentLines({ ...mid, moveIndex: 4 })).toEqual(['Springer-Kommentar']);
    });

    it('Review: EIN Absatz, Intro nur bei reviewIndex 0', () => {
      expect(buildCommentLines({ ...base, reviewMode: true, reviewIndex: 0, moveComment: null }))
        .toEqual(['Einleitung']);
      expect(buildCommentLines({ ...base, reviewMode: true, reviewIndex: 1, moveComment: null }))
        .toEqual([]);
      expect(buildCommentLines({ ...base, reviewMode: true, reviewIndex: 2, moveComment: 'Zug' }))
        .toEqual(['Zug']);
    });

    it('vor dem 1. Zug ohne Kommentare: Einleitung; solving=false ebenso', () => {
      expect(buildCommentLines({ ...base, moveComments: undefined, moveIndex: 0 })).toEqual(['Einleitung']);
      // gespielt, aber keine moveComments → leer (kein Intro während des Lösens)
      expect(buildCommentLines({ ...base, moveComments: undefined, moveIndex: 2 })).toEqual([]);
    });
  });

  describe('hasTrailingSolutionComment', () => {
    it('true bei lehrreichem Kommentar nach dem letzten Zug', () => {
      expect(hasTrailingSolutionComment('e2e4', { '0': 'Abschlusstext' })).toBeTrue();
    });
    it('false ohne Kommentar nach dem letzten Zug (nur Einleitung zählt nicht)', () => {
      expect(hasTrailingSolutionComment('e2e4', { '-1': 'nur intro' })).toBeFalse();
      expect(hasTrailingSolutionComment('', { '0': 'x' })).toBeFalse();
      expect(hasTrailingSolutionComment('e2e4', null)).toBeFalse();
    });
    it('false bei reiner Ergebnis-/Zitat-Floskel', () => {
      expect(hasTrailingSolutionComment('e2e4', { '0': 'White wins.' })).toBeFalse();
    });
  });
});
