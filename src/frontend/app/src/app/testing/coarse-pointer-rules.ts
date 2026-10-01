/**
 * Stilregeln innerhalb von `@media (pointer: coarse)` aus allen geladenen Stylesheets (globale styles.scss und die
 * Stile bereits erzeugter Komponenten). Headless-Chrome meldet einen feinen Zeiger, solche Regeln greifen im Test
 * also nie — geprüft wird deshalb, dass sie DA sind und was sie setzen (Codereview UX-007, Touch-Ziele ≥ 44 px).
 */
export function coarsePointerRules(): CSSStyleRule[] {
  const out: CSSStyleRule[] = [];
  for (const sheet of Array.from(document.styleSheets)) {
    let rules: CSSRuleList;
    try { rules = sheet.cssRules; } catch { continue; }   // fremde Herkunft: nicht lesbar
    for (const rule of Array.from(rules)) {
      if (!(rule instanceof CSSMediaRule) || !rule.media.mediaText.includes('pointer: coarse')) continue;
      for (const inner of Array.from(rule.cssRules)) {
        if (inner instanceof CSSStyleRule) out.push(inner);
      }
    }
  }
  return out;
}

/** Erste Regel unter grobem Zeiger, deren Selektorliste einen Teil-Selektor enthält, der ALLE Bruchstücke
 *  enthält (Komponenten-Stile tragen angehängte `[_ngcontent-…]`-Attribute, daher Bruchstücke statt Gleichheit). */
export function coarseRuleFor(...parts: string[]): CSSStyleRule | undefined {
  return coarsePointerRules().find(r =>
    r.selectorText.split(',').some(sel => parts.every(p => sel.includes(p))));
}
