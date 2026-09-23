import { Chess } from 'chess.js';
import { tryLoadFen } from './puzzle-move.util';

/**
 * Macht Zugfolgen in Buch-/Kurs-Kommentaren anklickbar (Vorschau auf dem Brett).
 *
 * Kommentar-Varianten (z. B. „… weil 2.fxe6 … 2…Kc7 erlaubt.") nutzen eine EIGENE Zug-Nummerierung
 * und lassen Züge aus — die Basis-Stellung lässt sich also NICHT frei aus dem Text ableiten. Wir
 * verankern jeden Zweig an der zu seiner Zugnummer passenden Hauptlinien-Stellung (Start-FEN + nach
 * jedem Zug) — und NUR dort: geht er dort nicht, bleibt er Text, statt in einer anderen Stellung der
 * Partie zu landen. Ein Zweig ohne Nummer hinter einem nummerierten ist Alternative oder Fortsetzung
 * DIESES Zweigs ({@link BranchContext}); erst ohne jede Nummer im Kommentar suchen wir die früheste
 * Stellung, aus der die Folge legal ist (= die aktuelle Stellung bei Einleitungs-Kommentaren). Ein
 * mehrdeutiger Zug („Ne4" mit zwei Springern) gilt nur, wenn die Folge danach die Figur festlegt
 * ({@link playOn}). Nur so validierte Züge werden klickbar; alles andere bleibt Text.
 *
 * ZWEIGE: Ein Kommentar kann mehrere unabhängige Linien enthalten (Chessable-Autoren schreiben sie als
 * Prosa). Wir trennen an
 *   (a) Zugnummer-Rücksprüngen  („… 43.h4 a4 . Weiß gewinnt nach 40.b5 …" → „40.b5" ist ein neuer Zweig),
 *   (b) Alternativ-Wörtern      („eine Zug wie c5, oder a5" → zwei getrennte Alternativzüge, KEINE Folge —
 *                                sonst spielte der Parser c5 mit Weiß und a5 danach mit Schwarz),
 * und lösen JEDEN Zweig eigenständig auf.
 *
 * VORWÄRTS-SPRUNG: Bricht die Auflösung genau an einem nummerierten Zug ab, der ÜBER die erwartete
 * Fortsetzung hinausspringt („… nach 2.Sf3 … 4.c3 Sc6" — dazwischen fehlen Züge), wird der Rest als
 * eigener Zweig aufgelöst, und zwar NUR an der Hauptlinien-Stellung seiner Zugnummer (kein Raten
 * anderer Stellungen). So bleiben absichtlich zusammenhängende Folgen mit Nummernlücke unverändert.
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

/** Ein Zweig: SAN-Folge + optionale absolute Ply des ERSTEN Zugs (aus seiner Zugnummer).
 *  `plies` = absolute Ply je Zug, soweit er eine Zugnummer trägt (parallel zu `sans`). */
export interface CommentBranch { sans: string[]; startPly?: number; plies?: (number | undefined)[]; }

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

// Ein nacktes Feld („d6") ist in Prosa oft eine ORTSANGABE, kein Bauernzug: „keine Angst vor dem Springer
// auf d6", „the knight on d6", „das e5-Feld". Solche Token zählen nicht als Zug. Bewusst eng: nur ohne
// Zugnummer, und nur direkt nach einer Präposition bzw. direkt vor „Feld"/„square"/„Bauer"/„pawn".
// „nach" fehlt absichtlich — „nach d4" heißt meist „nach dem Zug d4".
const BARE_SQUARE = /^[a-h][1-8]$/;
const SQUARE_BEFORE = /(?:^|[^\p{L}])(?:auf|von|vom|über|ueber|zum|zur|feld|felder|on|onto|square|squares|from|via|to)\s+$/iu;
// Nach dem Feld folgt oft die FIGUR darauf („the e7 bishop", „der e7-Läufer") — auch das ist eine
// Ortsangabe. Ohne die Figurennamen las der Parser „the e7 bishop" als Bauernzug e7; der stand dann
// als erster, unspielbarer Zug der Variante davor und riss sie mit (gemeldet 2026-09-18).
const SQUARE_AFTER =
  /^(?:\s|-)(?:feld|felder|felds|square|squares|bauer|bauern|pawn|pawns|bishop|bishops|knight|knights|rook|rooks|queen|queens|king|kings|l[äa]ufer|springer|turm|t[üu]rme|dame|k[öo]nig)(?![\p{L}])/iu;

