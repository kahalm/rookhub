import { Chess, Move, Square } from 'chess.js';
import { Key } from 'chessground/types';
import { applyUci, calcDests, tryFreeMove, tryLoadFen } from '../../features/puzzles/puzzle-move.util';

/**
 * Der gemeinsame Kern aller Löser: „ist dieser Zug der erwartete?" — EINMAL beantwortet.
 *
 * <p>Vorher stand die Frage in jedem Löser eigen da und bedeutete dabei Verschiedenes: die Puzzles
 * verglichen UCI mit einer Präfix-Regel, das Aufgabenblatt UCI mit einer eigenen Umwandlungs-Regel,
 * der Repertoire-Trainer normalisierte SAN. „Richtig gezogen" hieß damit an drei Stellen drei
 * verschiedene Dinge — und ein `Nbd2` gegen ein `Nd2` entschied je nach Löser anders.</p>
 *
 * <p>Hier ist es festgelegt: <b>verglichen werden FELDER</b> (von/nach/Umwandlung), nicht Text. Ein
 * erwarteter Zug darf als UCI ODER als SAN dastehen und wird auf dem AKTUELLEN Brett zu Feldern
 * aufgelöst; die SAN ist danach reine Anzeige. `Nbd2` und `b1d2` sind derselbe Zug, `0-0` und `O-O`
 * ebenso, und `Qxd8+`, `Qxd8` sowie `Qd8xd8`… beschreiben dieselben zwei Felder.</p>
 *
 * <p>Der Kern ist SYNCHRON und kennt weder Timer noch Engine noch Angular: Phasen, Verzögerungen,
 * Tipps, Eval und die Brett-Darstellung bleiben bei den Aufrufern. Er wird auf zwei Arten benutzt —
 * als Klasse {@link LineSolver} (eigenes Brett, eigene Halbzug-Zählung) oder als reine Funktionen
 * ({@link resolveExpectedUci}, {@link sameMove}, {@link judgeMove}) auf einer FREMDEN chess.js-Instanz,
 * für Löser, die ihr Brett schon selbst führen.</p>
 */

/**
 * Ein erwarteter Halbzug — als UCI (`e2e4`, `e7e8q`) oder als SAN (`Nbd2`, `O-O`, `Qxd8+`).
 * Aufgelöst wird immer gegen die Stellung, in der er fallen soll.
 */
export type ExpectedMove = { readonly uci: string } | { readonly san: string };

/**
 * Das Urteil über einen Nutzerzug, OHNE ihn anzuwenden.
 * <ul>
 *   <li>`correct` — der erwartete Zug.</li>
 *   <li>`alternative` — einer der geduldeten Alternativzüge dieses Halbzugs ([%alt]).</li>
 *   <li>`wrong` — legal, aber nicht der gesuchte Zug (auch: der erwartete Zug ist in dieser
 *       Stellung gar nicht auflösbar — dann kann kein Zug `correct` sein, siehe
 *       {@link resolveExpectedUci}).</li>
 *   <li>`illegal` — in dieser Stellung nicht spielbar.</li>
 *   <li>`not-your-turn` — die Figur auf dem Startfeld gehört der anderen Seite.</li>
 * </ul>
 */
export type Judgement = 'correct' | 'alternative' | 'wrong' | 'illegal' | 'not-your-turn';

/** Farbe im chess.js-Kürzel. */
export type SolverColor = 'w' | 'b';

/**
 * Vergleicht zwei Züge über die FELDER — die Präfix-Regel der Puzzles, hier für alle verbindlich:
 * <ul>
 *   <li>Fehlt im Nutzerzug die Umwandlungsfigur (`e7e8` gegen `e7e8q`), passt JEDE — der Aufrufer
 *       spielt ohnehin den erwarteten Zug und damit die erwartete Figur.</li>
 *   <li>Steht eine ANDERE Figur als erwartet (`e7e8q` gegen `e7e8r`), ist es falsch — eine
 *       Unterverwandlung ist ein eigener Zug.</li>
 * </ul>
 * Beides zusammen deckt die frühere Sonderregel des Aufgabenblatts
 * (`expected.length === 5 && expected.startsWith(orig + dest)`) ab: dort wird ohne Umwandlungsfigur
 * geurteilt, und genau dann greift die erste Zeile.
 */
