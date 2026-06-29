import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';

/** Direkter Konstruktor-Test (ohne TestBed) der „Track solves"-Link-Logik. */
function make(data: any): SharePuzzleDialogComponent {
  const snackbar: any = { copy: () => {} };
  const translate: any = { instant: (k: string) => k };
  return new SharePuzzleDialogComponent(data, snackbar, translate);
}

describe('SharePuzzleDialogComponent track solves', () => {
  it('canTrack is true only for a shared single book puzzle', () => {
    expect(make({ url: 'http://x/puzzles/book/5?single=1', source: 'book' }).canTrack).toBeTrue();
    // Standard-Puzzle (andere Quelle) → kein Track
    expect(make({ url: 'http://x/puzzles/5', source: 'standard' }).canTrack).toBeFalse();
    // Buch-Link ohne single=1 → kein Track
    expect(make({ url: 'http://x/puzzles/book/5', source: 'book' }).canTrack).toBeFalse();
  });

  it('activeUrl appends &track=1 only when trackSolves is enabled', () => {
    const c = make({ url: 'http://x/puzzles/book/5?single=1', source: 'book' });
    expect(c.activeUrl).toBe('http://x/puzzles/book/5?single=1');   // Default aus
    c.setTrack(true);
    expect(c.activeUrl).toBe('http://x/puzzles/book/5?single=1&track=1');
    c.setTrack(false);
    expect(c.activeUrl).toBe('http://x/puzzles/book/5?single=1');
  });

  it('does not append track for a non-trackable link even if toggled', () => {
    const c = make({ url: 'http://x/puzzles/5', source: 'standard' });
    c.setTrack(true);
    expect(c.activeUrl).toBe('http://x/puzzles/5');   // canTrack=false → kein track-Param
  });
});
