import { Chess } from 'chess.js';
import { AnalysisLine } from './analysis-engine.service';
import { engineArrows, engineCandidates, evalBar, legalDests, SearchClock, terminalStateOf } from './analysis-readout';

const line = (score: number, scoreType: 'cp' | 'mate' = 'cp', pv: string[] = ['e2e4']): AnalysisLine =>
  ({ multipv: 1, depth: 20, scoreType, score, evalText: String(score), pvUci: pv }) as AnalysisLine;

describe('analysis-readout', () => {
  it('erkennt Matt und Patt', () => {
    expect(terminalStateOf(new Chess('7k/6Q1/6K1/8/8/8/8/8 b - - 0 1'))).toBe('mate-white-wins');
    expect(terminalStateOf(new Chess('7k/5Q2/6K1/8/8/8/8/8 b - - 0 1'))).toBe('stalemate');
    expect(terminalStateOf(new Chess())).toBeNull();
  });

  it('Zugziele je Feld', () => {
    const d = legalDests(new Chess());
    expect(d.get('e2' as never)).toEqual(['e3', 'e4'] as never);
    expect(d.size).toBe(10);
  });

  it('Bewertungsleiste: Ergebnis, Mitte, Matt, Sigmoide in Grenzen', () => {
    expect(evalBar(null, 'mate-black-wins')).toEqual({ evalText: '0-1', whiteHeight: 0 });
    expect(evalBar(null, null)).toEqual({ evalText: '0.00', whiteHeight: 50 });
    expect(evalBar(line(-3, 'mate'), null).whiteHeight).toBe(0);
    expect(evalBar(line(0), null).whiteHeight).toBe(50);
    expect(evalBar(line(5000), null).whiteHeight).toBe(98);
    expect(evalBar(line(-5000), null).whiteHeight).toBe(2);
  });

  it('Pfeile und Kandidaten nur für Linien mit Zug', () => {
    const lines = [line(10), line(5, 'cp', [])];
    expect(engineArrows(lines, i => 'b' + i)).toEqual([{ orig: 'e2', dest: 'e4', brush: 'b0', label: { text: '1' } }] as never);
    expect(engineCandidates(lines)).toEqual([{ uci: 'e2e4', evalText: '10' }]);
  });

  it('Suchuhr: startet je Stellung, friert beim Ende ein', () => {
    const c = new SearchClock(0);
    c.onStatus(true, 'A', 1000);
    expect(c.read(31_000)).toEqual({ sinceUpdateSec: 30, searchElapsedSec: 30 });
    c.onStatus(true, 'A', 40_000);                // gleiche Stellung → Suchzeit läuft weiter
    expect(c.read(41_000)).toEqual({ sinceUpdateSec: 1, searchElapsedSec: 40 });
    c.onStatus(false, 'A', 50_000);
    expect(c.read(99_000)).toEqual({ sinceUpdateSec: 0, searchElapsedSec: 49 });
    c.onStatus(true, 'B', 100_000);               // neue Stellung → von vorn
    expect(c.read(102_000).searchElapsedSec).toBe(2);
  });
});