function isSquareMention(text: string, san: string, hasNumber: boolean, start: number, end: number): boolean {
  if (hasNumber || !BARE_SQUARE.test(san)) return false;
  return SQUARE_BEFORE.test(text.slice(Math.max(0, start - 20), start)) || SQUARE_AFTER.test(text.slice(end, end + 12));
}

/** Scannt die Token samt Position + implizierter (absoluter) Ply aus Zugnummer + Farbe.
 *  Feldangaben (siehe {@link isSquareMention}) fehlen — sie bleiben Text und trennen keine Zweige. */
function scanTokens(text: string): Tok[] {
  const re = new RegExp(TOKEN_RE.source, 'g');
  const out: Tok[] = [];
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    if (isSquareMention(text, m[3], !!m[1], m.index, m.index + m[0].length)) continue;
    const num = m[1] ? parseInt(m[1], 10) : undefined;
    const isBlack = !!m[2];
    // 0-basierte Ply: Weiß N → 2N-2, Schwarz N → 2N-1.
    const ply = num !== undefined ? (num * 2 - (isBlack ? 1 : 2)) : undefined;
    out.push({ san: m[3], ply, start: m.index, end: m.index + m[0].length });
  }
  return dropMentionedMoves(text, out);
}

/**
 * Wirft Züge OHNE Zugnummer weg, die eine SPÄTERE Zugnummer widerlegt — sie sind im Text erwähnt,
 * nicht gespielt. Beispiel (gemeldet 2026-09-18): „70…c6 Preventing Rd5+ runs into mate! 71.Rc3+ Kb5
 * …". Nach 70…c6 wäre `Rd5+` der Halbzug 140 — den beansprucht aber `71.Rc3+` selbst. Beide können
 * nicht derselbe Zug sein, also ist `Rd5+` eine Erwähnung. Ohne diese Regel hängte der Parser `Rd5+`
 * an die Variante, danach war Schwarz am Zug, `71.Rc3+` wurde illegal — die eigentliche Mattführung
 * blieb unklickbar und die Vorschau endete im falschen Zug.
 *
 * <p>Bewusst eng: Es entscheidet die NUMMERIERUNG, nicht die umgebende Prosa. Ein „besser war Sc3"
 * ohne widersprechende Nummer bleibt also klickbar, ebenso jede nummerierte Folge. Nach einem
 * Alternativ-Signal („oder", „/") zählt die Nummerierung nicht weiter — dort beginnt ein eigener
 * Zweig, dessen erster Zug keine Fortsetzung ist.</p>
 */
function dropMentionedMoves(text: string, toks: Tok[]): Tok[] {
  const out: Tok[] = [];
  let nextPly: number | undefined;    // Ply, die der nächste Fortsetzungszug hätte
  let lastNumbered: number | undefined;
  for (let i = 0; i < toks.length; i++) {
    const t = toks[i];
    const gap = i > 0 ? text.slice(toks[i - 1].end, t.start) : '';
    if (i > 0 && ALT_WORD.test(gap)) { nextPly = undefined; lastNumbered = undefined; }   // neuer Zweig

    if (t.ply === undefined && nextPly !== undefined) {
      const later = toks.slice(i + 1).find(n => n.ply !== undefined);
      // Beweiskräftig ist die spätere Nummer nur, wenn sie überhaupt eine FORTSETZUNG sein kann:
      // springt sie hinter die letzte Nummer zurück, beginnt dort ein eigener Zweig („… 43.h4 a4.
      // Weiß gewinnt nach 40.b5") und sagt nichts über diesen Zug. Beansprucht sie dagegen die Ply,
      // die dieser numerlose Zug hätte, können nicht beide derselbe Halbzug sein → Erwähnung.
      if (later && later.ply! >= lastNumbered! && later.ply! <= nextPly) continue;
    }
    out.push(t);
    if (t.ply !== undefined) { lastNumbered = t.ply; nextPly = t.ply + 1; }
    else if (nextPly !== undefined) nextPly++;
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
  let curPlies: (number | undefined)[] = [];
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
      out.push({ sans: cur, startPly: curStartPly, plies: curPlies });
      cur = [];
      curPlies = [];
      curStartPly = undefined;
      lastPly = -Infinity;
    }
    if (t.ply !== undefined) {
      lastPly = t.ply;
      if (!cur.length) curStartPly = t.ply;
    }
    cur.push(t.san);
    curPlies.push(t.ply);
    prevEnd = t.end;
  }
  if (cur.length) out.push({ sans: cur, startPly: curStartPly, plies: curPlies });
  return out;
}

