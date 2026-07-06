import { Chess } from 'chess.js';

/**
 * Macht Zugfolgen in Buch-/Kurs-Kommentaren anklickbar (Vorschau auf dem Brett).
 *
 * Kommentar-Varianten (z. B. „… weil 2.fxe6 … 2…Kc7 erlaubt.") nutzen eine EIGENE Zug-Nummerierung
 * und lassen Züge aus — die Basis-Stellung lässt sich also NICHT aus dem Text ableiten. Stattdessen
 * suchen wir unter den Stellungen der Puzzle-Hauptlinie (Start-FEN + nach jedem Zug) diejenige, aus
 * der die im Kommentar genannte SAN-Folge legal spielbar ist (längster legaler Präfix; bei Gleichstand
 * die späteste Stellung). Nur die so validierten Züge werden klickbar; alles andere bleibt Text.
 */

export interface CommentSegment {
  /** Reiner Text (nicht klickbar). */
  text?: string;
  /** Anklickbarer Zug (im Kommentar-Wortlaut, z. B. „2.fxe6"). Nur gesetzt, wenn spielbar. */
  move?: string;
  /** Stellung NACH diesem Zug (FEN) — Brett-Vorschau. */
  fen?: string;
  /** Ausgangs-/Zielfeld für die lastMove-Markierung. */
  from?: string;
  to?: string;
}

/** Ein Zug-Schritt der aufgelösten Variante (Stellung nach dem Zug + Feldmarkierung). */
export interface VariationStep { san: string; fen: string; from: string; to: string; }

// SAN-Kern: Rochade | Figurenzug | Bauernzug (+ optionale Umwandlung / Schach-Matt-Zeichen).
const SAN = '(?:O-O-O|O-O|[KQRBN][a-h]?[1-8]?x?[a-h][1-8]|[a-h](?:x[a-h])?[1-8](?:=[QRBN])?)[+#]?';
// Optionale Zugnummer davor („2." / „2…" / „2...") + der SAN-Zug. `g` für globales Scannen.
const TOKEN_RE = new RegExp(`(?:\\d+\\.(?:\\.\\.|\\u2026)?\\s?)?(${SAN})`, 'g');

/** Alle SAN-Token (nur der reine Zug, ohne Nummer) in Textreihenfolge. */
export function extractSanTokens(text: string): string[] {
  const out: string[] = [];
  const re = new RegExp(TOKEN_RE.source, 'g');
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) out.push(m[1]);
  return out;
}

/** FEN-Liste der Hauptlinie: Start-FEN, dann nach jedem (UCI-)Zug. Ungültige Züge brechen die Kette ab. */
function mainlineFens(startFen: string, ucis: string[]): string[] {
  const c = new Chess(startFen);
  const fens = [c.fen()];
  for (const u of ucis) {
    const mv = safeUci(c, u);
    if (!mv) break;
    fens.push(c.fen());
  }
  return fens;
}

function safeUci(c: Chess, uci: string): boolean {
  try {
    const r = c.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci.slice(4) || undefined });
    return !!r;
  } catch { return false; }
}

/** Spielt die SAN-Folge ab `baseFen`, bis ein Zug illegal wird; liefert die Schritte des legalen Präfix. */
function playPrefix(baseFen: string, sans: string[]): VariationStep[] {
  const c = new Chess(baseFen);
  const steps: VariationStep[] = [];
  for (const san of sans) {
    let mv;
    try { mv = c.move(san); } catch { break; }
    if (!mv) break;
    steps.push({ san, fen: c.fen(), from: mv.from, to: mv.to });
  }
  return steps;
}

/**
 * Löst die Kommentar-Variante gegen die Hauptlinie auf: die Stellung mit dem LÄNGSTEN legalen Präfix
 * der SAN-Folge (bei Gleichstand die späteste). Gibt die Schritte des Präfix zurück (ggf. leer).
 */
export function resolveVariation(startFen: string, ucis: string[], sans: string[]): VariationStep[] {
  if (!sans.length) return [];
  const bases = mainlineFens(startFen, ucis);
  let best: VariationStep[] = [];
  for (let i = bases.length - 1; i >= 0; i--) {   // späteste Stellung zuerst → bei Gleichstand bevorzugt
    const s = playPrefix(bases[i], sans);
    if (s.length > best.length) best = s;
    if (best.length === sans.length) break;       // volle Variante gefunden
  }
  return best;
}

/**
 * Zerlegt einen Kommentar in Segmente (Text + klickbare Züge). Ein Zug ist NUR klickbar, wenn er Teil
 * des aufgelösten legalen Präfix ist (sonst reiner Text). `startFen`/`ucis` = Puzzle-Hauptlinie.
 */
export function buildCommentSegments(text: string, startFen: string, ucis: string[]): CommentSegment[] {
  const steps = resolveVariation(startFen, ucis, extractSanTokens(text));
  const segments: CommentSegment[] = [];
  const re = new RegExp(TOKEN_RE.source, 'g');
  let last = 0;
  let tokenIdx = 0;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    if (m.index > last) segments.push({ text: text.slice(last, m.index) });
    const step = tokenIdx < steps.length ? steps[tokenIdx] : null;
    if (step) segments.push({ move: m[0].trim(), fen: step.fen, from: step.from, to: step.to });
    else segments.push({ text: m[0] });   // nicht spielbar → als Text belassen
    last = m.index + m[0].length;
    tokenIdx++;
  }
  if (last < text.length) segments.push({ text: text.slice(last) });
  return segments;
}
