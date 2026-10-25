import { leagueHubUrl } from '../../core/partner-site';
import {
  Component, DoCheck, OnInit, HostListener, inject, ChangeDetectionStrategy, signal, viewChild, DestroyRef,
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
import { GameAnalysisAlternative, GuessUploadStatus } from '../analysis/game-analysis.service';
import { AnalyzeGameService } from './analyze-game.service';
import { GameRoast, GamesService, SharedGame } from './games.service';
import { GameReviewComponent } from './game-review.component';
import { GameEvalsStatus } from './game-review.util';
import { DeepStored } from './deep-analysis.util';
import { MistakesTrainerComponent } from './mistakes-trainer.component';
import { GameTrainingController } from './game-training-controller';
import { MistakeJudgeService } from './mistake-judge.service';
import { MarkPositionButtonComponent } from '../../shared/mark-position/mark-position-button.component';
import { MarkOrigin } from '../../shared/mark-position/marked-positions.service';
import { PositionRepertoiresComponent } from '../repertoire/position-repertoires.component';
import { ExternalEngineInfo, ExternalEngineService, engineTagKey, isEngineOffline } from '../analysis/external-engine.service';
import { GameEngineController } from './game-engine-controller';
import { LiveEngineSession } from './live-engine-session';
import { LiveEnginePanelComponent } from './live-engine-panel.component';
import { PositionMenuComponent } from '../analysis/position-menu.component';
import { BoardBadge } from './move-badge.util';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialogModule } from '@angular/material/dialog';
import { GameActionsService } from './game-actions.service';
import { SimilarGamesComponent } from './similar-games.component';
import { GameRecapComponent } from './game-recap.component';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { SHARED_GAME_STYLES } from './shared-game.styles';
import { gameSourceLabel } from './game-source';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';

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
    SimilarGamesComponent, GameRecapComponent, LoadErrorComponent,
  ],
  providers: [PgnViewerService, GameActionsService],
  templateUrl: './shared-game.component.html',
  styles: [SHARED_GAME_STYLES]
})
export class SharedGameComponent implements OnInit, DoCheck {
  private auth = inject(AuthService);
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
  /** Aktionen an der Partie (PGN, Teilen, Löschen, Foto, Roast, Lc0) — je Seite bereitgestellt (V33, Z26). */
  private actions = inject(GameActionsService);

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

  /** Warum die Partie fehlt (a1-rk-vereinspartie-fehler-sackgasse): `locked` = Vereinspartie ohne Mitgliedschaft (403),
   *  `missing` = gibt es nicht (4xx), `failed` = Netz/Server — dann „Erneut versuchen" statt einer stummen Karte. */
  errorKind: 'locked' | 'missing' | 'failed' = 'missing';

  get notFoundKey(): string {
    if (this.errorKind === 'locked') return 'games.clubNoAccess';
    return this.own || this.club ? 'games.notFound' : 'games.shared.notFound';
  }

  /** Fehler beim Laden der Partie einordnen (siehe {@link errorKind}). */
  private loadFailed(e: { status?: number } | null | undefined): void {
    const status = e?.status ?? 0;
    this.errorKind = status === 403 && this.club ? 'locked'
      : status >= 400 && status < 500 && status !== 408 && status !== 429 ? 'missing' : 'failed';
    this.notFound = true;
    this.loading = false;
  }

  /** „Erneut versuchen" nach einem Ladefehler. */
  reload(): void {
    this.loading = true;
    this.notFound = false;
    this.ngOnInit();
  }
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
  /** „Eigene Fehler nachspielen": Fehlerliste, Seite, Sitzung, Selbststart, Meldungen (Zerlegung Z26). */
  readonly trainer = new GameTrainingController({
    stopLive: () => this.stopLive(),
    stopTrainingAnalysis: () => this.stopTrainingAnalysis(),
    goToMove: index => this.service.goToMove(index),
    own: () => this.own,
    gameId: () => this.gameId,
  }, inject(GamesService), this.mistakeJudge);
  /** Abfragbare Fehler beider Seiten, gemeldet vom Rückblick unter dem Brett. */
  readonly mistakes = this.trainer.mistakes;
  /** Vom Besitzer ausgeblendete Halbzüge („nicht mehr zeigen", 0.748.0) — nur bei der eigenen Partie geladen. */
  readonly dismissedPlies = this.trainer.dismissedPlies;
  /** Was der Trainer abfragt: die Fehler ohne die ausgeblendeten. */
  readonly trainable = this.trainer.trainable;
  /** Die Seite, die der Trainer abfragt — der Knopf zählt NUR sie. */
  readonly mistakeSide = this.trainer.mistakeSide;
  /** Seite aus dem geteilten Trainings-Link (`?train=white|black`, 0.740.0). */
  readonly trainSide = this.trainer.trainSide;
  readonly mistakeTotal = this.trainer.mistakeTotal;
  /** Laufendes Training „Eigene Fehler nachspielen" — `null` = die Seite zeigt die Partie. */
  readonly training = this.trainer.training;

