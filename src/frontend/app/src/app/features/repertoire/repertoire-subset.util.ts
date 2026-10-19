import { RepertoireLine } from './repertoire-viewer.service';

/**
 * „Neues Repertoire aus Auswahl" (Wunsch 2026-10-19: „mehrere kapitel oder/und lines markieren und ein neues repertoir
 * daraus machen"). Reine Hilfen ohne Zustand: Kapitel-Gruppen für den Dialog und das PGN der gewählten Abschnitte.
 */

export interface SubsetChapter {
  /** Kapitel-Schlüssel wie im Trainer (`[Black]`, getrimmt) — leer = ohne Kapitel. */
  chapter: string;
  lines: RepertoireLine[];
}

/** Linien nach Kapitel, in der Reihenfolge ihres ersten Auftretens (wie die Linienliste). */
export function groupByChapter(lines: readonly RepertoireLine[]): SubsetChapter[] {
  const out: SubsetChapter[] = [];
  const byKey = new Map<string, SubsetChapter>();
  for (const l of lines) {
    const key = l.chapter ?? '';
    let g = byKey.get(key);
    if (!g) { g = { chapter: key, lines: [] }; byKey.set(key, g); out.push(g); }
    g.lines.push(l);
  }
  return out;
}

/**
 * Das PGN der gewählten Abschnitte: je Linie ihr ORIGINALTEXT aus dem Repertoire (mit Varianten, Kommentaren, [FEN],
 * Kapitel-Kopf) in der Reihenfolge des Repertoires; ein Abschnitt kommt nur einmal vor. Ohne Originaltext fällt die
 * Linie weg — dann zählt sie auch nicht mit (`count`).
 */
export function subsetPgn(rawGames: readonly (string | undefined)[], gameIndices: Iterable<number>): { pgn: string; count: number } {
  const picked = [...new Set(gameIndices)].filter(i => (rawGames[i] ?? '').trim().length > 0).sort((a, b) => a - b);
  const pgn = picked.map(i => rawGames[i]!.trim()).join('\n\n');
  return { pgn: pgn ? pgn + '\n' : '', count: picked.length };
}

/** Kapitel-Schlüssel der gewählten Linien (für die Übernahme der Kapitelfarben). */
export function chaptersOf(lines: readonly RepertoireLine[], gameIndices: ReadonlySet<number>): Set<string> {
  return new Set(lines.filter(l => gameIndices.has(l.gameIndex)).map(l => l.chapter ?? ''));
}
