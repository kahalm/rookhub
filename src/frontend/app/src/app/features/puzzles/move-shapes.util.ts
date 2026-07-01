import { DrawShape } from 'chessground/draw';
import { Key } from 'chessground/types';

/** Ein rohes Board-Annotations-Element aus BookPuzzle.MoveShapes (`{o,d?,b}`). */
interface RawShape { o: string; d?: string | null; b?: string; }

/**
 * Parst den rohen `moveShapes`-JSON-String eines Buch-Puzzles
 * (`{ "ply": [{ "o":"d8", "d":"g8", "b":"green" }, …] }`, Schlüssel = 0-basierter Halbzug,
 * `-1` = Einleitung) in eine Map Ply → chessground-`DrawShape[]`. Pfeil = mit `d`, sonst
 * Feld-Markierung. Ungültiges/leeres → leere Map (nie werfen).
 */
export function parseMoveShapes(json: string | null | undefined): Record<number, DrawShape[]> {
  const out: Record<number, DrawShape[]> = {};
  if (!json) return out;
  let parsed: Record<string, RawShape[]>;
  try { parsed = JSON.parse(json); } catch { return out; }
  if (!parsed || typeof parsed !== 'object') return out;
  for (const [plyKey, raw] of Object.entries(parsed)) {
    const ply = Number(plyKey);
    if (!Number.isFinite(ply) || !Array.isArray(raw)) continue;
    const shapes: DrawShape[] = [];
    for (const s of raw) {
      if (!s || typeof s.o !== 'string') continue;
      const brush = s.b || 'green';
      if (s.d) shapes.push({ orig: s.o as Key, dest: s.d as Key, brush });
      else shapes.push({ orig: s.o as Key, brush });
    }
    if (shapes.length) out[ply] = shapes;
  }
  return out;
}
