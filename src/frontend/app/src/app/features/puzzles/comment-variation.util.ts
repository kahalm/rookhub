import { Chess } from 'chess.js';

/**
 * Macht Zugfolgen in Buch-/Kurs-Kommentaren anklickbar (Vorschau auf dem Brett).
 *
 * Kommentar-Varianten (z. B. „… weil 2.fxe6 … 2…Kc7 erlaubt.") nutzen eine EIGENE Zug-Nummerierung
 * und lassen Züge aus — die Basis-Stellung lässt sich also NICHT aus dem Text ableiten. Stattdessen
 * suchen wir unter den Stellungen der Puzzle-Hauptlinie (Start-FEN + nach jedem Zug) diejenige, aus
 * der die im Kommentar genannte SAN-Folge legal spielbar ist (längster legaler Präfix; bei Gleichstand
 * die späteste Stellung). Nur die so validierten Züge werden klickbar; alles andere bleibt Text.
 *
 * Ein Kommentar kann MEHRERE unabhängige Zweige enthalten (Chessable-Autoren schreiben sie als Prosa,
 * nicht als strukturierte PGN-Varianten). Beispiel:
 *   „… nur remis nach 39…d4 40.Kf4 d3 41.Ke3 … 43.h4 a4 . Weiß gewinnt nach 40.b5 a3 41.b6 …"
 * Hier ist „40.b5" ein NEUER Zweig (die Zugnummer springt von 43 zurück auf 40). Wir zerlegen die
 * Tokenfolge daher an solchen Zugnummer-Rücksprüngen in Zweige und lösen JEDEN Zweig eigenständig
 * gegen die Hauptlinie auf — sonst würde der zweite Zweig ab dem Rücksprung „illegal" und als Text
 * verloren gehen.
 *
 * Notation: Neben englischem SAN (KQRBN) werden auch deutsche Figurenbuchstaben (D/T/L/S → Q/R/B/N)
 * erkannt (übersetzte Kurse). Da D/T/L/S weder englische Figuren- noch Feld-Buchstaben sind, ist die
 * Zuordnung eindeutig; ein versehentlich als Zug erkanntes Wort spielt ohnehin nicht legal und bleibt
 * dadurch Text.
 */

export interface CommentSegment {
  /** Reiner Text (nicht klickbar). */
  text?: string;
  /** Anklickbarer Zug (im Kommentar-Wortlaut, z. B. „2.fxe6"). Nur gesetzt, wenn spielbar. */
  move?: string;
  /** Stellung NACH diesem Zug (FEN) — Brett-Vorschau. */
  fen?: string;
  /** Ausgangs-/Zielfeld für die lastMove-Markierung. */
  from?: string;
  to?: string;
}

/** Ein Zug-Schritt der aufgelösten Variante (Stellung nach dem Zug + Feldmarkierung). */
export interface VariationStep { san: string; fen: string; from: string; to: string; }

// Deutsche → englische Figurenbuchstaben (für übersetzte Kurse). K bleibt K.
const DE_PIECE: Record<string, string> = { D: 'Q', T: 'R', L: 'B', S: 'N' };

// SAN-Kern: Rochade (O oder 0) | Figurenzug (engl. KQRBN + dt. DTLS) | Bauernzug (+ optionale Umwandlung / Schach-Matt).
const SAN =
  '(?:O-O-O|O-O|0-0-0|0-0|[KQRBNDTLS][a-h]?[1-8]?x?[a-h][1-8]|[a-h](?:x[a-h])?[1-8](?:=[QRBNDTLS])?)[+#]?';
// Optionale Zugnummer davor („2." = Weiß / „2…"/„2..." = Schwarz) + der SAN-Zug. `g` für globales Scannen.
// Gruppen: 1=Zugnummer, 2=Punkte (>1 → Schwarzzug), 3=SAN.
const TOKEN_RE = new RegExp(`(?:(\\d+)\\.(\\.\\.|\\u2026)?\\s?)?(${SAN})`, 'g');

/** Übersetzt einen (evtl. deutschen) SAN-Zug in engl. SAN für chess.js. Englische Züge bleiben unverändert. */
function normalizeSan(san: string): string {
  let s = san.replace(/^0(?=-0)/, 'O').replace(/-0/g, '-O'); // 0-0(-0) → O-O(-O)
  const first = s[0];
  if (DE_PIECE[first]) s = DE_PIECE[first] + s.slice(1);
  return s.replace(/=([DTLS])/g, (_m, p: string) => '=' + DE_PIECE[p]);
}

/** Alle SAN-Token (nur der reine Zug, ohne Nummer) in Textreihenfolge. */
export function extractSanTokens(text: string): string[] {
  const re = new RegExp(TOKEN_RE.source, 'g');
  const out: string[] = [];
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) out.push(m[3]);
  return out;
}

interface Tok { san: string; ply?: number; }

