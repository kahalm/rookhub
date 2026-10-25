import { RepertoireLinesComponent } from '../repertoire/repertoire-lines.component';

/**
 * Design-Runde d3 (Training): Texte und Vorlagen, die sich nicht an einer einzelnen Komponente festmachen lassen.
 * Sprachdateien und Quelltexte liefert Karma aus (Muster i18n-parity.spec / i18n-usage.spec).
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

async function lang(code: string): Promise<Flat> {
  for (const url of [`/i18n/${code}.json`, `/base/i18n/${code}.json`]) {
    const res = await fetch(url);
    if (res.ok) return flatten(await res.json());
  }
  throw new Error(`${code}.json nicht ladbar`);
}

async function source(path: string): Promise<string> {
  const res = await fetch(`/base/${path}`);
  if (!res.ok) throw new Error(`${path} nicht ausgeliefert`);
  return res.text();
}

describe('Design-Runde d3 · Training (Texte und Vorlagen)', () => {
  // d3-rk-puzzle-button-case-inconsistent
  it('englische Puzzle-/Endlos-Knöpfe in Satzschreibung', async () => {
    const en = await lang('en');
    const expected: Record<string, string> = {
      'puzzles.actions.giveUp': 'Give up', 'puzzles.actions.nextPuzzle': 'Next puzzle',
      'puzzles.actions.share': 'Share puzzle', 'puzzles.actions.endlessMode': 'Endless mode',
      'puzzles.eval.show': 'Show eval', 'puzzles.eval.hide': 'Hide eval',
      'endless.config.newGame': 'New game', 'endless.gameOver.playAgain': 'Play again',
      'endless.config.viewHistory': 'View history', 'endless.history.startPlaying': 'Start playing',
    };
    for (const [k, v] of Object.entries(expected)) expect(en[k]).withContext(k).toBe(v);
  });

  // d3-rk-bewertung-anzeigen-beschriftung
  it('„Bewertung anzeigen“ sagt, welche Bewertung (Stellung, nicht Aufgaben-Wertung)', async () => {
    const de = await lang('de');
    expect(de['puzzles.eval.show']).toBe('Stellungsbewertung anzeigen');
    expect(de['puzzles.eval.hide']).toBe('Stellungsbewertung ausblenden');
  });

  // d3-rk-kurskarte-neu-holen-chip
  it('Kurs-Chip beschreibt einen Zustand, keine Aktion', async () => {
    expect((await lang('de'))['courses.reimport.chip']).toBe('Import veraltet');
    expect((await lang('en'))['courses.reimport.chip']).toBe('Import outdated');
  });

  // d3-rk-endless-doppelte-kennzahlen
  it('Endlos: Statistik-Karte ohne zweites Rating/Level/Herzen, Zeit heißt „Sitzung“', async () => {
    const html = await source('src/app/features/puzzles/endless-puzzle.component.html');
    const card = html.slice(html.indexOf('class="stats-card"'), html.indexOf('class="depth-control"'));
    expect(card).not.toContain('endless.game.statRating');
    expect(card).not.toContain('endless.game.statLevel');
    expect(card).not.toContain('lives-display');
    expect(card).toContain('endless.game.statSolved');
    expect((await lang('de'))['endless.game.statTime']).toBe('Sitzung');
    expect((await lang('en'))['endless.game.statTime']).toBe('Session');
  });

  // d3-rk-flaggen-symbol-doppelt (alle drei Puzzle-Modi; im Buch-Löser steht die Hinweiszeile seit Z18 in book-board-hint)
  for (const file of ['puzzle', 'book-board-hint', 'endless-puzzle']) {
    it(`${file}: „Tipps melden“ trägt kein Fähnchen (das gehört „Aufgeben“) und einen Namen per appIconLabel`, async () => {
      const html = await source(`src/app/features/puzzles/${file}.component.html`);
      const at = html.indexOf('<button class="board-hint-flag"');
      expect(at).toBeGreaterThan(-1);
      const btn = html.slice(at, html.indexOf('</button>', at));
      expect(btn).toContain('[appIconLabel]');
      expect(btn).toContain('thumb_down');
      expect(btn).not.toMatch(/'(outlined_)?flag'/);
    });
  }

  // d3-rk-info-abzeichen-kontrast-dunkel
  it('Linienliste: „Info“-Abzeichen aus Tokens statt festem Grau', () => {
    const css = ((RepertoireLinesComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles ?? []).join('\n')
      .replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s/g, '');
    const rule = /\.sr-badge\.info\{([^}]*)\}/.exec(css)![1];
    expect(rule).toContain('color:var(--mat-sys-on-surface-variant)');
    expect(rule).not.toContain('#616161');
  });
});
