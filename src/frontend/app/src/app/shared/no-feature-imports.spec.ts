/**
 * Codereview F8-013: `shared/` ist eine Schicht UNTER den Features — Geteiltes darf nichts aus `features/` importieren
 * (sonst zieht jede Oberfläche, die eine shared-Komponente nutzt, fremde Features mit, und Zyklen entstehen unbemerkt).
 * Erlaubt sind `core/`, `shared/`, `environments/` und Pakete. Die App-Kopfleiste liegt deshalb in `shell/`
 * (sie kennt Kurse, Katalog, Admin), `LEGAL_SITE` in `core/`, die Kapitelfarben in `shared/repertoire/`.
 *
 * Gelesen werden die Quelltexte wie in `i18n-usage.spec.ts`: karma.conf.js liefert `src*\/**\/*.ts` aus
 * (included: false), hier per fetch. Geprüft werden statische Importe, `export … from` und dynamische `import()`.
 *
 * {@link KNOWN_EXCEPTIONS} ist die abschließende Liste der heute noch offenen Importe — jede neue Stelle macht den
 * Test rot, und eine Ausnahme, die nicht mehr gebraucht wird, ebenfalls (die Liste soll nur schrumpfen).
 */
type Served = { __karma__?: { files?: Record<string, unknown> } };

/**
 * Datei unter `src/app/shared/` → die Feature-Module (Pfad ab `src/app/`, ohne Endung), die sie heute noch importiert.
 * Alle verbliebenen liegen in `features/games`, `features/analysis` und `features/puzzles`, die in der laufenden
 * Zerlegungs-Welle (2026-10-25) parallel von anderen Sitzungen umgebaut werden; ihr Umzug folgt danach:
 * - `pgn-viewer` ist im Kern der Partie-Betrachter von `features/games` (Rückblick, Bewertung, Analyse-Start) und
 *   gehört dorthin bzw. bekommt seine Typen aus einer gemeinsamen Datei;
 * - `game-replay` (Spielerkarte) bettet den Partie-Rückblick ein;
 * - `san.pipe`, `card-lang` und `line-solver` brauchen reine Zug-Helfer aus `features/puzzles`
 *   (`keyboard-move.util`, `puzzle-move.util`), die nach `shared/chess/` gehören.
 */
const KNOWN_EXCEPTIONS: Readonly<Record<string, readonly string[]>> = {
  'shared/pgn-viewer/pgn-viewer.component.ts': [
    'features/repertoire/position-repertoires.component',
    'features/games/game-review.component',
    'features/games/move-badge.util',
    'features/games/game-review.util',
    'features/games/analyze-game.service',
    'features/analysis/game-analysis.service',
  ],
  'shared/pgn-viewer/pgn-viewer.component.spec.ts': ['features/games/game-review.component'],
  'shared/player-card/game-replay.component.ts': ['features/games/game-review.component', 'features/games/move-badge.util'],
};

const IMPORT = /(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s+)(['"])([^'"]+)\1/g;

/** Pfad ab `src/app/` eines Importes aus `file` (ebenfalls ab `src/app/`), oder `null` für Pakete. */
export function resolveImport(file: string, spec: string): string | null {
  if (spec.startsWith('@rh/')) return spec.slice(4);
  if (!spec.startsWith('.')) return null;
  const parts = file.split('/').slice(0, -1);
  for (const seg of spec.split('/')) {
    if (seg === '..') parts.pop();
    else if (seg !== '.') parts.push(seg);
  }
  return parts.join('/');
}

/** Alle Importe einer Datei, die in `features/` landen (Pfad ab `src/app/`). */
export function featureImports(file: string, source: string): string[] {
  const out: string[] = [];
  for (const m of source.matchAll(IMPORT)) {
    const target = resolveImport(file, m[2]);
    if (target && target.startsWith('features/')) out.push(target);
  }
  return out;
}

describe('shared/ importiert nichts aus features/ (Codereview F8-013)', () => {
  it('resolveImport/featureImports erkennen relative, @rh- und dynamische Importe', () => {
    const file = 'shared/x/y.ts';
    expect(resolveImport(file, '../../features/a/b')).toBe('features/a/b');
    expect(resolveImport(file, '../z/w')).toBe('shared/z/w');
    expect(resolveImport(file, '@rh/features/a/b')).toBe('features/a/b');
    expect(resolveImport(file, '@angular/core')).toBeNull();
    const src = `import { A } from '../../features/a/a';\nexport * from "../../core/c";\nconst m = import('@rh/features/b/b');\nimport '../../features/c/side-effect';`;
    expect(featureImports(file, src)).toEqual(['features/a/a', 'features/b/b', 'features/c/side-effect']);
  });

  it('keine neuen Importe aus features/, keine veralteten Ausnahmen', async () => {
    const served = (globalThis as Served).__karma__?.files ?? {};
    // ohne diese Datei selbst (ihre Beispiel-Importe stehen in Strings)
    const urls = Object.keys(served).filter(u => /^\/base\/src\/app\/shared\/.+\.ts$/.test(u)
      && !u.endsWith('/shared/no-feature-imports.spec.ts'));
    expect(urls.length).withContext('karma.conf.js liefert die Quelltexte nicht aus').toBeGreaterThan(50);

    const found: Record<string, string[]> = {};
    await Promise.all(urls.map(async url => {
      const file = url.slice('/base/src/app/'.length);
      const hits = featureImports(file, await (await fetch(url)).text());
      if (hits.length) found[file] = [...new Set(hits)].sort();
    }));

    const unexpected = Object.entries(found)
      .flatMap(([file, hits]) => hits.filter(h => !(KNOWN_EXCEPTIONS[file] ?? []).includes(h)).map(h => `${file} → ${h}`));
    expect(unexpected).withContext('shared/ darf nicht aus features/ importieren — Helfer nach shared/ oder core/ ziehen').toEqual([]);

    const stale = Object.entries(KNOWN_EXCEPTIONS)
      .flatMap(([file, allowed]) => allowed.filter(h => !(found[file] ?? []).includes(h)).map(h => `${file} → ${h}`));
    expect(stale).withContext('Ausnahme wird nicht mehr gebraucht — aus KNOWN_EXCEPTIONS streichen').toEqual([]);
  });

  // core/ liegt noch unter shared/ (Specs dürfen Feature-Komponenten rendern). Einzige Ausnahme: die bewusst dynamischen
  // import() des Nachlaufs nach der Anmeldung (login-follow-up.service, dort begründet). Der Offline-Vorabruf der
  // Puzzle-Pools liegt deshalb in shell/, ThemeMode in core/theme-mode.ts.
  it('core/ (ohne Specs) importiert nichts aus features/', async () => {
    const served = (globalThis as Served).__karma__?.files ?? {};
    const urls = Object.keys(served).filter(u => /^\/base\/src\/app\/core\/.+\.ts$/.test(u) && !u.endsWith('.spec.ts'));
    expect(urls.length).withContext('karma.conf.js liefert die Quelltexte nicht aus').toBeGreaterThan(20);
    const hits = (await Promise.all(urls.map(async url => {
      const file = url.slice('/base/src/app/'.length);
      if (file === 'core/login-follow-up.service.ts') return [];
      return featureImports(file, await (await fetch(url)).text()).map(h => `${file} → ${h}`);
    }))).flat();
    expect(hits).toEqual([]);
  });
});
