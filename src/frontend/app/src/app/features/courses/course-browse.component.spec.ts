import { of } from 'rxjs';
import { CourseBrowseComponent } from './course-browse.component';
import { BookPuzzleDto } from '../puzzles/puzzle.service';

/**
 * Durchsehen-Ansicht: Gruppierung nach Kapitel, Kapitel-Filter, Zug-Durchsicht (goTo) inkl.
 * Kommentar-Rückwärtssuche und Orientierung aus der Startstellung, plus Linien-Navigation.
 */
describe('CourseBrowseComponent', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  function line(over: Partial<BookPuzzleDto>): BookPuzzleDto {
    return {
      id: 0, lineId: 'l', bookFileName: 'b.pgn', round: '1', fen: START, moves: 'e2e4 e7e5',
      ...over,
    } as BookPuzzleDto;
  }

  function build(puzzles: BookPuzzleDto[], chapterIndex: number | null = null, chapters: any[] = []): CourseBrowseComponent {
    const route: any = { snapshot: { paramMap: { get: (k: string) => k === 'bookId' ? '5' : (k === 'chapterIndex' ? (chapterIndex == null ? null : String(chapterIndex)) : null) } } };
    const courseService: any = { getBookPuzzles: () => of(puzzles), getChapters: () => of(chapters) };
    const prefs: any = { boardTheme: 'brown', pieceSet: 'cburnett' };
    const comp = new CourseBrowseComponent(route, {} as any, courseService, prefs, { info: () => {} } as any, { instant: (k: string) => k } as any);
    comp.ngOnInit();
    return comp;
  }

  it('loads all lines and groups them by chapter, selecting the first', () => {
    const comp = build([
      line({ id: 1, chapter: 'Intro' }),
      line({ id: 2, chapter: 'Intro' }),
      line({ id: 3, chapter: 'Tactics' }),
    ]);
    expect(comp.lines.length).toBe(3);
    expect(comp.groups.map(g => g.name)).toEqual(['Intro', 'Tactics']);
    expect(comp.groups[0].lines.length).toBe(2);
    expect(comp.selected?.id).toBe(1);
    expect(comp.totalPlies).toBe(2);
    expect(comp.sanMoves).toEqual(['e4', 'e5']);
  });

  it('filters to a single chapter when a chapter index is given', () => {
    const comp = build(
      [line({ id: 1, chapter: 'Intro' }), line({ id: 2, chapter: 'Tactics' }), line({ id: 3, chapter: 'Tactics' })],
      1,
      [{ index: 0, name: 'Intro' }, { index: 1, name: 'Tactics' }],
    );
    expect(comp.chapterName).toBe('Tactics');
    expect(comp.lines.map(l => l.id)).toEqual([2, 3]);
  });

  it('steps through the line and resolves comments backwards; orientation from the start FEN', () => {
    const comp = build([line({ id: 1, moveComments: { '-1': 'intro', '0': 'good' } })]);
    expect(comp.orientation).toBe('white'); // white to move in the start position

    comp.goTo(0);
    expect(comp.comment).toBe('intro');   // ply -1 (intro)
    comp.goTo(1);
    expect(comp.comment).toBe('good');     // ply 0
    comp.goTo(2);
    expect(comp.comment).toBe('good');     // ply 1 has none → keeps last seen
    expect(comp.lastMove).toEqual(['e7', 'e5']);
  });

  it('turns playable moves in a comment into clickable variation chips and previews them', () => {
    const comp = build([line({ id: 1, moveComments: { '-1': 'Instead 1.d4 is also good.' } })]);
    comp.goTo(0);
    expect(comp.comment).toContain('d4');

    const segs = comp.commentBlocks.flat();
    const chip = segs.find(s => s.move);
    expect(chip).toBeTruthy();
    expect(chip!.move).toContain('d4');

    comp.previewVariationMove(chip!);
    expect(comp.variationPreview).not.toBeNull();
    expect(comp.variationPreview!.fen).toBe(chip!.fen!);

    comp.goTo(1); // stepping ends the preview
    expect(comp.variationPreview).toBeNull();
  });

  it('navigates between lines', () => {
    const comp = build([line({ id: 1 }), line({ id: 2 }), line({ id: 3 })]);
    expect(comp.selectedIndex).toBe(0);
    comp.nextLine();
    expect(comp.selected?.id).toBe(2);
    comp.prevLine();
    expect(comp.selected?.id).toBe(1);
    comp.prevLine(); // clamp at start
    expect(comp.selected?.id).toBe(1);
  });
});
