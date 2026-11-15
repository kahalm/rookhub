import { Chess } from 'chess.js';

/**
 * Legale Züge je Stellung, einmal erzeugt und gemerkt (0.978.1). Anlass: die Partie-Zusammenfassung prüfte jeden Zug im
 * Text mit `chess.moves({ verbose: true })` — das baut für JEDEN legalen Zug ein volles Zug-Objekt (SAN, zwei FENs,
 * Schach-/Matt-Probe) und kostet rund 1,2 ms je Stellung, gut zehnmal so viel wie die bloße SAN-Liste. Bei ~700 Proben
 * blockierte das die Seite über eine Sekunde, bevor der Text erschien.
 *
 * Hier wird je FEN nur die SAN-Liste erzeugt (`chess.moves()`); von/nach/Umwandlung und die Stellung danach rechnet
 * {@link legalMove} erst für den EINEN Zug aus, der wirklich gebraucht wird — und merkt auch das. Die volle Liste
 * ({@link legalMoves}) gibt es weiterhin, sie entsteht aber nur auf Anforderung (Koordinaten-Eingabe „g1f3“).
 *
 * Der Speicher ist gedeckelt ({@link MAX_POSITIONS}); ist er voll, wird er geleert statt Einzelnes zu verdrängen —
 * eine Zusammenfassung braucht einige hundert Stellungen, der Deckel greift nur bei sehr langem Stöbern.
 */
export interface LegalMove {
  san: string;
  from: string;
  to: string;
  /** Ziehende Figur, klein und englisch (`p`, `n`, `b`, `r`, `q`, `k`). */
  piece: string;
  promotion?: string;
  /** FEN nach dem Zug. */
  after: string;
}

interface Entry {
  sans: readonly string[];
  bySan: Map<string, LegalMove>;
  all?: readonly LegalMove[];
}

export const MAX_POSITIONS = 2000;
const cache = new Map<string, Entry | null>();

function entry(fen: string): Entry | null {
  const hit = cache.get(fen);
  if (hit !== undefined) return hit;
  let e: Entry | null;
  try { e = { sans: new Chess(fen).moves(), bySan: new Map() }; } catch { e = null; }   // unlesbare Stellung
  if (cache.size >= MAX_POSITIONS) cache.clear();
  cache.set(fen, e);
  return e;
}

/** SAN aller legalen Züge in `fen` (englisch, wie chess.js sie schreibt); `null` = die FEN ist nicht lesbar. */
export function legalSans(fen: string): readonly string[] | null {
  return entry(fen)?.sans ?? null;
}

/** Der legale Zug mit genau dieser SAN (aus {@link legalSans}) samt Stellung danach; `null` = gibt es dort nicht. */
export function legalMove(fen: string, san: string): LegalMove | null {
  const e = entry(fen);
  if (!e) return null;
  const known = e.bySan.get(san);
  if (known) return known;
  if (!e.sans.includes(san)) return null;
  const chess = new Chess(fen);
  const m = chess.move(san);
  const move: LegalMove = { san: m.san, from: m.from, to: m.to, piece: m.piece, after: chess.fen(), ...(m.promotion ? { promotion: m.promotion } : {}) };
  e.bySan.set(san, move);
  return move;
}

/** Alle legalen Züge in `fen` ausgerechnet — teuer, nur wo von/nach ALLER Züge gebraucht werden; `null` = FEN nicht lesbar. */
export function legalMoves(fen: string): readonly LegalMove[] | null {
  const e = entry(fen);
  if (!e) return null;
  return e.all ??= e.sans.map(san => legalMove(fen, san)!);
}

/** Der legale Zug von `from` nach `to` (mit `promotion`, wenn es eine Umwandlung ist); `null` = nicht legal. */
export function legalMoveByCoords(fen: string, from: string, to: string, promotion?: string): LegalMove | null {
  const e = entry(fen);
  if (!e) return null;
  const fits = (m: LegalMove) => m.from === from && m.to === to && m.promotion === promotion;
  for (const m of e.bySan.values()) if (fits(m)) return m;   // meist gerade erst über die SAN aufgelöst
  return legalMoves(fen)!.find(fits) ?? null;
}

/** Nur für Tests: den Speicher leeren. */
export function clearLegalMoveCache(): void {
  cache.clear();
}
