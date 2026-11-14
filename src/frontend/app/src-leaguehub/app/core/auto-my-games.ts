/**
 * „Automatisch zu meinen Partien hinzufügen" — EINE Regel für jeden Weg, auf dem ein angemeldeter Nutzer eine Partie in
 * die Vereins-Datenbank legt (Formular-Prüfseite seit 0.672.7; Übersicht der Partiedaten — PGN, ChessBase, Lichess-Studie,
 * Analysebrett — seit 0.970.0). Die Kopie geht über `POST /api/games/import` ins eigene Konto: mit den Namen der Eingabe
 * (nicht „Schwaz"), der Server verbindet sie über die Züge mit der Vereinspartie und erkennt eine schon vorhandene.
 */

/** Gerätevorliebe: fehlt = an (Vorgabe), `'0'` = abgewählt. */
export const AUTO_MINE_KEY = 'lh-auto-my-games';

/** Bis zu so vielen Partien einer Liste geht die Kopie mit — ein größerer Import bleibt ein reiner Vereins-Import. */
export const AUTO_MINE_MAX_GAMES = 5;

export function readAutoMine(): boolean {
  try { return localStorage.getItem(AUTO_MINE_KEY) !== '0'; } catch { return true; }
}

export function writeAutoMine(on: boolean): void {
  try { if (on) localStorage.removeItem(AUTO_MINE_KEY); else localStorage.setItem(AUTO_MINE_KEY, '0'); } catch { /* nur Bequemlichkeit */ }
}

/** Die Regel: mit Konto, nicht abgewählt, und höchstens {@link AUTO_MINE_MAX_GAMES} Partien auf einmal. */
export function autoMineApplies(o: { account: boolean; autoMine: boolean; games: number }): boolean {
  return o.account && o.autoMine && o.games >= 1 && o.games <= AUTO_MINE_MAX_GAMES;
}

/** Setzt `[White …]`/`[Black …]` einer Partie neu (`null` = bleibt) — für einen in der Übersicht korrigierten Namen. */
export function withPlayers(pgn: string, white: string | null, black: string | null): string {
  const set = (text: string, key: 'White' | 'Black', name: string | null): string => {
    if (!name?.trim()) return text;
    const line = `[${key} "${name.trim().replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"]`;
    const tag = new RegExp(`^\\[${key}\\s+"(?:[^"\\\\]|\\\\.)*"\\][ \\t]*$`, 'm');
    return tag.test(text) ? text.replace(tag, () => line) : `${line}\n${text}`;
  };
  return set(set(pgn, 'Black', black), 'White', white);
}
