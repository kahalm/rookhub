/**
 * Gestufte Tipps eines Lösungsversuchs (Zerlegung Z23), rein: wie viele Stufen zum aktuell erwarteten Zug
 * aufgedeckt sind (`level`, je Zug zurückgesetzt) und die höchste je auf dem Lösungsweg aufgedeckte Stufe
 * (`maxLevel`, gilt fürs ganze Puzzle und speist das Statistikfeld `hintsUsed`).
 */
export class HintLadder {
  /** Ab so vielen aufgedeckten Tipps zu EINEM Zug zählt das Rätsel als nicht gelöst (Nutzerwunsch 20.10.2026). */
  static readonly FAIL_LEVEL = 3;

  level = 0;
  maxLevel = 0;

  /** Welche Tipp-Liste gilt: abseits des Lösungswegs die „falsch abgebogen"-Hinweise, im e.p.-Zwang die
   *  Anarchy-Hinweise, sonst die normalen Tipps (leere Sonderlisten fallen durch). */
  static effective(onSolutionPath: boolean, offPath: string[], enPassantForced: boolean, epForced: string[],
                   normal: string[]): string[] {
    if (!onSolutionPath && offPath.length) return offPath;
    return enPassantForced && epForced.length ? epForced : normal;
  }

  shown(hints: string[]): string[] { return hints.slice(0, this.level); }

  canReveal(total: number): boolean { return this.level < total; }

  /** Nächste Stufe aufdecken; nur auf dem Lösungsweg zählt sie für `maxLevel` (abseits ist die Linie ohnehin
   *  vergeigt). */
  reveal(total: number, onSolutionPath: boolean): void {
    if (!this.canReveal(total)) return;
    this.level++;
    if (onSolutionPath && this.level > this.maxLevel) this.maxLevel = this.level;
  }

  /** Alle drei Tipps zu einem Zug geholt: der Zug war vorgesagt → gewertet wie ein Fehlversuch. */
  get failsSolve(): boolean { return this.maxLevel >= HintLadder.FAIL_LEVEL; }
}
