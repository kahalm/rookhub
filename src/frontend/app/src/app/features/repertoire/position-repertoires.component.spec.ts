import { of, throwError } from 'rxjs';
import { PositionRepertoiresComponent } from './position-repertoires.component';
import { PositionLookupResult, PositionTreeResult, SimilarPositionsResult } from '../../core/repertoire.service';

describe('PositionRepertoiresComponent', () => {
  // Die gewählte Sicht wird in localStorage gemerkt — Tests starten bewusst auf „Liste".
  beforeEach(() => {
    localStorage.removeItem('rookhub_position_reps_mode');
    localStorage.removeItem('rookhub_position_reps_similar');
  });
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

  const similarResult: SimilarPositionsResult = {
    matches: [
      {
        repertoireId: 7, repertoireName: 'My Sicilian', chapter: 'Najdorf', lineName: 'Main line',
        gameIndex: 0, ply: 6, fen: 'r1bqkb1r/1p1p1ppp/p1n2n2/4p3/4P3/2N2N2/PPP2PPP/R1BQKB1R w KQkq - 0 7',
        score: 88, positionScore: 88, mirrored: false, pawnScore: 95, materialScore: 100, pieceScore: 70, kingScore: 80,
        moveSan: '', moveFrom: '', moveTo: '', moveMatch: null,
      },
      {
        repertoireId: 9, repertoireName: 'Caro-Kann', chapter: 'Advance', lineName: '',
        gameIndex: 3, ply: 9, fen: 'r1bqkb1r/1p1p1ppp/p1n2n2/4p3/4P3/2N2N2/PPP2PPP/R1BQKB1R w KQkq - 0 7',
        score: 61, positionScore: 61, mirrored: true, pawnScore: 64, materialScore: 88, pieceScore: 40, kingScore: 55,
        moveSan: '', moveFrom: '', moveTo: '', moveMatch: null,
      },
    ],
  };

  function make() {
    const repSvc: any = {
      lookupPosition: jasmine.createSpy('lookupPosition').and.returnValue(of(result)),
      lookupPositionTree: jasmine.createSpy('lookupPositionTree').and.returnValue(of(treeResult)),
      findSimilarPositions: jasmine.createSpy('findSimilarPositions').and.returnValue(of(similarResult)),
      list: jasmine.createSpy('list').and.returnValue(of([
        { id: 7, name: 'My Sicilian' }, { id: 9, name: 'Caro-Kann' },
      ])),
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

  // ===== Ähnliche Stellungen =====

  it('setMode("similar") loads the repertoire options and searches with the documented contract', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('similar');

    expect(repSvc.list).toHaveBeenCalledTimes(1);
    expect(c.simOptions.map(o => o.id)).toEqual([7, 9]);
    expect([...c.simSelected]).toEqual([7, 9]);            // Default: alle
    expect(repSvc.findSimilarPositions).toHaveBeenCalledTimes(1);
    const req = repSvc.findSimilarPositions.calls.mostRecent().args[0];
    expect(req.fen).toBe(c.fen);
    expect(req.repertoireIds).toEqual([7, 9]);
    expect(req.preset).toBe('ausgewogen');                 // Default-Voreinstellung
    expect(req.includeMirrored).toBeTrue();                // Spiegel-Treffer standardmäßig an
    expect(req.sameSideToMove).toBeFalse();                // Default aus — wie serverseitig
    expect(typeof req.limit).toBe('number');
    // Die Schwelle setzt der Server; eine eigene mitzuschicken ließ die Defaults auseinanderdriften.
    expect('minScore' in req).toBeFalse();
    expect('move' in req).toBeFalse();                     // ohne Zug bleibt die Anfrage die alte
    expect('onlyWithMove' in req).toBeFalse();
    expect(c.similar.length).toBe(2);
    expect(c.similar.map(m => m.score)).toEqual([88, 61]);  // nach Score sortiert
    expect(c.similar[1].mirrored).toBeTrue();
  });

  it('sorts the matches by score even if the server does not', () => {
    const { c, repSvc } = make();
    repSvc.findSimilarPositions.and.returnValue(of({ matches: [...similarResult.matches].reverse() }));
    c.toggle();
    c.setMode('similar');
    expect(c.similar.map(m => m.score)).toEqual([88, 61]);
  });

  it('changing the preset re-runs the search and is remembered per device', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('similar');

    c.setPreset('struktur');

    expect(repSvc.findSimilarPositions).toHaveBeenCalledTimes(2);
    expect(repSvc.findSimilarPositions.calls.mostRecent().args[0].preset).toBe('struktur');
    expect(JSON.parse(localStorage.getItem('rookhub_position_reps_similar')!).preset).toBe('struktur');

    c.setPreset('struktur');                                // gleiche Voreinstellung → kein Request
    expect(repSvc.findSimilarPositions).toHaveBeenCalledTimes(2);
  });

  it('turning mirrored hits off re-runs the search with includeMirrored=false', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('similar');

    c.setMirrored(false);

    expect(repSvc.findSimilarPositions.calls.mostRecent().args[0].includeMirrored).toBeFalse();
    expect(JSON.parse(localStorage.getItem('rookhub_position_reps_similar')!).mirrored).toBeFalse();
  });

  it('a narrowed repertoire selection is passed through; an empty one asks nothing', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('similar');

    c.setSimilarSelection([9]);
    expect(repSvc.findSimilarPositions.calls.mostRecent().args[0].repertoireIds).toEqual([9]);

    c.setSimilarSelection([]);
    // Leere Liste hieße für den Server „alle" — genau das Gegenteil. Also gar kein Request.
    expect(repSvc.findSimilarPositions).toHaveBeenCalledTimes(2);
    expect(c.similar).toEqual([]);
    expect(c.loading).toBeFalse();
  });

  it('a failing repertoire list does not break the search (server then searches all)', () => {
    const { c, repSvc } = make();
    repSvc.list.and.returnValue(throwError(() => new Error('offline')));
    c.toggle();
    c.setMode('similar');

    expect(c.error).toBeFalse();
    expect(c.simOptions).toEqual([]);
    expect(repSvc.findSimilarPositions.calls.mostRecent().args[0].repertoireIds).toEqual([]);
    expect(c.similar.length).toBe(2);
  });

  it('a failed similar request shows the error state and allows a retry', () => {
    const { c, repSvc } = make();
    repSvc.findSimilarPositions.and.returnValue(throwError(() => new Error('boom')));
    c.toggle();
    c.setMode('similar');
    expect(c.error).toBeTrue();
    expect(c.loading).toBeFalse();

    repSvc.findSimilarPositions.and.returnValue(of(similarResult));
    c.setMode('list');
    c.setMode('similar');
    expect(c.error).toBeFalse();
    expect(c.similar.length).toBe(2);
  });

  it('openMatch() opens the position exactly like the list view does', () => {
    const { c, router } = make();
    c.toggle();
    c.setMode('similar');

    c.openMatch(c.similar[0]);                              // Najdorf / Main line / gameIndex 0 / ply 6

    const [path, extras] = router.navigate.calls.mostRecent().args;
    expect(path).toEqual(['/repertoires', 7]);
    expect(extras.queryParams.ply).toBe(6);
    expect(typeof extras.queryParams.line).toBe('string');
    expect(extras.queryParams.line.length).toBeGreaterThan(1);
  });

  it('remembers preset, mirrored and same-side switch across instances', () => {
    const { c } = make();
    c.toggle();
    c.setMode('similar');
    c.setPreset('stellungsbild');
    c.setMirrored(false);
    c.setSameSide(true);

    const fresh = make().c;
    expect(fresh.simPreset).toBe('stellungsbild');
    expect(fresh.simMirrored).toBeFalse();
    expect(fresh.simSameSide).toBeTrue();
  });

  it('turning "same side to move" on re-runs the search with the flag', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('similar');

    c.setSameSide(true);

    expect(repSvc.findSimilarPositions).toHaveBeenCalledTimes(2);
    expect(repSvc.findSimilarPositions.calls.mostRecent().args[0].sameSideToMove).toBeTrue();
    c.setSameSide(true);                                    // gleicher Wert → kein Request
    expect(repSvc.findSimilarPositions).toHaveBeenCalledTimes(2);
  });

  // ===== Zug-Treffer =====

  it('a SAN move is resolved on the anchor position and sent as from/to', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('similar');

    c.setMoveText('Nc6');                                   // Schwarz am Zug: b8→c6

    expect(c.simMoveInvalid).toBeFalse();
    expect(c.simMoveSan).toBe('Nc6');
    expect(c.simMove).toEqual({ from: 'b8', to: 'c6' });
    const req = repSvc.findSimilarPositions.calls.mostRecent().args[0];
    expect(req.move).toEqual({ from: 'b8', to: 'c6' });
    expect('onlyWithMove' in req).toBeFalse();              // Bonus, kein Zwang
  });

  it('a differently disambiguated SAN is the SAME move and does not re-run the search', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('similar');
    c.setMoveText('Nc6');
    const calls = repSvc.findSimilarPositions.calls.count();

    c.setMoveText('Nbc6');                                  // dasselbe from/to, nur anders notiert

    expect(c.simMove).toEqual({ from: 'b8', to: 'c6' });
    expect(repSvc.findSimilarPositions).toHaveBeenCalledTimes(calls);
  });

  it('an illegal move is reported at the field and searched WITHOUT a move', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('similar');
    c.setMoveText('Nc6');

    c.setMoveText('Nc7');                                   // kein Springer kann nach c7

    expect(c.simMoveInvalid).toBeTrue();
    expect(c.simMove).toBeNull();
    expect(c.simMoveSan).toBe('');
    const req = repSvc.findSimilarPositions.calls.mostRecent().args[0];
    expect('move' in req).toBeFalse();                      // keine leere Trefferliste vortäuschen
  });

  it('typing towards a legal move does not fire a request per keystroke', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('similar');
    const before = repSvc.findSimilarPositions.calls.count();

    c.setMoveText('N');
    c.setMoveText('Nc');
    expect(repSvc.findSimilarPositions).toHaveBeenCalledTimes(before);   // beides ist kein Zug
    c.setMoveText('Nc6');
    expect(repSvc.findSimilarPositions).toHaveBeenCalledTimes(before + 1);
  });

  it('"only with this move" is passed through and hides hits without one', () => {
    const { c, repSvc } = make();
    repSvc.findSimilarPositions.and.returnValue(of({
      matches: [
        { ...similarResult.matches[0], moveSan: 'Nc6', moveFrom: 'b8', moveTo: 'c6', moveMatch: 'exact' as const },
        similarResult.matches[1],                            // ohne Zug-Treffer
      ],
    }));
    c.toggle();
    c.setMode('similar');
    c.setMoveText('Nc6');

    c.setOnlyWithMove(true);

    expect(repSvc.findSimilarPositions.calls.mostRecent().args[0].onlyWithMove).toBeTrue();
    // Sicherheitsnetz: auch ein Server, der den Schalter (noch) nicht kennt, liefert hier nichts Falsches.
    expect(c.similar.length).toBe(1);
    expect(c.similar[0].moveMatch).toBe('exact');
  });

  it('the switch stays without effect while there is no valid move', () => {
    const { c, repSvc } = make();
    c.toggle();
    c.setMode('similar');
    const calls = repSvc.findSimilarPositions.calls.count();

    c.setOnlyWithMove(true);

    expect(repSvc.findSimilarPositions).toHaveBeenCalledTimes(calls);   // nichts zu filtern
    expect(c.similar.length).toBe(2);
  });

  it('sorts by the FINAL score, so a move hit outranks the better position', () => {
    const { c, repSvc } = make();
    repSvc.findSimilarPositions.and.returnValue(of({
      matches: [
        { ...similarResult.matches[0], score: 88, positionScore: 88 },                       // Stellung besser
        { ...similarResult.matches[1], score: 92.5, positionScore: 85, moveMatch: 'exact' as const,
          moveSan: 'Nc6', moveFrom: 'b8', moveTo: 'c6' },
      ],
    }));
    c.toggle();
    c.setMode('similar');
    c.setMoveText('Nc6');

    expect(c.similar.map(m => m.score)).toEqual([92.5, 88]);
    expect(c.similar.map(m => m.positionScore)).toEqual([85, 88]);      // beide Zahlen bleiben da
  });

  it('re-reads the move on the NEW anchor when the position changes', () => {
    const { c } = make();
    c.toggle();
    c.setMode('similar');
    c.setMoveText('Nc6');
    expect(c.simMove).toEqual({ from: 'b8', to: 'c6' });

    // Weiter geklickt: 2…Nc6 3.Bb5 — jetzt ist Schwarz erneut am Zug, Nc6 ist gespielt.
    c.fen = 'r1bqkbnr/pp1ppppp/2n5/1Bp5/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3';
    c.ngOnChanges({ fen: { currentValue: c.fen, previousValue: '', firstChange: false, isFirstChange: () => false } });

    expect(c.simMoveInvalid).toBeTrue();     // derselbe Text, dort kein Zug mehr
    expect(c.simMove).toBeNull();
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
