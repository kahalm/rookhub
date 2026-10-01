import { FORMAT_LOCALES, SUPPORTED_LANGS } from './core/locale.service';
import { SOLVER_ACTION_KEYS, SOLVER_EVAL_KEYS } from './features/puzzles/solver-actions.util';

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
  /**
   * Codereview F2-012: Die Löse-Knöpfe (Zurücksetzen, Mausrutscher, Aufgeben, Bewertung an/aus) sind in
   * Standard, Endless und Buch dieselben. Endless hatte eigene Keys, die in de/hr nie übersetzt wurden
   * („Reset", „Mouseslip", „Give Up", „Show Eval"), das Buch eigene mit anderem Wort („Mausverrutscher").
   * Jeder Key, den ein Modus für diese Knöpfe holt, existiert und ist in den gepflegten Sprachen übersetzt.
   */
  for (const lang of FORMAT_LOCALES.filter(l => l !== 'en')) {
    it(`${lang}: Löse-Knöpfe aller drei Modi übersetzt`, async () => {
      const l = await load(lang);
      const keys = new Set<string>();
      for (const mode of ['standard', 'endless', 'book'] as const) {
        const a = SOLVER_ACTION_KEYS[mode];
        const e = SOLVER_EVAL_KEYS[mode];
        // start/now („Start“) dürfen wie en lauten — nur die Knopf-Beschriftungen müssen abweichen.
        [a.reset, a.mouseslip, a.giveUp, e.show, e.hide].forEach(k => keys.add(k));
        [e.start, e.now].forEach(k => expect(l[k]).withContext(`${lang}: ${k} fehlt`).toBeTruthy());
      }
      const untranslated = [...keys].filter(k => !l[k] || l[k] === en[k]).map(k => `${k} = „${l[k]}“`);
      expect(untranslated).withContext(`${lang}: Löse-Knöpfe fehlen oder stehen auf Englisch`).toEqual([]);
    });
  }

  /**
   * Codereview UX-006: Endless sprach in der deutschen und kroatischen Oberfläche Englisch („Endless Puzzle
   * Mode“, „Start Rating“, „Stockfish Depth“, „Themes (optional)“, „Game Over“, „Max Rating“). Kein
   * endless.*-Text einer gepflegten Sprache steht wortgleich wie en — außer den Einträgen unten, die in
   * de/hr/hu genauso lauten (Lehnwörter, Kürzel, reine Platzhalter).
   */
  const ENDLESS_SAME_AS_EN_OK = new Set([
    'endless.config.auto', 'endless.config.phase1Label', 'endless.config.phase2Label', 'endless.config.phase3Label',
    'endless.config.curvePuzzle', 'endless.config.modeNormal', 'endless.config.highscore', 'endless.config.start',
    'endless.game.levelRange', 'endless.game.statRating', 'endless.game.statLevel', 'endless.history.colEloDelta',
  ]);
  for (const lang of FORMAT_LOCALES.filter(l => l !== 'en')) {
    it(`${lang}: Endless-Texte übersetzt (nicht wortgleich wie en)`, async () => {
      const l = await load(lang);
      const english = Object.keys(en)
        .filter(k => k.startsWith('endless.') && !ENDLESS_SAME_AS_EN_OK.has(k) && l[k] === en[k])
        .map(k => `${k} = „${l[k]}“`);
      expect(english).withContext(`${lang}: endless.* steht auf Englisch`).toEqual([]);
    });
  }

  /**
   * Codereview F8-010: Gleiche Knopftexte standen je Feature neu (book.actions.next, worksheets.solve.next,
   * games.mistakes.next … neben common.next) und laufen dann auseinander — derselbe Mausrutscher-Knopf hieß
   * „Mausrutscher“, „Mouseslip“ und „Mausverrutscher“. Ein Key außerhalb von common.*, der in allen gepflegten
   * Sprachen wortgleich einen common.*-Text trägt, ist ein Doppel: stattdessen den common.*-Key benutzen.
   */
  it('kein Key außerhalb von common.* doppelt wortgleich einen common.*-Text', async () => {
    const all = [en, ...(await Promise.all(FORMAT_LOCALES.filter(l => l !== 'en').map(load)))];
    const common = Object.keys(en).filter(k => k.startsWith('common.'));
    const doubles = Object.keys(en)
      .filter(k => !k.startsWith('common.'))
      .map(k => [k, common.find(c => all.every(l => l[k] === l[c]))] as const)
      .filter(([, c]) => !!c)
      .map(([k, c]) => `${k} = ${c}`);
    expect(doubles).withContext('Doppel eines common.*-Texts — common.* verwenden').toEqual([]);
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

  /**
   * Rechtstexte (Codereview UX-022): Datenschutzerklaerung, Loeschseite und die Loesch-Karte im Profil (deren
   * Warnung vor der Passwort-Eingabe nennt seit UX-021 Kurse, Partien, Aufgabenblaetter usw., die Juni-Uebersetzungen
   * nur „Identitaet und persoenliche Daten“). Die gepflegten Sprachen tragen jeden Schluessel und denselben „Stand“
   * wie en. Die uebrigen Sprachen fallen per Schluessel-Luecke auf en zurueck und behalten nur Uebersetzungen von
   * en-Texten, die sich seit dem Uebersetzen nicht geaendert haben: bis 2026-10 stand dort „Stand: 3. Juni“ und zu
   * chess.com „keine automatische Datenuebertragung“, waehrend en den 6-Stunden-Abruf beschrieb. Aendert sich einer
   * der en-Texte unten, schlaegt der Test an — dann die Uebersetzungen dieses Schluessels in den uebrigen Sprachen
   * loeschen und den Eintrag streichen (es gilt en), oder alle neu uebersetzen und den Fingerabdruck nachziehen.
   */
  describe('Rechtstexte (legal.privacy, legal.accountDeletion, profile.delete)', () => {
    const isLegal = (k: string) =>
      k.startsWith('legal.privacy.') || k.startsWith('legal.accountDeletion.') || k.startsWith('profile.delete.');

    /** FNV-1a (32 bit) ueber den en-Text — der Stand, von dem aus die uebrigen Sprachen uebersetzt sind. */
    const fingerprint = (s: string): string => {
      let h = 0x811c9dc5;
      for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193) >>> 0;
      return h.toString(16).padStart(8, '0');
    };

    /** Was die nicht gepflegten Sprachen uebersetzt haben duerfen, mit dem Fingerabdruck des en-Texts von damals. */
    const TRANSLATED_FROM_EN: Readonly<Record<string, string>> = {
      'legal.privacy.title': '2526541f', 'legal.privacy.intro': '11947df0',
      'legal.privacy.controllerTitle': 'a15e8ea1', 'legal.privacy.controller': 'f991e755',
      'legal.privacy.dataTitle': 'df172352', 'legal.privacy.dataIntro': 'f6c135d3',
      'legal.privacy.dataAccount': '07a9ced8', 'legal.privacy.dataProfile': 'acf57914',
      'legal.privacy.dataUsage': 'e3661c7e', 'legal.privacy.dataTechnical': '22f3e113',
      'legal.privacy.purposesTitle': '8f067ecb', 'legal.privacy.purposes': '74d5163f',
      'legal.privacy.thirdTitle': '0883fee4', 'legal.privacy.thirdIntro': '66056906',
      'legal.privacy.thirdDiscord': 'd11a95bf', 'legal.privacy.thirdChessresults': 'c7a5e460',
      'legal.privacy.thirdLogging': '1c02fd53', 'legal.privacy.thirdHosting': '6325d96e',
      'legal.privacy.storageTitle': 'c9258e8d', 'legal.privacy.storage': 'ca3d4776',
      'legal.privacy.retentionTitle': 'f2846ea7', 'legal.privacy.retention': '731855af',
      'legal.privacy.rightsTitle': 'cccb11a5', 'legal.privacy.rights': '76ad9910',
      'legal.privacy.contactTitle': '73ac94c3', 'legal.privacy.contact': '36e2a115',
      'legal.privacy.back': '78247cd6',
      'legal.accountDeletion.title': '76a85421', 'legal.accountDeletion.intro': '815ca664',
      'legal.accountDeletion.inAppTitle': '3220e42f', 'legal.accountDeletion.removedTitle': '835d70fd',
      'legal.accountDeletion.removed1': '2d56c130', 'legal.accountDeletion.removed2': '81be7724',
      'legal.accountDeletion.keptTitle': '5398635d', 'legal.accountDeletion.kept': 'aef76a8e',
      'legal.accountDeletion.contactTitle': '73ac94c3', 'legal.accountDeletion.contact': '2d120dae',
      'legal.accountDeletion.back': '78247cd6',
      // profile.delete.warn fehlt bewusst: en seit UX-021 vollstaendig, die Juni-Uebersetzungen nicht (es gilt en).
      'profile.delete.title': '76a85421', 'profile.delete.hint': 'a5ce1ca7', 'profile.delete.button': '76a85421',
      'profile.delete.password': '2cc30838', 'profile.delete.confirm': 'd8100383',
      'profile.delete.deleting': '32be65e1', 'profile.delete.done': '24eca02e',
      'profile.delete.wrongPassword': '3024a25f', 'profile.delete.failed': '7639131d',
      'profile.delete.moreInfo': '8d527148',
    };

    /** Monatsnamen (Wortstaemme, klein) der gepflegten Sprachen — hr im Genitiv („30. rujna“). */
    const MONTHS: Readonly<Record<string, readonly string[]>> = {
      en: ['january', 'february', 'march', 'april', 'may', 'june', 'july', 'august', 'september', 'october', 'november', 'december'],
      de: ['januar', 'februar', 'märz', 'april', 'mai', 'juni', 'juli', 'august', 'september', 'oktober', 'november', 'dezember'],
      hr: ['siječ', 'veljač', 'ožuj', 'trav', 'svib', 'lip', 'srp', 'kolovoz', 'ruj', 'listopad', 'studen', 'prosin'],
      hu: ['január', 'február', 'március', 'április', 'május', 'június', 'július', 'augusztus', 'szeptember', 'október', 'november', 'december'],
    };
    /** „Stand: 30. September 2026“ → „2026-9-30“; Tag, Monat oder Jahr nicht erkannt → NaN/0 im Ergebnis. */
    const legalDate = (lang: string, text: string): string => {
      const nums = (text.match(/\d+/g) ?? []).map(Number);
      const lower = text.toLowerCase();
      const month = (MONTHS[lang] ?? []).findIndex(m => lower.includes(m)) + 1;
      return `${nums.find(n => n > 1000)}-${month}-${nums.find(n => n >= 1 && n <= 31)}`;
    };

    it('die en-Texte, von denen aus uebersetzt wurde, sind unveraendert', () => {
      const changed = Object.entries(TRANSLATED_FROM_EN)
        .filter(([k, fp]) => !(k in en) || fingerprint(en[k]) !== fp)
        .map(([k]) => `${k} (jetzt ${k in en ? fingerprint(en[k]) : 'geloescht'})`);
      expect(changed).withContext('en geaendert: Uebersetzungen loeschen oder neu uebersetzen').toEqual([]);
    });

    for (const lang of FORMAT_LOCALES.filter(l => l !== 'en')) {
      it(`${lang}: jeder Rechtstext-Schluessel von en, derselbe Stand`, async () => {
        const l = await load(lang);
        const missing = Object.keys(en).filter(k => isLegal(k) && !(k in l));
        expect(missing).withContext(`${lang}: fehlende Rechtstexte`).toEqual([]);
        expect(MONTHS[lang]).withContext(`${lang}: Monatsnamen fuer den Stand fehlen`).toBeDefined();
        expect(legalDate(lang, l['legal.privacy.updated'] ?? ''))
          .withContext(`${lang}: legal.privacy.updated „${l['legal.privacy.updated']}“`)
          .toBe(legalDate('en', en['legal.privacy.updated']));
      });
    }

    it('die uebrigen Sprachen tragen keinen eigenen Stand und keine veralteten Rechtstexte', async () => {
      expect(legalDate('en', en['legal.privacy.updated'])).toMatch(/^\d{4}-([1-9]|1[0-2])-\d{1,2}$/);
      for (const lang of SUPPORTED_LANGS.filter(l => !FORMAT_LOCALES.includes(l))) {
        const l = await load(lang);
        const stale = Object.keys(l).filter(k => isLegal(k) && !(k in TRANSLATED_FROM_EN));
        expect(stale).withContext(`${lang}: Rechtstexte ohne aktuellen en-Stand (loeschen, dann gilt en)`).toEqual([]);
      }
    });
  });

});
