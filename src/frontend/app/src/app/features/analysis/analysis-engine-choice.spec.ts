import { EngineChoiceList, pickEngineAfterLoad, speedHintText } from './analysis-engine-choice';
import { ExternalEngineInfo, ExternalEnginesResponse } from './external-engine.service';

const eng = (id: string): ExternalEngineInfo => ({ id, name: 'N' + id, maxThreads: 1, maxHash: 16 }) as ExternalEngineInfo;
const resp = (ids: string[], bg: string[] = []): ExternalEnginesResponse =>
  ({ hasCredentials: true, tokenInvalid: false, engines: ids.map(eng), backgroundEngineIds: bg });

describe('pickEngineAfterLoad', () => {
  it('blendet Hintergrund-Engines aus und stellt die gemerkte wieder her', () => {
    const p = pickEngineAfterLoad(resp(['a', 'b'], ['b']), null, 'a');
    expect(p.list.map(e => e.id)).toEqual(['a']);
    expect([p.selected, p.fromRequest, p.hasExternalEngines]).toEqual(['a', false, true]);
    expect(pickEngineAfterLoad(resp(['a']), null, 'gone').selected).toBeNull();
    expect(pickEngineAfterLoad(resp(['a']), null, 'wasm').selected).toBeNull();
  });

  it('die Engine eines Auftrags gilt — auch eine Hintergrund-Engine', () => {
    const p = pickEngineAfterLoad(resp(['a', 'b'], ['b']), 'b', 'a');
    expect(p.list.map(e => e.id)).toEqual(['a', 'b']);
    expect([p.selected, p.fromRequest]).toEqual(['b', true]);
    expect(pickEngineAfterLoad(resp(['a']), 'x', null).fromRequest).toBeFalse();
    expect(pickEngineAfterLoad(resp([]), null, null).hasExternalEngines).toBeFalse();
  });
});

describe('EngineChoiceList', () => {
  it('memoisiert je Sprache und Liste', () => {
    const c = new EngineChoiceList();
    const list = [eng('a')];
    const first = c.get('de', list, () => 'Browser');
    expect(first.map(x => x.id)).toEqual(['wasm', 'a']);
    expect(c.get('de', list, () => 'X')).toBe(first);
    expect(c.get('en', list, () => 'Browser')).not.toBe(first);
  });
});

describe('speedHintText', () => {
  const t = (k: string, p?: object) => p ? `${k}:${JSON.stringify(p)}` : k;
  it('wartet, Kurzform, Langform', () => {
    expect(speedHintText(t, 'de', 0, 5)).toBe('analysis.speedWaiting');
    expect(speedHintText(t, 'de', 3450000, null)).toBe('analysis.speedShort:{"speed":"3.450 kN/s"}');
    expect(speedHintText(t, 'de', 3450000, 8234567)).toBe('analysis.speedHint:{"speed":"3.450 kN/s","nodes":"8.235 kN"}');
  });
});
