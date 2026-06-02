import { TestBed } from '@angular/core/testing';
import { AnalysisEngineService } from './analysis-engine.service';

describe('AnalysisEngineService.parseInfo', () => {
  let svc: AnalysisEngineService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    svc = TestBed.inject(AnalysisEngineService);
  });

  it('parses a centipawn info line (white to move)', () => {
    const l = svc.parseInfo('info depth 20 seldepth 28 multipv 1 score cp 35 nodes 1 pv e2e4 e7e5 g1f3', 'w')!;
    expect(l).toBeTruthy();
    expect(l.multipv).toBe(1);
    expect(l.depth).toBe(20);
    expect(l.scoreType).toBe('cp');
    expect(l.score).toBe(35);
    expect(l.evalText).toBe('+0.35');
    expect(l.pvUci).toEqual(['e2e4', 'e7e5', 'g1f3']);
  });

  it('flips the score sign for black to move (→ white POV)', () => {
    const l = svc.parseInfo('info depth 18 multipv 2 score cp 40 pv d2d4', 'b')!;
    expect(l.multipv).toBe(2);
    expect(l.score).toBe(-40);
    expect(l.evalText).toBe('-0.40');
  });

  it('formats mate scores', () => {
    expect(svc.parseInfo('info depth 30 multipv 1 score mate 3 pv a1a8', 'w')!.evalText).toBe('#3');
    expect(svc.parseInfo('info depth 30 multipv 1 score mate 2 pv a1a8', 'b')!.evalText).toBe('#-2');
  });

  it('returns null without a pv', () => {
    expect(svc.parseInfo('info depth 5 score cp 10', 'w')).toBeNull();
  });
});
