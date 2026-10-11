/** Ein Stück der Einleitung einer Kurslinie: Zwischenüberschrift (`h`) oder Fließtext (`p`). */
export interface CourseTextBlock {
  kind: 'h' | 'p';
  text: string;
}

/** So lang darf eine Zwischenüberschrift höchstens sein. */
const HEADING_MAX = 80;

/**
 * Eine Überschrift, die beim Import ohne Trenner an den Satz danach geklebt wurde: „…mit dem König unterstützenSchauen
 * wir uns …". Gesucht wird ein Wort aus mindestens vier Buchstaben, das klein endet, direkt gefolgt von einem neuen
 * großgeschriebenen Wort. „dem"/„der" (drei Buchstaben) und Markennamen wie „ChessBase" bleiben stehen.
 */
const GLUED = /(?<!\p{L})(\p{Lu}\p{Ll}{3,}|\p{Ll}{4,})(?=\p{Lu}\p{Ll})/gu;
const GLUE_KEEP = new Set(['Chess', 'You', 'Play']);
/** Satzende (nicht „Abschnitt 5." — Ziffer davor) vor einer angeklebten Überschrift. */
const SENTENCE_END = /(?<!\d)[.!?]\s+/g;

/**
 * Zerlegt die Einleitung einer Kurslinie in Überschriften und Absätze (UI-Sweep 2026-10-11, r2-k-course-heading).
 * Chessable-Zwischenüberschriften kamen beim Import ohne Umbruch an („OppositionWir haben …" bzw. mit Leerzeichen
 * „Opposition Wir haben …") — für ein Kind ist der Satzanfang so unverständlich. Erkannt werden:
 *  - eine eigene kurze Zeile ohne Satzzeichen am Ende, auf die noch Text folgt;
 *  - eine an den nächsten Satz geklebte Überschrift (klein→GROSS ohne Leerzeichen), ab dem letzten Satzende davor.
 * Mit Leerzeichen angeklebte Überschriften („Opposition Wir") lassen sich nicht sicher erkennen — die braucht den
 * Umbruch aus der Quelle.
 */
export function splitCourseText(comment: string | null | undefined): CourseTextBlock[] {
  if (!comment?.trim()) return [];
  const text = unglue(comment);
  const lines = text.split(/\n+/).map(l => l.trim()).filter(l => l.length > 0);
  const blocks: CourseTextBlock[] = [];
  lines.forEach((line, i) => {
    const heading = i < lines.length - 1 && line.length <= HEADING_MAX && !/[.!?:,;]$/.test(line)
      && !/(?<!\d)[.!?]\s/.test(line);
    const last = blocks[blocks.length - 1];
    // Fließtext-Zeilen hintereinander bleiben EIN Absatz mit Umbruch (wie vorher mit white-space: pre-line).
    if (!heading && last?.kind === 'p') last.text += '\n' + line;
    else blocks.push({ kind: heading ? 'h' : 'p', text: line });
  });
  return blocks;
}

/** Setzt vor und hinter jede angeklebte Überschrift einen Umbruch. */
function unglue(text: string): string {
  let out = '';
  let from = 0;
  for (const m of text.matchAll(GLUED)) {
    if (GLUE_KEEP.has(m[1])) continue;
    const end = m.index! + m[0].length;
    const segment = text.slice(from, end);
    // Die Überschrift beginnt nach dem letzten Satzende (oder Umbruch) davor.
    let start = Math.max(segment.lastIndexOf('\n') + 1, 0);
    for (const s of segment.matchAll(SENTENCE_END)) start = Math.max(start, s.index! + s[0].length);
    if (segment.length - start > HEADING_MAX) continue;               // zu lang für eine Überschrift: so lassen
    out += segment.slice(0, start).trimEnd() + (start > 0 ? '\n' : '') + segment.slice(start) + '\n';
    from = end;
  }
  return out + text.slice(from);
}