export function sameMove(userUci: string, expectedUci: string): boolean {
  if (!userUci || !expectedUci) return false;
  return expectedUci.substring(0, userUci.length) === userUci;
}

/**
 * Löst einen erwarteten Zug auf der ÜBERGEBENEN Stellung zu seinem UCI auf; `null`, wenn er dort
 * nicht spielbar ist.
 *
 * <p>SAN geht über `chess.move(san)` auf einer KOPIE — das übergebene Brett bleibt unangetastet.
 * chess.js nimmt dabei in seiner (nicht-strikten) Vorgabe schon alles ab, was die Bestände
 * liefern: Suffixe (`Nf3!?`, `Qxd8+`), Rochade mit Nullen (`0-0`), Umwandlung ohne Gleichheitszeichen
 * (`e8Q`) und LANG-ALGEBRAISCHE Schreibweise (`Nb1d2`, `b1d2`) — nachgemessen an chess.js 1.4.0.
 * Eine eigene Vorreinigung im Stil von `normSan` braucht es deshalb nicht.</p>
 *
 * <p><b>Mehrdeutige SAN ergibt `null`</b>: chess.js weist `Nd2` ab, wenn zwei Springer nach d2
 * können (auch im nicht-strikten Modus — es gibt schlicht keinen einen Zug, der gemeint ist).
 * Das ist die richtige Antwort: raten wäre schlimmer als nachfragen. Für {@link judgeMove} heißt
 * ein unauflösbarer Erwartungszug, dass NICHTS `correct` sein kann; wer das unterscheiden muss,
 * fragt hier vorher (bzw. {@link LineSolver.expectedUci}).</p>
 */
export function resolveExpectedUci(chess: Chess, expected: ExpectedMove | null | undefined): string | null {
  if (!expected) return null;

  if ('uci' in expected) {
    const uci = (expected.uci || '').trim().toLowerCase();
    if (uci.length < 4) return null;
    const from = uci.substring(0, 2);
    const to = uci.substring(2, 4);
    const promotion = uci.substring(4, 5);
    const legal = chess.moves({ verbose: true })
      .some(m => m.from === from && m.to === to && (!promotion || m.promotion === promotion));
    return legal ? uci : null;
  }

  const san = (expected.san || '').trim();
  if (!san) return null;
  try {
    const probe = new Chess(chess.fen());
    const mv = probe.move(san);
    return mv ? mv.from + mv.to + (mv.promotion ?? '') : null;
  } catch {
    return null;   // illegal oder mehrdeutig in dieser Stellung
  }
}

/**
 * Urteilt über einen Nutzerzug, OHNE ihn anzuwenden — die reine Fassung für Löser, die ihr Brett
 * selbst führen. Angewendet wird getrennt (der Aufrufer entscheidet, WAS er spielt: den erwarteten
 * Zug, den Nutzerzug oder gar nichts).
 *
 * <p>Der Nutzerzug wird dabei bewusst aus den ROHEN Ereignis-Feldern gebaut (`orig + dest +
 * promotion`) und nicht aus dem chess.js-Treffer: nur so bleibt „ohne Umwandlungsfigur passt jede"
 * erhalten — chess.js' erster Treffer auf e7→e8 wäre der Springer.</p>
 */
