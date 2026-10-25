import { inject, Injectable, InjectionToken, signal } from '@angular/core';
import { PuzzleDto, PuzzleService } from './puzzle.service';
import { EndlessStorageService } from './endless-storage.service';
import { EndlessChainService } from './endless-chain.service';
import { ENDLESS_CHAIN_BLOCK } from './endless-prefetch.util';
import {
  EndlessGameOverSnapshot, EndlessPuzzleAttempt, EndlessRunSnapshot, EndlessSessionRecorder, restoreSessionPuzzles,
  SavedPuzzleAttempt,
} from './endless-session-recorder';
import { AuthService } from '../../core/auth.service';
import { EndlessProfile } from './endless-profile';
import { EndlessRunClock, EndlessRunTimer, realRunClock } from './endless-run-timer';

/** Austauschbare Uhr des {@link EndlessRunStore} (Tests: `ManualClock` + `now`). */
export const ENDLESS_RUN_CLOCK = new InjectionToken<EndlessRunClock>('ENDLESS_RUN_CLOCK', {
  providedIn: 'root', factory: () => realRunClock,
});

/** Bildschirm-Zustände, die der Lauf selbst setzt (die übrigen setzt der Löser). */
export type EndlessRunScreen = 'LOADING' | 'CONFIG' | 'WON' | 'GAME_OVER';

/** Was der Lauf von der Seite braucht: Bildschirm umschalten, aktuelles Ketten-Puzzle aufsetzen, Hinweis zeigen,
 *  Puzzle-Sekunden anzeigen. Die Komponente verdrahtet das mit Löser und Snackbar. */
export interface EndlessRunHost {
  setState(state: EndlessRunScreen): void;
  loadCurrent(): void;
  notice(key: string, duration: number): void;
  tick(puzzleSeconds: number): void;
}

const NO_HOST: EndlessRunHost = { setState: () => undefined, loadCurrent: () => undefined, notice: () => undefined, tick: () => undefined };

/** Offener Lauf, wie er lokal bzw. am Server liegt (Spielstände älterer Builds: Felder können fehlen). */
export type StoredEndlessGame = Partial<EndlessRunSnapshot> & { mistakes?: number[]; puzzleAttempts?: SavedPuzzleAttempt[] };

/**
 * Lauf-Zustand des Endlos-Modus (Zerlegung A4, PD-007): Kennzahlen (Leben, Serie, Kettenposition, Ratings, Zeit),
 * Kette + Seed + Offline-Vorrat, Start/Fortsetzen/Weiterrücken/Ergebnis, Sync des Spielstands und die einmalige
 * Aufzeichnung des beendeten Laufs. Je Seite in `providers` (Lebensdauer wie die Komponente); die Komponente hält
 * nur Löser, Brett und Dialoge und meldet sich über {@link attach} als {@link EndlessRunHost} an.
 */
@Injectable()
export class EndlessRunStore {
  readonly lives = signal(3);
  readonly level = signal(0);
  readonly solved = signal(0);
  /** Position des aktuell gespielten Puzzles in der Kette; Lösen UND Fehler rücken eine Stelle weiter. */
  readonly chainIndex = signal(0);
  /** Rating des aktuellen Puzzles (Anzeige, Fehler-Logging). */
  readonly currentMinRating = signal(0);
  readonly maxRatingReached = signal(0);
  readonly isNewHighscore = signal(false);
  readonly sessionSeconds = signal(0);
  /** Offener Lauf (lokal bzw. vom Server) — Grundlage für „Fortsetzen". */
  readonly activeGameState = signal<StoredEndlessGame | null>(null);
  readonly lastSessionId = signal<number | null>(null);
  readonly lastSessionArchived = signal(false);
  readonly archiving = signal(false);
  /** Die Game-Over-Ansicht zeigt einen abgeschlossenen Lauf aus der History (?session=ID), keinen gerade beendeten. */
  readonly historyView = signal(false);

  private readonly storage = inject(EndlessStorageService);
  private readonly chainService = inject(EndlessChainService);
  private readonly puzzleService = inject(PuzzleService);
  private readonly auth = inject(AuthService);

