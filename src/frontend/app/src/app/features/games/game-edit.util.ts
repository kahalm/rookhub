import { Chess } from 'chess.js';
import { ScoresheetOption, ScoresheetPly } from './scoresheet.service';

/**
 * Ein Halbzug auf der Korrekturseite (`/games/:id/edit`). Bei einer aus einem Formular eingelesenen Partie
 * trägt er, was dastand (`written`), woher er stammt (`w` = Index des Formular-Eintrags) und die
 * Lesarten, zwischen denen der Nutzer wählen kann; bei einer gewöhnlichen Partie nur Zug und Kommentar.
 */
export interface EditPly {
  san: string;
  uci: string;
  /** Formular-Eintrag, aus dem der Zug stammt; `null` = vom Nutzer eingefügt bzw. keine Einlesung. */
  w: number | null;
  written: string;
  match: string;
  uncertain: boolean;
  confirmed: boolean;
  options: ScoresheetOption[] | null;
  comment: string | null;
  /** Befund der Engine-Prüfung beim Einlesen (0.646.0): `replaced` = die Lesung ergab ein anhaltendes Zickzack in der
   *  Bewertung und wurde ersetzt (die alte steht als Lesart da), `suggested` = eine andere Lesart passt besser. */
  check?: string | null;
  /** Nicht legal in der Stellung davor — entsteht, wenn ein früherer Zug geändert wurde und der Rest nicht
   *  neu aufbereitet werden kann (gewöhnliche Partie). Wird beim Speichern verworfen. */
  illegal: boolean;
}

export const START_FEN = new Chess().fen();

/** Stellungen VOR jedem Halbzug und nach dem letzten legalen (Länge = legale Züge + 1). */
export function fensOf(plies: readonly EditPly[], startFen = START_FEN): string[] {
  const chess = new Chess(startFen);
  const fens = [chess.fen()];
  for (const p of plies) {
    if (p.illegal) break;
    try { chess.move(p.san); } catch { break; }
    fens.push(chess.fen());
  }
  return fens;
}

/**
 * Spielt die Züge nach und markiert ab dem ersten, der nicht mehr geht, alles als illegal. Ein Zug, der
 * nur in anderer Schreibweise dasteht („Nf3+" statt „Nf3"), wird dabei in die Schreibweise des Bretts
 * gebracht. Züge nach einem illegalen bleiben als illegal stehen, damit der Nutzer sie sieht.
 */
export function revalidate(plies: readonly EditPly[], startFen = START_FEN): EditPly[] {
  const chess = new Chess(startFen);
  let broken = false;
  return plies.map(p => {
    if (broken) return { ...p, illegal: true };
    try {
      const m = chess.move(p.san);
      return { ...p, san: m.san, uci: m.from + m.to + (m.promotion ?? ''), illegal: false };
    } catch {
      broken = true;
      return { ...p, illegal: true };
    }
  });
}

/** Der Formular-Eintrag, der zu Halbzug `i` gehört: sein eigener, sonst der nächste noch offene. */
export function writtenIndexAt(plies: readonly EditPly[], i: number): number {
  if (i < plies.length && plies[i].w != null) return plies[i].w!;
  return nextEntryAt(plies, i);
}

/**
 * Der nächste Formular-Eintrag, den noch kein Halbzug vor `i` verbraucht hat: der nach dem letzten Halbzug MIT
 * Eintrag. Ein eingefügter Zug („nicht auf dem Formular") oder ein selbst eingefügter (`w = null`) verbraucht KEINEN —
 * vorher wurde jeder Halbzug dazwischen mitgezählt, und nach einem eingefügten Zug ging das Neuaufbereiten einen
 * Eintrag zu weit (gemeldet 2026-09-27: Prod-Partie 27, Zug 36 — der Spieler hatte Bd5 vergessen, der Auflöser Rb4
 * eingefügt; wer dort etwas tat, verlor den Eintrag „Kd7" und die Partie verrutschte). Ohne jeden Halbzug mit
 * Eintrag (Stand passt nicht zur Partie) gilt wie bisher der Halbzug-Index.
 */