/** Nur die SAN-Listen der Zweige (Test-/Debug-Hilfe). */
export function splitBranches(text: string): string[][] {
  return branches(text).map(b => b.sans);
}

/** FEN-Liste der Hauptlinie: Start-FEN, dann nach jedem (UCI-)Zug. Ungültige Züge brechen die Kette ab.
 *  Leer, wenn chess.js schon die Start-FEN ablehnt (Chessable-Muster-Diagramme ohne König o. Ä.) — dann
 *  gibt es keine Basis-Stellung, gegen die ein Kommentar-Zug validiert werden könnte. */
function mainlineFens(startFen: string, ucis: string[]): string[] {
  const c = tryLoadFen(startFen);
  if (!c) return [];
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

/** Spielt die SAN-Folge ab `baseFen`, bis ein Zug illegal wird; liefert die Schritte des legalen Präfix.
 *  Leer bei einer von chess.js abgelehnten `baseFen` (illegales Diagramm) — nichts ist dann klickbar. */
function playPrefix(baseFen: string, sans: string[]): VariationStep[] {
  const c = tryLoadFen(baseFen);
  return c ? playOn(c, sans, 0) : [];
}

/**
 * Spielt `sans[i..]` ab der Stellung von `c` (ohne `c` zu verändern).
 *
 * <p><b>Mehrdeutige Züge</b> („Ne4", wenn zwei Springer dorthin können) lehnt chess.js ab — zu Recht,
 * die Notation sagt nicht, welcher. Autoren schreiben das in Prosa trotzdem, und Chessable reicht es
 * so weiter (gemeldet 2026-09-23: nach 15…Sc6 stehen Springer auf c3 und c5). Statt zu raten, wird
 * jede passende Fortsetzung durchgespielt: entscheidet der REST der Folge (nur nach einem der beiden
 * Züge geht der nächste Zug), ist die Frage beantwortet; tut er es nicht, endet der Präfix hier und
 * der Zug bleibt Text. Ein falscher Springer auf dem Vorschau-Brett wäre schlimmer als gar keiner.</p>
 */
function playOn(c: Chess, sans: string[], i: number): VariationStep[] {
  if (i >= sans.length) return [];
  const san = sans[i];
  const candidates = candidatesFor(c, normalizeSan(san));

  let best: VariationStep[] | null = null;
  let tied = false;
  for (const cand of candidates) {
    const next = tryLoadFen(c.fen());
    if (!next) continue;
    let mv;
    try { mv = next.move(cand); } catch { continue; }
    if (!mv) continue;
    const steps = [{ san, fen: next.fen(), from: mv.from, to: mv.to }, ...playOn(next, sans, i + 1)];
    if (!best || steps.length > best.length) { best = steps; tied = false; }
    else if (steps.length === best.length) tied = true;
  }
  return best && !tied ? best : [];
}

/** SAN-Zeichen, die für die Zuordnung nichts sagen (Schach, Matt, Bewertung). */
const SAN_DECORATION = /[+#!?]+$/;

/**
 * Welche Züge meint `san` in dieser Stellung? Eindeutig geschrieben → genau einer (der Weg über
 * chess.js selbst). Mehrdeutig („Ne4" mit zwei Springern) → alle, auf die Figur, Zielfeld, Umwandlung
 * und die geschriebene Linie/Reihe passen. Illegal → keiner.
 */
function candidatesFor(c: Chess, san: string): (string | { from: string; to: string; promotion?: string })[] {
  const probe = tryLoadFen(c.fen());
  if (probe) {
    try { if (probe.move(san)) return [san]; } catch { /* mehrdeutig oder illegal — unten genauer */ }
  }
  const m = /^([KQRBN])([a-h])?([1-8])?x?([a-h][1-8])(?:=([QRBN]))?$/.exec(san.replace(SAN_DECORATION, ''));
  if (!m) return [];
  const [, piece, file, rank, to, promotion] = m;
  return c.moves({ verbose: true })
    .filter(mv => mv.piece === piece.toLowerCase() && mv.to === to
      && (!file || mv.from[0] === file) && (!rank || mv.from[1] === rank)
      && (promotion ? mv.promotion === promotion.toLowerCase() : !mv.promotion))
    .map(mv => ({ from: mv.from, to: mv.to, promotion: mv.promotion }));
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
  const firstPly = baseStartPly(startFen);
  const order: number[] = [];
  if (startPly !== undefined) {
    const anchor = startPly - firstPly;
    // Passt die Zugnummer zur Linie, ist SIE die Antwort — und keine andere Stellung. Vorher war sie
    // nur Vorrang bei Gleichstand: ging der Zug dort nicht (mehrdeutig, Tippfehler), suchte der
    // Resolver weiter und fand „16.Ne4" in 1.d4 Sf6 2.c4 Se4 wieder — die Vorschau zeigte Zug 2
    // statt Zug 16 (gemeldet 2026-09-23). Text ist die ehrlichere Antwort als ein falsches Brett.
    if (anchor >= 0 && anchor < bases.length) return playPrefix(bases[anchor], sans);
  }
  for (let i = 0; i < bases.length; i++) {
    // Außerhalb der Linie (Kommentar zählt anders als die Start-FEN) bleibt die Suche — aber nur in
    // Stellungen, in denen die Seite am Zug ist, die die Nummer nennt: „16." ist ein Zug von Weiß.
    if (startPly !== undefined && (firstPly + i) % 2 !== startPly % 2) continue;
    order.push(i);
  }

  let best: VariationStep[] = [];
  for (const i of order) {
    const s = playPrefix(bases[i], sans);
    if (s.length > best.length) best = s;   // strikt > → bei Gleichstand gewinnt die zuerst geprüfte (Anker, sonst früheste)
    if (best.length === sans.length) break;
  }
  return best;
}

/** Ply, die der Zug an Position `k` als FORTSETZUNG der Züge davor hätte (ab dem ersten nummerierten
 *  Zug bekannt, numerlose Züge zählen mit); `undefined`, solange keiner nummeriert war. */
function continuationPly(plies: (number | undefined)[], k: number): number | undefined {
  let next: number | undefined;
  for (let i = 0; i < k; i++) {
    const p = plies[i];
    if (p !== undefined) next = p + 1;
    else if (next !== undefined) next++;
  }
  return next;
}

/** Spielt die Folge NUR an der Hauptlinien-Stellung ihrer Zugnummer; liegt die außerhalb → leer. */
function resolveAtAnchor(startFen: string, ucis: string[], sans: string[], startPly: number): VariationStep[] {
  const bases = mainlineFens(startFen, ucis);
  const anchor = startPly - baseStartPly(startFen);
  return anchor >= 0 && anchor < bases.length ? playPrefix(bases[anchor], sans) : [];
}

/**
 * Wo ein Zweig OHNE Zugnummer stehen kann, wenn vor ihm im selben Kommentar schon ein nummerierter
 * stand: als ALTERNATIVE zu dessen erstem Zug („16.Ne4, Nd5 oder Lf4") oder als FORTSETZUNG hinter
 * dessen letztem („16.Nd5 ist besser, nach Lf5 17.Sxe7 …"). Sonst nirgends — vorher suchte der
 * Resolver die ganze Partie ab und fand „after Bf5" hinter „16.Ne4" bei 10…Lf5 (gemeldet 2026-09-23).
 * `contFen` fehlt, wenn der Zweig davor nicht bis zum Ende aufging: dann ist unbekannt, wo er endet.
 */
interface BranchContext { altFen: string; contFen?: string; }

/** Löst einen Zweig auf (siehe Datei-Kopf, „VORWÄRTS-SPRUNG"): ein aufgelöster Schritt je Zug oder null. */
function resolveBranch(
  startFen: string, ucis: string[], b: CommentBranch, ctx?: BranchContext,
): { out: (VariationStep | null)[]; ctx?: BranchContext } {
  let steps: VariationStep[];
  let usedFen: string | undefined;
  if (b.startPly === undefined && ctx) {
    const alt = playPrefix(ctx.altFen, b.sans);
    const cont = ctx.contFen ? playPrefix(ctx.contFen, b.sans) : [];
    // Gleichstand → Alternative: dafür trennt das Komma überhaupt („c5, a5, Kd4" = Aufzählung).
    [steps, usedFen] = cont.length > alt.length ? [cont, ctx.contFen] : [alt, ctx.altFen];
  } else {
    steps = resolveVariation(startFen, ucis, b.sans, b.startPly);
  }
  const out: (VariationStep | null)[] = b.sans.map((_s, k) => (k < steps.length ? steps[k] : null));
  const plies = b.plies ?? [];
  let k = steps.length;
  while (k < b.sans.length) {
    const ply = plies[k];
    const expected = continuationPly(plies, k);
    // `expected === undefined` heißt: vor diesem Zug stand noch KEINE Zugnummer — dann gibt es auch
    // keine Erwartung, die er verletzen könnte, und seine eigene Nummer ist die einzige Auskunft.
    // Ohne diesen Fall riss ein unspielbarer numerloser Token am Anfang („the e7 bishop") die ganze
    // Variante mit, obwohl der Rest ab seiner Zugnummer sauber aufgeht (gemeldet 2026-09-18).
    if (ply === undefined || (expected !== undefined && ply <= expected)) { k++; continue; }
    const rest = resolveAtAnchor(startFen, ucis, b.sans.slice(k), ply);
    if (!rest.length) { k++; continue; }
    rest.forEach((step, j) => { out[k + j] = step; });
    k += rest.length;
  }
  return { out, ctx: nextContext(startFen, ucis, b, out, usedFen ?? ctx?.altFen) };
}

/** Der Rahmen für den NÄCHSTEN Zweig (siehe {@link BranchContext}). */
function nextContext(
  startFen: string, ucis: string[], b: CommentBranch, out: (VariationStep | null)[], inheritedAlt?: string,
): BranchContext | undefined {
  let altFen = inheritedAlt;
  if (b.startPly !== undefined) {
    // Nur eine Zugnummer INNERHALB der Linie gibt einen Ort; liegt sie außerhalb, weiß niemand, wo der
    // Zweig stand — dann bleibt es für den nächsten bei der alten Suche.
    const bases = mainlineFens(startFen, ucis);
    const anchor = b.startPly - baseStartPly(startFen);
    altFen = anchor >= 0 && anchor < bases.length ? bases[anchor] : undefined;
  }
  if (!altFen) return undefined;
  const last = out[out.length - 1];
  return { altFen, contFen: last?.fen };
}

/**
 * Zerlegt einen Kommentar in Segmente (Text + klickbare Züge). Jeder Zweig wird eigenständig gegen die
 * Puzzle-Hauptlinie (`startFen`/`ucis`) aufgelöst; ein Zug ist NUR klickbar, wenn er Teil des legalen
 * Präfix SEINES Zweigs ist (sonst reiner Text).
 */
export function buildCommentSegments(text: string, startFen: string, ucis: string[]): CommentSegment[] {
  // Pro Token in Original-Reihenfolge den aufgelösten Schritt (oder null) bestimmen.
  const perToken: (VariationStep | null)[] = [];
  let ctx: BranchContext | undefined;
  for (const b of branches(text)) {
    const r = resolveBranch(startFen, ucis, b, ctx);
    perToken.push(...r.out);
    ctx = r.ctx;
  }

  const segments: CommentSegment[] = [];
  let last = 0;
  // Dieselbe Tokenfolge wie in branches() — sonst verrutscht die Zuordnung Token ↔ Schritt.
  scanTokens(text).forEach((t, tokenIdx) => {
    if (t.start > last) segments.push({ text: text.slice(last, t.start) });
    const raw = text.slice(t.start, t.end);
    const step = tokenIdx < perToken.length ? perToken[tokenIdx] : null;
    if (step) segments.push({ move: raw.trim(), fen: step.fen, from: step.from, to: step.to });
    else segments.push({ text: raw });   // nicht spielbar → als Text belassen
    last = t.end;
  });
  if (last < text.length) segments.push({ text: text.slice(last) });
  return segments;
}
