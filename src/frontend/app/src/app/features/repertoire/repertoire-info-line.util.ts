/**
 * Info-Linie im Repertoire: eine Erklär-Linie (Chessable „Info"), die nur durchgeklickt, nicht abgefragt
 * wird. Erkennbar am Marker `[%info]` im Zugtext (piratechess/Chessable-Import) oder — nach einem
 * RookHub-Download, bei dem der Marker wegfällt — am Präfix „Info | " im White-Header. Gleiche Regel wie
 * der Kurs-Import (`PgnImportService`: `[%info]` oder `PgnParser.InfoLinePrefix`).
 */
export function isInfoLineGame(raw: string): boolean {
  if (!raw) return false;
  return /\[%info\b/i.test(raw) || /^[ \t]*\[White[ \t]+"Info \| /m.test(raw);
}
