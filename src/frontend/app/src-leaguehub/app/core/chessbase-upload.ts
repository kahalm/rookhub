import { HttpErrorResponse } from '@angular/common/http';
import { ChessBaseResult } from './club.models';

/**
 * ChessBase-Datenbanken hochladen (0.598.0, Wunsch 2026-09-29: „ein Import für 2cbh und cbh zusätzlich zu PGN"). Eine
 * Datenbank ist eine MENGE gleichnamiger Dateien; der Nutzer wählt am einfachsten alle im Ordner aus. Hochgeladen wird nur,
 * was der Server zum Lesen braucht, einzeln gepackt — die Spieldatei einer kommentierten Datenbank schrumpft dabei auf ein
 * Siebtel, und der Upload bleibt unter der 15-MB-Grenze vor der API. Der Server macht daraus ein PGN, und ab dort ist es
 * derselbe Weg wie mit einer PGN-Datei.
 */

/** Was der Server liest — SPIEGEL von `ChessBaseFiles.Upload` (beide Seiten mit literalem Test). */
export const CHESSBASE_UPLOAD_EXTENSIONS = ['.cbh', '.cbg', '.cbp', '.cbt', '.cbc', '.2cbh', '.2cbg', '.2lid'];

/** Die Grenze der allgemeinen `/api/`-Regel im Frontend-nginx (15 MB), mit Platz für den Multipart-Rahmen. */
export const CHESSBASE_MAX_UPLOAD_BYTES = 14.5 * 1024 * 1024;

function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(dot).toLowerCase() : '';
}

/** Aus einer Auswahl die Dateien, die hochgehen: die gebrauchten Endungen und ZIPs. Ohne Kopf (`.cbh`/`.2cbh`) und ohne
 * ZIP ist es keine Datenbank. */
export function chessBaseSelection(files: readonly File[]): { send: File[]; hasDatabase: boolean } {
  const send = files.filter(f => extensionOf(f.name) === '.zip' || CHESSBASE_UPLOAD_EXTENSIONS.includes(extensionOf(f.name)));
  const hasDatabase = send.some(f => ['.cbh', '.2cbh', '.zip'].includes(extensionOf(f.name)));
  return { send, hasDatabase };
}

/** Eine Datei für den Upload: einzeln gepackt als `name.gz` (ein ZIP bleibt, wie es ist). Ohne `CompressionStream` (sehr
 * alte Browser) geht sie ungepackt. */
export async function packForUpload(file: File): Promise<{ name: string; blob: Blob }> {
  if (extensionOf(file.name) === '.zip' || typeof CompressionStream === 'undefined') return { name: file.name, blob: file };
  const blob = await new Response(file.stream().pipeThrough(new CompressionStream('gzip'))).blob();
  return { name: `${file.name}.gz`, blob };
}

/** „1.100" — fest mit Punkt: `toLocaleString('de-AT')` setzt je nach Umgebung ein schmales Leerzeichen. */
const thousands = (k: number) => String(k).replace(/\B(?=(\d{3})+(?!\d))/g, '.');

/** Absage des Servers (Gründe aus `ChessBaseImportService.ConvertAsync`) als Satz. */
export function chessBaseErrorText(err: unknown): string {
  const e = err instanceof HttpErrorResponse ? err : null;
  const reason = e?.error?.reason as string | undefined;
  switch (reason) {
    case 'noFile': return 'Keine Datei ausgewählt.';
    case 'noDatabase': return 'Keine ChessBase-Datenbank dabei — die Datei mit der Endung .cbh oder .2cbh muss mit.';
    case 'multipleDatabases': return 'Das sind mehrere Datenbanken — bitte eine auf einmal.';
    case 'missingFile': return `${e?.error?.message ?? 'Es fehlt eine Datei der Datenbank.'}`;
    case 'tooLarge': return 'Die Datenbank ist zu groß für einen Upload.';
    case 'invalidZip': return 'Das ZIP lässt sich nicht lesen.';
    case 'invalidFile': return 'Eine Datei ließ sich nicht auspacken — bitte noch einmal auswählen.';
    case 'unreadable': return 'Diese Datenbank lässt sich nicht lesen (unbekannte ChessBase-Version oder beschädigt).';
    case 'busy': return 'Gerade werden andere Datenbanken gelesen — bitte gleich noch einmal.';
  }
  if (e?.status === 404) return 'Dieser Link ist abgelaufen.';
  if (e?.status === 403) return 'Dafür fehlt dir die Berechtigung (Vereinsmitglieder).';
  if (e?.status === 413) return 'Die Datenbank ist zu groß für einen Upload.';
  return 'Die Datenbank ließ sich nicht hochladen.';
}

/** Was aus der Datenbank wurde: „MeineSpiele: 78 Partien gelesen, 1 übersprungen (#12 A – B: …)." — die Aufteilung in
 * Pakete sagt `portionNote` (derselbe Weg wie bei einer PGN-Datei). */
export function chessBaseNote(r: ChessBaseResult): string {
  const n = (k: number, one: string, many: string) => `${thousands(k)} ${k === 1 ? one : many}`;
  let s = `${r.name}: ${n(r.converted, 'Partie gelesen', 'Partien gelesen')}`;
  if (r.skippedCount) {
    const first = r.skipped.slice(0, 3).map(g => `#${g.id} ${g.white} – ${g.black}: ${g.reason}`).join('; ');
    s += `, ${n(r.skippedCount, 'übersprungen', 'übersprungen')} (${first}${r.skippedCount > 3 ? '; …' : ''})`;
  }
  s += '.';
  if (r.truncated) s += ` Die Datenbank hat mehr Partien — gelesen wurden die ersten ${n(r.games, 'Partie', 'Partien')}.`;
  return s;
}