export function judgeMove(
  chess: Chess,
  expected: ExpectedMove | null | undefined,
  alts: readonly ExpectedMove[] | undefined,
  orig: string,
  dest: string,
  promotion?: string,
): Judgement {
  const piece = chess.get(orig as Square);
  if (piece && piece.color !== chess.turn()) return 'not-your-turn';

  const legal = chess.moves({ verbose: true })
    .some(m => m.from === orig && m.to === dest && (!promotion || m.promotion === promotion));
  if (!legal) return 'illegal';

  const userUci = orig + dest + (promotion || '');

  const expectedUci = resolveExpectedUci(chess, expected);
  if (expectedUci && sameMove(userUci, expectedUci)) return 'correct';

  for (const alt of alts ?? []) {
    const altUci = resolveExpectedUci(chess, alt);
    if (altUci && sameMove(userUci, altUci)) return 'alternative';
  }
  return 'wrong';
}

export interface LineSolverOptions {
  /** Ausgangsstellung. Nicht ladbar (Chessable-Info-Diagramme ohne König) ⇒ Ersatzbrett
   *  (Grundstellung) und {@link LineSolver.startFenAccepted} `false`. */
  fen?: string;
  /** Die Linie als Folge erwarteter Halbzüge — Löser- und Gegnerzüge im Wechsel. */
  line?: readonly ExpectedMove[];
  /** Geduldete Alternativen ([%alt]) je Halbzug-Index der Linie. */
  alts?: Readonly<Record<number, readonly ExpectedMove[]>>;
  /** Farbe des Lösers. Ohne Angabe: die Seite, die nach dem Vorspiel (`startPly`) am Zug ist. */
  solverColor?: SolverColor;
  /** Wie viele Halbzüge der Linie vorgespielt werden, bevor gelöst wird (Vorgabe 0). */
  startPly?: number;
}

/**
 * Eine Linie mit eigenem Brett: welcher Halbzug ist dran, wer ist am Zug, was ist der erwartete
 * Zug — und was ist von einem Nutzerzug zu halten.
 *
 * <p>Sie hält NUR Stellung und Zählstand. Wann ein Gegnerzug fällt, wie lange ein Alternativzug
 * stehen bleibt, ob eine Engine antwortet: alles Sache des Aufrufers. `chess` ist ausdrücklich
 * LESBAR (Matt, Zuglisten, Farbe am Zug lesen die Löser selbst).</p>
 */
export class LineSolver {
  /** Das Brett. Lesen ist erwünscht (`isCheckmate`, `history`, `turn`); geschrieben wird nur hier. */
  readonly chess: Chess;
  /** Die tatsächlich geladene Ausgangsstellung (nach Ersatz bei unbrauchbarer FEN). */
  readonly startFen: string;
  /** Hat chess.js die übergebene FEN angenommen? `false` = es steht das Ersatzbrett. */
  readonly startFenAccepted: boolean;
  readonly line: readonly ExpectedMove[];
  readonly alts: Readonly<Record<number, readonly ExpectedMove[]>>;
  /** Wie viele Halbzüge der Linie vorgespielt sind, bevor gelöst wird. */
  readonly startPly: number;
  readonly solverColor: SolverColor;

  /** Index des NÄCHSTEN erwarteten Halbzugs in {@link line}. */
  ply = 0;

  /** Je auf dem Brett liegendem Halbzug: war es einer der LINIE (true) oder ein freier (false)?
   *  Nur so weiß {@link undo}, ob es den Zählstand mit zurückdrehen darf. */
  private applied: boolean[] = [];

  constructor(opts: LineSolverOptions = {}) {
    const loaded = opts.fen !== undefined ? tryLoadFen(opts.fen) : new Chess();
    this.startFenAccepted = !!loaded;
    this.chess = loaded ?? new Chess();
    this.startFen = this.chess.fen();
    this.line = opts.line ?? [];
    this.alts = opts.alts ?? {};
    this.startPly = Math.max(0, Math.min(opts.startPly ?? 0, this.line.length));
    this.playPrologue();
    this.solverColor = opts.solverColor ?? (this.chess.turn() as SolverColor);
  }

  /** Der erwartete Halbzug, oder `null` am Ende der Linie. */
  get expected(): ExpectedMove | null { return this.line[this.ply] ?? null; }

