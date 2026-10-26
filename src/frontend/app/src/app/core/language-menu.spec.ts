import { I18N_COVERAGE } from './i18n-coverage';
import { languageMenu } from './language-menu';
import { FORMAT_LOCALES, LangOption, SUPPORTED_LANGS } from './locale.service';

/** Codereview F8-009: Sprachmenü in „gepflegt“ und „Teilweise übersetzt (NN %)“; Abdeckungsdatei passt zum Stand. */
describe('Sprachmenü mit Abdeckung (F8-009)', () => {
  const langs: LangOption[] = [
    { code: 'en', label: 'English' }, { code: 'de', label: 'Deutsch' }, { code: 'fr', label: 'Français' },
    { code: 'hr', label: 'Hrvatski' }, { code: 'ja', label: '日本語' }, { code: 'hu', label: 'Magyar' },
  ];

  it('gepflegte Sprachen oben in ihrer Reihenfolge, Teilsprachen darunter mit Prozent', () => {
    const m = languageMenu(langs, { en: 100, de: 100, hr: 100, hu: 100, fr: 17, ja: 23 });
    expect(m.full.map(l => l.code)).toEqual(['en', 'de', 'hr', 'hu']);
    expect(m.partial).toEqual([
      { code: 'fr', label: 'Français', coverage: 17 },
      { code: 'ja', label: '日本語', coverage: 23 },
    ]);
  });

  it('eine Sprache ohne Eintrag in der Abdeckung zählt als 0 %', () => {
    expect(languageMenu([{ code: 'fr', label: 'Français' }], {}).partial[0].coverage).toBe(0);
  });

  it('jede angebotene Sprache hat eine Abdeckung; die gepflegten stehen auf 100 %', () => {
    for (const l of SUPPORTED_LANGS) expect(I18N_COVERAGE[l]).withContext(l).toBeDefined();
    for (const l of FORMAT_LOCALES) expect(I18N_COVERAGE[l]).withContext(l).toBe(100);
  });

  // CI-Wache: die eingecheckte Datei (scripts/i18n-coverage.mjs) weicht höchstens 2 Punkte vom tatsächlichen Stand ab —
  // nicht jeder neue Schlüssel soll die Datei neu erzwingen, aber eine nachübersetzte Sprache muss sie mitbringen.
  it('i18n-coverage.ts passt zu den Sprachdateien (sonst: node scripts/i18n-coverage.mjs)', async () => {
    const flat = (o: unknown, p = '', out: string[] = []): string[] => {
      for (const [k, v] of Object.entries((o ?? {}) as Record<string, unknown>)) {
        const key = p ? `${p}.${k}` : k;
        if (v && typeof v === 'object') flat(v, key, out); else out.push(key);
      }
      return out;
    };
    const load = async (lang: string): Promise<string[]> => {
      for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
        const res = await fetch(url);
        if (res.ok) return flat(await res.json());
      }
      throw new Error(`${lang}.json nicht ladbar`);
    };
    const en = await load('en');
    const off: string[] = [];
    for (const lang of SUPPORTED_LANGS) {
      const have = new Set(await load(lang));
      const actual = Math.round((100 * en.filter(k => have.has(k)).length) / en.length);
      if (Math.abs(actual - I18N_COVERAGE[lang]) > 2) off.push(`${lang}: Datei ${I18N_COVERAGE[lang]} %, Stand ${actual} %`);
    }
    expect(off).withContext('node scripts/i18n-coverage.mjs ausführen').toEqual([]);
  });
});
