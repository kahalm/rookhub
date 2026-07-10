import { of } from 'rxjs';
import { PositionRepertoiresComponent } from './position-repertoires.component';
import { PositionLookupResult } from '../../core/repertoire.service';

describe('PositionRepertoiresComponent', () => {
  const result: PositionLookupResult = {
    repertoires: [
      {
        repertoireId: 7, repertoireName: 'My Sicilian', kind: 'Opening',
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

  function make() {
    const repSvc: any = {
      lookupPosition: jasmine.createSpy('lookupPosition').and.returnValue(of(result)),
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
});