  /** Linie zu Ende? */
  get done(): boolean { return this.ply >= this.line.length; }

  /** Farbe am Zug. */
  get turn(): SolverColor { return this.chess.turn() as SolverColor; }

  /** Wartet die Linie gerade auf einen Zug des LÖSERS? */
  get userToMove(): boolean { return !this.done && this.turn === this.solverColor; }

  /** Der erwartete Zug als UCI in der AKTUELLEN Stellung; `null`, wenn er dort nicht auflösbar ist. */
  expectedUci(): string | null { return resolveExpectedUci(this.chess, this.expected); }

  /** Urteil über einen Nutzerzug, ohne ihn anzuwenden. Ist der Löser nicht dran (Gegnerzug fällig,
   *  Linie zu Ende), lautet es `not-your-turn` — unabhängig davon, wie gut der Zug wäre. */
  judge(orig: string, dest: string, promotion?: string): Judgement {
    if (!this.userToMove) return 'not-your-turn';
    return judgeMove(this.chess, this.expected, this.alts[this.ply], orig, dest, promotion);
  }

  /** Spielt den erwarteten Halbzug (Löserzug vorzeigen, Gegnerantwort, Rest vorspielen) und rückt
   *  den Zählstand vor. `null` = nicht auflösbar, es wurde NICHTS gespielt. */
  playExpected(): string | null {
    const uci = this.expectedUci();
    if (!uci) return null;
    applyUci(this.chess, uci);
    this.applied.push(true);
    this.ply++;
    return uci;
  }

  /** Spielt die Linie weiter, solange der GEGNER am Zug ist — also bis der Löser wieder dran ist
   *  (oder die Linie endet). Gibt die gespielten Halbzüge zurück. */
  opponentReply(): string[] {
    const played: string[] = [];
    while (!this.done && this.turn !== this.solverColor) {
      const uci = this.playExpected();
      if (!uci) break;
      played.push(uci);
    }
    return played;
  }

  /** Freier Zug (Rechenbrett, Abweichung vom Pfad) — er rückt den Zählstand NICHT vor.
   *  Umwandlung ohne gewählte Figur wird zur Dame. `null` = illegal, nichts gespielt. */
  playFree(orig: string, dest: string, promotion?: string): Move | null {
    const mv = tryFreeMove(this.chess, orig as Key, dest as Key, promotion);
    if (mv) this.applied.push(false);
    return mv;
  }

  /** Nimmt bis zu `n` Halbzüge zurück (Mausrutscher, Zurücknehmen im Lern-Modus) und dreht den
   *  Zählstand für jeden zurückgenommenen LINIEN-Halbzug mit. Gibt zurück, wie viele es wurden. */
  undo(n = 1): number {
    let undone = 0;
    for (let i = 0; i < n; i++) {
      if (!this.chess.undo()) break;
      if (this.applied.pop()) this.ply = Math.max(0, this.ply - 1);
      undone++;
    }
    return undone;
  }

  /** Legale Züge als chessground-`dests`. */
  dests(enPassantForced = false): Map<Key, Key[]> {
    return calcDests(this.chess, enPassantForced);
  }

  /** Zuletzt gespielter Halbzug als Feldpaar (für die Brett-Markierung). */
  lastMove(): [Key, Key] | undefined {
    const hist = this.chess.history({ verbose: true });
    const mv = hist[hist.length - 1];
    return mv ? [mv.from as Key, mv.to as Key] : undefined;
  }

  /** Zurück auf die Ausgangsstellung — samt Vorspiel, damit derselbe Zustand steht wie nach dem
   *  Aufsetzen. */
  reset(): void {
    this.chess.load(this.startFen);
    this.applied = [];
    this.ply = 0;
    this.playPrologue();
  }

  private playPrologue(): void {
    while (this.ply < this.startPly && this.playExpected() !== null) { /* Vorspiel */ }
  }
}
