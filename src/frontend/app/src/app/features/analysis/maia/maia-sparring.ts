import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { Color } from 'chessground/types';
import { AuthService } from '../../../core/auth.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { AnalyzeGameService } from '../../games/analyze-game.service';
import { GamesService } from '../../games/games.service';
import { AnalysisLine } from '../analysis-engine.service';
import { EVAL_SETTLE_DEPTH } from '../analysis-settings';
import { AnalysisNode, isWithin, numberedSan, pathTo, playUci } from '../analysis-tree';
import { GuessUploadStatus } from '../game-analysis.service';
import { MaiaEngineService } from './maia-engine.service';
import {
  MAIA_CHECK_DEPTH, MAIA_DEFAULT_ELO, MAIA_ELO_KEY, MAIA_ELO_OPTIONS, MAIA_EVALBAR_KEY, MAIA_WARN_KEY,
} from './maia-model';
import { BadMoveVerdict, EvalPoint, SparringWarning, badMoveVerdict } from './sparring-check';
import { buildSparringPgn } from './sparring-pgn';

/** Was das Sparring vom Analysebrett braucht: Dienste und Zugriffe auf Brett, Baum und Engine — Abfragen als Funktion,
 *  damit es immer den aktuellen Stand sieht. */
export interface MaiaSparringHost {
  readonly maia: MaiaEngineService;
  readonly games: GamesService;
  readonly analyzeGame: AnalyzeGameService;
  readonly auth: AuthService;
  readonly snackbar: SnackbarService;
  readonly translate: TranslateService;
  readonly router: Router;
  root(): AnalysisNode;
  currentNode(): AnalysisNode;
  turnColor(): Color;
  /** Hat die Stellung auf dem Brett legale Züge? */
  hasMoves(): boolean;
  /** Steht der Stellungs-Editor offen? */
  editing(): boolean;
  engineOn(): boolean;
  /** Engine an/aus NUR für diese Sitzung (nicht in localStorage — die Dauereinstellung bleibt). */
  setEngineOn(on: boolean): void;
  setOrientation(color: Color): void;
  /** Die Haupt-Engine sucht gerade. */
  running(): boolean;
  depthSetting(): number;
  goToNode(node: AnalysisNode): void;
  /** Brett + Engine für die aktuelle Stellung neu aufsetzen. */
  refresh(): void;
  /** Der Zugbaum hat sich geändert (Zugliste baut neu auf). */
  treeChanged(): void;
  /** View markieren (Angular 22 refresht nach Asynchronem nicht von selbst). */
  changed(): void;
}

/** Sparring gegen Maia (0.632.0) im Analysebrett — einfache Felder + `changed()` nach allem Asynchronen.
 *
 * Maias Züge laufen über goToNode → refresh → historySaver.schedule: die gespielte Linie landet ohne weiteres Zutun im
 * Zugbaum und im Analyse-Verlauf. Die Karte (MaiaSparringCardComponent) lädt das Modell; hier steht nur das Spiel. */
