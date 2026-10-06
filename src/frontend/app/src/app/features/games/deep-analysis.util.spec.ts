import { AnalysisJob } from '../analysis/analysis-jobs.service';
import { DeepStored, deepAhead, deepProgress, prunedEarly, resultNodes } from './deep-analysis.util';

function job(p: Partial<AnalysisJob>): AnalysisJob {
  return { id: 1, fen: 'x', title: null, engineId: 'e', targetDepth: 40, multiPv: 3, status: 'running', reachedDepth: 0,
    resultJson: null, secondsSpent: 0, lastError: null, createdAt: '', updatedAt: '', lastRunAt: null, finishedAt: null, ...p };
}
const stored: DeepStored = { sf: { depth: 20, lines: [] }, lc0: { nodes: 100_000, lines: [] } };

describe('deep-analysis.util', () => {
  it('liest die Knoten aus der Ergebniszeile', () => {
    expect(resultNodes(job({ resultJson: '{"depth":12,"nodes":123456,"pvs":[]}' }))).toBe(123456);
    expect(resultNodes(job({ resultJson: 'kaputt' }))).toBe(0);
    expect(resultNodes(null)).toBe(0);
  });

  it('Fortschritt: Stockfish nach Tiefe, Lc0 nach Knoten, fertig = 100 %', () => {
    expect(deepProgress('sf', job({ reachedDepth: 18 }), { id: 1, depth: 20, nps: 1, seconds: 3 }, 40))
      .toEqual({ now: 20, target: 40, percent: 50 });
    expect(deepProgress('lc0', job({ resultJson: '{"nodes":100000}' }), { id: 1, depth: 9, nps: 1, seconds: 3, nodes: 125000 }, 500000).percent)
      .toBe(25);
    expect(deepProgress('lc0', job({ status: 'done', resultJson: '{"nodes":88000}' }), null, 500000).percent).toBe(100);
    expect(deepProgress('sf', null, null, 40)).toEqual({ now: 0, target: 40, percent: 0 });
  });

  it('zeigt das neue Ergebnis erst, wenn es weiter ist als das hinterlegte', () => {
    expect(deepAhead('sf', job({ reachedDepth: 20, resultJson: '{}' }), stored)).toBeFalse();
    expect(deepAhead('sf', job({ reachedDepth: 21, resultJson: '{}' }), stored)).toBeTrue();
    expect(deepAhead('lc0', job({ resultJson: '{"nodes":90000}' }), stored)).toBeFalse();
    expect(deepAhead('lc0', job({ resultJson: '{"nodes":100001}' }), stored)).toBeTrue();
    expect(deepAhead('sf', job({ reachedDepth: 1, resultJson: '{}' }), { sf: null, lc0: null })).toBeTrue();
    expect(deepAhead('sf', job({ reachedDepth: 30 }), stored)).toBeFalse();   // noch keine Zeile
  });

  it('erkennt das vorzeitige Ende von Lc0', () => {
    expect(prunedEarly(job({ status: 'done', resultJson: '{"nodes":88000}' }), 500000)).toBeTrue();
    expect(prunedEarly(job({ status: 'running', resultJson: '{"nodes":88000}' }), 500000)).toBeFalse();
    expect(prunedEarly(job({ status: 'done', resultJson: '{"nodes":500000}' }), 500000)).toBeFalse();
  });
});
