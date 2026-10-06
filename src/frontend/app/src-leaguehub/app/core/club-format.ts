import { HttpErrorResponse } from '@angular/common/http';
import { serverTime } from '@rh/features/games/scoresheet-timing';
import { ClubImportResult, ScoresheetScan, ScoresheetStatus } from './club.models';

// Reine Texte und Regeln der Vereins-Datenbank (ohne Angular-Komponenten) — die Seite ist deutsch.

export const ANON_NAME = 'Schwaz';

/** Warum eine Partie nicht übernommen wurde (Gründe aus LeagueClubService). */
export function reasonText(reason: string): string {
  switch (reason) {
    case 'noLeaguePlayer': return 'Kein Spieler erkannt (weder in der Liga noch in der Megabase) — Namen korrigieren.';
    case 'onlyOwnClub': return 'Nur Spieler von Schwaz — nach dem Ersetzen bleibt kein Gegner übrig.';
    case 'notFound': return 'Diese Partie steht nicht (mehr) in der Datei.';
    case 'invalidUrl': return 'Das ist keine Adresse einer Lichess-Studie (lichess.org/study/…).';
    case 'lichessNotFound': return 'Diese Studie gibt es nicht oder sie ist nicht öffentlich.';
    case 'lichessFailed': return 'Lichess hat gerade nicht geantwortet — bitte später noch einmal.';
    case 'fromPosition': return 'Beginnt nicht in der Grundstellung.';
    case 'illegal': return 'Ein Zug ist nicht legal.';
    case 'noMoves': return 'Keine Züge.';
    case 'tooLong': return 'Zu lang (über 300 Züge).';
    case 'duplicate': return 'Diese Partie ist schon in der Vereins-Datenbank.';
    case 'invalidResult': return 'Unbekanntes Ergebnis.';
    case 'empty': return 'Kein PGN.';
    case 'tooLarge': return 'Zu groß (höchstens 5 Millionen Zeichen je Upload).';
    case 'shareLimit': return 'Über den Teilen-Link gehen höchstens 50 Partien je Upload und 200 je Tag — den Rest bitte angemeldet oder morgen.';
    default: return 'Nicht übernommen.';
  }
}

/** „3 Partien übernommen (2 mit „Schwaz"), 1 schon da, 2 nicht übernommen." */
export function importSummary(r: ClubImportResult): string {
  const parts: string[] = [];
  const n = (k: number, one: string, many: string) => `${k} ${k === 1 ? one : many}`;
  parts.push(n(r.added, 'Partie übernommen', 'Partien übernommen') + (r.anonymized ? ` (${r.anonymized} mit „${ANON_NAME}“)` : ''));
  if (r.duplicates) parts.push(`${r.duplicates} schon da`);
  if (r.failed.length) parts.push(`${r.failed.length} nicht übernommen`);
  let s = parts.join(', ') + '.';
  if (r.remembered) s += ` ${n(r.remembered, 'Namens-Zuordnung', 'Namens-Zuordnungen')} gemerkt — das nächste Mal erkennt LeagueHub sie von selbst.`;
  if (r.truncated) s += ' Es wurden nur die ersten 500 Partien gelesen — den Rest bitte in einem zweiten Upload.';
  return s;
}

/** Absage beim Hochladen eines Formular-Fotos (Gründe aus ScoresheetScanService.CreateAsync). */
export function uploadErrorText(err: unknown): string {
  const e = err instanceof HttpErrorResponse ? err : null;
  switch (e?.error?.reason) {
    case 'notConfigured': return 'Das Einlesen ist gerade nicht eingerichtet.';
    case 'unsupportedImage': return 'Das Bild lässt sich nicht lesen (JPG, PNG oder WebP).';
    case 'tooLarge': return 'Das Foto ist zu groß.';
    case 'tooManyPages': return 'Höchstens drei Fotos je Formular.';
    case 'dailyLimit': return 'Das Tageslimit für Formulare ist erreicht (siehe oben, ab wann es wieder geht).';
    case 'anonDailyLimit': return 'Über Links wurden heute schon so viele Formulare eingelesen, wie am Tag gehen — bitte morgen wieder.';
    case 'tooManyOpen': return 'Es werden gerade noch andere Formulare gelesen.';
    case 'userDailyBudget':
    case 'userMonthlyBudget':
    case 'globalBudget': return 'Das Budget fürs Einlesen ist gerade aufgebraucht.';
    case 'noFile': return 'Kein Foto gewählt.';
  }
  // Eine 413 kommt vom Frontend-nginx als HTML-Seite ohne `reason` (Rumpf über seiner Grenze) — A6-020.
  if (e?.status === 413) return 'Das Foto ist zu groß.';
  if (e?.status === 403) return 'Dafür fehlt dir die Berechtigung (Vereinsmitglieder).';
  if (e?.status === 0) return 'Der Server ist gerade nicht erreichbar.';
  return `Hochladen hat nicht geklappt${e ? ` (HTTP ${e.status})` : ''}.`;
}