  /** Name des Spielers einer Seite für „Fehler von … nachspielen“ (d3-rk-eigene-fehler-fremde-partie); ohne Namen
   *  „Weiß“/„Schwarz“. */
  sidePlayer(side: 'white' | 'black'): string {
    const name = (side === 'white' ? this.game?.white : this.game?.black)?.trim();
    return name && name !== '?' ? name : this.translate.instant('games.review.' + side);
  }

  analysisRunning(): boolean {
    const s = this.reviewStatus();
    return s === 'pending' || s === 'running';
  }

  /** Id der eigenen Partie (`/games/:id`) — ohne sie wird nichts gemeldet (geteilte Ansicht). */
  gameId: number | null = null;

  toClub(): void {
    if (this.gameId) void this.handoff.jumpToLeagueHub(`verein/neu?partie=${this.gameId}`);
  }
  /** Formular-Einlesung der eigenen Partie (0.529.0) — `null` = kein Foto. */
  scanId: number | null = null;
  /** LeagueHub-Adresse für „Korrigieren" einer Vereinspartie (0.660.0); `null` außerhalb der Partner-Domains. */
  readonly leagueHub = leagueHubUrl();
  /** Pfeil für den besten Zug, geliefert vom Rückblick (Schalter dort); im Training leer. */
  readonly bestArrows = signal<BoardArrow[]>([]);
  /** Klasse des aktuellen Zugs als Symbol am Zielfeld (vom Rückblick; im Training und mit Live-Engine leer). */
  readonly moveBadge = signal<BoardBadge | null>(null);

  /** Der freigegebene Roast hinter `?roast=` (0.742.0) — null ohne Parameter oder wenn nicht (mehr) freigegeben. */
  readonly sharedRoast = signal<GameRoast | null>(null);
  /** Teilen-Kennung aus der Adresse (nur `/g/:token`) — die Analyse-Leiste holt darüber die Lc0-Analyse des Besitzers. */
  publicToken: string | null = null;
  private roastId: number | null = null;

  private loadSharedRoast(token: string | null | undefined): void {
    if (!token || this.roastId == null) return;
    this.games.sharedRoast(token, this.roastId).subscribe({ next: r => this.sharedRoast.set(r), error: () => undefined });
  }

  /** Live-Engine, ihre Engine-Wahl und „Analysieren" im Training (Zerlegung Z26, siehe `GameEngineController`). */
  readonly engine = new GameEngineController({
    createSession: () => this.createLiveSession(),
    currentFen: () => this.service.currentFen,
    currentMoveIndex: () => this.service.currentMoveIndex,
    training: () => this.training(),
    loggedIn: () => this.auth.isLoggedIn,
  }, this.externalEngines);
  /** Live-Engine + eigene Züge (0.525.0) — im Fehler-Training aus, dort verriete sie die Lösung. */
  readonly live = this.engine.live;
  /** „Analysieren" im Fehler-Training (0.526.2, siehe `GameEngineController.trainingAnalysis`). */
  readonly trainingAnalysis = this.engine.trainingAnalysis;
  /** Externe Engines fuer die Live-Wahl im ⋮ (0.681.0). */
  readonly liveEngines = this.engine.liveEngines;
  private readonly stopLiveOnDestroy = inject(DestroyRef).onDestroy(() => this.engine.destroy());

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

  toggleLive(): void { this.engine.toggleLive(); }

