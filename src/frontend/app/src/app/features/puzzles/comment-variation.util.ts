import { Chess } from 'chess.js';
import { tryLoadFen } from './puzzle-move.util';
import { whiteToMove } from '../../shared/chess/fen-side';

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
 *                                sonst spielte der Parser c5 mit Weiß und a5 danach mit Schwarz). Dazu zählen
 *                                Verneinung und Gegenüberstellung UNMITTELBAR vor einem Zug: „not", „nicht", „statt",
 *                                „anstatt", „instead of", „rather than" („3.fxg6 Qxe7! not 3...Bxg6", „Dxe7! spielen,
 *                                nicht 3...Lxg6" — der Zug dahinter ist der ANDERE an derselben Stelle; Buch-Aufgabe
 *                                35105, gemeldet 2026-11-19). Nur als ganzes Wort („cannot", „nothing" bleiben Prosa)
 *                                und nur direkt vor dem Zug („5.Qxh7! Not the only winning move … 5...Nxh7" läuft weiter),
 * und lösen JEDEN Zweig eigenständig auf. Ein „oder"/„or" ZWISCHEN ZWEI FELDANGABEN („can't take on g5 or
 * e4") ist kein solches Signal: die Felder sind Ortsangaben, keine Züge (Buch-Aufgabe 35167, gemeldet
 * 2026-11-18). Solche Prosa mit einer Feld-Aufzählung ist dafür (c) eine MÖGLICHE Trennstelle vor dem
 * nächsten nummerierten Zug — entschieden wird sie erst beim Auflösen ({@link resolveWithSoftSplits}).
 *
 * EIGENE ZUGZÄHLUNG DES BUCHS: Liegt eine Zugnummer AUSSERHALB der Linie (die Aufgaben-FEN beginnt bei Zug 1,
 * der Kommentar zitiert die Partie mit Zug 36), wird der Zweig per Suche gefunden. Die gefundene Stellung ist
 * dann sein Ort — auch für den numerlosen Zweig danach ({@link BranchContext}, `searched`; Buch-Aufgabe 34900,
 * gemeldet 2026-11-18).
 *
 * VORWÄRTS-SPRUNG: Bricht die Auflösung genau an einem nummerierten Zug ab, der ÜBER die erwartete
 * Fortsetzung hinausspringt („… nach 2.Sf3 … 4.c3 Sc6" — dazwischen fehlen Züge), wird der Rest als
 * eigener Zweig aufgelöst, und zwar NUR an der Hauptlinien-Stellung seiner Zugnummer (kein Raten
 * anderer Stellungen). So bleiben absichtlich zusammenhängende Folgen mit Nummernlücke unverändert.
 *
 * NOTATION: Neben englischem SAN (KQRBN) werden deutsche Figurenbuchstaben (D/T/L/S → Q/R/B/N, inkl.
 * Umwandlung =D usw.) erkannt (übersetzte Kurse). Da D/T/L/S weder englische Figuren- noch Feldbuchstaben
 * sind, ist die Zuordnung eindeutig; ein versehentlich erkanntes Wort spielt ohnehin nicht legal → Text.
 *
 * DROHUNG ALS NULLZUG (Woodpecker, Aufgabe 334, gemeldet 2026-11-27): „White already has two pawns, but Black threatens
 * 1...Bxe1 and 1...Rd4" — Weiß ist am Zug, „1...X" ist aber kein Zug NACH dem weißen Lösungszug, sondern die DROHUNG in
 * der Ausgangsstellung. An der Hauptlinie verankert spielte die Vorschau erst den weißen Lösungszug und verriet ihn.
 * Ein Zweig, dessen erster Zug der NICHT ziehenden Seite gehört und genau den Halbzug nach der Start-FEN trägt, wird
 * deshalb zuerst aus der Start-FEN mit getauschter Zugseite gespielt ({@link nullMoveFen}; nicht, wenn die ziehende
 * Seite im Schach steht) — wenn ein Signalwort davor steht ({@link THREAT_BEFORE}: „threatens", „droht", „idea" …)
 * oder der Kommentar selbst an der Start-FEN steht (`atStart`, Einleitung des Buch-Lösers). Geht der Zug so nicht,
 * gilt die gewohnte Verankerung. Solche Segmente tragen `threat`.
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
  /** Nur am ersten Segment eines Blocks: der Block ist eine Zwischenüberschrift (fett, ohne Zug-Chips) —
   *  beim Import an den Satz geklebte Chessable-Überschrift (v1-rk-course-comment-ueberschrift-angeklebt). */
  heading?: boolean;
  /** Der Zug gehört zu einer DROHUNG, gespielt aus der Start-FEN mit getauschter Zugseite (Nullzug, siehe Datei-Kopf) —
   *  die Vorschau-Leiste sagt dann „Zeigt die Drohung". */
  threat?: boolean;
}

/** Ein Zug-Schritt der aufgelösten Variante (Stellung nach dem Zug + Feldmarkierung); `threat` siehe
 *  {@link CommentSegment}. */
export interface VariationStep { san: string; fen: string; from: string; to: string; threat?: boolean; }

/** Optionen für {@link buildCommentSegments}. */
export interface CommentSegmentOptions {
  /** Der Kommentar steht an der Start-FEN (Einleitung vor dem ersten Lösungszug): ein Zug der nicht ziehenden Seite
   *  mit passender Zugnummer gilt auch ohne Signalwort zuerst als Drohung (Nullzug, siehe Datei-Kopf). */
  atStart?: boolean;
  /** Spoilerschutz beim Lösen: nur die ersten `played` Halbzüge von `ucis` sind gespielt. Die Stellungen dahinter
   *  bleiben als VERDECKT in der Linie ({@link HIDDEN_FEN}): ein Zweig mit ihrer Zugnummer wird Text — er wird weder
   *  dort gespielt noch (wie eine Nummer außerhalb der Linie) in den sichtbaren Stellungen gesucht. */
  played?: number;
}

/** Platzhalter für einen noch nicht gespielten Lösungszug bzw. die Stellung danach ({@link CommentSegmentOptions},
 *  `played`). chess.js lehnt die „FEN" ab — aus ihr geht kein Zug auf. */
const HIDDEN_UCI = '?';
const HIDDEN_FEN = 'hidden';

/** Ein Zweig: SAN-Folge + optionale absolute Ply des ERSTEN Zugs (aus seiner Zugnummer).
 *  `plies` = absolute Ply je Zug, soweit er eine Zugnummer trägt (parallel zu `sans`). */
export interface CommentBranch {
  sans: string[]; startPly?: number; plies?: (number | undefined)[];
  /** MÖGLICHE Trennstellen (Index in `sans`): nummerierter Zug hinter Prosa mit einer Feld-Aufzählung, siehe
   *  {@link branches} Regel (c). `exact`: seine Zugnummer ist genau die Fortsetzung der Züge davor. */
  soft?: { at: number; exact: boolean }[];
  /** Der erste Zug ist die ausdrückliche Alternative zum LETZTEN Zug des Zweigs davor („… Ke8 or 2...Nd5 …"):
   *  er steht an der Stellung, aus der jener gespielt wurde — siehe {@link resolveInSubline}, `first`. */
  altOfPrev?: boolean;
  /** Je Zug (parallel zu `sans`): steht zwischen ihm und dem Zug davor nur Leerraum, Satzzeichen oder ein
   *  Bewertungszeichen — keine Prosa („3.Qd1 3...Ref8", „Rf8-+ 20...f3+"). Siehe {@link resumeOuterVariation}. */
  tight?: boolean[];
  /** Vor dem ersten Zug steht (im selben Satz) ein Drohungs-Signalwort ({@link THREAT_BEFORE}). */
  threatSignal?: boolean;
}

// Deutsche → englische Figurenbuchstaben (für übersetzte Kurse). K bleibt K.
const DE_PIECE: Record<string, string> = { D: 'Q', T: 'R', L: 'B', S: 'N' };

// SAN-Kern: Rochade (O oder 0) | Figurenzug (engl. KQRBN + dt. DTLS) | Bauernzug (+ Umwandlung / Schach-Matt).
// Ein „+“ direkt vor „-“, „=“ oder „/“ ist eine Bewertung („5.Rxc1+-“, „+=“, „+/-“), kein Schach — bleibt Text (0.825.1).
const SAN =
  '(?:O-O-O|O-O|0-0-0|0-0|[KQRBNDTLS][a-h]?[1-8]?x?[a-h][1-8]|[a-h](?:x[a-h])?[1-8](?:=[QRBNDTLS])?)(?:#|\\+(?![-=/]))?';
// Optionale Zugnummer davor („2." = Weiß / „2…"/„2..." = Schwarz) + der SAN-Zug. `g` für globales Scannen.
// Gruppen: 1=Zugnummer, 2=Punkte (>1 → Schwarzzug), 3=SAN.
const TOKEN_RE = new RegExp(`(?:(\\d+)\\.(\\.\\.|\\u2026)?\\s?)?(${SAN})`, 'g');

// „Alternativ"-Signale zwischen zwei Zug-Token → neuer Zweig (kein Fortsetzungszug).
const ALT_WORD = /\b(?:or|oder|bzw\.?|beziehungsweise|resp\.?|respectively)\b|\//i;

// Verneinung und Gegenüberstellung UNMITTELBAR vor dem nächsten Zug („3.fxg6 Qxe7! not 3...Bxg6", „Dxe7! spielen,
// nicht 3...Lxg6", „statt", „instead of", „rather than"): der Zug dahinter ist der ANDERE an derselben Stelle, keine
// Fortsetzung (Buch-Aufgabe 35105, Woodpecker Exercise 246). Nur als ganzes Wort („cannot", „nothing", „Nichte"
// bleiben Prosa) und nur direkt vor dem Zug — „5.Qxh7! Not the only winning move, but … 5...Nxh7" ist Prosa über
// den Zug davor, die Folge läuft weiter (Buch-Aufgabe 35167).
const NEG_WORD = '(?<![\\p{L}\\d])(?:not|nicht|statt|anstatt|instead\\s+of|rather\\s+than)(?![\\p{L}\\d])';
const NEG_BEFORE = new RegExp(`${NEG_WORD}[\\s(:,]*$`, 'iu');

// Drohungs-Signalwörter (siehe Datei-Kopf, „DROHUNG ALS NULLZUG"): „threatens 1...Bxe1", „droht 1...Lxe1", „with the
// idea 1...Rd4", „Schwarz will 1...Td4" bewusst NICHT („will" ist im Englischen Futur). Ganze Wörter, im selben Satz vor
// dem Zug ({@link hasThreatSignal}).
const THREAT_BEFORE = new RegExp('(?<![\\p{L}])(?:threat(?:s|en|ens|ening|ened)?|menac(?:e|es|ing)|ideas?|wants?|wanting'
  + '|intends?|intending|intention|droh(?:t|en|te|ten|end|ende|ender|endes|enden)?|drohung(?:en)?|idee|absicht'
  + '|beabsichtigt|m[öo]chte)(?![\\p{L}])', 'iu');
const THREAT_WINDOW = 100;

/** Steht vor `start` im selben Satz (höchstens {@link THREAT_WINDOW} Zeichen) ein Drohungs-Signalwort? */
function hasThreatSignal(text: string, start: number): boolean {
  let before = text.slice(Math.max(0, start - THREAT_WINDOW), start);
  const ends = [...before.matchAll(/[.!?]\s/g)];
  if (ends.length) { const e = ends[ends.length - 1]; before = before.slice(e.index! + e[0].length); }
  return THREAT_BEFORE.test(before);
}

/** Zwischenraum ohne Prosa: Leerraum, Satzzeichen, Bewertungs- und Kommentarzeichen. */
const TIGHT_GAP = /^[\s.,;:()[\]+\-=/±∓!?□∞#⩲⩱]*$/u;

/** Steht im Zwischenraum vor einem Zug ein Alternativ-Signal (Alternativ-Wort irgendwo oder Verneinung direkt davor)? */
function isAltSignal(gap: string): boolean {
  return ALT_WORD.test(gap) || NEG_BEFORE.test(gap);
}

/** Übersetzt einen (evtl. deutschen) SAN-Zug in engl. SAN für chess.js. Englische Züge bleiben unverändert. */
function normalizeSan(san: string): string {
  let s = san.replace(/^0(?=-0)/, 'O').replace(/-0/g, '-O'); // 0-0(-0) → O-O(-O)
  const first = s[0];
  if (DE_PIECE[first]) s = DE_PIECE[first] + s.slice(1);
  return s.replace(/=([DTLS])/g, (_m, p: string) => '=' + DE_PIECE[p]);
}

/** `altOfPrev`: ausdrückliche Alternative zum numerlosen Zug unmittelbar davor („Ke8 or 2...Nd5"), siehe
 *  {@link dropMentionedMoves}. */
interface Tok { san: string; ply?: number; start: number; end: number; altOfPrev?: boolean; }

// Alle Alternativ-Signale eines Zwischenraums (zum Herausnehmen, siehe isAlternativeGap).
const ALT_WORD_ALL = new RegExp(`${ALT_WORD.source}|${NEG_WORD}`, 'giu');

/** Steht zwischen zwei Zügen NUR ein Alternativ-Signal („ or ", „, oder ", „! (or ", „+- / ") — keine weiteren
 *  Wörter oder Ziffern? Satzzeichen, Klammern und Bewertungszeichen zählen nicht. */
function isAlternativeGap(gap: string): boolean {
  return isAltSignal(gap) && !/[\p{L}\d]/u.test(gap.replace(ALT_WORD_ALL, ' '));
}

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

// „Qd5/g4" ist Kurzschrift für „Dd5 oder Dg4": das Feld hinter dem Schrägstrich hängt an der FIGUR
// davor und ist kein Bauernzug g4. Als Zug gelesen eröffnete es einen numerlosen Zweig, an dem die
// folgende Variante („B 1...Qa5 2.Ng5") hängen blieb (Buch-Aufgabe 35965, gemeldet 2026-11-12).
// „c5/a5" (zwei Bauernzüge) bleibt: dort steht vor dem Schrägstrich kein Figurenzug.
const PIECE_MOVE_SLASH = /[KQRBNDTLS][a-h]?[1-8]?x?[a-h][1-8][+#!?]*\/$/;

function isSquareMention(text: string, san: string, hasNumber: boolean, start: number, end: number): boolean {
  if (hasNumber || !BARE_SQUARE.test(san)) return false;
  if (PIECE_MOVE_SLASH.test(text.slice(Math.max(0, start - 12), start))) return true;
  return SQUARE_BEFORE.test(text.slice(Math.max(0, start - 20), start)) || SQUARE_AFTER.test(text.slice(end, end + 12));
}

// FELD-AUFZÄHLUNG hinter einer Präposition: „can't take on g5 or e4", „capturing on a8, e7 or e4", „auf g5
// oder e4", „auf a8, e7 und e4" — JEDES Feld der Liste ist Ortsangabe, nicht nur das erste direkt hinter der
// Präposition. Als Bauernzug gelesen eröffnete „e4" einen numerlosen Zweig, an dem die folgende Variante
// („3...dxe4 …") hängen blieb; ihre Fortsetzung landete hinter der falschen Nebenvariante und brach dort ab
// (Buch-Aufgabe 35167, gemeldet 2026-11-18). Bewusst eng:
//   (i)  direkt hinter „PRÄP Feld[, Feld …] KONJUNKTION" (or/and/oder/und/bzw./sowie) → Ortsangabe;
//   (ii) hinter „PRÄP Feld[, Feld …]," (nur Komma) → Ortsangabe NUR, wenn die Liste danach bis zu
//        „KONJUNKTION Feld" weiterläuft. „With the pawn on e5, e4 is a strong move" bleibt also ein Zug.
const LIST_PREP = 'auf|von|vom|über|ueber|zum|zur|feld|felder|on|onto|square|squares|from|via|to';
const LIST_SQ = '[a-h][1-8]';
const LIST_CONJ = '(?:or|and|oder|und|bzw\\.?|sowie)';
const LIST_SEP_CONJ = `(?:\\s*,\\s*|\\s+)${LIST_CONJ}\\s+`;
const LIST_SEP_COMMA = '\\s*,\\s*';
const LIST_HEAD = `(?:^|[^\\p{L}])(?:${LIST_PREP})\\s+(?:${LIST_SQ}(?:${LIST_SEP_CONJ}|${LIST_SEP_COMMA}))*${LIST_SQ}`;
const LIST_BEFORE_CONJ = new RegExp(`${LIST_HEAD}(${LIST_SEP_CONJ})$`, 'iu');
const LIST_BEFORE_COMMA = new RegExp(`${LIST_HEAD}(${LIST_SEP_COMMA})$`, 'iu');
const LIST_AFTER_TO_CONJ = new RegExp(`^(?:${LIST_SEP_COMMA}${LIST_SQ})*${LIST_SEP_CONJ}${LIST_SQ}(?![\\p{L}\\d])`, 'iu');
const LIST_WINDOW = 60;

/** Länge des Trenners VOR dem Feld, wenn es in einer Feld-Aufzählung hinter einer Präposition steht
 *  (siehe oben); sonst `undefined`. Der Trenner („ or ", „, ") gehört zur Ortsangabe. */
function squareListSeparator(text: string, start: number, end: number): number | undefined {
  const before = text.slice(Math.max(0, start - LIST_WINDOW), start);
  const conj = LIST_BEFORE_CONJ.exec(before);
  if (conj) return conj[1].length;
  const comma = LIST_BEFORE_COMMA.exec(before);
  if (comma && LIST_AFTER_TO_CONJ.test(text.slice(end, end + LIST_WINDOW))) return comma[1].length;
  return undefined;
}

/** Ergebnis von {@link scanTokens}: die Zug-Token und der Text für die ZWISCHENRAUM-Tests. In `gaps` sind
 *  die Trenner einer Feld-Aufzählung durch Leerzeichen ersetzt (gleiche Länge, Positionen bleiben): das
 *  „or" aus „on g5 or e4" verbindet zwei Felder und ist kein Alternativ-Signal zwischen zwei Zügen.
 *  `listSquares` = Textpositionen der Felder solcher Aufzählungen (siehe {@link branches}, Regel (c)). */
interface Scan { toks: Tok[]; gaps: string; listSquares: number[]; }

/** Scannt die Token samt Position + implizierter (absoluter) Ply aus Zugnummer + Farbe.
 *  Feldangaben (siehe {@link isSquareMention}, {@link squareListSeparator}) fehlen — sie bleiben Text und
 *  trennen keine Zweige. */
function scanTokens(text: string): Scan {
  const re = new RegExp(TOKEN_RE.source, 'g');
  const out: Tok[] = [];
  const listSquares: number[] = [];
  let gaps = text;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    const start = m.index;
    const end = m.index + m[0].length;
    if (!m[1] && BARE_SQUARE.test(m[3])) {
      const sep = squareListSeparator(text, start, end);
      if (sep !== undefined) {
        gaps = gaps.slice(0, start - sep) + ' '.repeat(sep) + gaps.slice(start);
        listSquares.push(start);
        continue;
      }
    }
    if (isSquareMention(text, m[3], !!m[1], start, end)) continue;
    const num = m[1] ? parseInt(m[1], 10) : undefined;
    const isBlack = !!m[2];
    // 0-basierte Ply: Weiß N → 2N-2, Schwarz N → 2N-1.
    const ply = num !== undefined ? (num * 2 - (isBlack ? 1 : 2)) : undefined;
    out.push({ san: m[3], ply, start, end });
  }
  return { toks: dropMentionedMoves(gaps, out), gaps, listSquares };
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
 * Alternativ-Signal („oder", „/", „not 3...", „nicht 3..." …) zählt die Nummerierung nicht weiter — dort beginnt ein
 * eigener Zweig, dessen erster Zug keine Fortsetzung ist. Und umgekehrt: eine Zugnummer direkt hinter einer
 * VERNEINUNG ist kein Beleg gegen den numerlosen Zug davor ({@link laterNumbered}): „3.fxg6 Qxe7! is forced, not
 * 3...Bxg6" — „3...Bxg6" ist die andere Antwort, „Qxe7" bleibt ein Zug (Buch-Aufgabe 35105). Ein „or" in der Prosa
 * davor („would allow Ke8, or so it seems. 2...Nd5") reicht dafür NICHT: dort ist „Ke8" wirklich nur erwähnt.</p>
 *
 * <p>AUSNAHME — die ausdrückliche Alternative (Buch-Aufgabe 35205, Woodpecker Exercise 346, gemeldet
 * 2026-11-18): „1...Kxf7 2.Qb3+ Ke8 or 2...Nd5 3.exd5 … 6.Nxe5+- with two pawns more 3.Qe6+ Qe7 4.Qxe7#" ist
 * die flachgeklopfte Schachtelung 2…Ke8 (2…Nd5 …) 3.Qe6+. „2...Nd5" beansprucht den Halbzug von „Ke8", weil
 * es die ANDERE Antwort ist — das „or" sagt es. Bis 0.994.1 flog „Ke8" trotzdem als Erwähnung raus, und
 * „3.Qe6+ Qe7 4.Qxe7#" fand danach kein Zweig-Ende, an dem es weitergeht. Bewusst eng: der nummerierte Zug
 * folgt UNMITTELBAR, und im Zwischenraum steht außer dem Alternativ-Signal kein Wort ({@link isAlternativeGap}).
 * Bestandsprüfung: mit Prosa dazwischen („he can either allow g4, or prevent it. … 32...Ra8") ist der
 * numerlose Zug wirklich nur erwähnt — dort riss die weitere Fassung 19 richtige Züge einer Variante mit
 * (Aufgabe 32429); ein Alternativ-Signal irgendwo vor der Nummer („Threatening Rc3+ … or so you may
 * think. … Rc2+ and Nd2+ … 47.a4", Aufgabe 12383) kostete im Bestand 160 vorher klickbare Züge. Beansprucht
 * die Alternative genau den Halbzug des numerlosen Zugs, merkt sie sich das (`altOfPrev`): sie steht an der
 * Stellung, aus der er gespielt wurde ({@link resolveInSubline}, `first`).</p>
 */
/** Der nächste nummerierte Zug hinter `toks[i]` — aber nur innerhalb DIESES Zweigs: steht auf dem Weg dorthin eine
 *  Verneinung direkt vor einem Zug („Qxe7! is best, not 3...Bxg6"), beginnt dort ein neuer Zweig, und seine Nummer
 *  sagt nichts über den numerlosen Zug davor. Ausnahme: der UNMITTELBAR folgende Zug hinter einem reinen
 *  Alternativ-Signal ({@link isAlternativeGap}) — das ist die ausdrückliche Alternative, siehe unten. „or"/„oder"
 *  beenden die Suche bewusst nicht (Buch-Aufgabe 35205, „would allow Ke8, or so it seems. 2...Nd5"). */
function laterNumbered(text: string, toks: Tok[], i: number): Tok | undefined {
  for (let j = i + 1; j < toks.length; j++) {
    const gap = text.slice(toks[j - 1].end, toks[j].start);
    if (NEG_BEFORE.test(gap) && !(j === i + 1 && isAlternativeGap(gap))) return undefined;
    if (toks[j].ply !== undefined) return toks[j];
  }
  return undefined;
}

function dropMentionedMoves(text: string, toks: Tok[]): Tok[] {
  const out: Tok[] = [];
  let nextPly: number | undefined;    // Ply, die der nächste Fortsetzungszug hätte
  let lastNumbered: number | undefined;
  for (let i = 0; i < toks.length; i++) {
    const t = toks[i];
    const gap = i > 0 ? text.slice(toks[i - 1].end, t.start) : '';
    if (i > 0 && isAltSignal(gap)) { nextPly = undefined; lastNumbered = undefined; }   // neuer Zweig

    if (t.ply === undefined && nextPly !== undefined) {
      const later = laterNumbered(text, toks, i);
      // Beweiskräftig ist die spätere Nummer nur, wenn sie überhaupt eine FORTSETZUNG sein kann:
      // springt sie hinter die letzte Nummer zurück, beginnt dort ein eigener Zweig („… 43.h4 a4.
      // Weiß gewinnt nach 40.b5") und sagt nichts über diesen Zug. Beansprucht sie dagegen die Ply,
      // die dieser numerlose Zug hätte, können nicht beide derselbe Halbzug sein → Erwähnung.
      if (later && later.ply! >= lastNumbered! && later.ply! <= nextPly) {
        // Es sei denn, der nummerierte Zug ist die AUSDRÜCKLICHE ALTERNATIVE: „2.Qb3+ Ke8 or 2...Nd5 …" — er
        // folgt unmittelbar, und dazwischen steht nichts als das Alternativ-Signal (siehe isAlternativeGap).
        // Dann beansprucht er den Halbzug nicht als derselbe Zug, sondern als der andere (Buch-Aufgabe 35205).
        if (later !== toks[i + 1] || !isAlternativeGap(text.slice(t.end, later.start))) continue;
        if (later.ply === nextPly) later.altOfPrev = true;
      }
    }
    out.push(t);
    if (t.ply !== undefined) { lastNumbered = t.ply; nextPly = t.ply + 1; }
    else if (nextPly !== undefined) nextPly++;
  }
  return out;
}

/** Alle SAN-Token (nur der reine Zug, ohne Nummer) in Textreihenfolge. */
export function extractSanTokens(text: string): string[] {
  return scanTokens(text).toks.map(t => t.san);
}

/**
 * Zerlegt die Tokenfolge in Zweige (siehe Datei-Kopf: Zugnummer-Rücksprung ODER Alternativ-Signal).
 * Ein Komma trennt nur, wenn der Folgezug KEINE eigene Nummer hat (Aufzählung „c5, a5, Kd4"); ein
 * Komma vor einem nummerierten Zug („… a3, 41.b6 …") gilt als Fortsetzung, nicht als Alternative.
 *
 * <p>(c) PROSA mit einer Feld-Aufzählung vor einem NUMMERIERTEN Zug („… preparing ...Nf6-g4 … Black's pawns
 * on d5 and e4 are the key … 14...Qh4 15.g3", „the double attack on b7 and f7 with Qf3 on the next move.
 * For example, 17...Be5 18.Qf3"): stand im Text DIESES Zweigs seit seiner letzten Zugnummer eine solche
 * Aufzählung, ist der nummerierte Zug eine MÖGLICHE Trennstelle (`soft`) — die Züge davor können erwähnte
 * Pläne oder eine andere Variante sein. Entschieden wird beim Auflösen ({@link resolveWithSoftSplits}) an der
 * Spielbarkeit, nicht an der bloßen Nummernfolge. Solange das zweite Feld der Aufzählung als (unspielbarer)
 * Bauernzug gelesen wurde, riss es die Folge an dieser Stelle zufällig ab; ohne die Regel hingen jetzt z. B.
 * „...Nf6-g4" und „14...Qh4" an einer gemeinsamen, zur Zugnummer unpassenden Stellung. Eine Aufzählung VOR
 * dem Trenner, der den Zweig eröffnet („… pieces on a4 and g7, … Nge7 11.d4"), gehört zum Zweig davor.</p>
 */
export function branches(text: string): CommentBranch[] {
  const { toks, gaps, listSquares } = scanTokens(text);
  const out: CommentBranch[] = [];
  let cur: string[] = [];
  let curPlies: (number | undefined)[] = [];
  let curSoft: { at: number; exact: boolean }[] = [];
  let curTight: boolean[] = [];
  let curStartPly: number | undefined;
  let curAltOfPrev = false;          // der Zweig beginnt als ausdrückliche Alternative zum Zug davor
  let lastPly = -Infinity;
  let nextPly: number | undefined;   // Ply, die der nächste Zug als direkte Fortsetzung hätte
  let listSince = false;             // Feld-Aufzählung im Text dieses Zweigs seit der letzten Zugnummer, siehe (c)
  let prevEnd = 0;
  let curThreat = false;             // Drohungs-Signalwort vor dem ersten Zug
  const push = () => out.push({
    sans: cur, startPly: curStartPly, plies: curPlies, tight: curTight,
    ...(curSoft.length ? { soft: curSoft } : {}), ...(curAltOfPrev ? { altOfPrev: true } : {}),
    ...(curThreat ? { threatSignal: true } : {}),
  });

  for (const t of toks) {
    let boundary = false;
    // Letztes Aufzählungs-Feld im Zwischenraum vor diesem Zug (-1: keines).
    const listAt = listSquares.reduce((last, p) => (p >= prevEnd && p < t.start ? p : last), -1);
    if (listAt >= 0) listSince = true;
    if (cur.length) {
      const gap = gaps.slice(prevEnd, t.start);
      if (t.ply !== undefined && t.ply <= lastPly) boundary = true;          // (a) Zugnummer-Rücksprung
      else if (isAltSignal(gap)) boundary = true;                          // (b) „oder"/„or"/„/" …
      else if (gap.includes(',') && t.ply === undefined) boundary = true;    // Komma + numerloser Zug = Aufzählung
      else if (t.ply !== undefined && listSince) curSoft.push({ at: cur.length, exact: t.ply === nextPly });   // (c)
      // Die Aufzählung zählt für den NEUEN Zweig nur, wenn sie hinter dem Trenner steht, der ihn eröffnet.
      if (boundary && listSince) {
        const tail = listAt >= 0 ? gaps.slice(listAt, t.start) : null;
        if (tail === null || isAltSignal(tail) || tail.includes(',')) listSince = false;
      }
    }
    if (boundary) {
      push();
      cur = [];
      curPlies = [];
      curSoft = [];
      curTight = [];
      curStartPly = undefined;
      curAltOfPrev = !!t.altOfPrev;
      lastPly = -Infinity;
      nextPly = undefined;
    }
    if (t.ply !== undefined) {
      lastPly = t.ply;
      nextPly = t.ply + 1;
      listSince = false;
      if (!cur.length) curStartPly = t.ply;
    } else if (nextPly !== undefined) nextPly++;
    curTight.push(cur.length > 0 && TIGHT_GAP.test(gaps.slice(prevEnd, t.start)));
    if (!cur.length) curThreat = hasThreatSignal(text, t.start);
    cur.push(t.san);
    curPlies.push(t.ply);
    prevEnd = t.end;
  }
  if (cur.length) push();
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
    if (u === HIDDEN_UCI || fens[fens.length - 1] === HIDDEN_FEN) { fens.push(HIDDEN_FEN); continue; }
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
  const white = whiteToMove(startFen);
  return (fullmove - 1) * 2 + (white ? 0 : 1);
}

/**
 * Die Start-FEN mit getauschter Zugseite (Nullzug): En-passant-Feld gelöscht, Vollzugzahl nach einem schwarzen
 * „Nullzug" um eins weiter. `undefined`, wenn chess.js die FEN ablehnt oder die ziehende Seite im Schach steht (dann
 * darf sie nicht „passen" — die Stellung danach hätte einen schlagbaren König).
 */
export function nullMoveFen(fen: string): string | undefined {
  const c = tryLoadFen(fen);
  if (!c || c.isCheck()) return undefined;
  const parts = c.fen().split(' ');
  const white = parts[1] === 'w';
  parts[1] = white ? 'b' : 'w';
  parts[3] = '-';
  if (!white) parts[5] = String((parseInt(parts[5], 10) || 1) + 1);
  const out = parts.join(' ');
  return tryLoadFen(out) ? out : undefined;
}

/** Drohung (siehe Datei-Kopf): der Zweig beginnt mit dem Halbzug direkt nach der Start-FEN und geht aus der
 *  Nullzug-Stellung auf. Leer sonst. */
function threatSteps(startFen: string, b: CommentBranch): { steps: VariationStep[]; fen?: string } {
  if (b.startPly === undefined || b.startPly !== baseStartPly(startFen) + 1) return { steps: [] };
  const fen = nullMoveFen(startFen);
  if (!fen) return { steps: [] };
  const steps = playPrefix(fen, b.sans);
  return steps.length ? { steps, fen } : { steps: [] };
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
  return resolveVariationFrom(startFen, ucis, sans, startPly).steps;
}

/** Wie {@link resolveVariation}, dazu die Basis-Stellung, aus der der Präfix gespielt wurde (`undefined`,
 *  wenn kein Zug aufging) — der ORT des Zweigs für den Rahmen des nächsten ({@link nextContext}). */
function resolveVariationFrom(
  startFen: string, ucis: string[], sans: string[], startPly?: number,
): { steps: VariationStep[]; baseFen?: string } {
  if (!sans.length) return { steps: [] };
  const bases = mainlineFens(startFen, ucis);
  const firstPly = baseStartPly(startFen);
  const order: number[] = [];
  if (startPly !== undefined) {
    const anchor = startPly - firstPly;
    // Passt die Zugnummer zur Linie, ist SIE die Antwort — und keine andere Stellung. Vorher war sie
    // nur Vorrang bei Gleichstand: ging der Zug dort nicht (mehrdeutig, Tippfehler), suchte der
    // Resolver weiter und fand „16.Ne4" in 1.d4 Sf6 2.c4 Se4 wieder — die Vorschau zeigte Zug 2
    // statt Zug 16 (gemeldet 2026-09-23). Text ist die ehrlichere Antwort als ein falsches Brett.
    if (anchor >= 0 && anchor < bases.length) {
      const steps = playPrefix(bases[anchor], sans);
      return { steps, baseFen: steps.length ? bases[anchor] : undefined };
    }
  }
  for (let i = 0; i < bases.length; i++) {
    // Außerhalb der Linie (Kommentar zählt anders als die Start-FEN) bleibt die Suche — aber nur in
    // Stellungen, in denen die Seite am Zug ist, die die Nummer nennt: „16." ist ein Zug von Weiß.
    if (startPly !== undefined && (firstPly + i) % 2 !== startPly % 2) continue;
    order.push(i);
  }

  let best: VariationStep[] = [];
  let baseFen: string | undefined;
  for (const i of order) {
    const s = playPrefix(bases[i], sans);
    if (s.length > best.length) { best = s; baseFen = bases[i]; }   // strikt > → bei Gleichstand gewinnt die zuerst geprüfte (Anker, sonst früheste)
    if (best.length === sans.length) break;
  }
  return { steps: best, baseFen };
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
 *
 * <p>`searched`: die Zugnummer des Zweigs davor liegt AUSSERHALB der Linie (das Buch zählt die Partiezüge),
 * sein Ort ist per Suche gefunden. Bis 0.993.0 gab es dann gar keinen Rahmen, und der numerlose Zweig suchte
 * wieder die ganze Linie ab — die Antwort auf den eben gezeigten Zug fand er dort nicht („36.Nxf7+ … was
 * played in the game, but Black had Rxf7 …", Buch-Aufgabe 34900, gemeldet 2026-11-18). Ein gesuchter Ort ist
 * aber ein ANGEBOT, kein Zwang: findet die freie Suche mehr Züge als Alternative und Fortsetzung, gilt sie
 * weiter (Bestandsprüfung: sonst würden hunderte erwähnte Züge, die bisher eine Stellung hatten, zu Text).
 * Und er zählt mit: `altPly`/`contPly` sind die Ply der Stellungen in der ZÄHLUNG DES KOMMENTARS (36. = 70);
 * eine Zugnummer im numerlosen Zweig, die dazu nicht passt, gehört nicht in diese Folge
 * ({@link numberedPrefix}).</p>
 */
interface BranchContext { altFen: string; contFen?: string; searched?: boolean; altPly?: number; contPly?: number; }

/**
 * Der Teil eines numerlosen Zweigs, dessen Zugnummern zur Zählung ab `basePly` passen: „36.Nxf7+ …, but
 * Black had Rxf7 37.g6 Rxf5+ 38.Rxf5" zählt hinter 36.Nxf7+ lückenlos weiter (Rxf7 = 71, „37." = 72 …) — die
 * ganze Folge. Trägt ein Zug eine Nummer, die NICHT passt („… ready for either Nxa5 or Nd6 next. 36.Nc4 does
 * win" hinter 41.Nc4 Nb6), endet die Folge vor ihm: er ist kein Fortsetzungszug, sondern steht an seiner
 * eigenen Zugnummer (Bestandsprüfung, Aufgabe 91401 — sonst spielte Schwarz dort „36.Nc4").
 */
function numberedPrefix(b: CommentBranch, basePly: number | undefined): string[] {
  if (basePly === undefined) return b.sans;
  const cut = (b.plies ?? []).findIndex((p, k) => p !== undefined && p !== basePly + k);
  return cut < 0 ? b.sans : b.sans.slice(0, cut);
}

/**
 * Ein NUMMERIERTER Zweig, der die Hauptlinie gar nicht meint, sondern eine Untervariante desselben
 * Kommentars fortsetzt: „… 2...Kf7 3.Rxf6+ Rxf6 4.Qxf6+ Ke8. Without rooks, he threatens both 5...bxa2
 * and a lot of checks. The trick is …: 5.Qxg6+ Kd8 6.Qf6+ …" (Woodpecker, Aufgabe 345, gemeldet
 * 2026-11-12). „5.Qxg6+" gehört hinter 4…Ke8 — an der Hauptlinien-Stellung von Zug 5 war Dxg6+
 * zufällig auch legal, die Vorschau sprang in die Partie.
 *
 * <p>`prior` sind die Stellungen aller bisher aufgelösten Kommentar-Züge (jüngste zuletzt). Ihre FEN
 * zählt ab der Start-FEN weiter, trägt also dieselbe Ply-Zählung wie die Zugnummern. Kandidat ist
 * jede, an der genau der Halbzug `startPly` dran ist und die NICHT die Hauptlinien-Stellung ist. Es
 * gewinnt die längste Folge; bei Gleichstand die Untervariante — sie steht im Text unmittelbar davor,
 * die Hauptlinie zeigt das Brett ohnehin.</p>
 *
 * <p>Unter den Untervarianten zählt zuerst ein ZWEIG-ENDE, dann erst eine Stellung mitten in einem
 * Zweig (je jüngste zuerst): „5...Kd6 6.Rd8+ Ke7 6...Ke5 7.Qe6 7.Qc7+" ist die aus dem PGN geklopfte
 * Schachtelung 6…Ke7 (6…Ke5 7.Qe6) 7.Qc7+ — die äußere Variante wartet hinter 6…Ke7 auf ihren
 * 7. Zug, in der eingeschobenen ist er mit 7.Qe6 schon vergeben.</p>
 *
 * <p>`first` geht allen voran: die Stellung, aus der der letzte Zug des Zweigs davor gespielt wurde, wenn dieser
 * Zweig dessen AUSDRÜCKLICHE ALTERNATIVE ist („3.Qxh6 Re8 or 3...f6 4.exf6" — „3...f6" steht hinter 3.Qxh6).
 * Seit „Re8" dort nicht mehr als Erwähnung wegfällt, ist diese Stellung kein Zweig-Ende mehr; ohne Vorrang
 * gewänne das Ende einer älteren, fremden Variante mit derselben Zugnummer (Bestandsprüfung, Aufgabe 51263:
 * „3...f6" landete in der Variante 1...Qxb4 statt hinter 1...Qc7 … 3.Qxh6). Eine andere Stellung gilt nur,
 * wenn dort MEHR Züge aufgehen.</p>
 */
interface PriorFen { fen: string; end: boolean; }

function resolveInSubline(
  b: CommentBranch, mainSteps: VariationStep[], mainFen: string | undefined, prior: PriorFen[], first?: string,
): { steps: VariationStep[]; fen: string } | undefined {
  let best: { steps: VariationStep[]; fen: string } | undefined;
  if (first !== undefined && baseStartPly(first) === b.startPly) {
    if (first === mainFen) { if (mainSteps.length) return undefined; }   // die Alternative steht in der Partie selbst
    else {
      const steps = playPrefix(first, b.sans);
      if (steps.length) best = { steps, fen: first };
    }
  }
  for (const wantEnd of [true, false]) {
    for (let i = prior.length - 1; i >= 0; i--) {
      const { fen, end } = prior[i];
      if (end !== wantEnd || fen === mainFen || baseStartPly(fen) !== b.startPly) continue;
      const steps = playPrefix(fen, b.sans);
      if (steps.length > (best?.steps.length ?? 0)) best = { steps, fen };
    }
  }
  return best && best.steps.length >= mainSteps.length ? best : undefined;
}

/** Löst einen Zweig auf (siehe Datei-Kopf, „VORWÄRTS-SPRUNG"): ein aufgelöster Schritt je Zug oder null. */
function resolveBranch(
  startFen: string, ucis: string[], b: CommentBranch, ctx?: BranchContext, priorFens: PriorFen[] = [], altBase?: string,
  threat: { atStart?: boolean; fens?: Set<string> } = {},
): { out: (VariationStep | null)[]; ctx?: BranchContext; guessed: number } {
  let steps: VariationStep[];
  let usedFen: string | undefined;
  let searchedFen: string | undefined;
  let unframed = false;   // Rahmen aus einer Suche ausgeschlagen: die freie Suche fand mehr
  let basePly: number | undefined;   // Ply des Zweig-Anfangs in der Zählung des Kommentars (nur `searched`)
  if (b.startPly === undefined && ctx) {
    const alt = playPrefix(ctx.altFen, ctx.searched ? numberedPrefix(b, ctx.altPly) : b.sans);
    const cont = ctx.contFen ? playPrefix(ctx.contFen, ctx.searched ? numberedPrefix(b, ctx.contPly) : b.sans) : [];
    // Gleichstand → Alternative: dafür trennt das Komma überhaupt („c5, a5, Kd4" = Aufzählung).
    [steps, usedFen, basePly] = cont.length > alt.length ? [cont, ctx.contFen, ctx.contPly] : [alt, ctx.altFen, ctx.altPly];
    if (ctx.searched) {
      const free = resolveVariation(startFen, ucis, b.sans);
      if (free.length > steps.length) { steps = free; usedFen = undefined; unframed = true; }
    }
  } else {
    // Drohung vor allem anderen (siehe Datei-Kopf): Signalwort davor oder Kommentar an der Start-FEN.
    const t = b.threatSignal || threat.atStart ? threatSteps(startFen, b) : { steps: [] };
    const found = t.fen ? { steps: t.steps, baseFen: t.fen } : resolveVariationFrom(startFen, ucis, b.sans, b.startPly);
    steps = found.steps;
    if (t.fen) {
      usedFen = t.fen;
      threat.fens?.add(t.fen);
    } else if (b.startPly !== undefined) {
      const main = anchorFen(startFen, ucis, b.startPly);
      if (priorFens.length) {
        const sub = resolveInSubline(b, steps, main, priorFens, altBase);
        if (sub) [steps, usedFen] = [sub.steps, sub.fen];
      }
      // Zugnummer AUSSERHALB der Linie (das Buch zählt die Partiezüge, die Aufgaben-FEN beginnt bei Zug 1):
      // der Zweig ist per Suche gefunden — die gefundene Stellung ist sein Ort, siehe nextContext.
      if (!usedFen && main === undefined) { searchedFen = found.baseFen; basePly = b.startPly; }
    }
  }
  const plies = b.plies ?? [];
  // Numerloser Zweig ohne Rahmen: sein Anfang ist per Suche irgendwo in der Partie gelandet — bis zum
  // ersten Zug, dessen Zugnummer die gefundene Stelle bestätigt (ab dort ist sie kein Raten mehr).
  let guessed = 0;
  if (b.startPly === undefined && (!ctx || unframed)) {
    const confirmed = steps.findIndex((_st, k) => k > 0 && b.plies?.[k] !== undefined && baseStartPly(steps[k - 1].fen) === b.plies[k]);
    guessed = confirmed < 0 ? steps.length : confirmed;
  }
  // Aus einer Drohungs-Stellung gespielt (Nullzug selbst, Fortsetzung oder Alternative dazu) → Drohung.
  if (usedFen !== undefined && threat.fens?.has(usedFen)) steps = steps.map(st => ({ ...st, threat: true }));
  const out: (VariationStep | null)[] = b.sans.map((_s, k) => (k < steps.length ? steps[k] : null));
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
  resumeOuterVariation(b, out, priorFens);
  // Ausgeschlagener Rahmen bleibt für den nächsten Zweig stehen, wie er war (auf erratene Züge baut keiner auf).
  if (unframed) return { out, ctx, guessed };
  const searched = b.startPly !== undefined ? !usedFen && searchedFen !== undefined : !!ctx?.searched;
  const next = nextContext(startFen, ucis, b, out, usedFen ?? ctx?.altFen, usedFen ?? searchedFen);
  if (next && searched) {
    next.searched = true;
    next.altPly = basePly;
    next.contPly = continuationPly(plies, b.sans.length) ?? (basePly === undefined ? undefined : basePly + b.sans.length);
  }
  return { out, ctx: next, guessed };
}

/**
 * Aus dem PGN geklopfte Schachtelung MITTEN in einem Zweig: „2.Bxd4+ Rf6 … A. 3.Qb5 B. 3.Qd3 and C. 3.Qd1 3...Ref8
 * 4.Qg5+-" (Woodpecker, Aufgabe 245, gemeldet 2026-11-23) ist `3.Qb5 (3.Qd3) (3.Qd1) 3...Ref8 4.Qg5` — die überflüssige
 * Zugnummer „3..." direkt hinter „3.Qd1" markiert die Rückkehr in die äußere Variante. Wörtlich gelesen setzt
 * „3...Ref8" den Zweig 3.Qd1 fort, und 4.Qg5 geht von d1 nicht: der letzte Zug blieb Text.
 *
 * <p>Bleibt im Zweig ein Zug unaufgelöst, wird die Folge ab einem Zug davor (vom spätesten an) noch einmal versucht,
 * dessen Zugnummer genau die Fortsetzung ist (die Nummer war also überflüssig) und der ohne Prosa direkt hinter dem
 * Zug davor steht — an den Stellungen der bisher aufgelösten Kommentar-Züge mit derselben Ply (Zweig-Enden zuerst,
 * jüngste zuerst; nicht der eigene Zweig). Sie gilt nur, wenn dort MEHR Züge aufgehen als im Zweig selbst — sonst
 * bleibt alles, wie es war. Mit Prosa dazwischen („3.Rc3 … prevents 3...b5") ist die Nummer Erzählung, kein Rücksprung.</p>
 */
function resumeOuterVariation(b: CommentBranch, out: (VariationStep | null)[], prior: PriorFen[]): void {
  const plies = b.plies ?? [];
  const miss = out.findIndex(x => x === null);
  if (miss <= 0 || !prior.length) return;
  for (let j = miss; j >= 1; j--) {
    const ply = plies[j];
    if (ply === undefined || ply !== continuationPly(plies, j) || !out[j - 1] || !b.tight?.[j]) continue;
    const own = out[j - 1]!.fen;
    let held = 0;
    while (j + held < out.length && out[j + held]) held++;
    let best: VariationStep[] = [];
    for (const wantEnd of [true, false]) {
      for (let i = prior.length - 1; i >= 0; i--) {
        const { fen, end } = prior[i];
        if (end !== wantEnd || fen === own || baseStartPly(fen) !== ply) continue;
        const steps = playPrefix(fen, b.sans.slice(j));
        if (steps.length > best.length) best = steps;
      }
    }
    if (best.length > held) { best.forEach((step, n) => { out[j + n] = step; }); return; }
  }
}

/** Hauptlinien-Stellung, an der der Halbzug `ply` dran ist; `undefined` außerhalb der Linie. */
function anchorFen(startFen: string, ucis: string[], ply: number): string | undefined {
  const bases = mainlineFens(startFen, ucis);
  const anchor = ply - baseStartPly(startFen);
  return anchor >= 0 && anchor < bases.length ? bases[anchor] : undefined;
}

/** Der Rahmen für den NÄCHSTEN Zweig (siehe {@link BranchContext}). */
function nextContext(
  startFen: string, ucis: string[], b: CommentBranch, out: (VariationStep | null)[], inheritedAlt?: string,
  ownFen?: string,
): BranchContext | undefined {
  let altFen = inheritedAlt;
  if (b.startPly !== undefined) {
    // Der Ort eines nummerierten Zweigs ist die Hauptlinien-Stellung seiner Zugnummer. `ownFen` geht vor:
    // er stand in einer Untervariante ({@link resolveInSubline}) — oder seine Nummer liegt AUSSERHALB der
    // Linie und die Suche hat ihn gefunden (`searched`, siehe {@link BranchContext}). Ging der Zweig gar
    // nicht auf, gibt es keinen Ort.
    altFen = ownFen ?? anchorFen(startFen, ucis, b.startPly);
  }
  if (!altFen) return undefined;
  const last = out[out.length - 1];
  return { altFen, contFen: last?.fen };
}

/** Was ein aufgelöster Zweig dem nächsten hinterlässt: Rahmen ({@link BranchContext}) und die Stellungen
 *  der bisher aufgelösten Kommentar-Züge ({@link resolveInSubline}). */
interface ResolveState {
  ctx?: BranchContext; prior: PriorFen[];
  /** Stellung, aus der der LETZTE Zug des zuletzt aufgelösten Zweigs gespielt wurde — der Ort einer
   *  ausdrücklichen Alternative zu ihm ({@link CommentBranch}, `altOfPrev`). Fehlt, wenn dieser Zug nicht
   *  aufging oder nur erraten ist. */
  lastBase?: string;
  /** Kommentar steht an der Start-FEN ({@link CommentSegmentOptions}). */
  atStart?: boolean;
  /** Stellungen aus Drohungen (Nullzug-Stellung + Stellungen nach Drohungs-Zügen): wer dort weiterspielt, zeigt
   *  ebenfalls die Drohung. */
  threatFens: Set<string>;
}

/** Löst einen Zweig auf und schreibt Rahmen + Stellungen in `state` fort. */
function resolveInto(startFen: string, ucis: string[], b: CommentBranch, state: ResolveState): (VariationStep | null)[] {
  const r = resolveBranch(startFen, ucis, b, state.ctx, state.prior, b.altOfPrev ? state.lastBase : undefined,
    { atStart: state.atStart, fens: state.threatFens });
  // Auf per Suche gefundene Züge (`guessed`, erwähnte Pläne wie „with Rfe1 and Qd5") baut kein späterer
  // Zweig auf; was dahinter an seiner Zugnummer verankert wurde, zählt.
  // Zweig-Ende nur, wenn der Zweig bis zum letzten Zug aufging — sonst weiß niemand, wo er endet.
  r.out.forEach((step, k) => {
    if (step && k >= r.guessed) state.prior.push({ fen: step.fen, end: k === r.out.length - 1 });
    if (step?.threat) state.threatFens.add(step.fen);
  });
  state.ctx = r.ctx;
  const n = r.out.length;
  const last = r.out[n - 1];
  const before = r.out[n - 2];
  state.lastBase = n >= 2 && n - 2 >= r.guessed && last && before ? before.fen : undefined;
  return r.out;
}

/** Teil eines Zweigs als eigener Zweig (ohne Trennstellen). */
function sliceBranch(b: CommentBranch, from: number, to: number): CommentBranch {
  const plies = (b.plies ?? []).slice(from, to);
  return { sans: b.sans.slice(from, to), startPly: plies[0], plies, tight: b.tight?.slice(from, to).map((x, k) => k > 0 && x),
    ...(from === 0 && b.altOfPrev ? { altOfPrev: true } : {}), ...(from === 0 && b.threatSignal ? { threatSignal: true } : {}) };
}

/**
 * Löst einen Zweig mit MÖGLICHEN Trennstellen auf ({@link branches}, Regel (c)). An jeder steht die Frage:
 * setzt der nummerierte Zug die Züge davor fort, oder beginnt er einen eigenen Zweig an seiner Zugnummer?
 *
 * <ul>
 * <li><b>Nummer springt oder davor stand keine</b> („with Qf3 on the next move. For example, 17...Be5",
 *   „12...Nxd5 - but look at the pawns … on d5 and d6 … 15.Bf4"), und sie liegt in der Linie: die Zugnummer
 *   ist der Anker — eigener Zweig.</li>
 * <li><b>Nummer ist die genaue Fortsetzung</b>, liegt aber außerhalb der Linie (Buch mit eigener Zugzählung,
 *   „11...Kg6 would be hopeless … pawns on h4 and e5 …, e.g. 12.Qd3+ f5"): die Folge findet ihre Stellung
 *   nur als GANZES — Fortsetzung.</li>
 * <li><b>Sonst</b> wird der Abschnitt bis zur nächsten Trennstelle in beiden Lesarten gespielt, und es
 *   gilt die, in der mehr seiner NUMMERIERTEN Züge aufgehen (numerlose sind oft nur erwähnt und gehen
 *   irgendwo zufällig); bei Gleichstand die Fortsetzung. „3.f5, when Black can't take on g5 or e4 …
 *   3...dxe4" bleibt so zusammen, während „5...Be7 … lands on c5 or c7 … The immediate 6.g3 which runs into
 *   6...Be5" zwar weiterzählt, aber in die Partie gehört (nach 5...Be7 ginge 6...Be5 nicht).</li>
 * </ul>
 */
function resolveWithSoftSplits(startFen: string, ucis: string[], b: CommentBranch, state: ResolveState): (VariationStep | null)[] {
  const soft = b.soft ?? [];
  const plies = b.plies ?? [];
  const out: (VariationStep | null)[] = [];
  const copy = (st: ResolveState): ResolveState =>
    ({ ctx: st.ctx, prior: [...st.prior], lastBase: st.lastBase, atStart: st.atStart, threatFens: new Set(st.threatFens) });
  /** Aufgelöste NUMMERIERTE Züge des Abschnitts, der bei `offset` beginnt. */
  const numbered = (steps: (VariationStep | null)[], offset: number) =>
    steps.filter((x, k) => x && plies[offset + k] !== undefined).length;
  let from = 0;   // Beginn der laufenden Gruppe (alles davor ist entschieden und steht in `out`/`state`)
  soft.forEach(({ at, exact }, i) => {
    const to = i + 1 < soft.length ? soft[i + 1].at : b.sans.length;
    const ply = plies[at];
    const inLine = ply !== undefined && anchorFen(startFen, ucis, ply) !== undefined;
    const headState = copy(state);
    const head = resolveInto(startFen, ucis, sliceBranch(b, from, at), headState);
    let own: boolean;
    if (!exact && inLine) own = true;
    else if (exact && !inLine) own = false;
    else {
      const joint = resolveInto(startFen, ucis, sliceBranch(b, from, to), copy(state));
      const alone = resolveInto(startFen, ucis, sliceBranch(b, at, to), copy(headState));
      own = numbered(alone, at) > numbered(joint.slice(at - from), at);
    }
    if (own) {
      out.push(...head);
      state.ctx = headState.ctx;
      state.prior = headState.prior;
      state.lastBase = headState.lastBase;
      state.threatFens = headState.threatFens;
      from = at;
    }
  });
  out.push(...resolveInto(startFen, ucis, sliceBranch(b, from, b.sans.length), state));
  return out;
}

/**
 * Zerlegt einen Kommentar in Segmente (Text + klickbare Züge). Jeder Zweig wird eigenständig gegen die
 * Puzzle-Hauptlinie (`startFen`/`ucis`) aufgelöst; ein Zug ist NUR klickbar, wenn er Teil des legalen
 * Präfix SEINES Zweigs ist (sonst reiner Text). Spoilerschutz: {@link CommentSegmentOptions}, `played`.
 */
export function buildCommentSegments(
  text: string, startFen: string, ucis: string[], opts: CommentSegmentOptions = {},
): CommentSegment[] {
  // Pro Token in Original-Reihenfolge den aufgelösten Schritt (oder null) bestimmen.
  const perToken: (VariationStep | null)[] = [];
  const played = opts.played;
  if (played !== undefined) ucis = ucis.map((u, i) => (i < played ? u : HIDDEN_UCI));
  const state: ResolveState = { prior: [], atStart: !!opts.atStart, threatFens: new Set() };
  for (const b of branches(text)) perToken.push(...resolveWithSoftSplits(startFen, ucis, b, state));

  const segments: CommentSegment[] = [];
  let last = 0;
  // Dieselbe Tokenfolge wie in branches() — sonst verrutscht die Zuordnung Token ↔ Schritt.
  scanTokens(text).toks.forEach((t, tokenIdx) => {
    if (t.start > last) segments.push({ text: text.slice(last, t.start) });
    const raw = text.slice(t.start, t.end);
    const step = tokenIdx < perToken.length ? perToken[tokenIdx] : null;
    if (step) segments.push({ move: raw.trim(), fen: step.fen, from: step.from, to: step.to, ...(step.threat ? { threat: true } : {}) });
    else segments.push({ text: raw });   // nicht spielbar → als Text belassen
    last = t.end;
  });
  if (last < text.length) segments.push({ text: text.slice(last) });
  return segments;
}
