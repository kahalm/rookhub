import { FORMAT_LOCALES, SUPPORTED_LANGS } from './core/locale.service';

/**
 * Sprachdateien-Parität (periodische Aufgabe „Übersetzungen prüfen", jetzt als Test):
 * en ist die Quelle. Die gepflegten Sprachen (FORMAT_LOCALES: de, hr) müssen exakt dieselben
 * Keys tragen — mit identischen {{Platzhaltern}} und ohne leere Werte. Alle SUPPORTED_LANGS
 * müssen valides JSON sein und dürfen keine Keys haben, die en nicht kennt (veraltete Reste
 * gelöschter Features). Karma serviert `public/` als Assets → fetch('/i18n/<lang>.json').
 */
type Flat = Record<string, string>;

function flatten(obj: unknown, prefix = '', out: Flat = {}): Flat {
  if (obj && typeof obj === 'object') {
    for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
      const key = prefix ? `${prefix}.${k}` : k;
      if (v && typeof v === 'object') flatten(v, key, out);
      else out[key] = String(v);
    }
  }
  return out;
}

async function load(lang: string): Promise<Flat> {
  // Angular-Karma serviert Assets je nach Version unter / oder /base/.
  for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
    const res = await fetch(url);
    if (res.ok) return flatten(await res.json());
  }
  throw new Error(`${lang}.json nicht ladbar`);
}

const placeholders = (s: string): string =>
  [...s.matchAll(/\{\{\s*([^}]+?)\s*\}\}/g)].map(m => m[1]).sort().join('|');

describe('i18n Sprachdateien', () => {
  let en: Flat;
  beforeAll(async () => { en = await load('en'); });

  /** Bewusst leere en-Werte: Suffixe, die es im Englischen nicht gibt (de „14:00 Uhr" → en „14:00"). */
  const INTENTIONALLY_EMPTY_EN = new Set(['weekly.oClock']);

  it('en ist die Quelle: viele Keys, keine (unbeabsichtigt) leeren Werte', () => {
    expect(Object.keys(en).length).toBeGreaterThan(1000);
    const empty = Object.entries(en).filter(([k, v]) => !v.trim() && !INTENTIONALLY_EMPTY_EN.has(k)).map(([k]) => k);
    expect(empty).toEqual([]);
  });

  for (const lang of FORMAT_LOCALES.filter(l => l !== 'en')) {
    it(`${lang} hat exakt die Keys von en`, async () => {
      const l = await load(lang);
      const missing = Object.keys(en).filter(k => !(k in l));
      const extra = Object.keys(l).filter(k => !(k in en));
      expect(missing).withContext(`${lang}: fehlende Keys`).toEqual([]);
      expect(extra).withContext(`${lang}: Keys ohne en-Gegenstück`).toEqual([]);
    });

    it(`${lang}: Platzhalter wie in en, keine leeren Werte`, async () => {
      const l = await load(lang);
      const empty = Object.entries(l).filter(([, v]) => !v.trim()).map(([k]) => k);
      const phDiff = Object.keys(en).filter(k => k in l && placeholders(en[k]) !== placeholders(l[k]));
      expect(empty).withContext(`${lang}: leere Werte`).toEqual([]);
      expect(phDiff).withContext(`${lang}: {{Platzhalter}} weichen von en ab`).toEqual([]);
    });
  }

  it('alle unterstützten Sprachen sind valides JSON ohne veraltete Keys', async () => {
    for (const lang of SUPPORTED_LANGS) {
      const l = await load(lang);
      const stale = Object.keys(l).filter(k => !(k in en));
      expect(stale).withContext(`${lang}: Keys, die en nicht kennt`).toEqual([]);
    }
  });
  it('kennt zu jedem Schnellstart-Eintrag Titel und Beschreibung (alle gepflegten Sprachen)', async () => {
    // `quickstartItems` baut die i18n-Keys aus dem `key` zusammen (`app.qs.<key>Title|Desc`) —
    // ein neuer Eintrag ohne Texte fiele sonst erst im UI als roher Schlüssel auf.
    const keys = ['random', 'mate', 'endless', 'daily', 'weekly'];
    for (const lang of ['en', ...FORMAT_LOCALES.filter(l => l !== 'en')]) {
      const flat = await load(lang);
      for (const key of keys) {
        expect(flat[`app.qs.${key}Title`]).withContext(`${lang}: app.qs.${key}Title`).toBeTruthy();
        expect(flat[`app.qs.${key}Desc`]).withContext(`${lang}: app.qs.${key}Desc`).toBeTruthy();
      }
    }
  });

});
