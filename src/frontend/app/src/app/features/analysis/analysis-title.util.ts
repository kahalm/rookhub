/**
 * Angezeigter Titel einer Partie-Analyse (v1-rk-analysis-pgn-placeholder-title): PGN-Platzhalter für fehlende Namen
 * („?", „??", „-") werden nicht zum Titel „? – ?" zusammengeklebt. Fehlen beide Namen (oder der ganze Titel), steht
 * „Ohne Titel" da; fehlt einer, „ohne Name" an seiner Stelle. Ein Knoten-Zusatz („ (50k nodes)") bleibt erhalten.
 * Bestandsdaten vor der API-Korrektur tragen die Platzhalter noch im gespeicherten Titel — deshalb hier.
 */
export interface AnalysisTitleTexts {
  /** „Ohne Titel" */
  untitled: string;
  /** „ohne Name" */
  noName: string;
}

const PLACEHOLDER = /^(\?+|-)$/;
const NAMES = /^(.*?) – (.*?)( \([^()]+ nodes\))?$/;

export function analysisTitle(title: string | null | undefined, texts: AnalysisTitleTexts): string {
  const t = (title ?? '').trim();
  if (!t || PLACEHOLDER.test(t)) return texts.untitled;
  const m = NAMES.exec(t);
  if (!m) return t;
  const white = m[1].trim(), black = m[2].trim(), suffix = m[3] ?? '';
  const wMissing = !white || PLACEHOLDER.test(white), bMissing = !black || PLACEHOLDER.test(black);
  if (wMissing && bMissing) return texts.untitled + suffix;
  if (!wMissing && !bMissing) return t;
  return `${wMissing ? texts.noName : white} – ${bMissing ? texts.noName : black}${suffix}`;
}

/** Titel eines geladenen PGN für den Verlauf: „Weiß – Schwarz" aus den Kopfdaten (ohne „?"), sonst einer der Namen
 *  oder das Turnier; null, wenn nichts da ist. */
export function pgnTitle(h: Record<string, string>): string | null {
  const white = h['White'] && h['White'] !== '?' ? h['White'] : null;
  const black = h['Black'] && h['Black'] !== '?' ? h['Black'] : null;
  if (white && black) return `${white} – ${black}`;
  return white ?? black ?? (h['Event'] && h['Event'] !== '?' ? h['Event'] : null);
}

/**
 * Kopf des Analysebretts (Wunsch 2026-11-28: „wenn der Titel der Partie bekannt ist, oben statt nur ‚Analyse'"): aus den
 * PGN-Kopfzeilen der geladenen Partie bzw. dem Titel eines Verlauf-Eintrags. `title` = ausdrücklicher Titel (Kapitel,
 * `Title`), sonst stehen die Namen einzeln da (null = Platzhalter/fehlt). Ohne Titel und ohne beide Namen: null —
 * dann bleibt oben „Analyse".
 */
export interface AnalysisHeading {
  title: string | null;
  white: string | null;
  black: string | null;
  /** „1–0", „0–1", „½–½" (offene Partie „*": null) */
  result: string | null;
  event: string | null;
  round: string | null;
  year: string | null;
}

const RESULTS: Record<string, string> = { '1-0': '1–0', '0-1': '0–1', '1/2-1/2': '½–½' };

function clean(v: string | null | undefined): string | null {
  const t = (v ?? '').trim();
  return t && !PLACEHOLDER.test(t) ? t : null;
}

/** @param explicitTitle Titel eines Verlauf-Eintrags („Weiß – Schwarz" oder frei) — wird bei „A – B" in Namen zerlegt. */
export function analysisHeading(headers: Record<string, string> | null | undefined, explicitTitle?: string | null): AnalysisHeading | null {
  const h = headers ?? {};
  let title = clean(h['ChapterName']) ?? clean(h['Title']);
  let white = clean(h['White']), black = clean(h['Black']);
  const ex = clean(explicitTitle);
  if (!title && !white && !black && ex) {
    const m = NAMES.exec(ex);
    if (m) { white = clean(m[1]); black = clean(m[2]); } else title = ex;
  }
  if (!title && !white && !black) return null;
  const date = /^(\d{4})/.exec((h['Date'] ?? h['UTCDate'] ?? '').trim());
  const event = clean(h['Event']);
  return {
    title, white, black,
    result: RESULTS[(h['Result'] ?? '').trim()] ?? null,
    event: event && event !== title ? event : null,
    round: clean(h['Round']),
    year: date ? date[1] : null,
  };
}

/** Überschrift: der ausdrückliche Titel, sonst „Weiß – Schwarz" (ein fehlender Name als `noName`). */
export function analysisHeadingText(h: AnalysisHeading, noName: string): string {
  return h.title ?? `${h.white ?? noName} – ${h.black ?? noName}`;
}

/** Zeile darunter: „1–0 · Event, Runde 3 · 2024" — nur, was da ist; leer = keine Zeile. */
export function analysisHeadingMeta(h: AnalysisHeading, roundLabel: (round: string) => string): string {
  const where = [h.event, h.round ? roundLabel(h.round) : null].filter(Boolean).join(', ');
  return [h.result, where, h.year].filter(Boolean).join(' · ');
}
