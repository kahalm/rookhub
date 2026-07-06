import { Chess } from 'chess.js';

/**
 * Macht Zugfolgen in Buch-/Kurs-Kommentaren anklickbar (Vorschau auf dem Brett).
 *
 * Kommentar-Varianten (z. B. „… weil 2.fxe6 … 2…Kc7 erlaubt.") nutzen eine EIGENE Zug-Nummerierung
 * und lassen Züge aus — die Basis-Stellung lässt sich also NICHT frei aus dem Text ableiten. Wir
 * verankern jeden Zweig an der zu seiner Zugnummer passenden Hauptlinien-Stellung (Start-FEN + nach
 * jedem Zug); ohne Nummer suchen wir die früheste Stellung, aus der die Folge legal ist (= die aktuelle
 * Stellung bei Einleitungs-Kommentaren). Nur so validierte Züge werden klickbar; alles andere bleibt Text.
 *
 * ZWEIGE: Ein Kommentar kann mehrere unabhängige Linien enthalten (Chessable-Autoren schreiben sie als
 * Prosa). Wir trennen an
 *   (a) Zugnummer-Rücksprüngen  („… 43.h4 a4 . Weiß gewinnt nach 40.b5 …" → „40.b5" ist ein neuer Zweig),
 *   (b) Alternativ-Wörtern      („eine Zug wie c5, oder a5" → zwei getrennte Alternativzüge, KEINE Folge —
 *                                sonst spielte der Parser c5 mit Weiß und a5 danach mit Schwarz),
 * und lösen JEDEN Zweig eigenständig auf.
 *
 * NOTATION: Neben englischem SAN (KQRBN) werden deutsche Figurenbuchstaben (D/T/L/S → Q/R/B/N, inkl.
 * Umwandlung =D usw.) erkannt (übersetzte Kurse). Da D/T/L/S weder englische Figuren- noch Feldbuchstaben
 * sind, ist die Zuordnung eindeutig; ein versehentlich erkanntes Wort spielt ohnehin nicht legal → Text.
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

/** Ein Zweig: SAN-Folge + optionale absolute Ply des ERSTEN Zugs (aus seiner Zugnummer). */
export interface CommentBranch { sans: string[]; startPly?: number; }

// Deutsche → englische Figurenbuchstaben (für übersetzte Kurse). K bleibt K.
const DE_PIECE: Record<string, string> = { D: 'Q', T: 'R', L: 'B', S: 'N' };

// SAN-Kern: Rochade (O oder 0) | Figurenzug (engl. KQRBN + dt. DTLS) | Bauernzug (+ Umwandlung / Schach-Matt).
const SAN =
  '(?:O-O-O|O-O|0-0-0|0-0|[KQRBNDTLS][a-h]?[1-8]?x?[a-h][1-8]|[a-h](?:x[a-h])?[1-8](?:=[QRBNDTLS])?)[+#]?';
// Optionale Zugnummer davor („2." = Weiß / „2…"/„2..." = Schwarz) + der SAN-Zug. `g` für globales Scannen.
// Gruppen: 1=Zugnummer, 2=Punkte (>1 → Schwarzzug), 3=SAN.
const TOKEN_RE = new RegExp(`(?:(\\d+)\\.(\\.\\.|\\u2026)?\\s?)?(${SAN})`, 'g');

// „Alternativ"-Signale zwischen zwei Zug-Token → neuer Zweig (kein Fortsetzungszug).
const ALT_WORD = /\b(?:or|oder|bzw\.?|beziehungsweise|resp\.?|respectively)\b|\//i;

/** Übersetzt einen (evtl. deutschen) SAN-Zug in engl. SAN für chess.js. Englische Züge bleiben unverändert. */
function normalizeSan(san: string): string {
  let s = san.replace(/^0(?=-0)/, 'O').replace(/-0/g, '-O'); // 0-0(-0) → O-O(-O)
  const first = s[0];
  if (DE_PIECE[first]) s = DE_PIECE[first] + s.slice(1);
  return s.replace(/=([DTLS])/g, (_m, p: string) => '=' + DE_PIECE[p]);
}

interface Tok { san: string; ply?: number; start: number; end: number; }