export class MaiaSparring {
  /** Läuft ein Sparring: ab welcher Stellung, welche Seite der Nutzer spielt, und ob die Engine vorher an war. `tip` = der
   *  zuletzt gespielte Zug im Teilbaum von `start` — die Partie fürs Analysieren ist die Linie `start` → `tip` (nicht die
   *  Fortsetzung von `start`: mitten in einer geladenen Partie stehen dort deren Züge). */
  sparring: { start: AnalysisNode; tip: AnalysisNode; userColor: Color; engineWasOn: boolean } | null = null;
  /** Die zuletzt beendete Sparring-Partie mit mindestens zwei Halbzügen — „Partie analysieren" nimmt sie. Verfällt mit
   *  jedem neuen Baum und wenn ihre Züge aus dem Baum gelöscht werden. */
  lastSparring: { start: AnalysisNode; tip: AnalysisNode; userColor: Color; elo: number } | null = null;
  /** „Partie analysieren" läuft (speichern + einreihen) — Doppelklick-Schutz. */
  analyzingSparring = false;
  /** Steht eine Engine bereit? Einmal gefragt, sobald der Knopf zum ersten Mal sichtbar würde; `null` = unbekannt. */
  analyzeStatus: GuessUploadStatus | null = null;
  private analyzeStatusAsked = false;
  maiaThinking = false;
  /** Gewählte Stärke (je Gerät gemerkt, nur Werte aus MAIA_ELO_OPTIONS). */
  maiaElo: number = MAIA_DEFAULT_ELO;
  /** Jede Navigation/jeder Abbruch zählt hoch — eine späte Maia-Antwort für eine verlassene Stellung verfällt. */
  maiaEpoch = 0;
  /** Mindest-Bedenkzeit, damit Maias Zug nicht im selben Augenblick wie der eigene aufs Brett knallt (Specs: 0). */
  maiaDelayMs = 500;
  /** Schalter der Maia-Karte, je Gerät gemerkt (Vorgabe aus): eigene schlechte Züge melden / die Bewertungsleiste
   *  während des Sparrings anlassen. Beide lassen die Analyse-Engine STILL mitlaufen (`sparringEngineQuiet`). */
  warnBadMoves = false;
  keepEvalBar = false;
  /** Warnung zum zuletzt geprüften eigenen Zug — `before` = Stellung VOR dem Zug („Analysieren" springt dorthin). */
  sparringWarning: (SparringWarning & { before: AnalysisNode; drop: number }) | null = null;
  /** Spur der Bewertungen der aktuellen Stellung je Tiefe (Weiß-Sicht) — die Vorher-Werte der Zug-Prüfung. */
  evalTrack: { fen: string; points: EvalPoint[] } = { fen: '', points: [] };
  /** Ein eigener Zug am Brett: die Stellung davor und ihre Spur — `requestMaiaMove` übernimmt sie. */
  private pendingBefore: { node: AnalysisNode; points: EvalPoint[] } | null = null;
  /** Die laufende Prüfung der Stellung NACH dem eigenen Zug, bedient aus onEngineUpdate. */
  private pendingCheck: {
    fen: string; mover: Color; before: EvalPoint[]; epoch: number;
    resolve: (v: BadMoveVerdict | null) => void; timer: ReturnType<typeof setTimeout>;
  } | null = null;
  /** Spätestens so lange wartet Maias Antwort auf das Urteil (Specs: klein). */
  maiaCheckTimeoutMs = 4000;

  constructor(private host: MaiaSparringHost) {
    try {
      const elo = parseInt(localStorage.getItem(MAIA_ELO_KEY) || '', 10);
      if ((MAIA_ELO_OPTIONS as readonly number[]).includes(elo)) this.maiaElo = elo;
      this.warnBadMoves = localStorage.getItem(MAIA_WARN_KEY) === '1';
      this.keepEvalBar = localStorage.getItem(MAIA_EVALBAR_KEY) === '1';
    } catch {}
  }

  /** Seite verlassen: eine späte Antwort verfällt, Maia wird freigegeben (~150 MB: Modell + Sitzung im Worker) — beim
   *  nächsten Besuch kommt es in ~1 s aus dem Cache. */
  destroy(): void {
    this.maiaEpoch++;
    this.cancelMoveCheck();
    this.host.maia.release();
  }

  /** Ein neuer Baum beendet ein Sparring (Maia-Regel 9) — die Engine kommt zurück wie beim „Beenden". Die alte Partie
   *  gehört nicht mehr aufs Brett, „Partie analysieren" verfällt mit ihr. */
  onNewTree(): void {
    if (this.sparring) this.endSparring();
    this.lastSparring = null;
  }

  /** „Schlechte Züge melden": vor einem eigenen Zug am Brett die Stellung davor samt ihrer Bewertungs-Spur merken —
   *  nur für Züge der Seite des Nutzers (Explorer/Repertoire-Züge werden nicht geprüft). */
  beforeBoardMove(before: AnalysisNode, mover: Color): void {
    if (!this.sparring || !this.warnBadMoves || mover !== this.sparring.userColor) return;
    this.pendingBefore = { node: before, points: this.evalTrack.fen === before.fen ? [...this.evalTrack.points] : [] };
  }

  /** Ein Zug am Brett ist gespielt: Partie fortschreiben; war es der eigene, antwortet Maia. Zieht der Nutzer für Maias
   *  Seite, kommt keine Antwort. */
  afterBoardMove(node: AnalysisNode, mover: Color): void {
    this.noteSparringMove(node);
    if (this.sparring && mover === this.sparring.userColor) this.requestMaiaMove();
  }