/** Geht von selbst vorbei (Neustart, Funkloch, Proxy ohne API) — nur dann lohnt stilles Nachfragen (UX-034). */
export function isTransientError(err: unknown): boolean {
  const s = err instanceof HttpErrorResponse ? err.status : -1;
  return s === 0 || s === 502 || s === 503 || s === 504;
}

/**
 * Ein Abruf kam nicht durch — Klartext für alle LeagueHub-Seiten statt „Fehler 500." (UX-034). Den nächsten Schritt
 * („Erneut versuchen", Rückweg) setzt die Seite dazu. Ein 403 heißt „Berechtigung fehlt" (das Recht prüft der Server live,
 * eine entzogene Rolle gilt sofort) — Seiten mit eigenem 403-Text (Vereinspartien, Ligaseite) fangen ihn vorher ab.
 */
export function loadErrorText(err: unknown): string {
  if (!(err instanceof HttpErrorResponse)) return 'Beim Laden ist etwas schiefgegangen.';
  if (isTransientError(err)) return 'Der Server ist gerade nicht erreichbar.';
  if (err.status === 403) return 'Dafür fehlt dir die Berechtigung (Vereinsmitglieder).';
  if (err.status === 429) return 'Gerade kamen zu viele Anfragen – bitte in einer Minute noch einmal versuchen.';
  return `Der Server hatte ein Problem (${err.status}). Bitte gleich noch einmal versuchen.`;
}

/** Wie weit ist eine Einlesung? */
export function scanStateText(s: ScoresheetScan): string {
  switch (s.status) {
    case 'pending': return 'wartet';
    case 'running': return 'wird gelesen …';
    case 'done': return `gelesen: ${Math.ceil(s.moveCount / 2)} Züge${s.uncertainCount ? `, ${s.uncertainCount} unsicher` : ''}`;
    default: return `gescheitert${s.error === 'unreadable' ? ' (unleserlich)' : s.error === 'noMoves' ? ' (keine Züge)' : ''}`;
  }
}