/** Scannt die Token samt Position + implizierter (absoluter) Ply aus Zugnummer + Farbe. */
function scanTokens(text: string): Tok[] {
  const re = new RegExp(TOKEN_RE.source, 'g');
  const out: Tok[] = [];
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    const num = m[1] ? parseInt(m[1], 10) : undefined;
    const isBlack = !!m[2];
    // 0-basierte Ply: Weiß N → 2N-2, Schwarz N → 2N-1.
    const ply = num !== undefined ? (num * 2 - (isBlack ? 1 : 2)) : undefined;
    out.push({ san: m[3], ply, start: m.index, end: m.index + m[0].length });
  }
  return out;
}

/** Alle SAN-Token (nur der reine Zug, ohne Nummer) in Textreihenfolge. */
export function extractSanTokens(text: string): string[] {
  return scanTokens(text).map(t => t.san);
}

/**
 * Zerlegt die Tokenfolge in Zweige (siehe Datei-Kopf: Zugnummer-Rücksprung ODER Alternativ-Signal).
 * Ein Komma trennt nur, wenn der Folgezug KEINE eigene Nummer hat (Aufzählung „c5, a5, Kd4"); ein
 * Komma vor einem nummerierten Zug („… a3, 41.b6 …") gilt als Fortsetzung, nicht als Alternative.
 */
export function branches(text: string): CommentBranch[] {
  const toks = scanTokens(text);
  const out: CommentBranch[] = [];
  let cur: string[] = [];
  let curStartPly: number | undefined;
  let lastPly = -Infinity;
  let prevEnd = 0;

  for (const t of toks) {
    let boundary = false;
    if (cur.length) {
      const gap = text.slice(prevEnd, t.start);
      if (t.ply !== undefined && t.ply <= lastPly) boundary = true;          // (a) Zugnummer-Rücksprung
      else if (ALT_WORD.test(gap)) boundary = true;                          // (b) „oder"/„or"/„/" …
      else if (gap.includes(',') && t.ply === undefined) boundary = true;    // Komma + numerloser Zug = Aufzählung
    }
    if (boundary) {
      out.push({ sans: cur, startPly: curStartPly });
      cur = [];
      curStartPly = undefined;
      lastPly = -Infinity;
    }
    if (t.ply !== undefined) {
      lastPly = t.ply;
      if (!cur.length) curStartPly = t.ply;
    }
    cur.push(t.san);
    prevEnd = t.end;
  }
  if (cur.length) out.push({ sans: cur, startPly: curStartPly });
  return out;
}

/** Nur die SAN-Listen der Zweige (Test-/Debug-Hilfe). */
export function splitBranches(text: string): string[][] {
  return branches(text).map(b => b.sans);
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

/** 0-basierte Ply der Start-FEN (aus Vollzugzahl + Farbe), zum Umrechnen absoluter Zugnummern → Basis-Index. */
function baseStartPly(startFen: string): number {
  const parts = startFen.split(' ');
  const fullmove = parseInt(parts[5] || '1', 10) || 1;
  const white = parts[1] !== 'b';
  return (fullmove - 1) * 2 + (white ? 0 : 1);
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
 * Löst EINEN Zweig gegen die Hauptlinie auf: längster legaler Präfix. Reihenfolge der geprüften
 * Basis-Stellungen: zuerst die zur `startPly` passende (aus der Zugnummer abgeleitet — maßgeblich bei
 * Gleichstand), danach von der FRÜHESTEN zur spätesten (= aktuelle Stellung zuerst; wichtig für
 * numerlose Einleitungs-Alternativen, die an der Ausgangsstellung gemeint sind). Der längste Präfix
 * gewinnt immer; der Anker verliert nie Züge (kein Abschneiden).
 */
export function resolveVariation(startFen: string, ucis: string[], sans: string[], startPly?: number): VariationStep[] {
  if (!sans.length) return [];
  const bases = mainlineFens(startFen, ucis);
  const order: number[] = [];
  if (startPly !== undefined) {
    const anchor = startPly - baseStartPly(startFen);
    if (anchor >= 0 && anchor < bases.length) order.push(anchor);
  }
  for (let i = 0; i < bases.length; i++) if (!order.includes(i)) order.push(i);

  let best: VariationStep[] = [];
  for (const i of order) {
    const s = playPrefix(bases[i], sans);
    if (s.length > best.length) best = s;   // strikt > → bei Gleichstand gewinnt die zuerst geprüfte (Anker, sonst früheste)
    if (best.length === sans.length) break;
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
  for (const b of branches(text)) {
    const steps = resolveVariation(startFen, ucis, b.sans, b.startPly);
    for (let k = 0; k < b.sans.length; k++) perToken.push(k < steps.length ? steps[k] : null);
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
