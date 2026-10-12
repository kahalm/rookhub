import { of } from 'rxjs';
import { AnalysisHistorySaver, HISTORY_SAVE_MS } from './analysis-history-saver';
import { AnalysisNode, addMove, createRoot } from './analysis-tree';
import { AnalysisHistoryEntry } from './analysis-history.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const AFTER_E4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';

describe('AnalysisHistorySaver', () => {
  let root: AnalysisNode;
  let current: AnalysisNode;
  let save: jasmine.Spy;
  let saver: AnalysisHistorySaver;

  beforeEach(() => {
    jasmine.clock().install();
    root = createRoot(START);
    current = root;
    save = jasmine.createSpy('save').and.returnValue(of({ id: 7 } as AnalysisHistoryEntry));
    saver = new AnalysisHistorySaver({ save, loggedIn: () => true, snapshot: () => ({ root, current }), blankFen: START });
  });
  afterEach(() => jasmine.clock().uninstall());

  it('speichert gedrosselt, übernimmt die Kennung und speichert Gleiches nicht noch einmal', () => {
    saver.schedule();
    jasmine.clock().tick(HISTORY_SAVE_MS);
    expect(save).not.toHaveBeenCalled();                        // leere Grundstellung ist keine Analyse
    current = addMove(root, { san: 'e4', uci: 'e2e4', fen: AFTER_E4 });
    saver.schedule();
    jasmine.clock().tick(HISTORY_SAVE_MS - 1);
    expect(save).not.toHaveBeenCalled();
    jasmine.clock().tick(1);
    expect(save).toHaveBeenCalledOnceWith(jasmine.objectContaining({ id: null, startFen: START, current: 0 }));
    expect(saver.id).toBe(7);
    saver.schedule();
    jasmine.clock().tick(HISTORY_SAVE_MS);
    expect(save).toHaveBeenCalledTimes(1);
  });

  it('eine neue Analyse speichert den alten Stand sofort und beginnt ohne Kennung', () => {
    current = addMove(root, { san: 'e4', uci: 'e2e4', fen: AFTER_E4 });
    saver.schedule();
    saver.newSession('A – B');
    expect(save).toHaveBeenCalledTimes(1);
    expect(saver.id).toBeNull();
  });

  it('ein geöffneter Eintrag behält seine Kennung und gilt als gespeichert', () => {
    current = addMove(root, { san: 'e4', uci: 'e2e4', fen: AFTER_E4 });
    saver.openEntry({ id: 3, title: null } as AnalysisHistoryEntry);
    saver.markSaved();
    saver.schedule();
    jasmine.clock().tick(HISTORY_SAVE_MS);
    expect(save).not.toHaveBeenCalled();
    expect(saver.id).toBe(3);
  });
});
