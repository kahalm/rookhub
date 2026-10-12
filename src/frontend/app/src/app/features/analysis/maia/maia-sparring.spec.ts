import { Color } from 'chessground/types';
import { AnalysisNode, createRoot, playUci, removeNode } from '../analysis-tree';
import { MaiaSparring, MaiaSparringHost } from './maia-sparring';
import { MAIA_ELO_KEY } from './maia-model';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const flush = () => new Promise<void>(r => setTimeout(r));

/** Das Sparring ohne Analysebrett: der Host führt Baum, aktuelle Stellung und Engine-Schalter selbst. */
describe('MaiaSparring', () => {
  let root: AnalysisNode;
  let current: AnalysisNode;
  let engineOn: boolean;
  let orientation: Color;
  let host: MaiaSparringHost;

  const turn = (): Color => current.fen.split(' ')[1] === 'w' ? 'white' : 'black';

  function make(): MaiaSparring {
    const spar = new MaiaSparring(host);
    spar.maiaDelayMs = 0;
    return spar;
  }

  beforeEach(() => {
    localStorage.removeItem(MAIA_ELO_KEY);
    root = createRoot(START);
    current = root;
    engineOn = true;
    orientation = 'white';
    host = {
      maia: {
        chooseMove: jasmine.createSpy('chooseMove').and.returnValue(Promise.resolve('e7e5')),
        release: jasmine.createSpy('release'), status: () => 'ready', prepare: () => Promise.resolve(true),
      } as any,
      games: {} as any, analyzeGame: {} as any, router: {} as any,
      auth: { isLoggedIn: false } as any,
      snackbar: { warn: jasmine.createSpy('warn') } as any,
      translate: { instant: (k: string) => k } as any,
      root: () => root, currentNode: () => current, turnColor: turn, hasMoves: () => true, editing: () => false,
      engineOn: () => engineOn, setEngineOn: on => { engineOn = on; }, setOrientation: c => { orientation = c; },
      running: () => false, depthSetting: () => 22, goToNode: n => { current = n; }, refresh: () => {},
      treeChanged: () => {}, changed: () => {},
    };
  });
  afterEach(() => localStorage.removeItem(MAIA_ELO_KEY));

  it('switches the engine off while sparring and restores it afterwards', () => {
    const spar = make();
    spar.startSparring();
    expect(spar.sparring?.userColor).toBe('white');
    expect(engineOn).toBeFalse();
    expect(orientation).toBe('white');
    spar.stopSparring();
    expect(spar.sparring).toBeNull();
    expect(engineOn).toBeTrue();
  });

  it('answers the own board move with Maia and keeps the game for „analyse game"', async () => {
    const spar = make();
    spar.startSparring();
    current = playUci(root, 'e2e4')!;
    spar.afterBoardMove(current, 'white');
    await flush();
    expect(host.maia.chooseMove).toHaveBeenCalledWith(root.children[0].fen, spar.maiaElo);
    expect(current.uci).toBe('e7e5');
    expect(spar.sparring?.tip).toBe(current);
    spar.stopSparring();
    expect(spar.lastSparring?.tip).toBe(current);
    spar.onNewTree();
    expect(spar.lastSparring).toBeNull();
  });

  it('ends the sparring when its start position is removed from the tree', () => {
    const e4 = playUci(root, 'e2e4')!;
    current = e4;
    const spar = make();
    spar.startSparring();
    const ends = spar.endedByRemoving(e4);
    const parent = removeNode(e4)!;
    spar.afterRemove(parent, ends);
    expect(ends).toBeTrue();
    expect(spar.sparring).toBeNull();
  });

  it('remembers the chosen strength per device', () => {
    make().onMaiaEloChange(1800);
    expect(make().maiaElo).toBe(1800);
  });
});
