import { of } from 'rxjs';
import { BookHints, BookHintsHost } from './book-hints';

describe('BookHints (Zerlegung Z18)', () => {
  function host(over: Record<string, unknown> = {}): BookHintsHost {
    return {
      puzzle: { id: 3, fen: 'x', moves: '', bookFileName: 'b', hints: { de: ['d1'], en: ['e1', 'e2'] } } as any,
      isLoggedIn: true, inWeekly: false, onSolutionPath: true, hasHints: true, hintLevel: 1,
      translate: { instant: (k: string) => k, currentLang: () => 'de', getFallbackLang: () => 'en' } as any,
      puzzleService: { flagBookPuzzleHints: jasmine.createSpy('flag').and.returnValue(of(null)) } as any,
      snackbar: { success: jasmine.createSpy('success'), warn: () => {} } as any,
      ...over,
    } as BookHintsHost;
  }

  it('vorberechnete Tipps nur am Schlüsselzug, in der UI-Sprache', () => {
    const h = new BookHints(host());
    expect(h.hasPrecomputed).toBeTrue();
    expect(h.available(true, null)).toEqual(['d1']);
    expect(h.available(false, null)).not.toEqual(['d1']);   // Folgezug: on-the-fly gestuft
  });

  it('melden nur angemeldet, auf dem Lösungspfad, nach einem Tipp und nicht im Wochenpost', () => {
    expect(new BookHints(host()).canFlag).toBeTrue();
    expect(new BookHints(host({ hintLevel: 0 })).canFlag).toBeFalse();
    expect(new BookHints(host({ inWeekly: true })).canFlag).toBeFalse();
    expect(new BookHints(host({ onSolutionPath: false })).canFlag).toBeFalse();
  });

  it('Melden setzt die Markierung am Puzzle und meldet Erfolg', () => {
    const h = host();
    const b = new BookHints(h);
    b.toggleFlag();
    expect(h.puzzleService.flagBookPuzzleHints).toHaveBeenCalledWith(3, true);
    expect(h.puzzle!.hintsFlagged).toBeTrue();
    expect(b.flagSaving).toBeFalse();
  });
});