  /** Vor dem Löschen eines Zweigs: fällt die Ausgangsstellung des Sparrings weg? (Dann endet es, Maia-Regel 9.) */
  endedByRemoving(node: AnalysisNode): boolean {
    return !!this.sparring && isWithin(this.sparring.start, node);
  }

  /** Nach dem Löschen eines Zweigs (`parent` = der Zug davor). */
  afterRemove(parent: AnalysisNode, endsSparring: boolean): void {
    if (endsSparring) this.endSparring();
    // Fiel nur ein Teil der laufenden Partie weg, ist ihr letzter Zug jetzt der Zug davor (er liegt noch unter `start`).
    if (this.sparring && !this.isAttached(this.sparring.tip)) this.sparring.tip = parent;
    // Hängt die zuletzt gespielte Partie nicht mehr am Baum, gibt es sie nicht mehr zu analysieren.
    if (this.lastSparring && !this.isAttached(this.lastSparring.tip)) this.lastSparring = null;
  }

  /** Während des Sparrings rechnet die Engine still mit (keine Linien, Pfeile, Kandidaten), wenn einer der beiden
   *  Maia-Schalter an ist — für die Zug-Prüfung bzw. die Bewertungsleiste. */
  get sparringEngineQuiet(): boolean {
    return !!this.sparring && (this.warnBadMoves || this.keepEvalBar);
  }

  /** Die Bewertung der besten Linie je Tiefe merken (nur die erste Zeile einer Tiefe zählt). */
  trackEval(fen: string, best: AnalysisLine): void {
    if (this.evalTrack.fen !== fen) this.evalTrack = { fen, points: [] };
    if (this.evalTrack.points.some(p => p.depth === best.depth)) return;
    this.evalTrack.points.push({ depth: best.depth, score: best.score, scoreType: best.scoreType, evalText: best.evalText });
  }

  /** Der tiefste Punkt der Spur für `fen` (ab `minDepth`), sonst null. */
  private deepestPoint(fen: string, minDepth = 0): EvalPoint | null {
    if (this.evalTrack.fen !== fen) return null;
    let deepest: EvalPoint | null = null;
    for (const p of this.evalTrack.points) if (p.depth >= minDepth && (!deepest || p.depth > deepest.depth)) deepest = p;
    return deepest;
  }

  /** Sparring läuft, Maia ist am Zug, und die Stellung ist nicht zu Ende — die Karte bietet dann „Maia zieht" an. */
  get maiaToMove(): boolean {
    return !!this.sparring && this.host.turnColor() !== this.sparring.userColor && this.host.hasMoves();
  }

  /** Von der Karte, sobald das Modell bereit ist: ab HIER, der Nutzer spielt die Seite am Zug, die Engine geht aus
   *  (ihr Zustand wird gemerkt, aber NICHT in localStorage — die Dauereinstellung des Nutzers bleibt). */
  startSparring(): void {
    if (this.sparring || this.host.editing() || !this.host.hasMoves()) return;   // zu Ende: es gibt nichts zu spielen
    this.sparring = { start: this.host.currentNode(), tip: this.host.currentNode(), userColor: this.host.turnColor(), engineWasOn: this.host.engineOn() };
    this.host.setEngineOn(false);
    this.host.setOrientation(this.sparring.userColor);
    this.host.refresh();
    this.host.changed();
  }

  /** „Beenden": eine laufende Antwort verfällt, die Engine kommt in den Zustand von vorher zurück. */
  stopSparring(): void {
    if (!this.sparring) return;
    this.endSparring();
    if (this.sparringAnalyzeVisible) this.ensureAnalyzeStatus();
    this.host.refresh();
    this.host.changed();
  }

  /** „Seite wechseln": ist jetzt Maia am Zug, zieht sie gleich. */
  switchSparringSides(): void {
    if (!this.sparring) return;
    this.abortMaia();
    const userColor: Color = this.sparring.userColor === 'white' ? 'black' : 'white';
    this.sparring = { ...this.sparring, userColor };
    this.sparringWarning = null;
    this.host.setOrientation(userColor);
    if (this.maiaToMove) this.requestMaiaMove();
    this.host.changed();
  }

