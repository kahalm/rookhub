import { AnalysisNode } from './analysis-tree';

/**
 * Zugwahl beim Blättern (0.857.0, Wunsch 2026-10-27: „wenn ich mit Pfeiltasten durchnavigiere und an eine Stelle komme, an der
 * ich schon eine Linie eingegeben hab, bring ein kleines Popup mit next move, Zug der Partie vorausgewählt, mit Pfeiltasten
 * rauf/runter navigierbar, mit Leertaste, Enter und Pfeil nach rechts bestätigen"). Rein, ohne Angular: Hat die Stellung mehr als
 * eine Fortsetzung, öffnet → die Wahl statt still der Linie zu folgen; vorausgewählt ist `children[0]` (Hauptlinie = Zug der
 * Partie).
 */
export interface NextMoveChoice {
  /** Die Fortsetzungen in Baumreihenfolge — `[0]` ist die Hauptlinie. */
  readonly options: readonly AnalysisNode[];
  readonly index: number;
}

/** Wahl für die Stellung `node` — `null`, wenn der nächste Zug eindeutig ist (0 oder 1 Fortsetzung). */
export function openNextMoveChoice(node: AnalysisNode): NextMoveChoice | null {
  return node.children.length > 1 ? { options: node.children, index: 0 } : null;
}

export type NextMoveKeyResult =
  | { kind: 'select'; choice: NextMoveChoice }
  | { kind: 'pick'; node: AnalysisNode }
  | { kind: 'close' }
  | { kind: 'ignore' };

/** Eine Taste bei offener Wahl: ↑/↓ wählen (ringsum), →/Enter/Leertaste nehmen, ←/Esc schließen; alles andere schließt nicht. */
export function nextMoveKey(choice: NextMoveChoice, key: string): NextMoveKeyResult {
  const n = choice.options.length;
  switch (key) {
    case 'ArrowDown': return { kind: 'select', choice: { ...choice, index: (choice.index + 1) % n } };
    case 'ArrowUp': return { kind: 'select', choice: { ...choice, index: (choice.index - 1 + n) % n } };
    case 'ArrowRight': case 'Enter': case ' ': return { kind: 'pick', node: choice.options[choice.index] };
    case 'ArrowLeft': case 'Escape': return { kind: 'close' };
    default: return { kind: 'ignore' };
  }
}

/**
 * Nebenlinie wechseln (0.858.0, Wunsch 2026-10-27: „Pfeiltaste rauf/runter soll zwischen nebeneinanderliegenden Lines wechseln
 * (oder halt zurück zur Mainline)"): von `node` aufwärts die nächste Verzweigung, an der es in Richtung `dir` noch eine Linie
 * gibt (−1 = ↑ zur Hauptlinie hin, +1 = ↓ zur nächsten Variante), dort die Nachbarlinie nehmen und auf ihr so weit
 * vorgehen, wie man in der alten Linie hinter der Verzweigung stand (`children[0]`, soweit vorhanden). `null` = keine
 * Nachbarlinie.
 */
export function siblingLine(node: AnalysisNode, dir: -1 | 1): AnalysisNode | null {
  let depth = 0;
  for (let n: AnalysisNode = node; n.parent; n = n.parent, depth++) {
    const siblings = n.parent.children;
    const target = siblings[siblings.indexOf(n) + dir];
    if (!target) continue;
    let out = target;
    for (let i = 0; i < depth && out.children[0]; i++) out = out.children[0];
    return out;
  }
  return null;
}