  /** Konfiguration, Rekord, History, Themen (Spieler-Seite). */
  readonly profile = new EndlessProfile(this.storage, this.puzzleService, this.auth);
  /** Session-/Puzzle-Zeit (Uhr austauschbar über {@link ENDLESS_RUN_CLOCK}). */
  readonly timer = new EndlessRunTimer(seconds => this.onTick(seconds), inject(ENDLESS_RUN_CLOCK));
  /** Lauf-Protokoll (Fehler-Ratings, gespielte Puzzles, einmalige Aufzeichnung) — Zerlegung Z19. */
  readonly recorder = new EndlessSessionRecorder(this.storage);

  /**
   * Gauntlet-Kette: die beim Run-Start (serverseitig per getRandomBatch) generierte, vollständig
   * vordefinierte Puzzle-Folge entlang der ~logarithmischen Kurve. Die Kette liegt lokal (Offline/Refresh →
   * exakt dasselbe Puzzle); `chainIndex`/`seed` werden zusätzlich im (synchronisierten) Spielstand abgelegt,
   * der Seed + die Ketten-IDs zusätzlich beim Session-Record am Server (für ein späteres Replay).
   */
  chain: PuzzleDto[] = [];
  /** Eindeutiger Run-Seed (crypto.randomUUID) — identifiziert die Kette lokal + am Server. */
  seed = '';
  /** Lokal gecachte Kette (Offline-Start / Resume); identisch zu {@link chain} während eines Runs. */
  offlinePool: PuzzleDto[] = [];
  /** Wird bei jedem Run-Start (startGame/resumeGame) erhöht. Ein im Hintergrund laufender
   *  prefetchRun darf den Pool/Seed eines inzwischen gestarteten Runs NICHT überschreiben →
   *  späte Prefetch-Antwort wird verworfen, wenn die Generation nicht mehr passt. */
  runGeneration = 0;
  /** Wie oft das Nachladen der Kette in Folge gescheitert ist — Riegel gegen die Endlos-Schleife
   *  aus handleChainEnd → generateChainBlock → Fehler → handleChainEnd. */
  chainFailures = 0;
  /** Nach so vielen Fehlversuchen in Folge wird nicht mehr nachgeneriert. */
  static readonly MaxChainFailures = 3;
  /** Am aktuellen Puzzle ging schon ein Leben verloren (ein Puzzle kostet höchstens eines). */
  puzzleLifeLost = false;
  private host: EndlessRunHost = NO_HOST;

  attach(host: EndlessRunHost): void { this.host = host; }

  /** Kurzform: aktuelle Konfiguration (zwei-Wege an Start-Bildschirm und Tiefen-Regler). */
  get config() { return this.profile.config; }

  // ===== Laden + Abgleich =====

  /** Lokaler Stand sofort (ohne Latenz): Konfiguration, Rekord, History, Run-Cache, offener Lauf. */
  loadLocal(): void {
    this.profile.loadLocal();
    this.offlinePool = this.storage.loadOfflinePool();   // evtl. vorhandener Run-Cache (Offline/Resume)
    this.profile.computeFasttrackSteps();

    const localGame = this.storage.loadActiveGameLocal();
    if (localGame) this.activeGameState.set(localGame as StoredEndlessGame);
    // Defensiv: ein resumebarer Run mit 0 Lives existiert nicht — entweder
    // Zombie-State aus aelterer Logik oder Race vor endGame(). Aufraeumen.
    if (this.activeGameState() && (this.activeGameState()!.lives ?? 0) <= 0) {
      this.activeGameState.set(null);
      this.storage.saveActiveGameLocal(null);
    }
  }

  /** Server-Stand (async) einmischen. `onConfigScreen` = die Seite zeigt noch den Start-Bildschirm. */
  syncFromServer(onConfigScreen: () => boolean): void {
    this.storage.loadFromServer().subscribe(serverData => {
      if (!serverData) return;
      if (serverData.progress || serverData.sessions.length > 0) {
        this.profile.mergeServer(serverData, onConfigScreen());

        // Server active game state takes priority
        if (serverData.progress?.activeGameState) {
          try { this.activeGameState.set(JSON.parse(serverData.progress.activeGameState)); } catch {}
        }
        // Auch Server-State auf 0-Lives-Zombie pruefen (Legacy aus aelteren Builds).
        if (this.activeGameState() && (this.activeGameState()!.lives ?? 0) <= 0) {
          this.activeGameState.set(null);
          this.storage.saveActiveGameLocal(null);
          this.storage.saveProgressImmediate(this.config, this.profile.highscore(), null);
        }
      } else {
        // Server empty: migrate localStorage data up (one-time)
        this.storage.migrateLocalToServer(this.config, this.profile.highscore(), this.profile.sessionHistory());
      }
    });
  }

