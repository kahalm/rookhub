import { localizeSan } from '../../shared/chess/keyboard-move.util';

/**
 * Vermerk „Warum diese Aufgabe“ einer Linie im Kurs „Persönliche Taktiken“ (1.001.0, Spiegel von `BookPuzzleWhyDto`): die
 * Motivstelle der Partie und die Art des Treffers, sprachneutral. Der Satz entsteht hier in der UI-Sprache (`book.why.*`).
 */
export interface LineWhy {
  /** Halbzug der Motivstelle (Stellung vor dem Zug). */
  ply: number;
  /** Die Seite, die das Motiv hatte. */
  white: boolean;
  /** missedTactic | allowedTactic | missedMate | playedMate | forcedMate | mateThreat */
  reason: string;
  /** Halbzug des Partiezugs, den der Satz nennt (bei allowedTactic der zulassende Zug davor). */
  movePly: number;
  /** Dieser Zug in SAN (englisch); fehlt an der Endstellung. */
  moveSan?: string | null;
  /** Lichess-Thema der Stelle (fork, backRankMate …). */
  theme?: string | null;
  mateIn?: number | null;
  /** strict | family | theme */
  match: string;
  /** Thema, aus dem bei match = theme aufgefüllt wurde. */
  matchTheme?: string | null;
  /** Buchdiagramm statt Lichess-Aufgabe. */
  book?: boolean;
  /** Ziehende Figur (K Q R B N P) — nur Gabel/Fesselung/Spieß mit gleicher Signatur. */
  piece?: string | null;
  /** Zielfiguren (Gabel alle, Fesselung/Spieß vorn dann hinten). */
  targets?: string | null;
  /** Weitere Stellen mit demselben Bild (Halbzüge). */
  also?: number[] | null;
}

export type Translate = (key: string, params?: Record<string, unknown>) => string;

const REASONS = ['missedTactic', 'allowedTactic', 'missedMate', 'playedMate', 'forcedMate', 'mateThreat'];
const TACTICS = ['missedTactic', 'allowedTactic'];
const PIECE_MOTIFS = ['fork', 'pin', 'skewer'];
const PIECES = 'KQRBNP';

/** Zugnummer wie im Titel der Linie: „21.“ (Weiß) bzw. „21…“, dahinter der Zug in den Figurenbuchstaben der Sprache. */
export function whyMoveLabel(movePly: number, san: string | null | undefined, lang: string | null | undefined): string {
  const number = `${Math.floor(movePly / 2) + 1}${movePly % 2 === 0 ? '.' : '…'}`;
  return san ? `${number}${localizeSan(san, lang)}` : number;
}

/** Name eines Lichess-Themas (`puzzleThemes.*`), fällt auf den Schlüssel selbst zurück. */
function themeName(theme: string, t: Translate): string {
  const key = `puzzleThemes.${theme}`;
  const name = t(key);
  return name && name !== key ? name : theme;
}

/**
 * Der Satz „Warum diese Aufgabe“ in der UI-Sprache; `null` = nichts Verlässliches zu sagen (unbekannter Grund — eine neuere
 * Fassung des Servers als die App). Zwei Teile: die Stelle der Partie, dann die Art des Treffers.
 */
export function lineWhyText(why: LineWhy | null | undefined, t: Translate, lang: string | null | undefined): string | null {
  if (!why || !REASONS.includes(why.reason)) return null;
  const side = t(why.white ? 'book.why.white' : 'book.why.black');
  const theme = why.theme ? themeName(why.theme, t) : '';
  const tactic = TACTICS.includes(why.reason);
  if (tactic && !theme) return null;
  const parts = [t(`book.why.reason.${why.reason}`, {
    move: whyMoveLabel(why.movePly, why.moveSan, lang), side, theme, n: why.mateIn ?? 1,
  })];

  const subject = t(why.book ? 'book.why.subject.book' : 'book.why.subject.puzzle');
  if (why.match === 'theme') {
    const filled = why.matchTheme ?? why.theme;
    if (filled) parts.push(t('book.why.match.theme', { subject, theme: themeName(filled, t) }));
  } else if (why.match === 'strict' || why.match === 'family') {
    const pieces = tactic && why.match === 'strict' && why.theme && PIECE_MOTIFS.includes(why.theme)
      && why.piece && PIECES.includes(why.piece) && why.targets && [...why.targets].every(c => PIECES.includes(c));
    if (pieces) {
      const name = (p: string) => t(`book.why.piece.${p}`);
      const targets = [...why.targets!].map(name);
      const all = targets.length > 1
        ? `${targets.slice(0, -1).join(', ')} ${t('book.why.and')} ${targets[targets.length - 1]}` : targets[0];
      parts.push(t(`book.why.pattern.${why.theme}`, { subject, piece: name(why.piece!), targets: all, front: targets[0], back: targets[1] ?? targets[0] }));
    } else {
      parts.push(t(`book.why.match.${tactic ? 'tactic' : 'mate'}.${why.match}`, { subject }));
    }
  }
  if (why.also?.length) {
    parts.push(t('book.why.also', { moves: why.also.map(p => whyMoveLabel(p, null, lang)).join(', ') }));
  }
  return parts.join(' ');
}
