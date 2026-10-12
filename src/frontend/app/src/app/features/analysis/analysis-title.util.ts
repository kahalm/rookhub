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
