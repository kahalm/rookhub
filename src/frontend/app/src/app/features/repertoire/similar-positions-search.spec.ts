import { Subject, of, throwError } from 'rxjs';
import { SIMILAR_LIMIT, SIMILAR_PREFS_KEY, SimilarPositionsSearch } from './similar-positions-search';
import { SimilarPositionMatch, SimilarPositionsResult } from '../../core/repertoire.service';

const FEN = 'rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2';

function match(score: number, moveMatch: SimilarPositionMatch['moveMatch'] = null): SimilarPositionMatch {
  return {
    repertoireId: 7, repertoireName: 'R', chapter: 'C', lineName: 'L', gameIndex: 0, ply: 4, fen: FEN,
    score, positionScore: score, mirrored: false, pawnScore: 1, materialScore: 1, pieceScore: 1, kingScore: 1,
    moveSan: '', moveFrom: '', moveTo: '', moveMatch,
  };
}

function make(list: any = () => of([{ id: 7, name: 'A' }, { id: 9, name: 'B' }])) {
  let fen = FEN;
  const api = {
    list: jasmine.createSpy('list').and.callFake(list),
    findSimilarPositions: jasmine.createSpy('find').and.returnValue(of({ matches: [match(61), match(88)] } as SimilarPositionsResult)),
  };
  const reload = jasmine.createSpy('reload');
  const s = new SimilarPositionsSearch({ api: () => api, fen: () => fen, reload });
  return { s, api, reload, setFen: (f: string) => fen = f };
}

describe('SimilarPositionsSearch', () => {
  beforeEach(() => localStorage.removeItem(SIMILAR_PREFS_KEY));
  afterEach(() => localStorage.removeItem(SIMILAR_PREFS_KEY));

  it('erste Suche: Liste holen, alle auswählen, Anfrage nach Vertrag; Liste nur einmal', () => {
    const { s, api } = make();
    let res: SimilarPositionsResult | null = null;
    s.search().subscribe(r => res = r);
    expect([...s.selected]).toEqual([7, 9]);
    expect(api.findSimilarPositions).toHaveBeenCalledWith({
      fen: FEN, repertoireIds: [7, 9], preset: 'ausgewogen', includeMirrored: true, sameSideToMove: false, limit: SIMILAR_LIMIT,
    });
    s.applyResult(res);
    expect(s.matches.map(m => m.score)).toEqual([88, 61]);
    s.search().subscribe();
    expect(api.list).toHaveBeenCalledTimes(1);
  });

  it('alles abgewählt: keine Anfrage, leere Treffer', () => {
    const { s, api, reload } = make();
    s.search().subscribe();
    s.setSelection([]);
    expect(reload).toHaveBeenCalled();
    let res: SimilarPositionsResult | null | undefined;
    s.search().subscribe(r => res = r);
    expect(res).toBeNull();
    expect(api.findSimilarPositions).toHaveBeenCalledTimes(1);
    s.applyResult(null);
    expect(s.matches).toEqual([]);
  });

  it('angefasste Auswahl bleibt; gelöschte Repertoires fallen heraus', () => {
    const opts = new Subject<{ id: number; name: string }[]>();
    const { s } = make(() => opts);
    s.setSelection([9, 11]);
    s.search().subscribe();
    opts.next([{ id: 7, name: 'A' }, { id: 9, name: 'B' }]);
    expect([...s.selected]).toEqual([9]);
  });

  it('scheiternde Liste: ohne Filter suchen (Server: alle)', () => {
    const { s, api } = make(() => throwError(() => new Error('x')));
    s.search().subscribe();
    expect(s.options).toEqual([]);
    expect(api.findSimilarPositions.calls.mostRecent().args[0].repertoireIds).toEqual([]);
  });

  it('Einstellungen: gleiche Werte tun nichts, neue werden gemerkt und laden neu', () => {
    const { s, reload } = make();
    s.setPreset('ausgewogen'); s.setMirrored(true); s.setSameSide(false);
    expect(reload).not.toHaveBeenCalled();
    s.setPreset('struktur'); s.setMirrored(false); s.setSameSide(true);
    expect(reload).toHaveBeenCalledTimes(3);
    expect(JSON.parse(localStorage.getItem(SIMILAR_PREFS_KEY)!)).toEqual({ preset: 'struktur', mirrored: false, sameSide: true });
    const fresh = make().s;
    expect([fresh.preset, fresh.mirrored, fresh.sameSide]).toEqual(['struktur', false, true]);
  });

  it('kaputte gemerkte Werte → Vorgaben', () => {
    localStorage.setItem(SIMILAR_PREFS_KEY, JSON.stringify({ preset: 'quatsch', mirrored: 'ja' }));
    const { s } = make();
    expect([s.preset, s.mirrored, s.sameSide]).toEqual(['ausgewogen', true, false]);
  });

  it('Zug: auf der Ankerstellung aufgelöst, nur ein neuer ZUG lädt neu, mitgeschickt samt Zug-Zwang', () => {
    const { s, api, reload } = make();
    s.setMoveText('N');
    expect(reload).not.toHaveBeenCalled();
    s.setMoveText('Nc6');
    expect(s.move).toEqual({ from: 'b8', to: 'c6' });
    expect(s.moveSan).toBe('Nc6');
    expect(reload).toHaveBeenCalledTimes(1);
    s.setMoveText('Nbc6');
    expect(reload).toHaveBeenCalledTimes(1);
    s.setOnlyWithMove(true);
    expect(reload).toHaveBeenCalledTimes(2);
    s.search().subscribe();
    const req = api.findSimilarPositions.calls.mostRecent().args[0];
    expect(req.move).toEqual({ from: 'b8', to: 'c6' });
    expect(req.onlyWithMove).toBeTrue();
    s.applyResult({ matches: [match(90), match(70, 'exact')] } as SimilarPositionsResult);
    expect(s.matches.map(m => m.score)).toEqual([70]);
  });

  it('Zug-Zwang ohne gültigen Zug lädt nicht neu', () => {
    const { s, reload } = make();
    s.setOnlyWithMove(true);
    expect(reload).not.toHaveBeenCalled();
  });

  it('reanchor liest denselben Text auf der neuen Stellung', () => {
    const { s, setFen } = make();
    s.setMoveText('Nc6');
    setFen('rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 1 2');
    s.reanchor();
    expect(s.moveInvalid).toBeTrue();
    expect(s.move).toBeNull();
  });
});
