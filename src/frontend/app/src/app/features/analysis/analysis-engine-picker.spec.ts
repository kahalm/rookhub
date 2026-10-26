import { of } from 'rxjs';
import { AnalysisEnginePicker, AnalysisEnginePickerHost } from './analysis-engine-picker';

const ENGINES = [
  { id: 'eei_a', name: 'Stockfish PC' }, { id: 'eei_bg', name: 'Hintergrund' },
] as any[];

function make(stored: string | null = null, compare: any = { on: false, engine: null, engineId: null, start: jasmine.createSpy('start') }) {
  const engine: any = { setRemoteEngine: jasmine.createSpy('setRemoteEngine') };
  const externalEngines: any = {
    listEngines: () => of({ engines: ENGINES, backgroundEngineIds: ['eei_bg'] }),
    analyse: jasmine.createSpy('analyse'),
  };
  const settings: any = { loadProvider: () => stored, saveProvider: jasmine.createSpy('saveProvider') };
  const runMain = jasmine.createSpy('runMain');
  const host: AnalysisEnginePickerHost = {
    engine, externalEngines, settings, compare: () => compare,
    t: k => k, currentLang: () => 'de', remoteFallback: () => false,
    mainShouldRun: () => true, runMain, changed: () => {},
  };
  return { picker: new AnalysisEnginePicker(host), engine, settings, runMain, compare };
}

describe('AnalysisEnginePicker', () => {
  it('ohne gemerkte Wahl bleibt der Browser; Hintergrund-Engines fehlen im Picker', () => {
    const { picker, engine } = make();
    picker.load();
    expect(picker.selectedId).toBe('wasm');
    expect(picker.list.map(e => e.id)).toEqual(['eei_a']);
    expect(picker.backgroundIds).toEqual(['eei_bg']);
    expect(picker.hasExternal).toBeTrue();
    expect(engine.setRemoteEngine).not.toHaveBeenCalled();
    expect(picker.choices.map(c => c.id)).toEqual(['wasm', 'eei_a']);
    expect(picker.mainName).toBe('analysis.engineBrowser');
  });

  it('die gemerkte Wahl wird verdrahtet und rechnet sofort', () => {
    const { picker, engine, runMain } = make('eei_a');
    picker.load();
    expect(picker.selectedId).toBe('eei_a');
    expect(engine.setRemoteEngine.calls.mostRecent().args[0].id).toBe('eei_a');
    expect(runMain).toHaveBeenCalled();
    expect(picker.mainName).toBe('Stockfish PC');
  });

  it('die Engine eines Auftrags gilt einmalig, auch eine Hintergrund-Engine', () => {
    const { picker } = make('eei_a');
    picker.requestedId = 'eei_bg';
    picker.load();
    expect(picker.selectedId).toBe('eei_bg');
    expect(picker.requestedId).toBeNull();
    expect(picker.list.map(e => e.id)).toContain('eei_bg');
  });

  it('Auswahl wird gemerkt; kollidiert sie mit dem Vergleich, weicht dieser aus', () => {
    const compare = { on: true, engine: {}, engineId: 'eei_a', start: jasmine.createSpy('start') };
    const { picker, settings } = make(null, compare);
    picker.load();
    picker.selectedId = 'eei_a';
    picker.select();
    expect(settings.saveProvider).toHaveBeenCalledWith('eei_a');
    expect(compare.start).toHaveBeenCalledTimes(1);
  });
});
