import { FORMAT_LOCALES } from '../core/locale.service';

/**
 * Design-Runde d4 · Partien/Texte (DESIGN 6, 5.13): Prüfungen über Sprachdateien und Quelltexte, die keiner einzelnen
 * Komponente gehören. Sprachdateien liefert Karma unter /i18n/, Quelltexte über `__karma__.files` (wie
 * core/date-format-usage.spec.ts).
 */
type Flat = Record<string, string>;
type Served = { __karma__?: { files?: Record<string, unknown> } };

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
  for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
    const res = await fetch(url);
    if (res.ok) return flatten(await res.json());
  }
  throw new Error(`${lang}.json nicht ladbar`);
}

async function source(suffix: string): Promise<string> {
  const served = (globalThis as Served).__karma__?.files ?? {};
  const url = Object.keys(served).find(u => u.endsWith(suffix));
  expect(url).withContext(`${suffix} wird von Karma nicht ausgeliefert`).toBeDefined();
  return (await fetch(url!)).text();
}

/** Eigennamen und Abkürzungen, die im Englischen groß bleiben. */
const PROPER = new Set(['RookHub', 'Lichess', 'Lc0', 'Stockfish', 'Chessable', 'Discord', 'FIDE', 'PGN', 'FEN', 'Elo',
  'ChessBase', 'KidHub', 'LeagueHub', 'ClubHub', 'Maia', 'Woodpecker', 'Chess.com', 'API', 'PWA', 'APK', 'QR', 'EPUB',
  'PDF', 'I', 'ID', 'GitHub', 'Android', 'iOS', 'OK', 'Esc', 'E-Book', 'E-Books']);

/** Wörter nach dem ersten, die groß geschrieben sind und kein Eigenname — Title Case. */
function titleCaseWords(v: string): string[] {
  const words = v.match(/[A-Za-z][\w’'.-]*/g) ?? [];
  return words.slice(1).filter(w => /^[A-Z]/.test(w) && !PROPER.has(w));
}

describe('Design-Runde d4 · Partien/Texte', () => {
  let en: Flat;
  beforeAll(async () => { en = await load('en'); });

  // d4-rk-en-title-case-labels [d3 nicht behoben]: 0.807.0 hatte nur die Puzzle- und Endlos-Schlüssel umgestellt, kein
  // Test hielt die übrigen fest. Menüeinträge, Startseiten-Links und Knopf-/Link-Schlüssel stehen in Satzschreibung.
  it('englische Menüeinträge, Startseiten-Links und Knöpfe in Satzschreibung (DESIGN 6)', () => {
    expect(en['dashboard.puzzles.solve']).toBe('Solve puzzles');
    expect(en['nav.puzzleHistory']).toBe('Puzzle history');
    expect(en['nav.trainingGoals']).toBe('Training goals');
    expect(en['legal.privacy.link']).toBe('Privacy policy');
    // Seitentitel darf Title Case behalten.
    expect(en['legal.privacy.title']).toBe('Privacy Policy');
    const isLabel = (k: string) => /^(nav|dashboard)\.|(^|\.)(actions|button|menu|label|link)(\.|$)/.test(k);
    const bad = Object.entries(en)
      .filter(([k, v]) => isLabel(k) && v.length <= 40 && !/[.?!]$/.test(v) && titleCaseWords(v).length > 0)
      .map(([k, v]) => `${k} = ${v}`);
    expect(bad).toEqual([]);
  });

  // d4-rk-en-analyse-analyze-uneinheitlich: dieselbe Aktion hieß „Analyse game“ und „Analyze“. Britisch wie die Mehrheit.
  it('englisch durchgehend britisch „analyse/analysed“ (Platzhalter ausgenommen)', () => {
    const bad = Object.entries(en)
      .filter(([, v]) => /analyz/i.test(v.replace(/\{\{[^}]*\}\}/g, '')))
      .map(([k, v]) => `${k} = ${v}`);
    expect(bad).toEqual([]);
    expect(en['dashboard.analysis.subtitle']).toBe('Analyse a position');
    expect(en['puzzles.actions.analyze']).toBe('Analyse');
  });

  // d4-rk-beschriftung-uhr-symbol-hinweis: der Leerhinweis schickte zu einem Uhr-Symbol, das es am Brett nicht gibt —
  // die Aktion steht im ⋮ der Stellung.
  it('Analyse-Aufträge: Leerhinweis und Hilfe nennen das ⋮ der Stellung statt eines Uhr-Symbols (de/en/hr/hu)', async () => {
    for (const lang of FORMAT_LOCALES) {
      const all = lang === 'en' ? en : await load(lang);
      for (const key of ['analysisJobs.emptyHint', 'help.s.analysis.p.1']) {
        expect(all[key]).withContext(`${lang} ${key}`).toContain('⋮');
        expect(all[key]).withContext(`${lang} ${key}`).not.toMatch(/clock|Uhr-Symbol|ikon\w* sata|óra ikon/i);
      }
      expect(all['analysisJobs.emptyHint']).withContext(lang).toContain(all['analysis.queueBackground']);
    }
  });

  // d4-rk-games-engine-hinweis-jargon u. a.: die Partienliste nutzte den Punktepartie-Text („eingeworfene Partien“).
  it('Partienliste ohne Engine: eigener Satz, nennt die Genauigkeit, kein Punktepartie-Fachwort (de/en/hr/hu)', async () => {
    for (const lang of FORMAT_LOCALES) {
      const all = lang === 'en' ? en : await load(lang);
      const t = all['games.listNoEngine'];
      expect(t).withContext(lang).toBeTruthy();
      expect(t).withContext(lang).not.toBe(all['guess.upload.noEngine']);
      expect(t).withContext(lang).toContain(' —');
    }
    expect(en['games.listNoEngine']).toContain('Accuracy');
    expect(en['games.listNoEngine']).not.toContain('added games');
  });

  // d4-rk-dashboard-prozent-rundung: die Trefferquote stand im Puzzle-Löser ganzzahlig („79%“), auf Startseite,
  // Statistik und im Freundesvergleich mit einer Nachkommastelle („78.6%“).
  it('Puzzle-Löser: Trefferquote mit höchstens einer Nachkommastelle wie Startseite und Statistik', async () => {
    const html = await source('/features/puzzles/puzzle.component.html');
    expect(html).toContain('stats.accuracy | pct:1');
    expect(html).not.toMatch(/stats\.accuracy \| pct \}\}/);
  });
});