/** Kann gerade eingelesen werden — und wenn nicht, warum? */
export function scanAvailability(s: ScoresheetStatus, now = new Date()): { ok: boolean; text: string } {
  if (!s.available) return { ok: false, text: 'Das Einlesen ist gerade nicht eingerichtet.' };
  if (s.blocked === 'anonDailyLimit') return { ok: false, text: 'Über Links wurden heute schon alle Formulare des Tages eingelesen — bitte morgen wieder.' };
  if (s.blocked) return { ok: false, text: 'Das Budget fürs Einlesen ist gerade aufgebraucht.' };
  const per = `${s.dailyLimit === 1 ? 'Eines' : s.dailyLimit} je 24 Stunden`;
  if (!s.unlimited && s.usedToday >= s.dailyLimit) {
    const next = s.nextAllowedAt ? new Date(s.nextAllowedAt) : null;
    const when = next && next > now
      ? next.toLocaleString('de-AT', { weekday: 'short', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
      : null;
    return { ok: false, text: `${per} — erreicht${when ? `, das nächste ab ${when}` : ''}.` };
  }
  return { ok: true, text: s.unlimited ? 'Ohne Tageslimit (Admin).' : `${per} (heute: ${s.usedToday} von ${s.dailyLimit}).` };
}

/** Jahr aus dem gelesenen Datum („2026-06-05", „5.6.26", „2024") — sonst `null`. */
export function yearOf(date: string | null | undefined, now = new Date()): number | null {
  const s = String(date ?? '');
  const four = /(19|20)\d{2}/.exec(s);
  let y = four ? Number(four[0]) : NaN;
  if (Number.isNaN(y)) {
    const two = /(?:^|\D)(\d{1,2})[./-](\d{1,2})[./-](\d{2})(?:\D|$)/.exec(s);
    if (two) y = 2000 + Number(two[3]);
  }
  return y >= 1900 && y <= now.getFullYear() + 1 ? y : null;
}

/**
 * Das Jahr zum Vorbelegen mit starkem Hang zu heuer (0.655.0, Wunsch 2026-10-04: „kaum jemand scannt 10 Jahre alte
 * Scoresheets — und wenn, soll er das anpassen"): gelesen heuer oder voriges Jahr → so; älter, unlesbar oder fehlt → heuer.
 * `read` = was die Erkennung lieferte, wenn es überstimmt wurde (für den Hinweis unter dem Feld).
 */
export function presetYear(date: string | null | undefined, now = new Date()): { year: number; read: number | null } {
  const thisYear = now.getFullYear();
  const y = yearOf(date, now);
  return y !== null && y >= thisYear - 1 && y <= thisYear ? { year: y, read: null } : { year: thisYear, read: y };
}

/** Ergebnis wie auf dem Formular gelesen → PGN-Ergebnis. */
export function normalizeResult(r: string | null | undefined): string {
  const s = String(r ?? '').replace(/\s/g, '').replace(/½/g, '1/2');
  if (s === '1-0' || s === '0-1' || s === '1/2-1/2') return s;
  if (s === '1/2' || s === '0.5-0.5' || s === '=') return '1/2-1/2';
  return '*';
}

/** Was ein geprüftes Formular als PGN hergibt (zum Herunterladen, Kopieren, für „Meine Partien"). */
export interface SheetPgnInput {
  moves: string[];
  white: string | null;
  black: string | null;
  result: string;
  event: string | null;
  year: number | null;
}

/**
 * Das PGN einer geprüften Partie (Wunsch 2026-09-28: „wenn ich fertig geprüft habe, PGN-Download + als Text kopieren").
 * Mit den Namen, wie sie im Formular stehen — die Datei bleibt beim Nutzer, „Schwaz" gilt nur für die Vereins-Datenbank.
 * Züge in englischer SAN (so versteht sie jedes Programm), Zeilen bis 80 Zeichen.
 */
export function sheetPgn(g: SheetPgnInput): string {
  const tag = (k: string, v: string) => `[${k} "${v.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"]`;
  const result = ['1-0', '0-1', '1/2-1/2'].includes(g.result) ? g.result : '*';
  const head = [
    tag('Event', g.event?.trim() || '?'), tag('Site', '?'), tag('Date', g.year ? `${g.year}.??.??` : '????.??.??'), tag('Round', '?'),
    tag('White', g.white?.trim() || '?'), tag('Black', g.black?.trim() || '?'), tag('Result', result),
  ];
  const tokens = g.moves.map((m, i) => (i % 2 === 0 ? `${i / 2 + 1}. ` : '') + m);
  tokens.push(result);
  const lines: string[] = [];
  let line = '';
  for (const t of tokens) {
    if (line && line.length + 1 + t.length > 80) { lines.push(line); line = t; }
    else line = line ? `${line} ${t}` : t;
  }
  if (line) lines.push(line);
  return `${head.join('\n')}\n\n${lines.join('\n')}\n`;
}

/** Dateiname fürs PGN: „Oberschmid_Hengl_2025.pgn". */
export function sheetPgnFileName(g: SheetPgnInput): string {
  const part = (s: string | null) => (s?.split(',')[0].trim() || '').replace(/[^\p{L}\p{N}-]+/gu, '') || 'Partie';
  return `${part(g.white)}_${part(g.black)}${g.year ? '_' + g.year : ''}.pgn`;
}

/** „28.09., 10:52" (Ortszeit) aus einem Zeitpunkt der API. */
export function shortDateTime(iso: string | null | undefined): string {
  const t = serverTime(iso);
  if (!Number.isFinite(t)) return '';
  const d = new Date(t);
  const two = (n: number) => String(n).padStart(2, '0');
  return `${two(d.getDate())}.${two(d.getMonth() + 1)}., ${two(d.getHours())}:${two(d.getMinutes())}`;
}
