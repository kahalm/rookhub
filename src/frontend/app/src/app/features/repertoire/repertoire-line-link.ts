import { Observable, of } from 'rxjs';
import { map, tap } from 'rxjs/operators';
import type {
  PositionTreeNode, RepertoireLineMatch, RepertoirePositionMatch, RepertoirePositionTree,
} from '../../core/repertoire.service';
import { parsePgnText, splitPgnGames } from '../../shared/pgn-viewer/pgn-parser';
import { lineKeyFromSans } from './repertoire-line-key.util';

/**
 * Vom Treffer der Stellungssuche zum Link auf die Linie (Zerlegung Z32, aus `PositionRepertoiresComponent`): der
 * Ziel-`lineKey` wird bewusst aus dem CLIENT-Parse des Repertoire-PGN berechnet (identisch zu Trainer/Linienliste),
 * nicht aus Server-SAN — so ist die Linien-Identität garantiert konsistent. Ohne Angular.
 */

/** Kopfzeile `[Tag "Wert"]` eines PGN-Abschnitts (ohne zu parsen). */
function headerOf(raw: string, tag: string): string {
  const m = new RegExp('^\\[' + tag + '\\s+"((?:[^"\\\\]|\\\\.)*)"\\]', 'm').exec(raw);
  return m ? m[1].replace(/\\(.)/g, '$1').trim() : '';
}

/** Der Abschnitt einer gemeldeten Linie: am gemeldeten Index, wenn Kapitel ([Black]) und Linienname ([White]) passen,
 *  sonst der erste mit diesen Namen, sonst der Index. */
export function findSection(sections: string[], line: { chapter: string | null; lineName: string | null; gameIndex: number }): string | null {
  const matches = (raw: string) => headerOf(raw, 'Black') === (line.chapter ?? '') && headerOf(raw, 'White') === (line.lineName ?? '');
  const atIndex = sections[line.gameIndex];
  if (atIndex && matches(atIndex)) return atIndex;
  return sections.find(matches) ?? atIndex ?? null;
}

export interface ChapterGroup { name: string; lines: RepertoireLineMatch[]; }

/** Linien eines Treffers nach Kapitel gruppiert, in der Reihenfolge des ersten Auftretens. */
export function chapterGroups(rep: RepertoirePositionMatch): ChapterGroup[] {
  const groups: ChapterGroup[] = [];
  const byName = new Map<string, ChapterGroup>();
  for (const line of rep.lines) {
    const name = line.chapter || '';
    let g = byName.get(name);
    if (!g) { g = { name, lines: [] }; byName.set(name, g); groups.push(g); }
    g.lines.push(line);
  }
  return groups;
}

/** Baum-Ergebnis → Repertoire-Treffer (ohne Linien) für die Navigation. */
export function treeAsMatch(tree: RepertoirePositionTree): RepertoirePositionMatch {
  return {
    repertoireId: tree.repertoireId, repertoireName: tree.repertoireName,
    kind: tree.kind, shared: tree.shared, lines: [],
  };
}

/** Baum-Knoten → Linien-Treffer (ply -1: die Detailansicht springt dann an den Linienanfang). */
export function treeNodeAsLine(node: PositionTreeNode): RepertoireLineMatch {
  return {
    chapter: node.chapter ?? '', lineName: node.lineName ?? '',
    gameIndex: node.gameIndex ?? 0, ply: -1,
  };
}

/**
 * Lädt (gecacht) das Repertoire-PGN, findet die passende Linie und berechnet deren lineKey. Gelesen wird nur DIESE
 * Partie (0.712.0): vorher wurde das ganze PGN mit dem Vorgabe-Deckel (2 MB/500 Partien) geparst — in einem großen
 * Chessable-Repertoire (6 MB, 1220 Linien) fehlten damit alle Linien ab Nr. 275, der Link trug `line=` leer und die
 * Repertoire-Seite öffnete keine Linie. Getrennt wird wie beim Server (`splitPgnGames` ↔ `ParseSections`), also passt
 * der gemeldete `gameIndex`; Kapitel/Name bestätigen ihn, sonst wird nach ihnen gesucht.
 */
export class LineKeyResolver {
  private readonly cache = new Map<number, string[]>();   // die Abschnitte des PGN (splitPgnGames), nicht geparst

  constructor(private readonly api: () => { getPgnText(id: number): Observable<string> }) {}

  resolve(repId: number, line: RepertoireLineMatch): Observable<string> {
    const cached = this.cache.get(repId);
    const sections$ = cached
      ? of(cached)
      : this.api().getPgnText(repId).pipe(
          map(pgn => splitPgnGames(pgn)),
          tap(sections => this.cache.set(repId, sections)),
        );
    return sections$.pipe(map(sections => {
      const raw = findSection(sections, line);
      const g = raw ? parsePgnText(raw)[0] : undefined;
      return g ? lineKeyFromSans(g.moves.map(m => m.san)) : '';
    }));
  }
}
