/**
 * Parst den rohen `BookPuzzleDto.altMoves`-JSON-String (`{ "ply": ["uci", …] }`) in eine
 * `Record<number, string[]>`-Map (Halbzug-Index → geduldete Alternativzüge als UCI).
 *
 * Quelle sind die von Chessable geduldeten Alternativzüge (softFail → `[%alt]`), die der Import
 * SAN→UCI umgesetzt hat. Der Solver nutzt sie, um einen solchen Zug als gleichwertige Alternative
 * zu erkennen (statt ihn als Fehler zu werten). Robuster Fallback `{}` bei fehlendem/kaputtem JSON.
 */
export function parseAltMoves(raw: string | null | undefined): Record<number, string[]> {
  if (!raw) return {};
  try {
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    const out: Record<number, string[]> = {};
    for (const [key, val] of Object.entries(parsed)) {
      const ply = Number(key);
      if (!Number.isInteger(ply) || !Array.isArray(val)) continue;
      const ucis = val.filter((u): u is string => typeof u === 'string' && u.length >= 4);
      if (ucis.length) out[ply] = ucis;
    }
    return out;
  } catch {
    return {};
  }
}
