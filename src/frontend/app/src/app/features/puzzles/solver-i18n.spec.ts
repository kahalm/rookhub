/**
 * Codereview F8-010: derselbe Löser-Knopf/-Status stand je Modus unter eigenem Schlüssel (`puzzles.*`, `endless.*`,
 * `book.*`) und lief auseinander („Mausrutscher“/„Mouseslip“/„Mausverrutscher“, „Du bist dran!“/„Du bist am Zug!“).
 * Seit der Zusammenlegung gibt es dafür nur noch den Namensraum `solver.*`; diese Wache verhindert, dass ein Modus
 * wieder eine eigene Kopie anlegt. Sprachdateien liefert Karma aus (Muster i18n-parity.spec).
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

/** Frühere Einzel-Schlüssel je Modus (Auszug), die jetzt unter `solver.*` stehen. */
const RETIRED = [
  'puzzles.actions.reset', 'puzzles.actions.mouseslip', 'puzzles.actions.giveUp', 'puzzles.eval.show',
  'puzzles.hints.show', 'book.hints.show', 'puzzles.viz.show', 'endless.game.vizShow', 'book.viz.show',
  'puzzles.result.correct', 'endless.game.correct', 'book.status.correct', 'endless.game.yourTurn', 'book.status.yourTurn',
  'puzzles.actions.nextPuzzle', 'book.actions.nextPuzzle', 'endless.game.sharePuzzle', 'book.actions.sharePuzzle',
];

describe('Löser-Texte: ein Namensraum solver.* (F8-010)', () => {
  for (const code of ['en', 'de', 'hr', 'hu']) {
    it(`${code}: keine Modus-Kopien der Löser-Knöpfe mehr`, async () => {
      const l = await lang(code);
      expect(RETIRED.filter(k => k in l)).toEqual([]);
      expect(Object.keys(l).filter(k => k.startsWith('solver.')).length).toBeGreaterThan(25);
    });
  }

  it('de: eine Formulierung je Knopf', async () => {
    const de = await lang('de');
    expect(de['solver.actions.mouseslip']).toBe('Mausrutscher');
    expect(de['solver.actions.reset']).toBe('Zurücksetzen');
    expect(de['solver.status.findBestMove']).toBe('Du bist am Zug! Finde den besten Zug.');
    expect(de['solver.status.loading']).toBe('Puzzle wird geladen…');
  });
});