  /** Rating-Spanne der Puzzle-DB laden; danach (online, ohne Cache) eine Kette vorab holen und `then` rufen. */
  loadRatingRange(then: () => void): void {
    this.puzzleService.getRatingRange().subscribe({
      next: r => {
        this.profile.puzzleRange = r;
        this.profile.clampConfig();
        // Schon beim Öffnen der Config einen Run vorab laden, damit Endless später
        // auch offline gestartet werden kann (nur online + wenn noch kein Cache da ist).
        if (navigator.onLine && this.offlinePool.length === 0) this.prefetchRun();
        then();
      },
      error: () => {}
    });
  }

  /** Konfiguration sichern. `runInProgress`: den zuletzt gesicherten Spielstand mitschicken — null heißt für den
   *  Server „kein offener Lauf" und löschte das geräteübergreifende Fortsetzen (über den Debounce sogar einen
   *  gerade geplanten Spielstand). Der lokale Stand ist der, den {@link syncActiveGame} zuletzt geschrieben hat
   *  (samt richtiger Fortsetz-Position) bzw. den {@link resume} beim Fortsetzen festgeschrieben hat. */
  saveConfig(runInProgress: boolean): void {
    this.profile.saveConfig(runInProgress ? this.storage.loadActiveGameLocal() : null);
  }

  // ===== Lauf starten / fortsetzen =====

  /** Frischer Lauf (nach Klemmen + Sichern der Konfiguration durch die Seite). */
  start(): void {
    this.lives.set(3);
    this.level.set(0);
    this.solved.set(0);
    this.chainIndex.set(0);
    this.seed = this.newSeed();
    this.currentMinRating.set(this.config.startElo);
    this.maxRatingReached.set(this.config.startElo);
    this.isNewHighscore.set(false);
    this.sessionSeconds.set(0);
    this.timer.resumePuzzleSeconds = 0;   // frischer Run → keine fortzusetzende Puzzle-Zeit
    this.recorder.startFresh();     // leeres Protokoll, noch nicht aufgezeichnet
    this.activeGameState.set(null);
    this.lastSessionId.set(null);
    this.lastSessionArchived.set(false);
    this.profile.computeFasttrackSteps();   // avgFirst/avgSecond = T1/T2 für die Ketten-Kurve
    this.startSessionTimer();

    if (navigator.onLine) {
      // Volle Kette serverseitig generieren (ein Batch) und beim Client ablegen.
      this.host.setState('LOADING');
      this.chain = [];
      this.generateChainBlock(0, () => this.host.loadCurrent());
    } else if (!this.startFromOfflinePool()) {
      this.abortToConfig('endless.offlineNoCache', 5000);
    }
  }