  /** „Nochmal ab der Ausgangsstellung": zurück zum Start; ist dort Maia am Zug, zieht sie. */
  restartSparring(): void {
    if (!this.sparring) return;
    this.sparringWarning = null;
    this.host.goToNode(this.sparring.start);
    if (this.maiaToMove) this.requestMaiaMove();
    this.host.changed();
  }

  /** Maia nach ihrem Zug in der aktuellen Stellung fragen und ihn — wenn er noch gefragt ist — wie einen eigenen Zug
   *  in den Baum spielen. Eine Navigation, ein Abbruch oder das Ende des Sparrings dazwischen lassen ihn verfallen. */
  requestMaiaMove(): void {
    // Der eigene Zug davor (aus onMove) — nur dieser Aufruf darf ihn prüfen.
    const before = this.pendingBefore;
    this.pendingBefore = null;
    if (!this.maiaToMove) return;
    const epoch = ++this.maiaEpoch;
    const node = this.host.currentNode();
    this.maiaThinking = true;
    this.host.changed();
    const pause = this.maiaDelayMs > 0
      ? new Promise<void>(resolve => setTimeout(resolve, this.maiaDelayMs))
      : Promise.resolve();
    // „Schlechte Züge melden": Maias Antwort wartet auf das Urteil. So steht die Warnung schon da, wenn ihr Zug aufs Brett
    // kommt, und die Suche der Stellung nach dem eigenen Zug wird nicht von Maias Zug abgewürgt.
    const check = before && this.warnBadMoves && this.sparring
      ? this.awaitMoveCheck(node.fen, this.sparring.userColor, before.points, epoch)
      : Promise.resolve(null);
    Promise.all([this.host.maia.chooseMove(node.fen, this.maiaElo), pause, check]).then(
      ([uci, , verdict]) => {
        if (!this.maiaStillWanted(epoch, node)) return;
        this.maiaThinking = false;
        if (before) {
          this.sparringWarning = verdict
            ? { san: numberedSan(node), before: before.node, beforeText: verdict.before.evalText,
                afterText: verdict.after.evalText, drop: verdict.drop }
            : null;
        }
        if (uci) {
          const next = playUci(node, uci);
          if (next) {
            this.host.treeChanged();
            this.host.goToNode(next);   // zählt maiaEpoch selbst hoch — die Antwort ist da schon verbucht
            this.noteSparringMove(next);
          } else {
            this.host.snackbar.warn(this.host.translate.instant('analysis.maia.moveFailed'));
          }
        }
        this.host.changed();
      },
      () => {
        this.cancelMoveCheck(epoch);   // Maias Zug kam nicht — die Prüfung dieses Aufrufs braucht niemand mehr
        if (!this.maiaStillWanted(epoch, node)) return;
        this.maiaThinking = false;
        this.host.snackbar.warn(this.host.translate.instant('analysis.maia.moveFailed'));   // das Sparring bleibt aktiv
        // Ist die Sitzung selbst weg (Worker gestorben → Status `error`), im Hintergrund neu aufbauen: das Modell liegt
        // im Cache, der nächste Klick auf „Maia zieht" geht dann wieder. War es nur diese eine Anfrage, bleibt alles.
        if (this.host.maia.status() !== 'ready') this.host.maia.prepare().catch(() => {});
        this.host.changed();
      },
    );
  }

  onMaiaEloChange(elo: number): void {
    if (!(MAIA_ELO_OPTIONS as readonly number[]).includes(elo)) return;
    this.maiaElo = elo;   // gilt ab dem nächsten Maia-Zug
    try { localStorage.setItem(MAIA_ELO_KEY, String(elo)); } catch {}
  }

  private maiaStillWanted(epoch: number, node: AnalysisNode): boolean {
    return epoch === this.maiaEpoch && !!this.sparring && this.host.currentNode() === node;
  }

  /** Eine laufende Maia-Antwort verfallen lassen (Navigation, Seite wechseln, Editor, Ende). */
  abortMaia(): void {
    this.maiaEpoch++;
    this.maiaThinking = false;
    this.cancelMoveCheck();
  }

