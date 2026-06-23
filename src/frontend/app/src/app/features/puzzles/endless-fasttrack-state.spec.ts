import { EndlessFasttrackState } from './endless-fasttrack-state';
import { EndlessConfig, EndlessSession } from './endless-storage.service';

function cfg(overrides: Partial<EndlessConfig> = {}): EndlessConfig {
  return { startElo: 700, themes: '', stockfishDepth: 16, ...overrides };
}

describe('EndlessFasttrackState', () => {
  it('compute(): without config overrides adopts the auto thresholds and derives steps', () => {
    const s = new EndlessFasttrackState();
    const config = cfg();

    s.compute(config, []);

    // Auto-Werte werden übernommen (kein Override)
    expect(s.avgFirst).toBe(s.autoFirst);
    expect(s.avgSecond).toBe(s.autoSecond);
    // Schwellen liegen über dem Start-Elo, Schritte sind positiv
    expect(s.autoFirst).toBeGreaterThan(config.startElo);
    expect(s.autoSecond).toBeGreaterThan(s.autoFirst);
    expect(s.phase1Step).toBeGreaterThan(0);
    expect(s.phase2Step).toBeGreaterThan(0);
  });

  it('compute(): honours manual config overrides over the auto thresholds', () => {
    const s = new EndlessFasttrackState();
    const config = cfg({ fasttrackThreshold1: 1234, fasttrackThreshold2: 1700 });

    s.compute(config, [] as EndlessSession[]);

    expect(s.avgFirst).toBe(1234);
    expect(s.avgSecond).toBe(1700);
    // Auto bleibt der berechnete Vorschlag (≠ Override) → reset könnte zurückfallen
    expect(s.autoFirst).not.toBe(1234);
  });

  it('applyOverrides(): writes only diverging values as overrides, equal-to-auto clears them', () => {
    const s = new EndlessFasttrackState();
    const config = cfg();
    s.compute(config, []);

    // Nutzer ändert nur T1 weg vom Auto-Wert, T2 bleibt = Auto
    s.avgFirst = s.autoFirst + 150;
    s.applyOverrides(config);

    expect(config.fasttrackThreshold1).toBe(s.autoFirst + 150);
    expect(config.fasttrackThreshold2).toBeUndefined();
  });

  it('reset(): restores a threshold to its auto value and clears the override', () => {
    const s = new EndlessFasttrackState();
    const config = cfg({ fasttrackThreshold1: 1500 });
    s.compute(config, []);
    expect(s.avgFirst).toBe(1500);

    s.reset(1, config);

    expect(s.avgFirst).toBe(s.autoFirst);
    expect(config.fasttrackThreshold1).toBeUndefined();
    // T2 unangetastet
    expect(s.avgSecond).toBe(s.autoSecond);
  });

  it('reset(2): only touches the second threshold', () => {
    const s = new EndlessFasttrackState();
    const config = cfg({ fasttrackThreshold1: 1300, fasttrackThreshold2: 1900 });
    s.compute(config, []);

    s.reset(2, config);

    expect(s.avgSecond).toBe(s.autoSecond);
    expect(config.fasttrackThreshold2).toBeUndefined();
    // T1-Override bleibt erhalten
    expect(s.avgFirst).toBe(1300);
    expect(config.fasttrackThreshold1).toBe(1300);
  });
});