/** Scannt die Token samt implizierter Ply-Nummer (aus Zugnummer + Farbe), für die Zweig-Erkennung. */
function scanTokens(text: string): Tok[] {
  const re = new RegExp(TOKEN_RE.source, 'g');
  const out: Tok[] = [];
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    const num = m[1] ? parseInt(m[1], 10) : undefined;
    const isBlack = !!m[2];
    // 0-basierte Ply: Weiß N → 2N-2, Schwarz N → 2N-1.
    const ply = num !== undefined ? (num * 2 - (isBlack ? 1 : 2)) : undefined;
    out.push({ san: m[3], ply });
  }
  return out;
}

/**
 * Zerlegt die Tokenfolge in Zweige. Ein neuer Zweig beginnt, sobald ein Token eine explizite
 * Zugnummer trägt, deren Ply NICHT größer ist als die zuletzt gesehene explizite Ply im laufenden
 * Zweig (= Rücksprung auf eine frühere Stellung → alternativer Zweig). Rückgabe: SAN-Listen je Zweig.
 */
export function splitBranches(text: string): string[][] {
  const toks = scanTokens(text);
  const branches: string[][] = [];
  let cur: string[] = [];
  let lastPly = -Infinity;
  for (const t of toks) {
    if (t.ply !== undefined && t.ply <= lastPly && cur.length) {
      branches.push(cur);
      cur = [];
      lastPly = -Infinity;
    }
    if (t.ply !== undefined) lastPly = t.ply;
    cur.push(t.san);
  }
  if (cur.length) branches.push(cur);
  return branches;
}

/** FEN-Liste der Hauptlinie: Start-FEN, dann nach jedem (UCI-)Zug. Ungültige Züge brechen die Kette ab. */
function mainlineFens(startFen: string, ucis: string[]): string[] {
  const c = new Chess(startFen);
  const fens = [c.fen()];
  for (const u of ucis) {
    if (!safeUci(c, u)) break;
    fens.push(c.fen());
  }
  return fens;
}

function safeUci(c: Chess, uci: string): boolean {
  try {
    const r = c.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci.slice(4) || undefined });
    return !!r;
  } catch { return false; }
}

/** Spielt die SAN-Folge ab `baseFen`, bis ein Zug illegal wird; liefert die Schritte des legalen Präfix. */
function playPrefix(baseFen: string, sans: string[]): VariationStep[] {
  const c = new Chess(baseFen);
  const steps: VariationStep[] = [];
  for (const san of sans) {
    let mv;
    try { mv = c.move(normalizeSan(san)); } catch { break; }
    if (!mv) break;
    steps.push({ san, fen: c.fen(), from: mv.from, to: mv.to });
  }
  return steps;
}

/**
 * Löst EINEN Zweig gegen die Hauptlinie auf: die Stellung mit dem LÄNGSTEN legalen Präfix der
 * SAN-Folge (bei Gleichstand die späteste). Gibt die Schritte des Präfix zurück (ggf. leer).
 */
export function resolveVariation(startFen: string, ucis: string[], sans: string[]): VariationStep[] {
  if (!sans.length) return [];
  const bases = mainlineFens(startFen, ucis);
  let best: VariationStep[] = [];
  for (let i = bases.length - 1; i >= 0; i--) {   // späteste Stellung zuerst → bei Gleichstand bevorzugt
    const s = playPrefix(bases[i], sans);
    if (s.length > best.length) best = s;
    if (best.length === sans.length) break;       // volle Variante gefunden
  }
  return best;
}

/**
 * Zerlegt einen Kommentar in Segmente (Text + klickbare Züge). Jeder Zweig wird eigenständig gegen die
 * Puzzle-Hauptlinie (`startFen`/`ucis`) aufgelöst; ein Zug ist NUR klickbar, wenn er Teil des legalen
 * Präfix SEINES Zweigs ist (sonst reiner Text).
 */
export function buildCommentSegments(text: string, startFen: string, ucis: string[]): CommentSegment[] {
  // Pro Token in Original-Reihenfolge den aufgelösten Schritt (oder null) bestimmen.
  const perToken: (VariationStep | null)[] = [];
  for (const branch of splitBranches(text)) {
    const steps = resolveVariation(startFen, ucis, branch);
    for (let k = 0; k < branch.length; k++) perToken.push(k < steps.length ? steps[k] : null);
  }

  const segments: CommentSegment[] = [];
  const re = new RegExp(TOKEN_RE.source, 'g');
  let last = 0;
  let tokenIdx = 0;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    if (m.index > last) segments.push({ text: text.slice(last, m.index) });
    const step = tokenIdx < perToken.length ? perToken[tokenIdx] : null;
    if (step) segments.push({ move: m[0].trim(), fen: step.fen, from: step.from, to: step.to });
    else segments.push({ text: m[0] });   // nicht spielbar → als Text belassen
    last = m.index + m[0].length;
    tokenIdx++;
  }
  if (last < text.length) segments.push({ text: text.slice(last) });
  return segments;
}
