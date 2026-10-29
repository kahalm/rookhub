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
    expect(speedHintText(t, 'de', 3450000, null)).toBe('analysis.speedShort:{"speed":"3.450.000"}');
    expect(speedHintText(t, 'de', 3450000, 8234567)).toBe('analysis.speedHint:{"speed":"3.450.000","nodes":"8.234.567"}');
  });

  // d8-rk-engine-tooltip-kn-jargon: kein „kN“ — weder in den Zahlen noch in den Texten aller vier Sprachen; die Texte
  // nennen die Einheit in Worten.
  it('ohne „kN“-Jargon, Einheit in Worten (de/en/hr/hu)', async () => {
    const words: Record<string, RegExp> = { de: /Stellungen pro Sekunde/, en: /positions per second/,
      hr: /pozicija u sekundi/, hu: /másodpercenként/ };
    for (const lang of Object.keys(words)) {
      const res = await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`));
      const a = (await res.json()).analysis as Record<string, string>;
      const tr = (k: string, p?: object) =>
        a[k.replace('analysis.', '')].replace(/\{\{(\w+)\}\}/g, (_m, n) => (p as Record<string, string> | undefined)?.[n] ?? '');
      for (const text of [speedHintText(tr, lang, 201000, 337000), speedHintText(tr, lang, 201000, null)]) {
        expect(text).withContext(lang).not.toMatch(/kN/);
        expect(text).withContext(lang).toMatch(words[lang]);
      }
    }
  });
});
