import { Chess, Move } from 'chess.js';
import { Key } from 'chessground/types';

/**
 * Zug-Eingabe per Tastatur (Codereview F2-004): ein getippter Zug — SAN („Nf3", „Sf3", „exd5", „e8=D", „O-O")
 * oder Koordinaten („g1f3", „g1-f3", „Ng1-f3", „e7e8q") — wird in der gegebenen Stellung zu von/nach/Umwandlung.
 *
 * Nur LEGALE Züge kommen heraus, und nur eindeutige: „Nd2", wenn zwei Springer nach d2 können, ist `null`
 * (dafür gibt es „Nbd2" oder die Koordinaten). Ohne Brett, ohne Angular — getestet mit literalen Stellungen.
 */
export interface KeyboardMove {
  orig: Key;
  dest: Key;
  /** Umwandlungsfigur, wenn sie getippt wurde. */
  promotion?: 'q' | 'r' | 'b' | 'n';
  /** Umwandlung OHNE genannte Figur („e8", „e7e8") — die Auswahl muss fragen. */
  needsPromotion: boolean;
}

const EN: Readonly<Record<string, string>> = { K: 'K', Q: 'Q', R: 'R', B: 'B', N: 'N' };

/**
 * Figurenbuchstaben je Oberflächensprache → englischer SAN-Buchstabe. Englisch gilt überall dort, wo die Sprache
 * den Buchstaben nicht selbst belegt: deutsch/kroatisch kommen D T L S dazu, ungarisch V B F H — und dort heißt
 * B der TURM (bástya), nicht der Läufer. Andere Sprachen tippen englisch (oder Koordinaten).
 */
const LETTERS: Readonly<Record<string, Readonly<Record<string, string>>>> = {
  de: { ...EN, D: 'Q', T: 'R', L: 'B', S: 'N' },
  hr: { ...EN, D: 'Q', T: 'R', L: 'B', S: 'N' },
  hu: { ...EN, V: 'Q', B: 'R', F: 'B', H: 'N' },
};

export function pieceLettersFor(lang: string | null | undefined): Readonly<Record<string, string>> {
  return LETTERS[(lang ?? '').split('-')[0].toLowerCase()] ?? EN;
}

/** SAN ohne Zeichen, die für die Identität des Zugs nichts sagen: Schach/Matt, Bewertung, Schlagzeichen, „=". */
function sanKey(san: string): string {
  return san.replace(/[+#!?x:=]/g, '');
}

/** Umwandlungsbuchstabe → q/r/b/n. Klein q/r/b/n ist die übliche Koordinaten-Schreibweise (immer englisch),
 *  sonst gilt der Buchstabe der Sprache. */
function promotionOf(letter: string, letters: Readonly<Record<string, string>>): 'q' | 'r' | 'b' | 'n' | null {
  if (/^[qrbn]$/.test(letter)) return letter as 'q' | 'r' | 'b' | 'n';
  const en = letters[letter.toUpperCase()];
  return en && en !== 'K' ? (en.toLowerCase() as 'q' | 'r' | 'b' | 'n') : null;
}

function pick(cands: Move[], promotion: 'q' | 'r' | 'b' | 'n' | undefined): KeyboardMove | null {
  if (!cands.length) return null;
  const first = cands[0];
  if (first.promotion) {
    if (!promotion) return { orig: first.from as Key, dest: first.to as Key, needsPromotion: true };
    const m = cands.find(c => c.promotion === promotion);
    return m ? { orig: m.from as Key, dest: m.to as Key, promotion, needsPromotion: false } : null;
  }
  if (promotion || cands.length > 1) return null;
  return { orig: first.from as Key, dest: first.to as Key, needsPromotion: false };
}

/** Der getippte SAN-Text in englischer Schreibweise und ohne Schlag-/Bindezeichen; `null` = kein SAN. */
function toEnglishSanKey(text: string, letters: Readonly<Record<string, string>>): string | null {
  const s = text.replace(/\s+/g, '');
  if (/^[0oO]-?[0oO]$/.test(s)) return 'O-O';
  if (/^[0oO]-?[0oO]-?[0oO]$/.test(s)) return 'O-O-O';
  let body = s;
  let promo = '';
  const promoMatch = /^(.*[1-8])=?([A-Za-z])$/.exec(body);
  if (promoMatch) {
    const p = promotionOf(promoMatch[2], letters);
    if (!p) return null;
    body = promoMatch[1];
    promo = p.toUpperCase();
  }
  const head = body.charAt(0);
  if (/[A-Z]/.test(head)) {
    const en = letters[head];
    if (!en) return null;
    body = en + body.slice(1);
  }
  body = body.replace(/[x:\-=]/g, '');
  return body ? body + promo : null;
}

export function parseKeyboardMove(fen: string, text: string, lang?: string | null): KeyboardMove | null {
  let chess: Chess;
  try { chess = new Chess(fen); } catch { return null; }   // unlesbare/illegale Stellung (Info-Diagramm)
  const raw = (text ?? '').trim().replace(/[+#!?]+$/, '').trim();
  if (!raw) return null;
  const letters = pieceLettersFor(lang);
  const legal = chess.moves({ verbose: true });

  // Koordinaten, wahlweise mit Figurenbuchstaben davor („Ng1-f3") — der muss dann zur ziehenden Figur passen.
  const coord = /^([A-Z])?([a-h][1-8])\s*[-x:]?\s*([a-h][1-8])\s*=?\s*([A-Za-z])?$/.exec(raw);
  if (coord) {
    const [, piece, from, to, promoLetter] = coord;
    const promotion = promoLetter ? promotionOf(promoLetter, letters) : undefined;
    if (promotion === null) return null;
    let cands = legal.filter(m => m.from === from && m.to === to);
    if (piece) {
      const en = letters[piece];
      if (!en) return null;
      cands = cands.filter(m => m.piece === en.toLowerCase());
    }
    return pick(cands, promotion);
  }

  const key = toEnglishSanKey(raw, letters);
  if (!key) return null;
  const exact = legal.filter(m => sanKey(m.san) === key);
  if (exact.length === 1) {
    const m = exact[0];
    return m.promotion
      ? { orig: m.from as Key, dest: m.to as Key, promotion: m.promotion as KeyboardMove['promotion'], needsPromotion: false }
      : { orig: m.from as Key, dest: m.to as Key, needsPromotion: false };
  }
  if (exact.length > 1) return null;
  // Umwandlung ohne Figur („e8", „dxe8"): alle passenden Züge sind derselbe Bauernzug mit verschiedenen Figuren.
  const promos = legal.filter(m => m.promotion && sanKey(m.san).replace(/[QRBN]$/, '') === key);
  if (promos.length && promos.every(m => m.from === promos[0].from && m.to === promos[0].to)) {
    return { orig: promos[0].from as Key, dest: promos[0].to as Key, needsPromotion: true };
  }
  return null;
}