  /** Offenen Lauf ({@link activeGameState}) fortsetzen. */
  resume(): void {
    const g = this.activeGameState()!;
    // Fortgesetzten Stand lokal festschreiben: der Server-Stand hat Vorrang, ohne lokal abgelegt zu sein.
    // saveConfig (Tiefen-Regler) schickt den lokalen Stand mit — ohne das hier wäre das bis zum ersten
    // Ergebnis ein älterer Stand dieses Geräts (Gerätewechsel-Ping-Pong).
    this.storage.saveActiveGameLocal(g);
    this.lives.set(g.lives ?? 3);
    this.solved.set(g.solved ?? 0);
    this.chainIndex.set(g.chainIndex ?? 0);
    this.level.set(g.level ?? this.chainIndex());
    this.seed = g.seed ?? '';
    this.currentMinRating.set(g.currentMinRating ?? this.config.startElo);
    this.maxRatingReached.set(g.maxRatingReached ?? this.currentMinRating());
    this.sessionSeconds.set(g.sessionSeconds ?? 0);
    // Der sekündlich persistierte Live-Zeitstand ist i. d. R. jünger als der letzte Sync-Punkt
    // des Spielstands: Session-Zeit auf den größeren Wert anheben und — wenn der Refresh mitten
    // im SELBEN Puzzle passierte — dessen Zwischenzeit fortsetzen. Nur für denselben Lauf (Seed).
    const live = this.storage.loadLiveElapsed();
    if (live && live.seed && live.seed === this.seed) {
      this.sessionSeconds.set(Math.max(this.sessionSeconds(), Math.floor(live.session) || 0));
      if (live.chainIndex === this.chainIndex() && live.puzzle > 0)
        this.timer.resumePuzzleSeconds = Math.floor(live.puzzle);
    }
    this.recorder.resumeFrom(g);   // fortgesetzter Run läuft weiter → noch nicht aufgezeichnet
    this.isNewHighscore.set(false);
    this.lastSessionId.set(null);
    this.lastSessionArchived.set(false);
    this.profile.computeFasttrackSteps();
    // Timer dort fortsetzen, wo er aufgehört hat (gewertet wird ab jetzt nur aktive Tab-Zeit).
    this.startSessionTimer(this.sessionSeconds());

    // Kette wiederherstellen: lokal nur, wenn der Token zu DIESEM Run passt (Refresh auf demselben
    // Gerät → exakt dasselbe Puzzle). Sonst (anderes Gerät / Cache überschrieben) die Kette von vorne
    // bis über die aktuelle Position neu generieren, damit chainIndex weiterhin korrekt zeigt.
    // Steht der Zeiger direkt HINTER der lokalen Kette (Ergebnis am letzten Puzzle eines Blocks),
    // passt sie trotzdem: loadCurrent verlängert sie (offline: Sieg) wie ohne Neuladen.
    const idx = this.chainIndex();
    const localOk = !!this.seed && this.storage.loadChainSeed() === this.seed
      && (this.offlinePool.length > idx || (idx > 0 && this.offlinePool.length === idx));
    if (localOk) {
      this.chain = this.offlinePool;
      this.host.loadCurrent();
    } else if (navigator.onLine) {
      this.host.setState('LOADING');
      this.chain = [];
      this.seed = this.seed || this.newSeed();
      this.generateChainBlock(0, () => this.host.loadCurrent(), idx + ENDLESS_CHAIN_BLOCK);
    } else if (this.offlinePool.length > idx) {
      // Offline ohne passenden Token, aber genug Puzzles vorhanden → bestmöglich fortsetzen.
      this.chain = this.offlinePool;
      this.host.loadCurrent();
    } else {
      this.abortToConfig('endless.offlineNoCache', 5000);
    }
  }