  /** Eigene Engine-Instanz je Seite — als Methode, damit Tests keinen echten Stockfish starten müssen. */
  protected createLiveSession(): LiveEngineSession {
    return new LiveEngineSession(undefined, this.engine.storedDepth());
  }

  /** Die Stellung auf dem Brett — in der Nebenvariante der Live-Engine deren Ende, sonst der Partiezug. */
  positionFen(): string { return this.engine.positionFen(); }

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

  stopLive(): void { this.engine.stopLive(); }

  get loggedIn(): boolean { return this.auth.isLoggedIn; }

  /** Hinterlegtes der Partie-Analyse zur Stellung auf dem Brett (aus dem Rückblick). */
  readonly reviewStored = signal<DeepStored>({ sf: null, lc0: null });
  /** „Tiefe Analyse" im ⋮-Menü (0.686.0): nur angemeldet; in einer eigenen Nebenvariante gibt es nichts Hinterlegtes. */
  deepStored(): DeepStored | null {
    if (!this.loggedIn) return null;
    return this.live()?.variation()?.length ? { sf: null, lc0: null } : this.reviewStored();
  }

  loadLiveEngines(): void { this.engine.loadLiveEngines(); }

  /** Die gerade gewaehlte Live-Engine: die der laufenden Sitzung, sonst die gemerkte Wahl; `null` = Browser. */
  currentEngineId(): string | null { return this.engine.currentEngineId(); }

  engineOffline(e: ExternalEngineInfo): boolean { return isEngineOffline(e); }
  engineTag(e: ExternalEngineInfo): string | null { return engineTagKey(e); }

  /** Live-Engine waehlen (⋮ → Live-Engine), gemerkt wie am Analysebrett (siehe `GameEngineController`). */
  chooseLiveEngine(e: ExternalEngineInfo | null): void { this.engine.chooseLiveEngine(e); }

  toggleTrainingAnalysis(): void { this.engine.toggleTrainingAnalysis(); }

  stopTrainingAnalysis(): void { this.engine.stopTrainingAnalysis(); }

  /**
   * Mit der Partie abgleichen: geblättert → Nebenvariante weg, neue Stellung → rechnen (billig ohne Änderung).
   * Bleibt `ngDoCheck`: die Stellung steht im `PgnViewerService` als Feld, nicht als Signal — ein `effect`/`computed`
   * sähe das Blättern nicht.
   */
  ngDoCheck(): void { this.engine.sync(); }

  trainMistakes(): void { this.trainer.trainMistakes(); }

  /** Alle ausgeblendeten Fehler dieser Partie wieder einblenden. */
  restoreDismissed(): void { this.trainer.restoreDismissed(); }

  onTrainingMove(e: UserBoardMove): void { this.trainer.onMove(e); }

  endTraining(): void { this.trainer.end(); }

  analyzeTooltip(): string {
    if (this.analysisRunning()) return 'games.review.running';
    // d4-rk-engine-hinweis-jargon: nicht der Punktepartie-Text („eingeworfene Partien“), sondern der Grund der Partieseite.
    return this.uploadStatus()?.engineAvailable === false ? 'games.analyzeNoEngine' : 'games.analyzeHint';
  }

  /** Quelle im Partiekopf — dieselbe Abbildung wie die Partieliste (b1-rk-partie-quelle-pgn: vorher stand „pgn“ roh da). */
  sourceLabel(): string {
    return gameSourceLabel(this.game?.source, this.translate);
  }

  constructor(
    public service: PgnViewerService,
    private route: ActivatedRoute,
    private games: GamesService,
    public preferences: PreferencesService,
  ) {}

