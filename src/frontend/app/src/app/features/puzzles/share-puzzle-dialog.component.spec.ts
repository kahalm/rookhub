import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';

/** Direkter Konstruktor-Test (ohne TestBed) der Link-Auswahl. */
function make(data: any): SharePuzzleDialogComponent {
  const snackbar: any = { copy: () => {} };
  const translate: any = { instant: (k: string) => k };
  return new SharePuzzleDialogComponent(data, snackbar, translate);
}

describe('SharePuzzleDialogComponent', () => {
  it('activeUrl returns the (single) share link as-is — Tracking ist immer an, kein Param', () => {
    const c = make({ url: 'http://x/puzzles/book/5?single=1', source: 'book' });
    expect(c.activeUrl).toBe('http://x/puzzles/book/5?single=1');
  });

  it('toggle switches between current and previous puzzle link', () => {
    const c = make({
      url: 'http://x/puzzles/book/5?single=1',
      previousUrl: 'http://x/puzzles/book/4?single=1',
      source: 'book',
    });
    expect(c.activeUrl).toBe('http://x/puzzles/book/5?single=1');
    c.toggle();
    expect(c.activeUrl).toBe('http://x/puzzles/book/4?single=1');
  });
});