export function nextEntryAt(plies: readonly EditPly[], i: number): number {
  for (let k = Math.min(i, plies.length) - 1; k >= 0; k--) {
    if (plies[k].w != null) return plies[k].w! + 1;
  }
  return i;
}

/** Ein Halbzug, den der Nutzer selbst eingegeben (oder gewählt) hat — gilt als bestätigt. */
export function userPly(san: string, uci: string, w: number | null, written: string, comment: string | null = null): EditPly {
  return { san, uci, w, written, match: 'user', uncertain: false, confirmed: true, options: null, comment, illegal: false };
}

/** Halbzüge des Servers (Auflösung) in die Form der Korrekturseite bringen. */
export function fromServer(plies: readonly ScoresheetPly[], comments: readonly (string | null)[] = []): EditPly[] {
  return plies.map((p, i) => ({
    san: p.san,
    uci: p.uci,
    w: p.w ?? null,
    written: p.written ?? '',
    match: p.match,
    uncertain: !!p.uncertain && !p.confirmed,
    confirmed: !!p.confirmed,
    options: p.options ?? null,
    comment: comments[i] ?? null,
    check: p.check ?? null,
    illegal: false,
  }));
}

/** Zurück in die Form, die der Server beim Speichern als Anzeige-Zustand ablegt. */
export function toServer(plies: readonly EditPly[]): ScoresheetPly[] {
  return plies.filter(p => !p.illegal).map(p => ({
    w: p.w, written: p.written, san: p.san, uci: p.uci, match: p.match,
    uncertain: p.uncertain && !p.confirmed, confirmed: p.confirmed, options: p.options, check: p.check ?? null,
  }));
}

/**
 * Was die Korrekturseite beim Server neu aufbereiten lässt, nachdem der Nutzer an Stelle `i` etwas
 * geändert hat: die festen Züge davor + der neue, und ab welchem Formular-Eintrag weitergelesen wird.
 * - ersetzen: der Eintrag von `i` ist verbraucht → weiter beim nächsten;
 * - einfügen: der Eintrag von `i` ist NOCH offen (der eingefügte Zug stand gar nicht auf dem Formular);
 * - löschen: der Eintrag von `i` war einer zu viel → weiter beim nächsten, ohne neuen Zug.
 * Hat der Halbzug `i` GAR KEINEN Eintrag (eingefügt, `w = null`), verbraucht er beim Ersetzen und Löschen auch
 * keinen: weiter beim nächsten offenen. Anhängen am Ende verbraucht dagegen den ersten offenen Eintrag.
 */
export function resolveRequest(plies: readonly EditPly[], i: number, mode: 'replace' | 'insert' | 'delete',
  newSan?: string): { prefix: string[]; writtenFrom: number } {
  const before = plies.slice(0, i).filter(p => !p.illegal).map(p => p.san);
  const w = writtenIndexAt(plies, i);
  const withoutEntry = i < plies.length && plies[i].w == null;
  if (mode === 'delete') return { prefix: before, writtenFrom: withoutEntry ? w : w + 1 };
  const prefix = [...before, newSan!];
  return { prefix, writtenFrom: mode === 'insert' || withoutEntry ? w : w + 1 };
}

/** Kommentare einer PGN je Halbzug (Index = Halbzug), über die Stellung nach dem Zug zugeordnet. */
export function commentsOf(pgn: string, count: number): (string | null)[] {
  const out: (string | null)[] = new Array(count).fill(null);
  try {
    const chess = new Chess();
    chess.loadPgn(pgn);
    const byFen = new Map(chess.getComments().map(c => [c.fen, c.comment] as const));
    // Die Stellung nach dem Zug aus der Zugliste selbst — ein Nachspielen ab der Grundstellung scheiterte bei einer
    // Partie mit FEN-Kopf schon am ersten Zug, und alle Kommentare fehlten.
    const history = chess.history({ verbose: true });
    for (let i = 0; i < Math.min(count, history.length); i++) {
      out[i] = byFen.get(history[i].after) ?? null;
    }
  } catch { /* keine Kommentare lesbar — dann eben keine */ }
  return out;
}

