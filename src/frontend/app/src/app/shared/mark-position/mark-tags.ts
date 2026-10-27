/**
 * Die festen Schlagworte für „+"-Stellungen (0.862.0, Wunsch 2026-10-27: „die interessanten Stellungen auch klassifizieren").
 * Spiegel von `MarkedPositionService.KnownTags` am Server — dort und hier gleich halten. Übersetzt über
 * `markPosition.tag.<code>`; alles andere ist ein eigenes Wort des Nutzers und wird so gezeigt, wie er es geschrieben hat.
 */
export const KNOWN_MARK_TAGS = [
  'tactic', 'combination', 'sacrifice', 'attack', 'defence', 'strategy', 'positional', 'calculation',
  'endgame', 'opening', 'trap', 'critical', 'instructive', 'beautiful',
] as const;

/** Grenzen wie am Server (`MaxTags`, `MaxTagLength`, `MaxCommentLength`). */
export const MAX_MARK_TAGS = 12;
export const MAX_MARK_TAG_LENGTH = 30;
export const MAX_MARK_COMMENT_LENGTH = 1000;

/** Fester Code (klein geschrieben) oder `null` für ein eigenes Wort. */
export function knownMarkTag(tag: string): string | null {
  const t = tag.trim().toLowerCase();
  return (KNOWN_MARK_TAGS as readonly string[]).includes(t) ? t : null;
}

/** Ein Schlagwort hinzufügen (Groß/klein egal, ohne Leere, `;` und Überlänge) → neue Liste oder `null`, wenn es nicht geht. */
export function addMarkTag(tags: readonly string[], raw: string): string[] | null {
  const tag = knownMarkTag(raw) ?? raw.trim();
  if (!tag || tag.includes(';') || tag.length > MAX_MARK_TAG_LENGTH || tags.length >= MAX_MARK_TAGS) return null;
  if (tags.some(t => t.toLowerCase() === tag.toLowerCase())) return null;
  return [...tags, tag];
}
