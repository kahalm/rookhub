import { leagueHubUrl } from '../../core/partner-site';
import {
  Component, DoCheck, OnInit, HostListener, inject, ChangeDetectionStrategy, computed, effect, signal, viewChild,
  untracked, DestroyRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { BoardArrow, ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { MoveListComponent } from '../../shared/pgn-viewer/move-list.component';
import { PgnViewerService } from '../../shared/pgn-viewer/pgn-viewer.service';
import { PreferencesService } from '../../core/preferences.service';
import { HandoffService } from '../../core/handoff.service';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { downloadBlob } from '../../shared/download.util';
import { pgnFileName } from '../../shared/pgn-export.util';
import { GameAnalysisService, GuessUploadStatus } from '../analysis/game-analysis.service';
import { AnalyzeGameService } from './analyze-game.service';
import { GameRoast, GamesService, SharedGame } from './games.service';
import { GameReviewComponent } from './game-review.component';
import { GameEvalsStatus } from './game-review.util';
import { DeepStored } from './deep-analysis.util';
import { Mistake, MistakesBySide, NO_MISTAKES, mistakesOf, trainingSide, withoutPlies } from './mistakes.util';
import { MistakesTrainerComponent } from './mistakes-trainer.component';
import { MistakesSession } from './mistakes-session';
import { MistakeJudgeService } from './mistake-judge.service';
import { MarkPositionButtonComponent } from '../../shared/mark-position/mark-position-button.component';
import { MarkOrigin } from '../../shared/mark-position/marked-positions.service';
import { PositionRepertoiresComponent } from '../repertoire/position-repertoires.component';
import { ExternalEngineInfo, ExternalEngineService, engineTagKey, isEngineOffline } from '../analysis/external-engine.service';
import { ANALYSIS_DEPTH_KEY, ANALYSIS_PROVIDER_KEY } from '../analysis/analysis-settings';
import { LiveEngineSession } from './live-engine-session';
import { LiveEnginePanelComponent } from './live-engine-panel.component';
import { PositionMenuComponent } from '../analysis/position-menu.component';
import { BoardBadge } from './move-badge.util';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ScoresheetService, openPhotoBlob, photoFileName } from './scoresheet.service';
import { ScoresheetPhotoDialogComponent } from './scoresheet-photo-dialog.component';
import { GameRoastData, GameRoastDialogComponent } from './game-roast-dialog.component';
import { SimilarGamesComponent } from './similar-games.component';
import { GameRecapComponent } from './game-recap.component';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { SHARED_GAME_STYLES } from './shared-game.styles';

/** Ab so viel Bewegung (px) bzw. Dauer (ms) ist eine Berührung des Bretts kein Tipp mehr, sondern Ziehen/Halten. */
const TAP_SLOP_PX = 10;
const TAP_MAX_MS = 500;

/**
 * Nachspiel-Seite einer Partie — in ZWEI Rollen, dieselbe Ansicht:
 * - <c>/g/:token</c>: die geteilte Partie, öffentlich, kein Login nötig (Route ohne <c>data.mode</c>);
 * - <c>/games/:id</c> (<c>data.mode = 'own'</c>): die eigene gespeicherte Partie aus <c>/games</c>. Bis 0.512.0
 *   öffnete die Liste dafür den PGN-Viewer-DIALOG; der Nutzer wollte eine Seite (2026-09-23) — und die gab es
 *   für den Teilen-Link längst, samt Kurve und Analysieren-Knopf. Eigenes dazu: Zurück-Pfeil, Teilen-Link.
 * Reused den PgnViewerService + chess-board/move-list aus dem PGN-Viewer.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-shared-game',
  standalone: true,
  imports: [IconLabelDirective,
    CommonModule, RouterLink, MatButtonModule, MatIconModule, MatCardModule, MatProgressSpinnerModule, MatTooltipModule,
    TranslatePipe, ChessBoardComponent, MoveListComponent, PositionRepertoiresComponent, GameReviewComponent,
    MistakesTrainerComponent, LiveEnginePanelComponent, PositionMenuComponent, MarkPositionButtonComponent, MatMenuModule, MatDialogModule,
    SimilarGamesComponent, GameRecapComponent,
  ],
  providers: [PgnViewerService],
  templateUrl: './shared-game.component.html',
  styles: [SHARED_GAME_STYLES]
})
export class SharedGameComponent implements OnInit, DoCheck {
  private auth = inject(AuthService);
  private gameAnalyses = inject(GameAnalysisService);
  private handoff = inject(HandoffService);
  /** „In die Vereins-Datenbank" (LeagueHub, Wunsch 2026-09-28): nur mit dem Recht dazu und wenn es ein LeagueHub zu diesem
   * RookHub gibt. Die Partie wird drüben geladen und läuft durch dieselbe Übersicht wie ein PGN-Upload. */
  readonly clubImport = this.auth.has('league.contribute') && !!this.handoff.leagueHubUrl;
  private router = inject(Router);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private analyzeGame = inject(AnalyzeGameService);
  private mistakeJudge = inject(MistakeJudgeService);
  private externalEngines = inject(ExternalEngineService);
  private scoresheets = inject(ScoresheetService);
  private dialog = inject(MatDialog);

  game: SharedGame | null = null;
  loading = true;
  notFound = false;
  flipped = false;
  /** Eigene Partie (`/games/:id`) statt Teilen-Link — entscheidet Datenquelle, Adressen und Kopfzeile. */
  own = false;
  /** Vereinspartie aus LeagueHub (`/club-games/:id`, 0.653.0): Bewertungen aus der Hintergrund-Analyse des Vereins,
   *  kein „Partie analysieren" (die rechnet von selbst), keine Erklärungen (die gibt es dort nicht). */
  club = false;
  /** Teilen-Token der eigenen Partie (für „Teilen-Link kopieren"). */
  shareToken: string | null = null;

  get notFoundKey(): string { return this.own || this.club ? 'games.loadError' : 'games.shared.notFound'; }
  /** `GET …/evals` dieser Partie — anonym die Kurve des Teilenden, angemeldet ersatzweise die eigene. */
  evalsUrl: string | null = null;
  /** `GET …/similar` — ähnliche Meisterpartien (0.544.0). */
  similarUrl: string | null = null;
  private analyzeUrl = '';
  // Signale statt Felder: sie ändern sich in HTTP-Antworten und in der Ausgabe der Kind-Komponente,
  // und nur ein gelesenes Signal markiert die Ansicht zuverlässig zum Neuzeichnen (Angular 22).
  /** Läuft gerade der Aufruf „Partie analysieren"? Sperrt den Knopf gegen Doppelklick — zwei Klicks
   *  binnen Millisekunden sähe auch der Server noch nicht als dieselbe Analyse. */
  readonly analyzing = signal(false);
  /** Ob eine Engine da ist und wie viele Partien noch frei sind — nur angemeldet abgefragt (der
   *  Endpunkt braucht ein Konto); ohne Antwort bleibt der Knopf benutzbar und der Server entscheidet. */
  readonly uploadStatus = signal<GuessUploadStatus | null>(null);
  /** Stand der Kurve unter dem Brett (`none` = noch keine Analyse → Knopf anbieten). */
  readonly reviewStatus = signal<GameEvalsStatus>('none');
  private readonly review = viewChild(GameReviewComponent);
  /** Abfragbare Fehler beider Seiten, gemeldet vom Rückblick unter dem Brett. */
  readonly mistakes = signal<MistakesBySide>(NO_MISTAKES);
  /** Vom Besitzer ausgeblendete Halbzüge („nicht mehr zeigen", 0.748.0) — nur bei der eigenen Partie geladen. */
  readonly dismissedPlies = signal<readonly number[]>([]);
  /** Was der Trainer abfragt: die Fehler ohne die ausgeblendeten. */
  readonly trainable = computed(() => withoutPlies(this.mistakes(), this.dismissedPlies()));
  /** Wessen Partie — als Signal, damit Zähler und Trainer-Seite nachziehen, sobald die Partie da ist. */
  private readonly ownerSide = signal<'white' | 'black' | null>(null);
  /** Die Seite, die der Trainer abfragt — der Knopf zählt NUR sie (siehe `trainingSide`). */
  readonly mistakeSide = computed(() => this.trainSide() ?? trainingSide(this.trainable(), this.ownerSide()));
  /** Seite aus dem geteilten Trainings-Link (`?train=white|black`, 0.740.0) — schlägt die Seite des Besitzers. */
  readonly trainSide = signal<'white' | 'black' | null>(null);
  /** Mit `?train=` geöffnet: das Training startet von selbst, sobald die Analyse Aufgaben dieser Seite hergibt (einmal). */
  private autoTrain = false;
  private readonly autoTrainEffect = effect(() => {
    const side = this.trainSide();
    if (!side || !this.autoTrain || this.training()) return;
    if (mistakesOf(this.trainable(), side).length === 0) return;
    this.autoTrain = false;
    untracked(() => this.trainMistakes());
  });
  readonly mistakeTotal = computed(() => mistakesOf(this.trainable(), this.mistakeSide()).length);

  analysisRunning(): boolean {
    const s = this.reviewStatus();
    return s === 'pending' || s === 'running';
  }

  /** Laufendes Training „Eigene Fehler nachspielen" — `null` = die Seite zeigt die Partie. */
  readonly training = signal<MistakesSession | null>(null);
  /** Id der eigenen Partie (`/games/:id`) — ohne sie wird nichts gemeldet (geteilte Ansicht). */
  gameId: number | null = null;

  toClub(): void {
    if (this.gameId) void this.handoff.jumpToLeagueHub(`verein/neu?partie=${this.gameId}`);
  }
  /** Formular-Einlesung der eigenen Partie (0.529.0) — `null` = kein Foto. */
  scanId: number | null = null;
  /** LeagueHub-Adresse für „Korrigieren" einer Vereinspartie (0.660.0); `null` außerhalb der Partner-Domains. */
  readonly leagueHub = leagueHubUrl();
  /** Schon gemeldete Halbzüge und Aufgabenzahl: verhindert, dass jeder Zug dieselbe Meldung wiederholt. */
  private readonly reportedPlies = new Set<number>();
  private reportedTotal = -1;
  /** Pfeil für den besten Zug, geliefert vom Rückblick (Schalter dort); im Training leer. */
  readonly bestArrows = signal<BoardArrow[]>([]);
  /** Klasse des aktuellen Zugs als Symbol am Zielfeld (vom Rückblick; im Training und mit Live-Engine leer). */
  readonly moveBadge = signal<BoardBadge | null>(null);

  /** Der freigegebene Roast hinter `?roast=` (0.742.0) — null ohne Parameter oder wenn nicht (mehr) freigegeben. */
  readonly sharedRoast = signal<GameRoast | null>(null);
  private roastId: number | null = null;

  private loadSharedRoast(token: string | null | undefined): void {
    if (!token || this.roastId == null) return;
    this.games.sharedRoast(token, this.roastId).subscribe({ next: r => this.sharedRoast.set(r), error: () => undefined });
  }

  /**
   * Zugliste und Kurve laufen mit: je Aufgabe springt die Partie auf die Stellung VOR dem Fehler — man
   * sieht, wo in der Partie man ist, und nach dem Beenden steht man genau dort.
   */
  private readonly followTask = effect(() => {
    const m = this.training()?.current();
    if (m) this.service.goToMove(m.ply - 1);
  });

  /**
   * Trainiert wird die Seite des Besitzers; ohne Zuordnung (fremde geteilte Partie) die mit den meisten
   * Fehlern — in der Leiste lässt sich umschalten, sobald beide Seiten welche haben. Gespielt wird auf dem
   * Brett dieser Seite (bis 0.518.0 ein Dialog mit eigenem, kleinerem Brett).
   */
  /** Jeder Treffer wird gemeldet — auch der, den die Browser-Engine erst verzögert bestätigt. */
  private readonly meldeTreffer = effect(() => {
    const t = this.training();
    const solved = t?.solvedPlies() ?? [];
    if (t && solved.length) untracked(() => this.reportMistakes(t, solved));
  });

  /** Live-Engine + eigene Züge (0.525.0) — im Fehler-Training aus, dort verriete sie die Lösung. */
  readonly live = signal<LiveEngineSession | null>(null);
  private readonly stopLiveOnDestroy = inject(DestroyRef).onDestroy(() => { this.stopLive(); this.stopTrainingAnalysis(); });

  /** Grobe Zeiger (Handy, Tablet): dort heißt ein Tipp aufs Brett „blättern", gezogen wird per Drag. */
  readonly coarsePointer = typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches;
  /** Beginn einer Berührung auf dem Brett — ob daraus ein Tipp wird, entscheidet das Loslassen. */
  tapStart: { x: number; y: number; t: number } | null = null;

  onBoardPointerDown(e: PointerEvent): void {
    this.tapStart = e.pointerType === 'touch' && e.isPrimary ? { x: e.clientX, y: e.clientY, t: Date.now() } : null;
  }

  /** Ein TIPP (kurz, ohne Bewegung) auf die linken bzw. rechten 40 % des Bretts = Zug zurück bzw. vor — wie vorher die
   *  Tippzonen. Alles mit Bewegung ist ein Ziehen und gehört dem Brett. Nur Touch: mit der Maus gibt es die Pfeile. */
  onBoardTap(e: PointerEvent): void {
    const s = this.tapStart;
    this.tapStart = null;
    if (!s || e.pointerType !== 'touch') return;
    if (Math.hypot(e.clientX - s.x, e.clientY - s.y) > TAP_SLOP_PX || Date.now() - s.t > TAP_MAX_MS) return;
    const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
    if (r.width <= 0) return;
    const rel = (e.clientX - r.left) / r.width;
    if (rel < 0.4) this.navPrev();
    else if (rel > 0.6) this.navNext();
  }

  /** Eine Figur auf dem Partie-Brett gezogen: wie auf Lichess eine eigene Variante ab hier — dafür gibt es die Live-Engine
   *  (sie führt die Nebenvariante und rechnet sie gleich). Ist sie aus, wird sie dafür eingeschaltet. */
  onBoardMove(move: UserBoardMove): void {
    if (!this.live()) this.toggleLive();
    this.live()?.play(move, this.service.currentFen);
  }

  toggleLive(): void {
    if (this.live()) { this.stopLive(); return; }
    const session = this.createLiveSession();
    this.live.set(session);
    session.sync(this.service.currentMoveIndex, this.service.currentFen);
    this.useStoredRemoteEngine(session);
  }

  /** Eigene Engine-Instanz je Seite — als Methode, damit Tests keinen echten Stockfish starten müssen. */
  protected createLiveSession(): LiveEngineSession {
    return new LiveEngineSession(undefined, this.storedDepth());
  }

  /** Die Stellung auf dem Brett — in der Nebenvariante der Live-Engine deren Ende, sonst der Partiezug. */
  positionFen(): string {
    return this.live()?.fen(this.service.currentFen) ?? this.service.currentFen;
  }

  /** Herkunft für den „+"-Knopf (0.749.0): welche Partie — gesetzt in ngOnInit je Ansicht. */
  markSource: Partial<MarkOrigin> = {};
  private markOriginCache: { ply: number | null; origin: MarkOrigin } | null = null;
  /** Herkunft der Stellung auf dem Brett; in einer eigenen Nebenvariante ohne Halbzug (sie steht in keiner Partie). */
  markOrigin(): MarkOrigin {
    const ply = this.live()?.variation()?.length ? null : this.service.currentMoveIndex + 1;
    // Gleiches Objekt, solange sich nichts ändert — das Signal-Input des Knopfs soll nicht bei jeder Prüfung feuern.
    if (this.markOriginCache?.ply !== ply) this.markOriginCache = { ply, origin: { ...this.markSource, context: 'analysis', ply } };
    return this.markOriginCache.origin;
  }

  stopLive(): void {
    this.live()?.destroy();
    this.live.set(null);
  }

  get loggedIn(): boolean { return this.auth.isLoggedIn; }

  /** Hinterlegtes der Partie-Analyse zur Stellung auf dem Brett (aus dem Rückblick). */
  readonly reviewStored = signal<DeepStored>({ sf: null, lc0: null });
  /** „Tiefe Analyse" im ⋮-Menü (0.686.0): nur angemeldet; in einer eigenen Nebenvariante gibt es nichts Hinterlegtes. */
  deepStored(): DeepStored | null {
    if (!this.loggedIn) return null;
    return this.live()?.variation()?.length ? { sf: null, lc0: null } : this.reviewStored();
  }

  /** Externe Engines fuer die Live-Wahl im ⋮ (0.681.0) — ohne die Hintergrund-Engines, die gehoeren den Auftraegen
   *  (dieselbe Regel wie beim Uebernehmen der gemerkten Wahl). */
  readonly liveEngines = signal<ExternalEngineInfo[]>([]);

  loadLiveEngines(): void {
    if (!this.auth.isLoggedIn) return;
    this.externalEngines.listEngines().subscribe({
      next: r => {
        const background = r.backgroundEngineIds ?? [];
        this.liveEngines.set(r.engines.filter(e => !background.includes(e.id)));
      },
      error: () => this.liveEngines.set([]),
    });
  }

  /** Die gerade gewaehlte Live-Engine: die der laufenden Sitzung, sonst die gemerkte Wahl; `null` = Browser. */
  currentEngineId(): string | null {
    const session = this.live();
    if (session) return session.engineId();
    try {
      const stored = localStorage.getItem(ANALYSIS_PROVIDER_KEY);
      return stored && stored !== 'wasm' ? stored : null;
    } catch { return null; }
  }

  engineOffline(e: ExternalEngineInfo): boolean { return isEngineOffline(e); }
  engineTag(e: ExternalEngineInfo): string | null { return engineTagKey(e); }

  /**
   * Live-Engine waehlen (⋮ → Live-Engine). Gemerkt wird sie unter DEMSELBEN Schluessel wie am Analysebrett, die Wahl
   * gilt also auf beiden Seiten. Laeuft die Live-Engine schon, wechselt sie sofort; sonst startet sie mit der Wahl.
   */
  chooseLiveEngine(e: ExternalEngineInfo | null): void {
    try { localStorage.setItem(ANALYSIS_PROVIDER_KEY, e?.id ?? 'wasm'); } catch { /* kein Speicher: gilt nur jetzt */ }
    const session = this.live();
    if (!session) { this.toggleLive(); return; }
    if (e) session.useRemote(e, (id, work) => this.externalEngines.analyse(id, work));
    else session.useBrowser();
  }

  /**
   * „Analysieren" im Fehler-Training (seit 0.526.2): das Brett wird frei, die Live-Engine rechnet. Gilt bis zur nächsten
   * Aufgabe: `ngDoCheck` beendet die Analyse, sobald die Aufgabe, die Seite oder die Phase („nochmal") wechselt.
   *
   * WO die Analyse anfängt, hängt daran, ob die Lösung schon bekannt ist (seit 0.527.2, gemeldet 2026-09-24):
   * - gefunden/gezeigt: ab der Stellung VOR dem Fehler, der Zug steht schon drauf — ← darf dorthin zurück;
   * - nach einem Fehlversuch: ab der Stellung NACH dem eigenen Zug, und das ist der Anfang der Variante. Wer ← dahinter
   *   zurückkönnte, stünde in der Aufgabenstellung, und die Engine zeigte dort Pfeil und Linien des gesuchten Zugs.
   */
  readonly trainingAnalysis = signal<{
    session: LiveEngineSession; base: string; index: number; side: string;
    /** Zug, der auf dem Brett markiert bleibt, solange die Variante leer ist (der eigene Fehlversuch). */
    lastMove?: [string, string];
  } | null>(null);

  toggleTrainingAnalysis(): void {
    if (this.trainingAnalysis()) { this.stopTrainingAnalysis(); return; }
    const t = this.training();
    const m = t?.current();
    if (!t || !m) return;
    const session = this.createLiveSession();
    const move = t.lastMove();
    if (t.phase() === 'wrong') {
      const base = t.boardFen();
      session.sync(-2, base);
      this.useStoredRemoteEngine(session);
      this.trainingAnalysis.set({ session, base, index: t.index(), side: t.side(), lastMove: move ?? undefined });
      return;
    }
    session.sync(-2, m.fenBefore);
    const san = t.phase() === 'right' ? t.foundSan() : m.bestSan;
    if (move && t.boardFen() !== m.fenBefore) {
      session.play({ from: move[0], to: move[1], san, fen: t.boardFen() }, m.fenBefore);
    }
    this.useStoredRemoteEngine(session);
    this.trainingAnalysis.set({ session, base: m.fenBefore, index: t.index(), side: t.side() });
  }

  stopTrainingAnalysis(): void {
    this.trainingAnalysis()?.session.destroy();
    this.trainingAnalysis.set(null);
  }

  /** Mit der Partie abgleichen: geblättert → Nebenvariante weg, neue Stellung → rechnen (billig ohne Änderung). */
  ngDoCheck(): void {
    this.live()?.sync(this.service.currentMoveIndex, this.service.currentFen);
    const a = this.trainingAnalysis();
    if (a) {
      const t = this.training();
      const phase = t?.phase();
      if (!t || t.index() !== a.index || t.side() !== a.side || phase === 'ask' || phase === 'checking' || phase === 'done') {
        this.stopTrainingAnalysis();
      } else {
        a.session.sync(-2, a.base);
      }
    }
  }

  /** Dieselbe Tiefe wie am Analysebrett (dort gewählt und gemerkt), sonst 22. */
  private storedDepth(): number {
    try {
      const d = parseInt(localStorage.getItem(ANALYSIS_DEPTH_KEY) || '', 10);
      return d >= 6 && d <= 50 ? d : 22;
    } catch { return 22; }
  }

  /**
   * Hat man am Analysebrett eine externe Engine gewählt (Lichess-Anbindung, z. B. die eigene Cloud-Engine), rechnet
   * auch hier sie — sonst Stockfish im Browser. Hintergrund-Engines bleiben außen vor (sie gehören den Aufträgen).
   */
  private useStoredRemoteEngine(session: LiveEngineSession): void {
    if (!this.auth.isLoggedIn) return;
    let stored: string | null = null;
    try { stored = localStorage.getItem(ANALYSIS_PROVIDER_KEY); } catch { /* kein Speicher → Browser */ }
    if (!stored || stored === 'wasm') return;
    this.externalEngines.listEngines().subscribe({
      next: r => {
        const background = r.backgroundEngineIds ?? [];
        const info = r.engines.find(e => e.id === stored && !background.includes(e.id));
        // Nur, solange die Session noch läuft: die Live-Engine ODER die „Analysieren"-Leiste des Fehler-Trainings
        // (dort ist live() immer null, trainMistakes beendet sie — F4-008).
        const active = this.live() === session || this.trainingAnalysis()?.session === session;
        if (info && active) session.useRemote(info, (id, work) => this.externalEngines.analyse(id, work));
      },
      error: () => { /* bleibt beim Browser */ },
    });
  }

  trainMistakes(): void {
    this.stopLive();
    // Nicht gelistete Züge prüft die Browser-Engine nach (nur wo die Analyse das offen lässt).
    this.training.set(new MistakesSession(this.trainable(), this.mistakeSide(),
      (m, fen) => this.mistakeJudge.judge(m, fen), fen => this.mistakeJudge.evaluate(fen),
      this.own && this.gameId ? m => this.dismissMistake(m) : undefined));
  }

  /** „Diesen Fehler nicht mehr zeigen" (0.748.0): auf dem Server merken; ein Fehlschlag bleibt still (wie die Meldung). */
  private dismissMistake(m: Mistake): void {
    if (!this.gameId) return;
    this.dismissedPlies.update(p => p.includes(m.ply) ? p : [...p, m.ply]);
    const total = mistakesOf(this.mistakes(), m.white ? 'white' : 'black').length;
    this.games.dismissMistake(this.gameId, m.ply, true, total).subscribe({ error: () => { /* nächster Versuch beim nächsten Klick */ } });
  }

  /** Alle ausgeblendeten Fehler dieser Partie wieder einblenden. */
  restoreDismissed(): void {
    if (!this.gameId) return;
    const before = this.dismissedPlies();
    this.dismissedPlies.set([]);
    this.games.dismissMistake(this.gameId, -1, false, mistakesOf(this.mistakes(), this.mistakeSide()).length)
      .subscribe({ error: () => this.dismissedPlies.set(before) });
  }

  onTrainingMove(e: UserBoardMove): void {
    this.training()?.onMove(e);
  }

  endTraining(): void {
    // Auch ohne neuen Treffer melden: dann steht in der Übersicht „0 von 7", und man sieht, dass die
    // Partie schon einmal offen war. Der Server vereinigt additiv, hier geht nichts verloren.
    const t = this.training();
    if (t) this.reportMistakes(t, t.solvedPlies());
    this.stopTrainingAnalysis();
    this.training.set(null);
  }

  /**
   * Gefundene Fehler an RookHub melden — daraus zeigt `/games` „4 von 7 · 3 offen". Nur bei der EIGENEN
   * Partie (die geteilte Ansicht kennt keine Id des Betrachters). Still im Fehlerfall: das ist
   * Buchführung im Hintergrund, und die nächste Meldung trägt denselben Stand erneut.
   */
  private reportMistakes(session: MistakesSession, solved: readonly number[]): void {
    if (!this.own || !this.gameId) return;
    const neu = solved.filter(p => !this.reportedPlies.has(p));
    if (!neu.length && this.reportedTotal === session.list().length) return;
    neu.forEach(p => this.reportedPlies.add(p));
    this.reportedTotal = session.list().length;
    this.games.recordMistakes(this.gameId, session.list().length, [...solved]).subscribe({
      error: () => { neu.forEach(p => this.reportedPlies.delete(p)); this.reportedTotal = -1; },
    });
  }

  analyzeTooltip(): string {
    if (this.analysisRunning()) return 'games.review.running';
    return this.uploadStatus()?.engineAvailable === false ? 'guess.upload.noEngine' : 'games.analyzeHint';
  }

  constructor(
    public service: PgnViewerService,
    private route: ActivatedRoute,
    private games: GamesService,
    public preferences: PreferencesService,
  ) {}

  ngOnInit(): void {
    const train = this.route.snapshot.queryParamMap?.get('train');
    if (train === 'white' || train === 'black') { this.trainSide.set(train); this.autoTrain = true; }
    const roast = Number(this.route.snapshot.queryParamMap?.get('roast'));
    this.roastId = Number.isInteger(roast) && roast > 0 ? roast : null;
    this.own = this.route.snapshot.data?.['mode'] === 'own';
    this.club = this.route.snapshot.data?.['mode'] === 'club';
    if (this.club) {
      const id = Number(this.route.snapshot.paramMap.get('id'));
      this.markSource = { clubGameId: id };
      this.evalsUrl = this.games.clubEvalsUrl(id);
      this.games.clubGame(id).subscribe({
        next: g => this.show(g),
        error: () => { this.notFound = true; this.loading = false; },
      });
      return;
    }
    if (this.own) {
      const id = Number(this.route.snapshot.paramMap.get('id'));
      this.gameId = id;
      this.markSource = { savedGameId: id };
      this.evalsUrl = this.games.evalsUrl(id);
      this.similarUrl = this.games.similarUrl(id);
      this.analyzeUrl = this.games.analyzeUrl(id);
      this.games.mistakes(id).subscribe(p => this.dismissedPlies.set(p?.dismissedPlies ?? []));
      this.games.get(id).subscribe({
        next: g => { this.shareToken = g.shareToken; this.scanId = g.scanId ?? null; this.show(g); this.loadSharedRoast(g.shareToken); },
        error: () => { this.notFound = true; this.loading = false; },
      });
      return;
    }
    const token = this.route.snapshot.paramMap.get('token') || '';
    this.markSource = { shareToken: token };
    this.evalsUrl = this.games.sharedEvalsUrl(token);
    this.similarUrl = this.games.sharedSimilarUrl(token);
    this.analyzeUrl = this.games.sharedAnalyzeUrl(token);
    this.games.getShared(token).subscribe({
      next: g => {
        // Der eigene Teilen-Link: dieselbe Ansicht wie über die Partienliste (Zurück-Pfeil, Teilen-Knopf, gemerktes
        // Fehler-Training …) — gewünscht 2026-09-24. `replaceUrl`, damit „Zurück" im Browser nicht wieder hierher führt.
        if (g.ownGameId && this.auth.isLoggedIn) {
          // Trainings- bzw. Roast-Link gilt auch für den Besitzer selbst
          const queryParams: Record<string, string | number> = {};
          const side = this.trainSide();
          if (side) queryParams['train'] = side;
          if (this.roastId != null) queryParams['roast'] = this.roastId;
          this.router.navigate(['/games', g.ownGameId], Object.keys(queryParams).length ? { replaceUrl: true, queryParams } : { replaceUrl: true });
          return;
        }
        this.show(g);
        this.loadSharedRoast(token);
      },
      error: () => { this.notFound = true; this.loading = false; },
    });
  }

  private show(g: SharedGame): void {
    this.game = g;
    this.ownerSide.set(g.ownerSide ?? null);
    // Aus der Sicht des Besitzers: spielte er Schwarz, startet das Brett gedreht (Flip-Knopf bleibt).
    this.flipped = g.ownerSide === 'black';
    this.service.loadPgn(g.pgn);
    // `?ply=n`: aus einer geernteten Aufgabe hierher — auf die Stellung nach n Halbzügen springen
    const ply = Number(this.route.snapshot.queryParamMap?.get('ply'));
    if (Number.isInteger(ply) && ply > 0) this.service.goToMove(ply - 1);
    this.loading = false;
    if (this.auth.isLoggedIn) {
      this.analyzeGame.status().subscribe(u => this.uploadStatus.set(u));
    }
  }

  /** Das Formular-Foto der eigenen, eingelesenen Partie anzeigen (Dialog) oder herunterladen. */
  /** „Roast my game" (0.535.0) — nur die eigene Partie. */
  roast(): void {
    if (!this.gameId) return;
    this.dialog.open(GameRoastDialogComponent, {
      data: { gameId: this.gameId, shareUrl: this.shareToken ? this.games.shareUrl(this.shareToken) : null, shareToken: this.shareToken ?? null } satisfies GameRoastData,
      maxWidth: '96vw',
    });
  }

  photo(download: boolean): void {
    if (!this.gameId) return;
    if (!download) { ScoresheetPhotoDialogComponent.open(this.dialog, this.gameId); return; }
    this.scoresheets.photo(this.gameId).subscribe({
      next: blob => openPhotoBlob(blob, photoFileName(this.gameId!, blob)),
      error: () => this.snackbar.warn(this.translate.instant('games.photo.loadError')),
    });
  }

  /** Im Analysebrett öffnen — mit dem GANZEN PGN (Kopfdaten, Kommentare), nicht nur den Zügen (0.592.0); „Zurück" führt
   *  hierher. Übergabe per Router-State wie aus der Partienliste. */
  openInAnalysis(): void {
    const pgn = this.game?.pgn;
    if (!pgn) return;
    void this.router.navigate(['/analysis'], { state: { pgn }, queryParams: { from: this.router.url } });
  }

  /** Das PGN der Partie in die Zwischenablage (so, wie es gespeichert ist — samt Kopfdaten und Kommentaren). */
  /** „Mit Lc0 analysieren" — angemeldet, Vereinsmitglied, und die Partie hat noch keine Lc0-Analyse. */
  canLc0(): boolean {
    return this.loggedIn && this.auth.has('league.view') && !!this.game?.pgn && !this.review()?.hasLc0();
  }

  analyzeLc0(): void {
    const pgn = this.game?.pgn;
    if (!pgn) return;
    this.gameAnalyses.startLc0(pgn).subscribe({
      next: () => {
        this.snackbar.success(this.translate.instant('games.lc0.started'));
        this.review()?.refreshAlternatives();
      },
      error: () => this.snackbar.warn(this.translate.instant('games.lc0.failed')),
    });
  }

  copyPgn(): void {
    const pgn = this.game?.pgn;
    if (!pgn) return;
    navigator.clipboard?.writeText(pgn).then(
      () => this.snackbar.copy(this.translate.instant('games.pgnCopied')),
      () => this.snackbar.warn(this.translate.instant('games.pgnCopyFailed')),
    );
  }

  /** Das PGN als Datei „Weiß_Schwarz_Datum.pgn". */
  downloadPgn(): void {
    const g = this.game;
    if (!g?.pgn) return;
    const players = [g.white, g.black].filter(n => !!n).join(' ');
    downloadBlob(new Blob([g.pgn.endsWith('\n') ? g.pgn : g.pgn + '\n'], { type: 'application/x-chess-pgn' }),
      pgnFileName(players || 'game', (g.playedAt ?? g.createdAt ?? '').slice(0, 10)));
  }

  /** Link „Fehler nachspielen" für eine Seite (0.740.0, Wunsch 2026-10-10): der Teilen-Link mit `?train=`. Wer ihn öffnet —
   *  angemeldet oder nicht —, landet direkt im Fehler-Training dieser Farbe. */
  trainingShareUrl(side: 'white' | 'black'): string | null {
    return this.shareToken ? `${this.games.shareUrl(this.shareToken)}?train=${side}` : null;
  }

  shareTraining(side: 'white' | 'black'): void {
    const url = this.trainingShareUrl(side);
    if (!url) return;
    navigator.clipboard?.writeText(url).then(
      () => this.snackbar.copy(this.translate.instant('games.shareTrainingCopied')),
      () => this.snackbar.warn(url),
    );
  }

  readonly trainSides: readonly ('white' | 'black')[] = ['white', 'black'];

  /** Anzahl Aufgaben je Seite — für die Menüpunkte „Fehler-Training teilen". */
  mistakeCount(side: 'white' | 'black'): number { return mistakesOf(this.mistakes(), side).length; }

  /** Teilen-Link der eigenen Partie in die Zwischenablage — wie in der Liste. */
  share(): void {
    if (!this.shareToken) return;
    const url = this.games.shareUrl(this.shareToken);
    navigator.clipboard?.writeText(url).then(
      () => this.snackbar.copy(this.translate.instant('games.shareCopied')),
      () => this.snackbar.warn(url),
    );
  }

  /** Die Partie rechnen lassen (siehe {@link AnalyzeGameService}). Die Seite ist ohne Anmeldung
   *  erreichbar, der Einwurf nicht: ohne Konto geht es zur Anmeldung und danach wieder hierher. Danach
   *  bleibt man HIER — die Kurve lädt neu und fragt nach, bis die Partie durch ist. */
  analyze(): void {
    if (!this.game || this.analyzing()) return;
    if (!this.auth.isLoggedIn) {
      this.snackbar.info(this.translate.instant('games.analyzeLogin'));
      this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
      return;
    }
    this.analyzing.set(true);
    this.analyzeGame.submit(this.analyzeUrl, this.uploadStatus()).subscribe(ok => {
      this.analyzing.set(false);
      if (ok) this.review()?.reload();
    });
  }

  // ----- Knöpfe unter dem Brett, Tipp aufs Brett, ← → (0.667.0): in einer eigenen Variante durch SIE -----

  /** Steht man in einer eigenen Variante (Live-Engine, mindestens ein eigener Zug auf dem Brett oder dahinter)? */
  private inVariation(): LiveEngineSession | null {
    const l = this.live();
    return l && (l.variation().length > 0 || l.canRedo()) ? l : null;
  }

  canNavBack(): boolean {
    return !!this.inVariation()?.variation().length || this.service.currentMoveIndex >= 0;
  }

  canNavForward(): boolean {
    const v = this.inVariation();
    if (v && v.variation().length) return v.canRedo();   // am Variantenende geht es nicht in der Partie weiter
    const g = this.service.currentGame;
    return !!v?.canRedo() || (!!g && this.service.currentMoveIndex < g.moves.length - 1);
  }

  navPrev(): void {
    const v = this.inVariation();
    if (v && v.variation().length) { v.undo(this.service.currentFen); return; }
    this.service.goBack();
  }

  navNext(): void {
    const v = this.inVariation();
    if (v) { if (v.canRedo()) v.redo(this.service.currentFen); return; }
    this.service.goForward();
  }

  /** ⏮: in einer Variante an ihren Anfang (Abzweig, die Züge bleiben), sonst an den Partieanfang. */
  navStart(): void {
    const v = this.inVariation();
    if (v && v.variation().length) { v.toStart(this.service.currentFen); return; }
    this.service.goToStart();
  }

  /** ⏭: in einer Variante an ihr Ende, sonst ans Partieende. */
  navEnd(): void {
    const v = this.inVariation();
    if (v) { v.toEnd(this.service.currentFen); return; }
    this.service.goToEnd();
  }

  /** ← / → in einer eigenen Variante; `true`, wenn die Taste dort etwas getan hat (oder am Variantenende nichts tun darf). */
  private stepVariation(event: KeyboardEvent, session: LiveEngineSession, baseFen: string): boolean {
    if (event.key === 'ArrowLeft' && session.variation().length) {
      event.preventDefault(); session.undo(baseFen); return true;
    }
    if (event.key === 'ArrowRight' && (session.canRedo() || session.variation().length)) {
      event.preventDefault(); session.redo(baseFen); return true;
    }
    return false;
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    // Nicht im Titel-Feld des Auftragsdialogs (der Cursor blieb stehen, die Partie dahinter blaetterte) und
    // nicht in einem offenen Menue/Dialog.
    if (!isBoardHotkey(event)) return;
    // Im Training zeigt das Brett die Aufgabe — die Pfeile blätterten sonst unsichtbar in der Partie darunter. In
    // der Analyse einer Aufgabe laufen sie durch die eigene Variante.
    const t = this.training();
    if (t) {
      // Leertaste nach der Lösung (gefunden oder gezeigt) = nächste Aufgabe (seit 0.526.2). Der Fokus geht vorher
      // vom Knopf weg — sonst löste die Leertaste den zuletzt geklickten Knopf beim Loslassen ein zweites Mal aus.
      if (event.key === ' ' && (t.phase() === 'right' || t.phase() === 'shown')) {
        event.preventDefault();
        (document.activeElement as HTMLElement | null)?.blur?.();
        t.next();
        return;
      }
      const a = this.trainingAnalysis();
      if (a) this.stepVariation(event, a.session, a.base);
      return;
    }
    // In der eigenen Nebenvariante laufen ← und → durch sie (← zurück, → wieder vor); an ihrem Anfang blättert ←
    // wie gewohnt in der Partie.
    if (event.key === 'ArrowLeft') { event.preventDefault(); this.navPrev(); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); this.navNext(); }
  }
}
