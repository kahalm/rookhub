import { ParsedGame, START_FEN } from '../../shared/pgn-viewer/pgn-parser';

/**
 * Serialisiert eine geparste Repertoire-Linie (ein <see cref="ParsedGame"/>) zurück zu einem
 * eigenständigen PGN-Text — inklusive Zug-Kommentaren (in geschweiften Klammern) und, falls die
 * Linie nicht von der Grundstellung startet, einem FEN-Header. Wird zum Teilen einer einzelnen
 * Linie als öffentlicher Nur-Ansehen-Link genutzt (der Empfänger-Viewer re-parst diesen PGN).
 *
 * Zug-Nummerierung + Anzugsseite werden aus der Start-FEN abgeleitet, damit Linien, die mitten
 * in einer Partie beginnen, korrekt nummeriert sind.
 */
export function parsedGameToPgn(game: ParsedGame, opts?: { title?: string }): string {
  const h = game.headers || {};
  const start = (game.fens && game.fens[0]) || START_FEN;
  const esc = (s: string) => (s || '').replace(/\\/g, '\\\\').replace(/"/g, '\\"');

  const header: string[] = [];
  header.push(`[Event "${esc(opts?.title || h['Event'] || 'Repertoire line')}"]`);
  header.push(`[White "${esc(h['White'] || '?')}"]`);
  header.push(`[Black "${esc(h['Black'] || '?')}"]`);
  const result = h['Result'] || '*';
  header.push(`[Result "${esc(result)}"]`);
  if (start && start !== START_FEN) {
    header.push('[SetUp "1"]');
    header.push(`[FEN "${esc(start)}"]`);
  }

  const comments = game.comments || {};
  const moves = game.moves || [];
  const parts: string[] = [];
  if (comments[-1]) parts.push(`{ ${comments[-1]} }`);

  const seg = start.split(' ');
  let fullMoveNo = parseInt(seg[5], 10);
  if (!fullMoveNo || fullMoveNo < 1) fullMoveNo = 1;
  let whiteToMove = (seg[1] || 'w') !== 'b';

  for (let i = 0; i < moves.length; i++) {
    if (whiteToMove) parts.push(`${fullMoveNo}.`);
    else if (i === 0) parts.push(`${fullMoveNo}...`);
    parts.push(moves[i].san);
    if (comments[i]) parts.push(`{ ${comments[i]} }`);
    if (!whiteToMove) fullMoveNo++;
    whiteToMove = !whiteToMove;
  }
  parts.push(result);

  return header.join('\n') + '\n\n' + parts.join(' ') + '\n';
}
