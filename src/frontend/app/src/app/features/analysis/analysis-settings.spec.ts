import { AnalysisSettings, ANALYSIS_ENGINE_ON_KEY, ANALYSIS_LINES_KEY, ANALYSIS_DEPTH_KEY, ANALYSIS_PROVIDER_KEY } from './analysis-settings';

describe('AnalysisSettings', () => {
  const KEYS = [ANALYSIS_LINES_KEY, ANALYSIS_ENGINE_ON_KEY, ANALYSIS_DEPTH_KEY, ANALYSIS_PROVIDER_KEY];
  beforeEach(() => KEYS.forEach(k => localStorage.removeItem(k)));
  afterEach(() => KEYS.forEach(k => localStorage.removeItem(k)));
  const DEF = { linesCount: 3, engineOn: true, depthSetting: 22 };

  it('liest gültige Werte, behält sonst die Vorgabe', () => {
    const s = new AnalysisSettings();
    expect(s.load(DEF)).toEqual(DEF);
    s.saveLines(5); s.saveEngineOn(false); s.saveDepth(40); s.saveProvider('eei_a');
    expect(s.load(DEF)).toEqual({ linesCount: 5, engineOn: false, depthSetting: 40 });
    expect(s.loadProvider()).toBe('eei_a');
    localStorage.setItem(ANALYSIS_DEPTH_KEY, '41');
    localStorage.setItem(ANALYSIS_LINES_KEY, 'x');
    expect(s.load(DEF)).toEqual({ linesCount: 3, engineOn: false, depthSetting: 22 });
  });
});