  /** Offenen Lauf als Session ablegen (und archivieren), danach `then` (= neuen Lauf starten). */
  archiveOpenRun(then: () => void): void {
    const g = this.activeGameState()!;
    // Seed aus dem Spielstand; die Kette liegt hier nur lokal (offlinePool), falls Token passt.
    const session = EndlessSessionRecorder.session(this.config, g.solved ?? 0, g.maxRatingReached ?? 0,
      g.sessionSeconds ?? 0, g.mistakes ?? [], g.seed ?? this.seed,
      this.offlinePool.length && this.storage.loadChainSeed() === (g.seed ?? this.seed)
        ? this.offlinePool.map(p => p.id).join(',') : undefined);
    // Die bisher gespielten Puzzles gehören mit in den gespeicherten Lauf (Detail-Ansicht der History).
    this.storage.recordSessionToServer(session, restoreSessionPuzzles(g.puzzleAttempts)).subscribe(id => {
      if (id && this.auth.isLoggedIn) {
        this.storage.archiveSession(id).subscribe();
      }
      this.activeGameState.set(null);
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.profile.highscore(), null);
      this.profile.sessionHistory.set(this.storage.recordSession(this.profile.sessionHistory(), session));
      then();
    });
  }

  archiveLastSession(): void {
    const id = this.lastSessionId();
    if (!id || this.archiving()) return;
    this.archiving.set(true);
    this.storage.archiveSession(id).subscribe(() => {
      this.lastSessionArchived.set(true);
      this.archiving.set(false);
    });
  }

  /** Beendeten Run vollständig verwerfen (in-memory + Speicher), damit der Config-Screen ihn nicht erneut zum
   *  Fortsetzen anbietet — sonst „Resume → Aufgeben → Nochmal → wieder fortsetzbar"-Schleife. */
  discardFinishedRun(): void {
    this.activeGameState.set(null);
    this.storage.saveActiveGameLocal(null);
    this.host.setState('CONFIG');
    this.lastSessionId.set(null);
    this.lastSessionArchived.set(false);
    this.profile.computeFasttrackSteps();
  }

  /** Abgeschlossenen Lauf aus der History im Game-Over-Layout zeigen; `onMissing`, wenn es ihn nicht gibt. */
  loadHistorySession(id: number, onMissing: () => void): void {
    this.historyView.set(true);
    this.host.setState('LOADING');
    this.storage.getSessionDetail(id).subscribe(detail => {
      if (!detail) { onMissing(); return; }
      this.maxRatingReached.set(detail.maxRating);
      this.solved.set(detail.totalSolved);
      this.sessionSeconds.set(detail.durationSeconds);
      this.recorder.showDetail(detail);   // reine History-Ansicht → nie (neu) aufzeichnen
      // „Level" = Anzahl gespielter Puzzles (Kettenposition); fällt auf TotalSolved zurück,
      // falls für einen Altlauf keine Einzel-Puzzles persistiert sind.
      this.level.set(this.recorder.puzzles.length || detail.totalSolved);
      this.isNewHighscore.set(false);
      this.lastSessionId.set(detail.id);
      this.lastSessionArchived.set(detail.isArchived);
      this.host.setState('GAME_OVER');
    });
  }

  /** Rückkehr aus der Analyse nach dem letzten Herz (?gameover=1): Zusammenfassung wiederherstellen. Der Lauf
   *  wurde vor dem Absprung schon aufgezeichnet → nicht erneut posten (Doppel-Eintrag durchs Sicherheitsnetz). */
  restoreGameOver(s: EndlessGameOverSnapshot | null, pendingSessionId: number | null | undefined): void {
    if (s) {
      this.maxRatingReached.set(s.maxRatingReached ?? 0);
      this.solved.set(s.solved ?? 0);
      this.level.set(s.level ?? 0);
      this.lives.set(s.lives ?? 0);
      this.sessionSeconds.set(s.sessionSeconds ?? 0);
      this.isNewHighscore.set(s.isNewHighscore ?? false);
      this.recorder.mistakes = s.currentSessionMistakes ?? [];
      this.recorder.puzzles = s.currentSessionPuzzles ?? [];
    }
    // lastSessionId könnte inzwischen asynchron eingetroffen sein (recordSession-Subscribe)
    if (pendingSessionId !== undefined) this.lastSessionId.set(pendingSessionId);
    this.recorder.recorded = true;
  }

  /** Zusammenfassung des beendeten Laufs für die Rückkehr aus der Analyse (?gameover=1). */
  gameOverSnapshot(): EndlessGameOverSnapshot {
    return {
      maxRatingReached: this.maxRatingReached(),
      solved: this.solved(),
      level: this.level(),
      lives: this.lives(),
      sessionSeconds: this.sessionSeconds(),
      isNewHighscore: this.isNewHighscore(),
      currentSessionMistakes: [...this.recorder.mistakes],
      currentSessionPuzzles: [...this.recorder.puzzles],
    };
  }

  // ===== Kette =====

  /**
   * Lauf aus der vorab geladenen Kette starten; `false`, wenn keine vorliegt.
   *
   * <p>Zwei Wege führen hierher: das Gerät ist offline, ODER es gilt als online, aber der Server
   * antwortet nicht (Funkloch, VPN, Server weg). Den zweiten gab es früher nicht — `navigator.onLine`
   * blieb `true`, die Kette scheiterte, und der Lauf brach mit „keine neuen Puzzles" ab, obwohl die
   * Kette fertig im Speicher lag (2026-09-15 mit abgeschaltetem Server nachgestellt).</p>
   */
  private startFromOfflinePool(): boolean {
    if (this.offlinePool.length === 0) return false;
    this.chain = this.offlinePool;
    this.storage.saveOfflinePool(this.chain);
    this.storage.saveChainSeed(this.seed);
    this.syncActiveGame();
    this.host.loadCurrent();
    return true;
  }

  /**
   * Generiert (online) einen Ketten-Block für die absoluten Indizes [startIndex, startIndex+count)
   * via getRandomBatch entlang der Kurven-Fenster und hängt ihn an die Kette an (bzw. ersetzt sie
   * bei startIndex 0). `then` wird nach dem Eintreffen aufgerufen. Bleibt leer/leerer Block:
   * Run-Start → zurück zur Config; Verlängerung → „You win".
   */
  private generateChainBlock(startIndex: number, then?: () => void, count = ENDLESS_CHAIN_BLOCK): void {
    this.profile.ensureWorstThemes(() => {
    const { themes, themesAny } = this.profile.batchThemes();
    this.chainService.fetchBlock(this.profile.chainRequest(startIndex, count), themes, themesAny).subscribe({
      next: pool => {
        const block = pool || [];
        if (block.length === 0) {
          if (startIndex === 0) this.abortToConfig('endless.offlineNoCache', 5000);
          else this.finish('WON');   // online, aber keine weiteren Puzzles mehr → Kette durchgespielt
          return;
        }
        this.chainFailures = 0;
        this.chain = startIndex === 0 ? block : this.chain.concat(block);
        this.offlinePool = this.chain;
        this.storage.saveOfflinePool(this.chain);
        this.storage.saveChainSeed(this.seed);
        this.syncActiveGame();
        if (then) then();
      },
      error: () => {
        // FALLE: einfach `then()` (= loadCurrent) rufen führte bei einem SERVER-Fehler in eine
        // unbegrenzte Schleife — loadCurrent findet keine Kette, landet in handleChainEnd, das
        // generiert erneut, der Request scheitert wieder … Das Frontend feuerte dauerhaft Requests
        // (und verstärkte damit die Störung), die Karte blieb für immer auf „lädt" und nannte keinen
        // Grund. Der ursprüngliche Kommentar galt nur für den OFFLINE-Fall; bei 5xx, Proxy-Fehler
        // oder Rate-Limit ist `navigator.onLine` weiter true.
        this.chainFailures++;
        const canContinue = this.chainIndex() < this.chain.length;
        if (canContinue) { if (then) then(); return; }   // vorhandene Kette weiterspielen
        // Lauf-START ohne Antwort vom Server: die vorab geladene Kette nehmen, falls es sie gibt.
        if (startIndex === 0 && this.chain.length === 0 && this.startFromOfflinePool()) return;
        if (!navigator.onLine) { this.finish('WON'); return; } // wirklich offline: Lauf als Sieg beenden
        // Kein Nachschub und online → ehrlich abbrechen statt endlos zu drehen.
        this.host.notice('endless.loadFailed', 4000);
        this.host.setState('CONFIG');
      }
    });
    });
  }

  /** Abbruch vor dem ersten Puzzle: Uhr anhalten, Hinweis, zurück zum Start-Bildschirm. */
  private abortToConfig(key: string, duration: number): void {
    this.stopSessionTimer();
    this.host.notice(key, duration);
    this.host.setState('CONFIG');
  }

  /**
   * Lädt im Hintergrund eine Kette vorab und legt sie im Storage ab, damit Endless auch offline
   * gestartet werden kann. Invalidiert den Ketten-Token (dieser Cache gehört zu keinem Run).
   */
  prefetchRun(): void {
    const gen = this.runGeneration;
    this.profile.computeFasttrackSteps();
    const windows = this.chainService.chainWindows(this.profile.chainRequest(0));
    if (!windows.length) return;
    this.profile.ensureWorstThemes(() => {
      const { themes, themesAny } = this.profile.batchThemes();
      this.chainService.fetchBatch(windows, themes, themesAny).subscribe({
        next: pool => {
          // Inzwischen ein Run gestartet? Dann gehört dessen Pool/Seed ihm — Prefetch verwerfen.
          if (gen !== this.runGeneration) return;
          this.offlinePool = pool || [];
          this.storage.saveOfflinePool(this.offlinePool);
          this.storage.saveChainSeed('');   // Prefetch gehört zu keinem laufenden Run
        },
        error: () => { /* offline/Fehler: bestehenden Pool behalten */ }
      });
    });
  }

  /** Aktuelles Ketten-Puzzle; `null` am Kettenende. */
  currentPuzzle(): PuzzleDto | null {
    return this.chainIndex() < this.chain.length ? this.chain[this.chainIndex()] : null;
  }

  /** Kettenende erreicht: online → nächsten Block nachgenerieren; offline → „You win". */
  handleChainEnd(): void {
    if (navigator.onLine && this.chainFailures < EndlessRunStore.MaxChainFailures) {
      this.host.setState('LOADING');
      this.generateChainBlock(this.chain.length, () => this.host.loadCurrent());
    } else {
      this.finish('WON');
    }
  }

  /** Eine Stelle in der Kette weiterrücken (nach Lösen ODER Fehler) und das nächste Puzzle laden. */
  advance(): void {
    this.chainIndex.update(i => i + 1);
    this.level.set(this.chainIndex());
    this.syncActiveGame();
    this.host.loadCurrent();
  }

  /** Neues Ketten-Puzzle steht: Leben-Sperre lösen, Puzzle-Uhr auf „nicht begonnen“, Rating übernehmen. */
  enterPuzzle(puzzle: PuzzleDto): void {
    this.puzzleLifeLost = false;
    this.timer.clearPuzzle();   // Uhr läuft erst ab dem Setup-Zug (beginPuzzle)
    this.currentMinRating.set(puzzle.rating);   // Anzeige/Fehler-Logging am tatsächlichen Puzzle-Rating
    if (puzzle.rating > this.maxRatingReached()) this.maxRatingReached.set(puzzle.rating);
  }

  // ===== Ergebnisse =====

  /** Gelöst: Serie zählen, Puzzle protokollieren, Spielstand mit Fortsetzen beim NÄCHSTEN Puzzle sichern. */
  markSolved(puzzle: PuzzleDto | null): void {
    this.solved.update(n => n + 1);
    if (puzzle) this.pushPuzzle(puzzle, true);
  }

  /** Leben verloren am aktuellen Puzzle — höchstens einmal je Puzzle; `false`, wenn es schon eines kostete.
   *  Danach meldet die Seite den Versuch und ruft {@link saveAfterLoss}. */
  registerLoss(puzzle: PuzzleDto | null): boolean {
    if (this.puzzleLifeLost) return false;
    this.puzzleLifeLost = true;
    this.recorder.addMistake(this.currentMinRating());
    if (puzzle) this.pushPuzzle(puzzle, false);
    this.lives.update(l => l - 1);
    return true;
  }

  /** Nach einem Fehler: Spielstand mit Fortsetzen beim NÄCHSTEN Puzzle — bei 0 Leben stattdessen räumen (kein
   *  „Unfinished run | 0 lives"-Zombie am Server, falls der User die Seite vor „Weiter" verlässt). */
  saveAfterLoss(): void {
    if (this.lives() > 0) this.syncActiveGame(this.chainIndex() + 1);
    else this.clearActiveGame();
  }

  /** Lauf beenden (Game Over bzw. Kette durch = Sieg): Uhr, Rekord, Aufzeichnung, Spielstand räumen. */
  finish(screen: 'WON' | 'GAME_OVER'): void {
    this.stopSessionTimer();
    this.checkHighscore();
    this.ensureSessionRecorded();
    this.clearActiveGame();
    this.host.setState(screen);
  }

  /** Sicherheitsnetz: Ein beendeter Lauf (0 Leben), den der User verlässt, BEVOR er im Game-Over „Weiter"
   *  geklickt hat, würde sonst nirgends landen — bei 0 Leben ist der aktive Lauf bereits vom Server gelöscht
   *  und endGame() lief nie. Reine History-Ansichten (?session=ID) sind ausgenommen; idempotent. */
  rescueUnrecordedRun(): void {
    if (this.historyView()) return;
    if (this.lives() <= 0 && !this.recorder.recorded) this.ensureSessionRecorded();
  }

  /** Offenen Lauf lokal und am Server löschen. */
  clearActiveGame(): void {
    this.storage.saveActiveGameLocal(null);
    this.storage.saveProgressImmediate(this.config, this.profile.highscore(), null);
  }

  /** Sichert den Spielstand (lokal + Server); `resumeAt` siehe {@link EndlessSessionRecorder.activeGame}. */
  syncActiveGame(resumeAt = this.chainIndex()): void {
    this.recorder.sync(this.snapshot(), this.config, this.profile.highscore(), resumeAt);
  }

  /** Kennzahlen des laufenden Laufs für den Spielstand. */
  snapshot(): EndlessRunSnapshot {
    return {
      lives: this.lives(), solved: this.solved(), level: this.level(), chainIndex: this.chainIndex(), seed: this.seed,
      currentMinRating: this.currentMinRating(), maxRatingReached: this.maxRatingReached(), sessionSeconds: this.sessionSeconds(),
    };
  }

  private checkHighscore(): void {
    const result = this.storage.checkHighscore(this.maxRatingReached(), this.profile.highscore());
    this.profile.highscore.set(result.highscore);
    if (result.isNew) this.isNewHighscore.set(true);
  }

  /** Zeichnet den beendeten Lauf genau EINMAL auf (idempotent). Egal über welchen Pfad der Run
   *  endet — „Weiter" im Game-Over, Absprung in die Analyse oder Verlassen der Seite — die Session
   *  landet so garantiert in der History. */
  private ensureSessionRecorded(): void {
    const history = this.recorder.recordOnce(
      () => EndlessSessionRecorder.session(this.config, this.solved(), this.maxRatingReached(), this.sessionSeconds(),
        [...this.recorder.mistakes], this.seed, this.chain.map(p => p.id).join(',')),
      this.profile.sessionHistory(),
      id => this.lastSessionId.set(id),
    );
    if (history) this.profile.sessionHistory.set(history);
  }

  /** Hängt das Puzzle (mit Start-/Endzeit) an die Session-Liste an. */
  private pushPuzzle(puzzle: PuzzleDto, solved: boolean): void {
    this.recorder.pushPuzzle(puzzle, solved, this.timer.puzzleStartTime, this.timer.clock.now());
  }

  /** Eindeutiger Seed für einen neuen Lauf (crypto.randomUUID, Fallback Zeit+Zufall). */
  newSeed(): string {
    try {
      const c = (globalThis as any).crypto;
      if (c?.randomUUID) return c.randomUUID();
    } catch {}
    return `${this.timer.clock.now().toString(36)}-${Math.floor(Math.random() * 1e9).toString(36)}`;
  }

  // ===== Zeit =====

  /** `initialSeconds` > 0 setzt einen fortgesetzten Lauf (Resume) zeitlich korrekt fort. */
  private startSessionTimer(initialSeconds = 0): void {
    this.timer.startSession(initialSeconds);
    this.sessionSeconds.set(initialSeconds);
  }

  private onTick(sessionSeconds: number): void {
    this.sessionSeconds.set(sessionSeconds);
    this.host.tick(this.timer.puzzleSeconds());
    // Live-Zeitstand mitschreiben, damit ein Refresh mitten im Puzzle die Zeit nicht verliert
    // (der Spielstand selbst wird nur an Sync-Punkten — nach jedem Puzzle-Ergebnis — geschrieben).
    this.saveLiveElapsedNow();
  }

  stopSessionTimer(): void { this.timer.stop(); }

  /** Laufender Run: finalen Live-Zeitstand sichern — der 1-s-Tick kann bis zu 1 s hinterherhängen. */
  saveLiveIfRunning(): void {
    if (this.timer.running && this.lives() > 0) this.saveLiveElapsedNow();
  }

  /** Persistiert den Live-Zeitstand (Session-/Puzzle-Sekunden) des aktiven Laufs. */
  saveLiveElapsedNow(): void {
    if (!this.seed) return;
    this.storage.saveLiveElapsed({
      seed: this.seed,
      chainIndex: this.chainIndex(),
      session: this.sessionSeconds(),
      puzzle: this.timer.puzzleSeconds(),
    });
  }

  /** Lauf-Protokoll für die Ergebnis-Bildschirme. */
  get mistakes(): number[] { return this.recorder.mistakes; }
  get puzzles(): EndlessPuzzleAttempt[] { return this.recorder.puzzles; }
}
