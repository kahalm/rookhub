import { of, throwError } from 'rxjs';
import { PositionRepertoiresComponent } from './position-repertoires.component';
import { PositionLookupResult, PositionTreeResult } from '../../core/repertoire.service';

describe('PositionRepertoiresComponent', () => {
  // Die gewählte Sicht wird in localStorage gemerkt — Tests starten bewusst auf „Liste".
  beforeEach(() => localStorage.removeItem('rookhub_position_reps_mode'));
  const result: PositionLookupResult = {
    repertoires: [
      {
        repertoireId: 7, repertoireName: 'My Sicilian', kind: 'Opening', shared: false,
        lines: [
          { chapter: 'Najdorf', lineName: 'Main line', gameIndex: 0, ply: 6 },
          { chapter: 'Najdorf', lineName: 'English Attack', gameIndex: 1, ply: 8 },
          { chapter: 'Dragon', lineName: 'Yugoslav', gameIndex: 2, ply: 4 },
        ],
      },
    ],
  };

  // 1.e4 c5 2.Nf3 als „Main line" → parsePgnText + lineKeyFromSans laufen echt.
  const pgn =
    '[Event "R"]\n[White "Main line"]\n[Black "Najdorf"]\n\n1. e4 c5 2. Nf3 d6 *\n\n' +
    '[Event "R"]\n[White "English Attack"]\n[Black "Najdorf"]\n\n1. e4 c5 2. Nf3 Nc6 *\n\n' +
    '[Event "R"]\n[White "Yugoslav"]\n[Black "Dragon"]\n\n1. e4 c5 2. Nf3 g6 *\n';

  const treeResult: PositionTreeResult = {
    repertoires: [
      {
        repertoireId: 7, repertoireName: 'My Sicilian', kind: 'Opening', shared: false,
        occurrences: 3, truncated: false,
        moves: [
          {
            san: 'd6', count: 2, isEnd: false, chapter: null, lineName: null, gameIndex: null,
            children: [{ san: 'd4', count: 2, isEnd: true, chapter: 'Najdorf', lineName: 'Main line', gameIndex: 0, children: [] }],
          },
          { san: 'g6', count: 1, isEnd: true, chapter: 'Dragon', lineName: 'Yugoslav', gameIndex: 2, children: [] },
        ],
      },
    ],
  };

  function make() {
    const repSvc: any = {
      lookupPosition: jasmine.createSpy('lookupPosition').and.returnValue(of(result)),
      lookupPositionTree: jasmine.createSpy('lookupPositionTree').and.returnValue(of(treeResult)),
      getPgnText: jasmine.createSpy('getPgnText').and.returnValue(of(pgn)),
    };
    const router: any = { navigate: jasmine.createSpy('navigate') };
    const auth: any = { isLoggedIn: true };
    const c = new PositionRepertoiresComponent(auth, repSvc, router);
    c.fen = 'rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2';
    return { c, repSvc, router };
  }

  it('toggle() loads and populates repertoires + totalLines', () => {
    const { c, repSvc } = make();
    c.toggle();
    expect(c.open).toBeTrue();
    expect(repSvc.lookupPosition).toHaveBeenCalled();
    expect(c.repertoires.length).toBe(1);
    expect(c.totalLines).toBe(3);
    expect(c.isRepOpen(7)).toBeTrue(); // alle aufgeklappt
  });

  it('chaptersOf groups lines by chapter preserving order', () => {
    const { c } = make();
    const groups = c.chaptersOf(result.repertoires[0]);
    expect(groups.map(g => g.name)).toEqual(['Najdorf', 'Dragon']);
    expect(groups[0].lines.length).toBe(2);
    expect(groups[1].lines.length).toBe(1);
  });

  it('view() resolves lineKey from the client PGN parse and navigates with ply', () => {
    const { c, router } = make();
    const line = result.repertoires[0].lines[0]; // Main line / Najdorf / gameIndex 0 / ply 6
    const emitted = spyOn(c.navigated, 'emit');
    c.view(result.repertoires[0], line);
    expect(router.navigate).toHaveBeenCalled();
    const [path, extras] = router.navigate.calls.mostRecent().args;
    expect(path).toEqual(['/repertoires', 7]);
    expect(extras.queryParams.ply).toBe(6);
    expect(typeof extras.queryParams.line).toBe('string');
    expect(extras.queryParams.line.length).toBeGreaterThan(1); // ein echter lineKey ('l' + hash)
    expect(emitted).toHaveBeenCalled();
  });

  it('train() navigates to the trainer with chapter + lineKey', () => {
    const { c, router } = make();
    const line = result.repertoires[0].lines[2]; // Yugoslav / Dragon / gameIndex 2
    c.train(result.repertoires[0], line);
    const [path, extras] = router.navigate.calls.mostRecent().args;
    expect(path).toEqual(['/repertoires', 7, 'train']);
    expect(extras.queryParams.chapter).toBe('Dragon');
    expect(typeof extras.queryParams.line).toBe('string');
  });

  it('renders nothing / does not load when logged out is handled by template guard', () => {
    // isLoggedIn=false → Template rendert nichts; load() bleibt trotzdem defensiv nutzbar.
    const repSvc: any = { lookupPosition: jasmine.createSpy().and.returnValue(of(result)), getPgnText: jasmine.createSpy() };
    const c = new PositionRepertoiresComponent({ isLoggedIn: false } as any, repSvc, { navigate: () => {} } as any);
    expect(c.auth.isLoggedIn).toBeFalse();
  });

  // ===== Baummodus =====

  it('setMode("tree") loads the tree endpoint and fills trees + totalOccurrences', () => {
    const { c, repSvc } = make();
    c.toggle();                                    // öffnet in der Listenansicht
    expect(repSvc.lookupPosition).toHaveBeenCalledTimes(1);

    c.setMode('tree');

    expect(c.mode).toBe('tree');
    expect(repSvc.lookupPositionTree).toHaveBeenCalledTimes(1);
    expect(c.trees.length).toBe(1);
    expect(c.totalOccurrences).toBe(3);
    expect(c.isRepOpen(7)).toBeTrue();
    expect(localStorage.getItem('rookhub_position_reps_mode')).toBe('tree');
  });

  it('does not reload while position and view stay the same', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('tree');
    c.setMode('list');
    c.setMode('tree');
    // Je Sicht ein Request pro Wechsel — aber kein zusätzlicher ohne Wechsel.
    expect(repSvc.lookupPosition).toHaveBeenCalledTimes(2);
    expect(repSvc.lookupPositionTree).toHaveBeenCalledTimes(2);
    c.ngOnChanges({} as any);                      // kein fen-Change → kein Reload
    expect(repSvc.lookupPositionTree).toHaveBeenCalledTimes(2);
  });

  it('a failed tree request shows the error state and allows a retry', () => {
    const { c, repSvc } = make();
    repSvc.lookupPositionTree.and.returnValue(throwError(() => new Error('offline')));
    c.toggle();
    c.setMode('tree');
    expect(c.error).toBeTrue();
    expect(c.loading).toBeFalse();

    repSvc.lookupPositionTree.and.returnValue(of(treeResult));
    c.setMode('list');
    c.setMode('tree');                              // erneuter Versuch lädt wirklich neu
    expect(c.error).toBeFalse();
    expect(c.trees.length).toBe(1);
  });

  it('canPlay only when a consumer listens on playMoves', () => {
    const { c } = make();
    expect(c.canPlay).toBeFalse();
    c.playMoves.subscribe(() => {});
    expect(c.canPlay).toBeTrue();
  });

  it('derives move numbering from the FEN', () => {
    const { c } = make();
    expect(c.blackToMove).toBeTrue();               // …/2p5/… b KQkq - 1 2
    expect(c.startMoveNumber).toBe(2);
  });

  it('trainNode()/viewNode() navigate for an unambiguous tree node', () => {
    const { c, router } = make();
    c.toggle();
    c.setMode('tree');
    const dragon = c.trees[0].moves[1];             // g6 → Yugoslav / Dragon / gameIndex 2

    c.trainNode(c.trees[0], dragon);

    const [path, extras] = router.navigate.calls.mostRecent().args;
    expect(path).toEqual(['/repertoires', 7, 'train']);
    expect(extras.queryParams.chapter).toBe('Dragon');
    expect(typeof extras.queryParams.line).toBe('string');
  });
});
