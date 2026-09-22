/**
 * Hilfen für PGN-Downloads (Kurse, Repertoires).
 */

/**
 * Entfernt die RookHub-internen Kommentar-Marker `[%alt …]` (von Chessable geduldete Züge) und `[%info]`
 * (Info-Linie) aus einem PGN, das heruntergeladen wird — ChessBase & Co. kennen sie nicht und zeigen sie als
 * Text, Chessable blendet die geduldeten Züge ebenfalls aus. Dadurch leer gewordene Kommentare fallen weg;
 * `[%cal]`/`[%csl]`/`[%tqu]` bleiben. Gleiche Regel wie `PgnParser.StripInternalMarkers` im Backend.
 * NUR für Downloads: Viewer und Repertoire-Trainer lesen dasselbe PGN und brauchen `[%alt]`.
 */
export function stripInternalMarkers(pgn: string): string {
  if (!pgn) return pgn;
  return pgn
    .replace(/\[%(?:alt|info)\b[^\]]*\]/gi, '')
    .replace(/\{\s*\}/g, '')
    .replace(/[ \t]{2,}/g, ' ');
}

const INFO_PREFIX = 'Info | ';

/**
 * Repertoire-Download: wie {@link stripInternalMarkers}, aber eine Info-Linie (Partie mit `[%info]` im
 * Zugtext) behält ihre Kennung — der `White`-Header bekommt „Info | " vorangestellt, denn der Marker selbst
 * fällt beim Entfernen weg und ChessBase & Co. zeigen die Linie sonst wie jede andere.
 */
export function repertoireDownloadPgn(pgn: string): string {
  if (!pgn) return pgn;
  // Eine Partie = Header-Block (Zeilen `[Tag "…"]`) + Zugtext bis zum nächsten Header-Block.
  const game = /((?:^[ \t]*\[[A-Za-z0-9_]+[ \t]+"[^\n]*\][ \t]*\r?(?:\n|$))+)([\s\S]*?)(?=^[ \t]*\[[A-Za-z0-9_]+[ \t]+"|(?![\s\S]))/gm;
  const marked = pgn.replace(game, (_all, headers: string, moves: string) => {
    if (!/\[%info\b/i.test(moves)) return headers + moves;
    return headers.replace(/^([ \t]*\[White[ \t]+")([^\n]*)("\][ \t]*)$/m, (m, open: string, name: string, close: string) =>
      name.startsWith(INFO_PREFIX) ? m : `${open}${INFO_PREFIX}${name}${close}`) + moves;
  });
  return stripInternalMarkers(marked);
}

/** Dateiname „Name_Zusatz.pgn" wie im Backend: nur Buchstaben/Ziffern, alles andere als ein „_". */
export function pgnFileName(name: string | null | undefined, suffix: string): string {
  const clean = (s: string | null | undefined) =>
    (s ?? '').replace(/[^\p{L}\p{N}]+/gu, '_').replace(/^_+|_+$/g, '').slice(0, 80).replace(/_+$/, '');
  const joined = [clean(name), clean(suffix)].filter(p => p.length > 0).join('_');
  return `${joined || 'course'}.pgn`;
}