  /** Sparring beenden, ohne neu zu zeichnen — die Aufrufer tun es selbst (refresh bzw. goToNode). Die gespielte Partie
   *  bleibt für „Partie analysieren" stehen, wenn sie mindestens zwei Halbzüge hat. */
  private endSparring(): void {
    const sparring = this.sparring;
    if (!sparring) return;
    this.abortMaia();
    this.host.setEngineOn(sparring.engineWasOn);
    this.sparring = null;
    this.pendingBefore = null;
    this.sparringWarning = null;
    this.lastSparring = this.sparringPlies(sparring.start, sparring.tip) >= 2
      ? { start: sparring.start, tip: sparring.tip, userColor: sparring.userColor, elo: this.maiaElo }
      : null;
  }

  // ---- „Schlechte Züge melden" + „Bewertungsleiste anlassen" (0.645.0) ----

  onWarnBadMovesChange(on: boolean): void {
    this.warnBadMoves = on;
    try { localStorage.setItem(MAIA_WARN_KEY, on ? '1' : '0'); } catch {}
    if (!on) { this.pendingBefore = null; this.cancelMoveCheck(); this.sparringWarning = null; }
    if (this.sparring) this.host.refresh();   // die stille Engine startet bzw. stoppt sofort
    this.host.changed();
  }

  onKeepEvalBarChange(on: boolean): void {
    this.keepEvalBar = on;
    try { localStorage.setItem(MAIA_EVALBAR_KEY, on ? '1' : '0'); } catch {}
    if (this.sparring) this.host.refresh();   // aus → Leiste neutral; an → sie folgt ab der nächsten belastbaren Tiefe
    this.host.changed();
  }

  /** „Analysieren" an der Warnung: Sparring beenden (die Partie bleibt für „Partie analysieren"), Engine EIN — nur für
   *  diese Sitzung, nicht in localStorage — und auf die Stellung VOR dem schlechten Zug; er steht dort als Fortsetzung im
   *  Zugbaum, die Linien zeigen, was besser war. */
  analyzeWarning(): void {
    const w = this.sparringWarning;
    if (!w || !this.sparring) return;
    this.endSparring();
    this.sparringWarning = null;
    if (this.sparringAnalyzeVisible) this.ensureAnalyzeStatus();
    this.host.setEngineOn(true);
    if (this.isAttached(w.before)) this.host.goToNode(w.before);
    else this.host.refresh();
    this.host.changed();
  }

  /** Urteil über den eigenen Zug: sobald die Suche der Stellung nach dem Zug `min(MAIA_CHECK_DEPTH, Tiefe)` erreicht oder
   *  vorher endet; spätestens nach `maiaCheckTimeoutMs` mit dem besten Wert ab EVAL_SETTLE_DEPTH, sonst `null`. */
  private awaitMoveCheck(fen: string, mover: Color, before: EvalPoint[], epoch: number): Promise<BadMoveVerdict | null> {
    this.cancelMoveCheck();
    return new Promise(resolve => {
      const timer = setTimeout(() => this.finishMoveCheck(this.deepestPoint(fen, EVAL_SETTLE_DEPTH)), this.maiaCheckTimeoutMs);
      this.pendingCheck = { fen, mover, before, epoch, resolve, timer };
      this.serveMoveCheck(fen);   // die Spur der Stellung kann schon tief genug sein
    });
  }

  /** Aus onEngineUpdate: ist die Prüfung dieser Stellung so weit? */
  serveMoveCheck(fen: string): void {
    const check = this.pendingCheck;
    if (!check || check.fen !== fen) return;
    const after = this.deepestPoint(fen);
    if (!after) return;
    if (after.depth >= Math.min(MAIA_CHECK_DEPTH, this.host.depthSetting()) || !this.host.running()) this.finishMoveCheck(after);
  }

  private finishMoveCheck(after: EvalPoint | null): void {
    const check = this.pendingCheck;
    if (!check) return;
    clearTimeout(check.timer);
    this.pendingCheck = null;
    check.resolve(after ? badMoveVerdict(check.before, after, check.mover) : null);
  }

  /** Eine offene Prüfung ohne Urteil beenden (Navigation, Abbruch, Ende) — nur die eines bestimmten Aufrufs, wenn
   *  `epoch` angegeben ist. */
  private cancelMoveCheck(epoch?: number): void {
    const check = this.pendingCheck;
    if (!check || (epoch !== undefined && check.epoch !== epoch)) return;
    clearTimeout(check.timer);
    this.pendingCheck = null;
    check.resolve(null);
  }