/** Ausgangsstellung einer PGN: der FEN-Kopf einer Stellungspartie (so, wie chess.js ihn beim Einlesen nimmt), sonst die
 *  Grundstellung. Die Halbzüge aus `pliesOfPgn` sind relativ zu ihr — Stellungen und Legalität müssen ab hier rechnen. */
export function startFenOf(pgn: string): string {
  const chess = new Chess();
  try { chess.loadPgn(pgn); } catch { return START_FEN; }
  const first = chess.history({ verbose: true })[0];
  return first ? first.before : chess.fen();
}

/** Halbzüge einer PGN (gewöhnliche Partie ohne Einlesung). */
export function pliesOfPgn(pgn: string): EditPly[] {
  const chess = new Chess();
  try { chess.loadPgn(pgn); } catch { return []; }
  const comments = commentsOf(pgn, chess.history().length);
  return chess.history({ verbose: true }).map((m, i) => ({
    san: m.san, uci: m.from + m.to + (m.promotion ?? ''), w: null, written: '', match: 'user',
    uncertain: false, confirmed: false, options: null, comment: comments[i], illegal: false,
  }));
}

/** Kopfdaten einer PGN (Header-Werte, „?" = leer). */
export function headersOf(pgn: string): Record<string, string> {
  const out: Record<string, string> = {};
  for (const m of pgn.matchAll(/^\[(\w+)\s+"((?:[^"\\]|\\.)*)"\]\s*$/gm)) {
    const v = m[2].replace(/\\"/g, '"').replace(/\\\\/g, '\\');
    out[m[1]] = v === '?' || v === '????.??.??' ? '' : v;
  }
  return out;
}

/** PGN-Datum („2026.06.05", auch mit „??") → `yyyy-MM-dd` für das Datumsfeld; sonst leer. */
export function isoDateOf(pgnDate: string | undefined): string {
  const m = /^(\d{4})\.(\d{2})\.(\d{2})$/.exec(pgnDate ?? '');
  return m ? `${m[1]}-${m[2]}-${m[3]}` : '';
}

/** Vom Server erzeugte Hinweise im PGN-Kommentar (siehe `ScoresheetScanService.CommentsFor`). */
export const SHEET_NOTE = 'sheet: ';
export const SHEET_OPEN = 'sheet, not resolved: ';
export const SHEET_EXTRA = 'sheet, extra: ';
/** Hinweis an einem Zug, der auf dem Formular FEHLTE (vom Auflöser eingeschoben). */
export const SHEET_MISSING = 'sheet: —';

/** Den Nutzer-Anteil eines Kommentars — ohne die vom Server erzeugten Formular-Hinweise. */
export function stripSheetNotes(comment: string | null | undefined): string | null {
  if (!comment) return null;
  const kept = comment.split(' | ').map(s => s.trim())
    .filter(s => s && !s.startsWith(SHEET_NOTE) && !s.startsWith(SHEET_OPEN) && !s.startsWith(SHEET_EXTRA));
  return kept.length ? kept.join(' | ') : null;
}

/**
 * Kommentare zum Speichern: der des Nutzers, bei einem nicht bestätigten zurechtgebogenen Zug dazu, was auf
 * dem Formular stand, und am letzten Zug die Einträge, die sich nicht zuordnen ließen. So erzählt das PGN
 * dasselbe wie die Korrekturseite — und ein bestätigter Zug verliert seinen Hinweis.
 */
