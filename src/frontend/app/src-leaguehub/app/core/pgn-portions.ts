/**
 * Große Partielisten in Paketen (0.598.1, Wunsch 2026-09-29: „auch beim PGN-Upload alle einlesen und dann in Paketen
 * anbieten"). Die Übersicht vor dem Import nimmt höchstens {@link IMPORT_PORTION} Partien und 5 Millionen Zeichen auf
 * einmal (`LeagueClubService.MaxImportGames`/`MaxImportChars`) — bis hierher fiel der Rest weg. Jetzt teilt die Seite die
 * Liste vorher: das erste Paket geht in die Übersicht, die übrigen werden offene Listen (Entwürfe).
 *
 * Getrennt wird mit der Regel des Servers (`PgnParser.SplitGamesCore`), damit ein Paket dort genauso viele Partien hat:
 * eine Kopfzeile nach Zugtext beginnt eine neue Partie, ebenso eine WIEDERHOLTE Kopfzeile ohne Zugtext dazwischen; in
 * einem offenen `{…}`-Kommentar ist nichts eine Kopfzeile. Partien ohne Zugtext zählt der Server nicht — hier auch nicht.
 */

/** So viele Partien nimmt die Übersicht auf einmal (`LeagueClubService.MaxImportGames`). */
export const IMPORT_PORTION = 500;

/** Zeichen je Paket — unter `LeagueClubService.MaxImportChars` (5 Mio.), mit Luft für die Kopfzeilen dazwischen. */
export const PORTION_MAX_CHARS = 4_500_000;

const HEADER_LINE = /^\s*\[\s*([A-Za-z][A-Za-z0-9_]*)\s+"(.*)"\s*\]\s*$/;

/** Die Partien als Rohtext (Kopfzeilen + Zugtext mit den Original-Umbrüchen), nur solche mit Zugtext. */
export function splitGameBlocks(pgn: string): string[] {
  const blocks: string[] = [];
  let keys = new Set<string>();
  let raw: string[] = [];
  let inMoves = false;
  let hasContent = false;
  let hasMoves = false;
  let openComments = 0;
  const flush = () => {
    if (hasMoves) blocks.push(raw.join('\n').trim());
    keys = new Set<string>();
    raw = [];
    inMoves = hasContent = hasMoves = false;
  };
  for (const line of pgn.replace(/^﻿/, '').replace(/\r\n?/g, '\n').split('\n')) {
    const header = openComments === 0 ? HEADER_LINE.exec(line) : null;
    if (header) {
      if (inMoves || (hasContent && keys.has(header[1].toLowerCase()))) flush();
      keys.add(header[1].toLowerCase());
      hasContent = true;
    } else if (openComments === 0 && line.trimStart().startsWith('[')) {
      continue;                                                        // tag-artig, aber keine Kopfzeile: der Server wirft sie weg
    } else if (line.trim()) {
      inMoves = hasContent = hasMoves = true;
      for (const ch of line) {
        if (ch === '{') openComments++;
        else if (ch === '}' && openComments > 0) openComments--;
      }
    }
    raw.push(line);
  }
  if (hasContent) flush();
  return blocks;
}

/** Die Liste in Pakete zu höchstens `maxGames` Partien und `maxChars` Zeichen. Passt sie in eines, kommt sie UNVERÄNDERT
 * zurück (auch eine ohne erkennbare Partie — dazu sagt der Server etwas). */
export function pgnPortions(pgn: string, maxGames = IMPORT_PORTION, maxChars = PORTION_MAX_CHARS): string[] {
  const blocks = splitGameBlocks(pgn);
  if (blocks.length <= maxGames && pgn.length <= maxChars) return [pgn];
  const parts: string[] = [];
  let current: string[] = [];
  let chars = 0;
  for (const b of blocks) {
    if (current.length && (current.length >= maxGames || chars + b.length + 2 > maxChars)) {
      parts.push(current.join('\n\n') + '\n');
      current = [];
      chars = 0;
    }
    current.push(b);
    chars += b.length + 2;
  }
  if (current.length) parts.push(current.join('\n\n') + '\n');
  return parts;
}

/** „MeineSpiele.pgn (Teil 2 von 3)" — ohne Namen (eingefügt) nur „Teil 2 von 3". */
export function partLabel(label: string | null, index: number, count: number): string | null {
  if (count < 2) return label;
  const part = `Teil ${index + 1} von ${count}`;
  return label ? `${label} (${part})` : part;
}

/** Was mit den Paketen geschah: `parked` der `parts − 1` weiteren liegen als offene Listen; `null` = wird gerade abgelegt. */
export function portionNote(parts: number, parked: number | null): string | null {
  if (parts < 2) return null;
  const head = `Die Liste ist in ${parts} Pakete zu höchstens ${IMPORT_PORTION} Partien aufgeteilt — das hier ist das erste`;
  if (parked === null) return `${head}; die übrigen werden gerade als offene Listen abgelegt …`;
  if (parked >= parts - 1) return `${head}, die übrigen stehen danach unter „Deine offenen Listen“.`;
  const missing = parts - 1 - parked;
  return `${head}; ${missing === 1 ? '1 weiteres konnte' : `${missing} weitere konnten`} nicht abgelegt werden (zu viele offene `
    + 'Listen) — lade die Datei später noch einmal hoch, schon importierte Partien erkennt LeagueHub als doppelt.';
}