  // ---- „Partie analysieren" nach dem Sparring ----
  //
  // Derselbe Weg wie in „Meine Partien": die Sparring-Partie wird eine gewöhnliche Partie (`POST /api/games/import`, mit
  // der eigenen Seite) und läuft dann durch `AnalyzeGameService.submit` — keine eigene Analyse, keine eigene Engine-Wahl.

  /** Ein Zug kam während des Sparrings aufs Brett (eigener, Maias, aus Explorer/Repertoire): liegt er unter `start`, ist
   *  er der neue letzte Zug der Partie — auch nach Zurückgehen und anders Weiterspielen. */
  noteSparringMove(node: AnalysisNode): void {
    if (!this.sparring || !isWithin(node, this.sparring.start)) return;
    this.sparring.tip = node;
    if (this.sparringAnalyzeVisible) this.ensureAnalyzeStatus();
  }

  /** Den Knopf zeigen: angemeldet, eine Partie mit mindestens zwei Halbzügen, und entweder ist das Sparring vorbei oder
   *  seine Stellung zu Ende (mitten in der Partie hat die Karte schon vier Symbole). */
  get sparringAnalyzeVisible(): boolean {
    if (!this.host.auth.isLoggedIn) return false;
    const s = this.sparring;
    if (s) return this.sparringPlies(s.start, s.tip) >= 2 && this.host.currentNode() === s.tip && !this.host.hasMoves();
    return !!this.lastSparring;
  }

  analyzeSparring(): void {
    if (this.analyzingSparring || !this.sparringAnalyzeVisible) return;
    if (this.sparring) this.stopSparring();   // läuft es noch (Stellung zu Ende), endet es wie mit „Beenden"
    const game = this.lastSparring;
    if (!game) return;
    const startDepth = pathTo(game.start).length;
    const pgn = buildSparringPgn({
      startFen: game.start.fen,
      sans: pathTo(game.tip).slice(startDepth).map(n => n.san),
      userColor: game.userColor,
      userName: this.host.auth.currentUser?.username ?? '',
      elo: game.elo,
      date: new Date(),
    });
    if (!pgn) { this.host.snackbar.warn(this.host.translate.instant('analysis.maia.saveFailed')); return; }
    this.analyzingSparring = true;
    this.host.changed();
    const done = () => { this.analyzingSparring = false; this.host.changed(); };
    this.host.games.importPgn(pgn, game.userColor).subscribe({
      next: res => {
        const id = res?.ids?.[0];
        if (!id) { this.host.snackbar.warn(this.host.translate.instant('analysis.maia.saveFailed')); done(); return; }
        // Absage (keine Engine, Deckel): die Snackbar nennt den Grund, die Partie liegt trotzdem in „Meine Partien".
        this.host.analyzeGame.submit(this.host.games.analyzeUrl(id), this.analyzeStatus).subscribe(ok => {
          done();
          if (ok) void this.host.router.navigate(['/games', id]);
        });
      },
      error: () => { this.host.snackbar.warn(this.host.translate.instant('analysis.maia.saveFailed')); done(); },
    });
  }

  /** Halbzüge von `start` bis `tip` (`tip` liegt im Teilbaum von `start`). */
  private sparringPlies(start: AnalysisNode, tip: AnalysisNode): number {
    return pathTo(tip).length - pathTo(start).length;
  }

  /** Hängt der Knoten noch am aktuellen Baum? (Gelöschte Zweige verlieren ihren Elternknoten.) */
  private isAttached(node: AnalysisNode): boolean {
    let n: AnalysisNode | null = node;
    while (n?.parent) n = n.parent;
    return n === this.host.root();
  }

  /** Die Engine-Auskunft höchstens einmal holen — nur angemeldet. */
  private ensureAnalyzeStatus(): void {
    if (this.analyzeStatusAsked || !this.host.auth.isLoggedIn) return;
    this.analyzeStatusAsked = true;
    this.host.analyzeGame.status().subscribe(status => { this.analyzeStatus = status; this.host.changed(); });
  }
}
