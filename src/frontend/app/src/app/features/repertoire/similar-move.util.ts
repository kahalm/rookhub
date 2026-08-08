import { Chess } from 'chess.js';
import { SimilarMoveInput, SimilarMovePromotion } from '../../core/repertoire.service';

/**
 * Reine Helfer für den optionalen Zug der Ähnlichkeitssuche („ich erwäge hier 12.Nd5 — wo geht der
 * noch?"). Der Nutzer tippt SAN, weil das ist, was ein Schachspieler tippt; über die Leitung geht
 * aber from→to (+ Umwandlungsfigur). Diese Umrechnung passiert genau hier, auf der ANKER-Stellung —
 * dieselbe SAN kann in zwei Stellungen zwei verschiedene Züge meinen, und `Nbd2`/`Nd2` meinen
 * denselben. Kein Zustand, kein Service, keine Anzeige.
 */

/** Ergebnis der Eingabe-Auswertung: entweder ein Zug, oder „leer", oder „hier nicht legal". */
export interface ParsedMoveInput {
  /** Auf der Ankerstellung aufgelöster Zug; `null` bei leerer oder unlesbarer Eingabe. */
  move: SimilarMoveInput | null;
  /** Kanonischer SAN, wie chess.js ihn schreibt (Eingabe `Ncd5` → `Nd5`); '' ohne Zug. */
  san: string;
  /** Eingabe war nicht leer, ließ sich auf dieser Stellung aber nicht als Zug lesen. */
  invalid: boolean;
}

const EMPTY: ParsedMoveInput = { move: null, san: '', invalid: false };

/**
 * Schreibvarianten, die ein Mensch tippt, aber chess.js ablehnt, auf die kanonische Form bringen:
 * Rochade mit Nullen/Kleinbuchstaben (`0-0`, `o-o-o`, `OO`) und ein klein geschriebener
 * Figurenbuchstabe (`nd5`). `b` bleibt bewusst unangetastet-mehrdeutig: es ist Läufer UND
 * b-Linie — dafür werden beide Lesarten als Kandidaten probiert.
 */
function candidates(raw: string): string[] {
  const t = raw.trim().replace(/\s+/g, '');
  if (!t) return [];
  const out: string[] = [];
  const castle = /^([0oO])([-\s]?[0oO]){1,2}$/.exec(t);
  if (castle) {
    const zeros = (t.match(/[0oO]/g) ?? []).length;
    out.push(zeros >= 3 ? 'O-O-O' : 'O-O');
  }
  out.push(t);
  if (/^[nbrqk]/.test(t)) out.push(t[0].toUpperCase() + t.slice(1));
  return out;
}

/**
 * Liest die SAN-Eingabe auf der Ankerstellung. Ist der Zug dort nicht legal, kommt
 * `invalid: true` zurück — der Aufrufer sagt das am Feld, statt eine leere Trefferliste zu zeigen
 * (eine leere Liste hieße „nirgends gespielt", und das wäre eine andere Aussage).
 * Eine FEN, die chess.js nicht lädt (z. B. Muster-Diagramm ohne König), gilt ebenfalls als
 * „hier nicht legal" — auf so einem Brett gibt es keine Züge zu prüfen.
 */
export function parseMoveInput(fen: string, text: string): ParsedMoveInput {
  const tries = candidates(text ?? '');
  if (tries.length === 0) return EMPTY;
  for (const san of tries) {
    try {
      const chess = new Chess(fen);
      const m = chess.move(san);
      return {
        move: {
          from: m.from as string,
          to: m.to as string,
          ...(m.promotion ? { promotion: m.promotion as SimilarMovePromotion } : {}),
        },
        san: m.san,
        invalid: false,
      };
    } catch { /* nächster Kandidat; alle durch → invalid */ }
  }
  return { move: null, san: '', invalid: true };
}

/** Vergleichsschlüssel eines Zuges — damit „Eingabe geändert" nicht mit „Zug geändert" verwechselt
 * wird (`Ncd5` nach `Nd5` getippt ist derselbe Zug und darf keine neue Suche auslösen). */
export function moveKey(m: SimilarMoveInput | null): string {
  return m ? `${m.from}${m.to}${m.promotion ?? ''}` : '';
}

/**
 * Lange Notation für die schwächere Trefferstufe: „Nf3-d5" — dort zieht dieselbe Figurenart aufs
 * gleiche Zielfeld, aber von woanders, und genau das Ausgangsfeld ist die Information. Der
 * Figurenbuchstabe kommt aus dem SAN (Bauernzüge und Rochade haben keinen).
 */
export function longMoveLabel(san: string, from: string, to: string): string {
  if (!from || !to) return san ?? '';
  const piece = /^[KQRBN]/.test(san ?? '') ? san[0] : '';
  return `${piece}${from}-${to}`;
}
