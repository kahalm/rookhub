/**
 * Trainingsfarbe je Kapitel für farbGEMISCHTE Repertoires.
 *
 * Ein Chessable-Repertoire kann Linien enthalten, die man als Weiß spielt (z. B. „Sizilianisch",
 * „Caro Kann") UND Linien, die man als Schwarz spielt (z. B. „French w black"). Der frühere
 * globale Weiß/Schwarz-Umschalter im Trainer konnte das nicht abbilden — mit „Weiß" wurden die
 * Schwarz-Kapitel aus der falschen Seite abgefragt und umgekehrt.
 *
 * Da das hochgeladene PGN KEINE Orientierungs-Metadaten trägt (alle FENs = Grundstellung mit Weiß
 * am Zug, kein Chessable-Kursbezug), leiten wir die Trainingsfarbe PRO KAPITEL heuristisch ab:
 * die Seite, die in der MEHRHEIT der Kapitel-Linien den LETZTEN Halbzug zieht — Chessable beendet
 * eine gelernte Linie meist mit dem Zug der trainierten Seite. Gleichstand / kein Signal →
 * Wurzel-Fallback (zieht Weiß an der Wurzel zuerst, trainieren wir mangels Besserem Schwarz —
 * klassisches „Antwort-Repertoire"). Der User kann die Erkennung pro Kapitel dauerhaft überschreiben
 * (localStorage, pro Gerät — analog zum bisherigen Farb-Toggle).
 */

export type TrainColor = 'w' | 'b';

/** Seite am Zug in dieser (Start-)FEN. */
export function rootSideOf(fen: string): TrainColor {
  return fen.split(/\s+/)[1] === 'b' ? 'b' : 'w';
}

/** Seite, die den LETZTEN Halbzug einer Linie zieht — null bei 0 Zügen. */
export function sideOfLastMove(startFen: string, movesLength: number): TrainColor | null {
  if (movesLength <= 0) return null;
  const start = rootSideOf(startFen);
  // Halbzug 0 = start-Seite, danach alternierend; letzter Halbzug hat Index movesLength-1.
  return (movesLength - 1) % 2 === 0 ? start : (start === 'w' ? 'b' : 'w');
}

/** Ein Sample je Linie für die Kapitel-Aggregation. */
export interface ChapterColorSample {
  chapter: string;
  /** Seite des letzten Halbzugs (null = leere Linie, zählt nicht). */
  side: TrainColor | null;
  /** Seite an der Wurzel (für den Gleichstands-Fallback). */
  rootSide: TrainColor;
}

/** Automatisch erkannte Trainingsfarbe je Kapitel (Mehrheit „Seite des letzten Zugs"). */
export function autoChapterColors(samples: ChapterColorSample[]): Map<string, TrainColor> {
  const agg = new Map<string, { w: number; b: number; root: TrainColor }>();
  for (const s of samples) {
    let a = agg.get(s.chapter);
    if (!a) { a = { w: 0, b: 0, root: s.rootSide }; agg.set(s.chapter, a); }
    if (s.side === 'w') a.w++;
    else if (s.side === 'b') a.b++;
  }
  const out = new Map<string, TrainColor>();
  for (const [chapter, a] of agg) {
    const majority: TrainColor = a.w > a.b ? 'w' : a.b > a.w ? 'b' : (a.root === 'w' ? 'b' : 'w');
    out.set(chapter, majority);
  }
  return out;
}

const OVR_KEY = (id: number) => `rookhub_rep_train_chaptercolor_${id}`;

/** Manuelle Farb-Overrides je Kapitel aus dem localStorage (pro Gerät, pro Repertoire). */
export function readChapterColorOverrides(repId: number): Record<string, TrainColor> {
  try {
    const raw = localStorage.getItem(OVR_KEY(repId));
    if (!raw) return {};
    const obj = JSON.parse(raw) as Record<string, unknown>;
    const out: Record<string, TrainColor> = {};
    for (const k of Object.keys(obj || {})) {
      if (obj[k] === 'w' || obj[k] === 'b') out[k] = obj[k];
    }
    return out;
  } catch { return {}; }
}

/** Override für ein Kapitel setzen (überschreibt die Auto-Erkennung dauerhaft). */
export function setChapterColorOverride(repId: number, chapter: string, color: TrainColor): void {
  const cur = readChapterColorOverrides(repId);
  cur[chapter] = color;
  try { localStorage.setItem(OVR_KEY(repId), JSON.stringify(cur)); } catch { /* Storage voll/blockiert → ignorieren */ }
}

/** Effektive Farb-Map je Kapitel = Auto-Erkennung, von den manuellen Overrides überschrieben. */
export function resolveChapterColors(repId: number, auto: Map<string, TrainColor>): Map<string, TrainColor> {
  const out = new Map(auto);
  const ovr = readChapterColorOverrides(repId);
  for (const k of Object.keys(ovr)) out.set(k, ovr[k]);
  return out;
}