export function commentsForSave(plies: readonly EditPly[], unresolved: readonly string[]): (string | null)[] {
  const legal = plies.filter(p => !p.illegal);
  const out = legal.map(p => {
    const parts: string[] = [];
    if (p.comment?.trim()) parts.push(p.comment.trim());
    if (!p.confirmed && (p.match === 'fuzzy' || p.match === 'guess')) parts.push(SHEET_NOTE + (p.written || '?'));
    if (!p.confirmed && p.match === 'inserted') parts.push(SHEET_MISSING);
    return parts.length ? parts.join(' | ') : null;
  });
  if (unresolved.length && out.length) {
    const note = SHEET_OPEN + unresolved.join(' ');
    const last = out.length - 1;
    out[last] = out[last] ? `${out[last]} | ${note}` : note;
  }
  return out;
}

/** Ein Ausschnitt des Formular-Fotos als CSS-Werte: das Foto liegt im Rahmen, verschoben und vergrößert, und die
 *  Markierung umrahmt den Eintrag. Alle Angaben in Prozent des Rahmens, `aspect` = Breite/Höhe des Rahmens. */
export interface CropView {
  aspect: number;
  imgW: number;
  imgH: number;
  left: number;
  top: number;
  markLeft: number;
  markTop: number;
  markW: number;
  markH: number;
}

/**
 * Der Ausschnitt um einen Formular-Eintrag. `box` = [x0, y0, x1, y1] in 0..1000 des aufrechten Fotos (vom Modell,
 * `ScoresheetTranscription.Entry.NormalizedBox`); die Pixelmaße braucht es für das Seitenverhältnis.
 *
 * Gezeigt wird mehr als der Kasten: seitlich die halbe Nachbarzelle, darüber und darunter je gut eine Zeile — ob ein
 * Eintrag verrutscht, in die falsche Zeile geschrieben oder durchgestrichen ist, sieht man erst mit dem Umfeld.
 * `null` ohne brauchbaren Kasten oder bevor das Foto geladen ist.
 */
export function cropView(box: readonly number[] | null | undefined, naturalW: number, naturalH: number): CropView | null {
  if (!box || box.length !== 4 || naturalW <= 0 || naturalH <= 0) return null;
  const [x0, y0, x1, y1] = box;
  const bw = x1 - x0, bh = y1 - y0;
  if (bw <= 0 || bh <= 0) return null;
  const clamp = (v: number) => Math.min(1000, Math.max(0, v));
  const rx0 = clamp(x0 - Math.max(bw * 0.6, 40)), rx1 = clamp(x1 + Math.max(bw * 0.6, 40));
  const ry0 = clamp(y0 - Math.max(bh * 1.3, 25)), ry1 = clamp(y1 + Math.max(bh * 1.3, 25));
  const cw = rx1 - rx0, ch = ry1 - ry0;
  const pct = (v: number) => Math.round(v * 1000) / 1000;
  return {
    aspect: pct((cw * naturalW) / (ch * naturalH)),
    imgW: pct((1000 / cw) * 100),
    imgH: pct((1000 / ch) * 100),
    left: pct((-rx0 / cw) * 100),
    top: pct((-ry0 / ch) * 100),
    markLeft: pct(((x0 - rx0) / cw) * 100),
    markTop: pct(((y0 - ry0) / ch) * 100),
    markW: pct((bw / cw) * 100),
    markH: pct((bh / ch) * 100),
  };
}

/** Die nächste noch offene unsichere Stelle ab `from` (über das Ende hinweg wieder von vorn); `null` = keine mehr.
 *  Nach dem Bestätigen oder Wählen einer Lesart geht die Korrekturseite dorthin. */
export function nextUncertainFrom(plies: readonly EditPly[], from: number): number | null {
  const n = plies.length;
  for (let k = 0; k < n; k++) {
    const i = (((from + k) % n) + n) % n;
    const p = plies[i];
    if (p.uncertain && !p.confirmed && !p.illegal) return i;
  }
  return null;
}