  ngOnInit(): void {
    const train = this.route.snapshot.queryParamMap?.get('train');
    if (train === 'white' || train === 'black') this.trainer.startFromLink(train);
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
        error: e => this.loadFailed(e),
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
        error: e => this.loadFailed(e),
      });
      return;
    }
    const token = this.route.snapshot.paramMap.get('token') || '';
    this.publicToken = token || null;
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
      error: e => this.loadFailed(e),
    });
  }

  private show(g: SharedGame): void {
    this.game = g;
    this.trainer.ownerSide.set(g.ownerSide ?? null);
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

  /** „Roast my game" (0.535.0) — nur die eigene Partie. */
  roast(): void {
    if (this.gameId) this.actions.roast(this.gameId, this.shareToken);
  }

  /** Das Formular-Foto der eigenen, eingelesenen Partie anzeigen (Dialog) oder herunterladen. */
  photo(download: boolean): void {
    if (this.gameId) this.actions.photo(this.gameId, download);
  }

  /** Im Analysebrett öffnen — mit dem GANZEN PGN (Kopfdaten, Kommentare), nicht nur den Zügen (0.592.0); „Zurück" führt
   *  hierher. Übergabe per Router-State wie aus der Partienliste. */
  openInAnalysis(): void { this.actions.openInAnalysis(this.game?.pgn, this.router.url); }

  /** „Mit Lc0 analysieren" — angemeldet, Vereinsmitglied, und die Partie hat noch keine Lc0-Analyse. */
  canLc0(): boolean {
    return this.loggedIn && this.auth.has('league.view') && !!this.game?.pgn && !this.review()?.hasLc0();
  }

  analyzeLc0(): void { this.actions.startLc0(this.game?.pgn, () => this.review()?.refreshAlternatives()); }

  /**
   * Die Lc0-Analyse der Partie, solange sie wartet oder rechnet (0.833.0, Wunsch 2026-10-25: „mir fehlt der Menüeintrag"):
   * statt „Mit Lc0 analysieren" still wegzulassen, zeigt das ⋮ ihren Stand — und ob die Engine überhaupt abfragt.
   */
  lc0Waiting(): GameAnalysisAlternative | null {
    const review = this.review();
    const alt = review?.alternative();
    if (!this.loggedIn || !alt || !review!.hasLc0()) return null;
    const status = review!.altEvals()?.status ?? alt.status;
    return status === 'pending' || status === 'running' ? alt : null;
  }

  /** Fortschritt der wartenden Lc0-Analyse: aus den mitgeladenen Bewertungen, sonst aus der Liste. */
  lc0Progress(alt: GameAnalysisAlternative): { done: number; total: number } {
    const evals = this.review()?.altEvals();
    return evals ? { done: evals.analyzed, total: evals.total } : { done: alt.analyzedPlies, total: alt.plyCount };
  }

  cancelLc0(alt: GameAnalysisAlternative): void { this.actions.cancelLc0(alt.id, () => this.review()?.refreshAlternatives()); }

  restartLc0(alt: GameAnalysisAlternative): void { this.actions.restartLc0(alt.id, () => this.review()?.refreshAlternatives()); }

  /** Das PGN der Partie in die Zwischenablage (so, wie es gespeichert ist — samt Kopfdaten und Kommentaren). */
  copyPgn(): void { this.actions.copyPgn(this.game?.pgn); }

  /** Eigene Partie löschen (d4-rk-partie-menue-loeschen-fehlt-partieseite): Rückfrage wie in der Partienliste, danach
   *  zurück zur Liste — wer die Partie offen hat, muss dafür nicht erst zurück. */
  remove(): void {
    const id = this.gameId;
    if (!this.own || !id) return;
    this.actions.deleteGame(id).subscribe(() => void this.router.navigate(['/games']));
  }

  /** Das PGN als Datei „Weiß_Schwarz_Datum.pgn". */
  downloadPgn(): void { this.actions.downloadPgn(this.game); }

  /** Link „Fehler nachspielen" für eine Seite (0.740.0, Wunsch 2026-10-10): der Teilen-Link mit `?train=`. Wer ihn öffnet —
   *  angemeldet oder nicht —, landet direkt im Fehler-Training dieser Farbe. */
  trainingShareUrl(side: 'white' | 'black'): string | null { return this.actions.trainingShareUrl(this.shareToken, side); }

  shareTraining(side: 'white' | 'black'): void { this.actions.copyTrainingLink(this.trainingShareUrl(side)); }

  readonly trainSides: readonly ('white' | 'black')[] = ['white', 'black'];

  /** Anzahl Aufgaben je Seite — für die Menüpunkte „Fehler-Training teilen". */
  mistakeCount(side: 'white' | 'black'): number { return this.trainer.mistakeCount(side); }

  /** Teilen-Link der eigenen Partie in die Zwischenablage — wie in der Liste. */
  share(): void { this.actions.copyShareLink(this.shareToken); }

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

  private inVariation(): LiveEngineSession | null { return this.engine.inVariation(); }

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
