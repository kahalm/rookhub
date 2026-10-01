import { throwError } from 'rxjs';
import { EndlessPuzzleComponent } from './endless-puzzle.component';
import { EndlessChainService } from './endless-chain.service';
import { ThemePreset } from './puzzle-theme-presets';
import { chainRatingAt, CHAIN_FLAT_STEP, CHAIN_T1_INDEX, CHAIN_T2_INDEX } from './endless-prefetch.util';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { provideTranslateService, TranslatePipe, TranslateService } from '@ngx-translate/core';
import { PuzzleService } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { EndlessStorageService } from './endless-storage.service';
import { AuthService } from '../../core/auth.service';
import { PreferencesService } from '../../core/preferences.service';
import { OfflineService } from '../../core/offline.service';
import { SnackbarService } from '../../core/snackbar.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { LongSolveService } from './long-solve.service';
import { FavoritesService } from '../../core/favorites.service';
import { SolveModeService } from '../../core/solve-mode.service';
import { WorksheetService } from '../worksheets/worksheet.service';

/**
 * Fokussierter Test der Analyse-Navigation im Endless-Modus (ohne TestBed/Template):
 * - „Analysieren" beim Aufgeben öffnet das AKTUELLE Puzzle im Analysemodus.
 * - „Letztes Puzzle analysieren" öffnet das zuletzt GELÖSTE Puzzle (bleibt nach dem
 *   Auto-Advance verfügbar, da die lastSolved*-Felder dort gemerkt werden).
 * Rücksprungziel ist jeweils der Endless-Modus.
 */
/** Synchroner Observable-Stub: ruft next sofort auf (für getRandomBatch/recordSessionToServer). */
function sub(value: any): any {
  return {
    subscribe: (h: any) => {
      const next = typeof h === 'function' ? h : h?.next;
      if (next) next(value);
      return { unsubscribe() {} };
    },
  };
}

const PUZZLE = { id: 7, fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', moves: 'e2e4 e7e5 g1f3', rating: 1500 };

/** Vordefinierte Kette, die getRandomBatch im Test zurückgibt. */
const CHAIN = [
  { id: 100, lichessId: 'a', fen: PUZZLE.fen, moves: PUZZLE.moves, rating: 700 },
  { id: 101, lichessId: 'b', fen: PUZZLE.fen, moves: PUZZLE.moves, rating: 900 },
  { id: 102, lichessId: 'c', fen: PUZZLE.fen, moves: PUZZLE.moves, rating: 1100 },
];

/** Minimaler SolveModeService-Ersatz: merkt sich die Wahl je Bereich und zählt die Abfragen.
 *  `antwort` = was der (echte) Dialog liefern würde; `dialogCalls` zählt, wie oft tatsächlich
 *  gefragt worden wäre (der echte Service fragt nur beim ersten Mal je Bereich). */
function makeSolveMode(antwort: 'training' | 'easy' = 'training', prefsViz = 3): any {
  const gemerkt: Record<string, string> = {};
  const stub: any = { gemerkt, dialogCalls: 0 };
  Object.assign(stub, {
    ensure: jasmine.createSpy('ensure').and.callFake((scope: string) => {
      if (!gemerkt[scope]) { stub.dialogCalls++; gemerkt[scope] = antwort; }
      return sub(gemerkt[scope]);
    }),
    get: (scope: string) => gemerkt[scope] ?? null,
    set: jasmine.createSpy('set').and.callFake((scope: string, mode: string) => { gemerkt[scope] = mode; }),
    levelFor: (mode: string) => (mode === 'easy' ? 0 : Math.max(1, prefsViz)),
    modeForLevel: (level: number) => (level > 0 ? 'training' : 'easy'),
  });
  return stub;
}

function makeComponent(params: Record<string, string> = {}, solveMode: any = makeSolveMode(), storageOverrides: any = {}): any {
  const prefs: any = {
    boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, visualization: 0,
    offPathWarnMoves: 3, enPassantForced: true,
    setVisualization(v: number) { this.visualization = v; },
  };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const auth: any = { isLoggedIn: false };
  const puzzleService: any = {
    getRatingRange: () => ({ subscribe: () => {} }),
    getRandomBatch: jasmine.createSpy('getRandomBatch').and.callFake(() => sub(CHAIN.map(p => ({ ...p })))),
    recordAttempt: () => sub(null),
    recordAnonymousAttempt: () => sub(null),
    ensureSessionId: () => 'sess',
    getAllThemes: () => sub(['advancedPawn', 'backRankMate', 'endgame', 'fork', 'pin']),
  };
  const storage: any = {
    loadConfig: (c: any) => c,
    loadHighscore: () => 0,
    loadSessionHistory: () => [],
    loadOfflinePool: () => [],
    loadActiveGameLocal: () => null,
    saveActiveGameLocal: () => {},
    saveOfflinePool: () => {},
    saveChainSeed: () => {},
    loadChainSeed: () => '',
    saveConfig: () => {},
    saveProgressToServer: () => {},
    saveProgressImmediate: () => {},
    checkHighscore: (max: number, hs: number) => ({ highscore: Math.max(max, hs), isNew: max > hs }),
    recordSession: (hist: any[]) => hist,
    recordSessionToServer: () => sub(null),
    loadFromServer: () => ({ subscribe: () => {} }),   // async Merge: im Test no-op
    loadLiveElapsed: () => null,
    saveLiveElapsed: () => {},
    ...storageOverrides,
  };
  const router: any = { navigate: jasmine.createSpy('navigate') };
  const route: any = { snapshot: { queryParamMap: { get: (k: string) => params[k] ?? null } } };
  const dialog: any = {};
  const translate: any = { instant: (k: string) => k };
  const offline: any = { puzzleCount: 0, endlessRuns: 0 };
  const snackBar: any = { info: jasmine.createSpy('info') };
  const offlineQueue: any = { enqueue: jasmine.createSpy('enqueue') };
  const longSolve: any = { resolve: (s: number) => sub(s) };
  const favorites: any = { contains: () => sub(false), add: () => sub(true), remove: () => sub(false), count: () => sub(0), list: () => sub([]) };
  // Echter Ketten-Service über DIESELBE puzzleService-Instanz → getRandomBatch-Spy + spätere
  // Neuzuweisungen (c.puzzleService.getRandomBatch = …) wirken durch den Service hindurch.
  const chainService = new EndlessChainService(puzzleService);
  const worksheets: any = { sendAndNotify: jasmine.createSpy('sendAndNotify') };
  return new EndlessPuzzleComponent(
    puzzleService, stockfish, storage, auth, prefs, router, route, dialog, translate, offline, snackBar, offlineQueue, longSolve, favorites, chainService, solveMode, worksheets
  );
}

describe('EndlessPuzzleComponent analyse', () => {
  it('analyzeCurrentPuzzle opens the current puzzle in the analysis board (give-up case)', () => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.orientation = 'black';

    c.analyzeCurrentPuzzle();

    expect(c.router.navigate).toHaveBeenCalledWith(['/analysis'], {
      queryParams: { fen: PUZZLE.fen, moves: 'e2e4,e7e5,g1f3', orientation: 'black', from: '/puzzles/endless?resume=1' },
    });
  });

  it('reviewLastPuzzle opens the last solved puzzle (survives auto-advance)', () => {
    const c = makeComponent();
    // Zustand wie nach einem gelösten Puzzle (puzzleSolved merkt sich id/fen/moves/orientation):
    c.lastSolvedPuzzleId = 7;
    c.lastSolvedFen = PUZZLE.fen;
    c.lastSolvedMoves = PUZZLE.moves;
    c.lastSolvedOrientation = 'white';

    c.reviewLastPuzzle();

    expect(c.router.navigate).toHaveBeenCalledWith(['/analysis'], {
      queryParams: { fen: PUZZLE.fen, moves: 'e2e4,e7e5,g1f3', orientation: 'white', from: '/puzzles/endless?resume=1' },
    });
  });

  it('reviewLastPuzzle does nothing when no puzzle has been solved yet', () => {
    const c = makeComponent();
    c.lastSolvedPuzzleId = null;
    c.lastSolvedFen = null;

    c.reviewLastPuzzle();

    expect(c.router.navigate).not.toHaveBeenCalled();
  });

  // Bug: nach Game-Over + „Nochmal spielen" konnte der beendete Run erneut fortgesetzt
  // werden, weil playAgain den in-memory activeGameState nicht löschte (nur der Storage
  // war genullt). Der Config-Screen zeigte dann wieder den Resume-Banner.
  it('playAgain clears the finished run so it cannot be resumed again', () => {
    const c = makeComponent();
    c.activeGameState = { lives: 1, solved: 5, level: 3, currentMinRating: 1800, maxRatingReached: 1800 };
    c.state = 'GAME_OVER';

    c.playAgain();

    expect(c.state).toBe('CONFIG');
    expect(c.activeGameState).toBeNull();
  });
});

describe('EndlessPuzzleComponent deep-link params', () => {
  it('themes/tags param preselects the theme filter (OR, space-joined)', () => {
    const c = makeComponent({ themes: 'fork,pin' });
    c.ngOnInit();
    expect(c.config.themes).toBe('fork pin');
    expect(c.config.worstTags).toBeFalsy();
  });

  it('anarchy=max forces en passant + crazy board (applied at setup)', () => {
    const c = makeComponent({ anarchy: 'max' });
    c.ngOnInit();
    expect(c.anarchyForcedByUrl).toBeTrue();
    expect(c.themeMode).toBe('crazy');
    c.onSetupStart();                 // e.p.-Zwang wird beim Puzzle-Setup gesetzt
    expect(c.enPassantForced).toBeTrue();
  });

  it('anarchy=max+1 additionally sets square crazy-piece mode', () => {
    const c = makeComponent({ anarchy: 'max+1' });
    c.ngOnInit();
    expect(c.anarchyForcedByUrl).toBeTrue();
    expect(c.crazyPieceMode).toBe('square');
    c.onSetupStart();
    expect(c.enPassantForced).toBeTrue();
  });

  it('elo param sets the start rating', () => {
    const c = makeComponent({ elo: '1500' });
    c.ngOnInit();
    expect(c.config.startElo).toBe(1500);
  });

  it('start=1 requests autostart, and maybeAutoStart begins the game from CONFIG', () => {
    const c = makeComponent({ start: '1' });
    c.ngOnInit();
    const spy = spyOn(c, 'startGame');
    c.maybeAutoStart();
    expect(spy).toHaveBeenCalled();
  });

  it('no start param → maybeAutoStart does nothing', () => {
    const c = makeComponent();
    c.ngOnInit();
    const spy = spyOn(c, 'startGame');
    c.maybeAutoStart();
    expect(spy).not.toHaveBeenCalled();
  });
});

/** Server-Abgleich, dessen Antwort der Test selbst auslöst — wie im Browser trifft sie erst NACH ngOnInit ein. */
function delayedServerSync(): { overrides: any; respond: () => void } {
  let cb: ((d: any) => void) | null = null;
  const data = { progress: { startElo: 900, themes: 'endgame', stockfishDepth: 12, highscore: 1800, updatedAt: '' }, sessions: [] };
  const overrides: any = {
    loadFromServer: () => ({ subscribe: (h: any) => { cb = h; return { unsubscribe() {} }; } }),
    // „Server wins" für die Server-Felder, lokale Felder bleiben (wie EndlessStorageService.mergeServerData).
    mergeServerData: jasmine.createSpy('mergeServerData').and.callFake((local: any, hs: number, hist: any[], d: any) => ({
      config: { ...local, startElo: d.progress.startElo, themes: d.progress.themes, stockfishDepth: d.progress.stockfishDepth },
      highscore: Math.max(hs, d.progress.highscore),
      history: hist,
    })),
  };
  return { overrides, respond: () => cb!(data) };
}

describe('EndlessPuzzleComponent Server-Abgleich nach dem Start', () => {
  it('Deep-Link ?themes/?elo überlebt die später eintreffende Server-Antwort', () => {
    const sync = delayedServerSync();
    const c = makeComponent({ themes: 'fork', elo: '1500' }, makeSolveMode(), sync.overrides);
    c.ngOnInit();
    sync.respond();
    expect(c.config.themes).toBe('fork');
    expect(c.config.startElo).toBe(1500);
    expect(c.config.worstTags).toBeFalse();
    expect(c.config.stockfishDepth).toBe(12);   // nicht per Link gesetzt → Server-Stand
    expect(c.highscore).toBe(1800);
  });

  it('Abgleich-Basis ist der gespeicherte Stand, nicht die Link-Werte („schwächste Themen" bleibt gespeichert)', () => {
    const sync = delayedServerSync();
    const stored = { worstTags: true, themes: 'hangingPiece pin' };
    const c = makeComponent({ themes: 'fork' }, makeSolveMode(),
      { ...sync.overrides, loadConfig: (d: any) => ({ ...d, ...stored }) });
    c.ngOnInit();
    sync.respond();
    expect(c.storage.mergeServerData.calls.mostRecent().args[0].worstTags).toBeTrue();
    expect(c.config.worstTags).toBeFalse();     // in diesem Aufruf gilt weiter der Link
    expect(c.config.themes).toBe('fork');
  });

  it('ein schon laufender Lauf behält seine Konfiguration, Highscore wird übernommen', () => {
    const sync = delayedServerSync();
    const c = makeComponent({}, makeSolveMode(), sync.overrides);
    c.ngOnInit();
    c.config = { startElo: 1500, themes: 'fork', stockfishDepth: 20 };
    c.state = 'PLAYING';                        // z. B. ?start=1 mit schnellerer Rating-Range-Antwort
    sync.respond();
    expect(c.config.themes).toBe('fork');
    expect(c.config.stockfishDepth).toBe(20);
    expect(c.highscore).toBe(1800);
  });
});

describe('EndlessPuzzleComponent on-the-fly Tipps', () => {
  it('setupPuzzle setzt hintLevel zurück (Tipps gelten pro Zug)', () => {
    const c = makeComponent();
    spyOn(c as any, 'setupSolver');   // echten Solver (Stockfish/Brett) neutralisieren
    c.hintLevel = 2;
    (c as any).setupPuzzle({ ...PUZZLE });
    expect(c.hintLevel).toBe(0);
  });

  it('availableHints klassifiziert den AKTUELL erwarteten Zug on-the-fly', () => {
    const c = makeComponent();
    // Basis-chess = Grundstellung → solutionMoves[moveIndex]=e2e4 (ruhiger Bauernzug).
    (c as any).solutionMoves = ['e2e4', 'e7e5', 'g1f3'];
    (c as any).moveIndex = 0;
    expect(c.hasHints).toBeTrue();
    expect(c.availableHints.length).toBe(3);
    expect(c.availableHints[0]).toBe('puzzles.hints.t1Quiet');
  });

  it('showNextHint deckt die Tipps gestuft auf', () => {
    const c = makeComponent();
    (c as any).solutionMoves = ['e2e4', 'e7e5', 'g1f3'];
    (c as any).moveIndex = 0;

    expect(c.shownHints.length).toBe(0);
    c.showNextHint();
    expect(c.shownHints.length).toBe(1);
    c.showNextHint(); c.showNextHint();
    expect(c.shownHints.length).toBe(3);
    expect(c.canShowMoreHints).toBeFalse();
    c.showNextHint();   // über Maximum hinaus bleibt bei 3
    expect(c.shownHints.length).toBe(3);
  });

  it('ohne spielbaren erwarteten Zug gibt es keine Tipps', () => {
    const c = makeComponent();
    (c as any).solutionMoves = [];
    (c as any).moveIndex = 0;
    expect(c.hasHints).toBeFalse();
    expect(c.availableHints).toEqual([]);
  });

  it('toggleHintsFlag setzt das Flag und ruft den Service', () => {
    const c = makeComponent();
    c.snackbar.success = jasmine.createSpy('success');
    const spy = jasmine.createSpy('flag').and.returnValue(sub({ id: 9, hintsFlagged: true }));
    c.puzzleService.flagPuzzleHints = spy;
    c.puzzle = { id: 9, fen: 'x', moves: 'a', hintsFlagged: false };

    c.toggleHintsFlag();

    expect(spy).toHaveBeenCalledWith(9, true);
    expect(c.puzzle.hintsFlagged).toBeTrue();
    expect(c.flagSaving).toBeFalse();
  });
});

describe('EndlessPuzzleComponent Themen-Multiselect', () => {
  it('selectedThemes liest die leerzeichengetrennten Themen aus der Config', () => {
    const c = makeComponent();
    c.config.themes = 'fork pin';
    expect(c.selectedThemes).toEqual(['fork', 'pin']);
  });

  it('addThemeValue hängt ein Thema an und schreibt es als String zurück (keine Duplikate)', () => {
    const c = makeComponent();
    c.config.themes = 'fork';
    (c as any).addThemeValue('pin');
    expect(c.config.themes).toBe('fork pin');
    (c as any).addThemeValue('pin');   // Duplikat ignoriert
    expect(c.config.themes).toBe('fork pin');
  });

  it('removeTheme entfernt das Thema aus dem String', () => {
    const c = makeComponent();
    c.config.themes = 'fork pin endgame';
    c.removeTheme('pin');
    expect(c.config.themes).toBe('fork endgame');
  });

  it('filteredThemes blendet bereits gewählte aus und filtert nach dem Suchtext', () => {
    const c = makeComponent();
    c.allThemes = ['advancedPawn', 'backRankMate', 'endgame', 'fork', 'pin'];
    c.config.themes = 'fork';        // fork ist gewählt → raus aus Vorschlägen
    c.themeInput = 'ba';             // Suchtext
    expect(c.filteredThemes).toEqual(['backRankMate']);
  });

  it('onThemeInputTokenEnd übernimmt frei getippte Themen und leert das Eingabefeld', () => {
    const c = makeComponent();
    c.config.themes = '';
    const clear = jasmine.createSpy('clear');
    (c as any).onThemeInputTokenEnd({ value: 'zwischenzug', chipInput: { clear } });
    expect(c.config.themes).toBe('zwischenzug');
    expect(clear).toHaveBeenCalled();
    expect(c.themeInput).toBe('');
  });

  it('applyThemePreset setzt das Bündel und deaktiviert „schwächste Themen"', () => {
    const c = makeComponent();
    c.config.worstTags = true;
    const preset = c.themePresets.find((p: ThemePreset) => p.labelKey === 'endless.themePreset.basicTactics')!;
    c.applyThemePreset(preset);
    expect(c.config.worstTags).toBe(false);
    expect(c.selectedThemes).toEqual(preset.themes);
    expect(c.isThemePresetActive(preset)).toBe(true);
  });

  it('isThemePresetActive ist false, sobald die Themen abweichen oder worstTags aktiv ist', () => {
    const c = makeComponent();
    const preset = c.themePresets[0];
    c.applyThemePreset(preset);
    expect(c.isThemePresetActive(preset)).toBe(true);
    (c as any).addThemeValue('fork');               // Auswahl weicht ab
    expect(c.isThemePresetActive(preset)).toBe(false);
  });
});

/** Tijdelijk navigator.onLine = false innerhalb von fn(). */
function withOffline(fn: () => void): void {
  Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => false });
  try { fn(); } finally { delete (navigator as any).onLine; }
}

describe('EndlessPuzzleComponent gauntlet (Kette)', () => {
  it('startGame generiert die Kette und lädt das erste Ketten-Puzzle', () => {
    const c = makeComponent();
    c.startGame();
    expect(c['puzzleService'].getRandomBatch).toHaveBeenCalled();
    expect(c['chain'].length).toBe(3);
    expect(c.chainIndex).toBe(0);
    expect(c.puzzle.id).toBe(100);   // chain[0]
    c.ngOnDestroy();
  });

  it('Lösen rückt eine Stelle in der Kette weiter', () => {
    const c = makeComponent();
    c.startGame();
    c.continueAfterSolve();
    expect(c.chainIndex).toBe(1);
    expect(c.puzzle.id).toBe(101);   // chain[1]
    c.ngOnDestroy();
  });

  it('Ein Fehler kostet ein Leben UND rückt weiter (Gauntlet)', () => {
    const c = makeComponent();
    c.startGame();
    expect(c.lives).toBe(3);
    c['loseLife']();                 // Fehler: Leben -1, Status FAILED
    expect(c.lives).toBe(2);
    expect(c.state).toBe('FAILED');
    c.continueAfterWrong();          // weiter zum nächsten (höheren) Puzzle
    expect(c.chainIndex).toBe(1);
    expect(c.puzzle.id).toBe(101);
    c.ngOnDestroy();
  });

  it('continueAfterWrong bei 0 Leben beendet das Spiel (Game Over)', () => {
    const c = makeComponent();
    c.startGame();
    c.lives = 0;
    c.continueAfterWrong();
    expect(c.state).toBe('GAME_OVER');
    expect(c.chainIndex).toBe(0);    // kein Weiterrücken mehr
    c.ngOnDestroy();
  });

  it('retry erlaubt auch beim letzten verlorenen Leben einen Neuversuch desselben Puzzles', () => {
    const c = makeComponent();
    c.startGame();
    c.lives = 1;
    c['loseLife']();                 // letztes Leben weg → lives 0, Status FAILED
    expect(c.lives).toBe(0);
    expect(c.state).toBe('FAILED');

    c.retry();                       // früher: no-op bei 0 Leben; jetzt: Puzzle erneut aufsetzen
    expect(c.state).not.toBe('FAILED');   // wieder spielbar (SETUP/AWAITING)
    expect(c.chainIndex).toBe(0);    // kein Weiterrücken
    expect(c.puzzle.id).toBe(100);   // dasselbe Puzzle, an dem der Run scheiterte
    expect(c.lives).toBe(0);         // Retry kostet kein weiteres Leben
    c.ngOnDestroy();
  });

  it('Lösen eines Retry bei 0 Leben belebt den Lauf NICHT (Game Over statt Weiterspielen mit 0 Herzen)', () => {
    const c = makeComponent();
    c.startGame();
    c.lives = 1;
    c['loseLife']();                 // letztes Leben weg → lives 0, Status FAILED
    c.retry();                       // tödliches Puzzle erneut spielbar, lives bleibt 0
    expect(c.lives).toBe(0);
    c.continueAfterSolve();          // Retry gelöst → „Weiter"
    expect(c.state).toBe('GAME_OVER');
    expect(c.chainIndex).toBe(0);    // kein Weiterrücken trotz gelöstem Retry
    c.ngOnDestroy();
  });

  it('Kette offline durchgespielt → „You win"', () => {
    const c = makeComponent();
    c['chain'] = [{ ...CHAIN[0] }];
    c.chainIndex = 1;                // hinter dem Kettenende
    withOffline(() => c['loadCurrent']());
    expect(c.state).toBe('WON');
    c.ngOnDestroy();
  });

  // Gerät gilt als online, der Server antwortet aber nicht (Funkloch, VPN, Server weg): früher
  // brach der Start mit „keine neuen Puzzles" ab, obwohl die Kette vorab geladen im Speicher lag.
  it('Server nicht erreichbar beim Start → Lauf startet aus der vorab geladenen Kette', () => {
    const c = makeComponent();
    c['offlinePool'] = CHAIN.map(p => ({ ...p }));
    c['puzzleService'].getRandomBatch = jasmine.createSpy('getRandomBatch')
      .and.returnValue(throwError(() => new Error('server down')));
    c.startGame();
    expect(c['chain'].length).toBe(3);
    expect(c.puzzle.id).toBe(100);
    expect(c.state).not.toBe('CONFIG');
    expect(c['snackbar'].info).not.toHaveBeenCalledWith('endless.loadFailed', jasmine.anything());
    c.ngOnDestroy();
  });

  it('Server nicht erreichbar beim Start und KEINE vorab geladene Kette → ehrlicher Abbruch', () => {
    const c = makeComponent();
    c['offlinePool'] = [];
    c['puzzleService'].getRandomBatch = jasmine.createSpy('getRandomBatch')
      .and.returnValue(throwError(() => new Error('server down')));
    c.startGame();
    expect(c.state).toBe('CONFIG');
    expect(c['snackbar'].info).toHaveBeenCalledWith('endless.loadFailed', jasmine.anything());
    c.ngOnDestroy();
  });

  it('Fortsetzen stellt bei passendem Seed das aktuelle Ketten-Puzzle wieder her', () => {
    const c = makeComponent();
    c['storage'].loadChainSeed = () => 'seed-xyz';
    c['offlinePool'] = CHAIN.map(p => ({ ...p }));
    c.activeGameState = { lives: 2, solved: 2, chainIndex: 2, seed: 'seed-xyz', maxRatingReached: 1100 };
    c.resumeGame();
    expect(c.chainIndex).toBe(2);
    expect(c.puzzle.id).toBe(102);   // exakt dasselbe Puzzle wie vor dem Refresh
    c.ngOnDestroy();
  });

  it('startGame vergibt einen eindeutigen Seed und schreibt Seed + Ketten-IDs in den Session-Record', () => {
    const c = makeComponent();
    const sessions: any[] = [];
    c['storage'].recordSessionToServer = (s: any) => { sessions.push(s); return sub(null); };
    c.startGame();
    expect(c['seed']).toBeTruthy();
    const seed = c['seed'];
    // Lauf beenden (Game Over) → Session wird mit Seed + Ketten-IDs aufgezeichnet.
    c.lives = 0;
    c.continueAfterWrong();
    expect(c.state).toBe('GAME_OVER');
    expect(sessions.length).toBe(1);
    expect(sessions[0].seed).toBe(seed);
    expect(sessions[0].chainPuzzleIds).toBe('100,101,102');   // geordnete Ketten-IDs
    c.ngOnDestroy();
  });
});

describe('EndlessPuzzleComponent Session-Aufzeichnung (Verlust-Schutz)', () => {
  function trackSessions(c: any): any[] {
    const sessions: any[] = [];
    c['storage'].recordSessionToServer = (s: any) => { sessions.push(s); return sub(null); };
    return sessions;
  }

  it('zeichnet einen beendeten Run auf, wenn der User die Seite verlässt BEVOR er „Weiter" klickt (Sicherheitsnetz)', () => {
    const c = makeComponent();
    const sessions = trackSessions(c);
    c.startGame();
    c.lives = 1;
    c['loseLife']();                 // letztes Leben weg → lives 0, FAILED, kein „Weiter"
    expect(c.state).toBe('FAILED');
    expect(sessions.length).toBe(0);   // wartet normalerweise auf den Continue-Klick (endGame)
    c.ngOnDestroy();                   // User wechselt z.B. zur History → Sicherheitsnetz greift
    expect(sessions.length).toBe(1);
  });

  it('postet NICHT doppelt, wenn der Run schon via „Weiter" (endGame) aufgezeichnet wurde', () => {
    const c = makeComponent();
    const sessions = trackSessions(c);
    c.startGame();
    c.lives = 0;
    c.continueAfterWrong();          // endGame → 1 Aufzeichnung
    expect(sessions.length).toBe(1);
    c.ngOnDestroy();                 // Verlassen darf KEINEN zweiten Post auslösen
    expect(sessions.length).toBe(1);
  });

  it('zeichnet nichts auf, wenn nur ein Lauf aus der History angesehen wird', () => {
    const c = makeComponent();
    const sessions = trackSessions(c);
    c.historyView = true;
    c.lives = 0;                     // History-Detail zeigt einen abgeschlossenen (0-Leben-)Lauf
    c.ngOnDestroy();
    expect(sessions.length).toBe(0);
  });

  it('das pagehide-Handler rettet einen noch nicht aufgezeichneten beendeten Lauf ebenfalls (genau einmal)', () => {
    const c = makeComponent();
    const sessions = trackSessions(c);
    c.startGame();
    c.lives = 1;
    c['loseLife']();
    c.onPageHide();
    expect(sessions.length).toBe(1);
    c.ngOnDestroy();                 // kein zweiter Post nach pagehide
    expect(sessions.length).toBe(1);
  });

  it('ein noch laufender Run (Leben übrig) wird beim Verlassen NICHT als beendet aufgezeichnet', () => {
    const c = makeComponent();
    const sessions = trackSessions(c);
    c.startGame();                   // lives = 3
    c.ngOnDestroy();
    expect(sessions.length).toBe(0);
  });
});

/**
 * F2-001: Der Server speichert die Puzzle-Liste als Detail des Laufs (History, „Level" = ihre Länge).
 * Vorher schickte recordSession nur Puzzles mit startedAt > 0 — nach jedem Fortsetzen (Neuladen,
 * Analyse-Rückkehr mit ?resume=1, anderes Gerät) fielen alle vorher gespielten dauerhaft weg.
 */
describe('EndlessPuzzleComponent gespeicherter Lauf nach dem Fortsetzen', () => {
  const OLD_ATTEMPTS = [
    { puzzleNumber: 1, puzzleId: 100, lichessId: 'a', rating: 700, solved: true, startedAt: 1000, endedAt: 2000 },
    { puzzleNumber: 2, puzzleId: 101, lichessId: 'b', rating: 900, solved: true },   // Spielstand aus älterem Build: ohne Zeiten
  ];

  function trackRecords(c: any): { session: any; puzzles: any[] }[] {
    const calls: { session: any; puzzles: any[] }[] = [];
    c['storage'].recordSessionToServer = (session: any, puzzles: any[] = []) => { calls.push({ session, puzzles }); return sub(null); };
    return calls;
  }

  it('schickt beim Lauf-Ende ALLE Puzzles mit, auch die vor dem Fortsetzen gespielten', () => {
    const c = makeComponent();
    const calls = trackRecords(c);
    c['storage'].loadChainSeed = () => 'seed-xyz';
    c['offlinePool'] = CHAIN.map(p => ({ ...p }));
    c.activeGameState = { lives: 1, solved: 2, chainIndex: 2, seed: 'seed-xyz', maxRatingReached: 1100,
      puzzleAttempts: OLD_ATTEMPTS.map(p => ({ ...p })) };

    c.resumeGame();
    expect(c.puzzle.id).toBe(102);
    c['loseLife']();                 // Puzzle 3 verloren → 0 Leben
    c.continueAfterWrong();          // endGame → Lauf wird aufgezeichnet

    expect(calls.length).toBe(1);
    const puzzles = calls[0].puzzles;
    expect(puzzles.map((p: any) => p.puzzleId)).toEqual([100, 101, 102]);
    expect(puzzles[0].startedAt).toBe(1000);            // Zeit aus dem Spielstand bleibt erhalten
    expect(puzzles[1].startedAt).toBe(0);               // unbekannt → Server speichert, loggt aber nicht
    expect(puzzles[2].startedAt).toBeGreaterThan(0);
    c.ngOnDestroy();
  });

  it('der Spielstand trägt Start-/Endzeit je Puzzle mit', () => {
    const c = makeComponent();
    const states: any[] = [];
    c['storage'].saveActiveGameLocal = (g: any) => states.push(g);
    c.startGame();

    c['loseLife']();                 // Fehler bei 3 Leben → Spielstand wird gesichert

    const last = states.filter(Boolean).pop();
    expect(last.puzzleAttempts.length).toBe(1);
    expect(last.puzzleAttempts[0].startedAt).toBeGreaterThan(0);
    expect(last.puzzleAttempts[0].endedAt).toBeGreaterThan(0);
    c.ngOnDestroy();
  });

  it('Archivieren eines offenen Laufs nimmt dessen Puzzles in den gespeicherten Lauf mit', () => {
    const c = makeComponent();
    const calls = trackRecords(c);
    c.activeGameState = { lives: 2, solved: 2, chainIndex: 2, seed: 'seed-xyz', maxRatingReached: 1100,
      puzzleAttempts: OLD_ATTEMPTS.map(p => ({ ...p })) };

    c.archiveAndStartNew();

    expect(calls.length).toBe(1);
    expect(calls[0].puzzles.map((p: any) => p.puzzleId)).toEqual([100, 101]);
    c.ngOnDestroy();
  });
});

/**
 * F2-002: Das Ergebnis (gelöst, Fehler, Zurücksetzen) wurde gespeichert, BEVOR die Kette weiterrückte —
 * ein Neuladen, „Letztes Puzzle ansehen" oder „Analysieren" im SOLVED-Zustand (Rückkehr mit ?resume=1)
 * spielte dasselbe Puzzle erneut: doppelt gezählt bzw. ein zweites Leben am selben Puzzle.
 */
describe('EndlessPuzzleComponent Fortsetzen nach einem Ergebnis', () => {
  /** Spielt mit einer frischen Instanz einen Lauf an und liefert die gesicherten Spielstände. */
  function started(): { c: any; states: any[] } {
    const c = makeComponent();
    const states: any[] = [];
    c['storage'].saveActiveGameLocal = (g: any) => states.push(g);
    c.startGame();                   // Kette 100/101/102, Puzzle 100
    return { c, states };
  }

  /** Neue Instanz wie nach Neuladen bzw. ?resume=1, mit dem zuletzt gesicherten Spielstand. */
  function resumed(state: any, live: any = null): any {
    const d = makeComponent();
    d['storage'].loadChainSeed = () => state.seed;
    d['storage'].loadLiveElapsed = () => live;
    d['offlinePool'] = CHAIN.map(p => ({ ...p }));
    d.activeGameState = state;
    d.resumeGame();
    return d;
  }

  afterEach(() => sessionStorage.removeItem('rookhub_last_solved_endless'));

  it('gelöst → Fortsetzen lädt das NÄCHSTE Puzzle, gelöst zählt nicht doppelt', () => {
    const { c, states } = started();
    c['puzzleSolved'](false);
    const state = states.filter(Boolean).pop();
    c.ngOnDestroy();

    expect(state.chainIndex).toBe(1);
    expect(state.solved).toBe(1);
    const d = resumed(state);
    expect(d.chainIndex).toBe(1);
    expect(d.puzzle.id).toBe(101);
    expect(d.solved).toBe(1);
    d.ngOnDestroy();
  });

  it('Fehler → Fortsetzen lädt das nächste Puzzle (kein zweites Leben am selben Puzzle)', () => {
    const { c, states } = started();
    c['loseLife']();
    const state = states.filter(Boolean).pop();
    c.ngOnDestroy();

    const d = resumed(state);
    expect(d.chainIndex).toBe(1);
    expect(d.puzzle.id).toBe(101);
    expect(d.lives).toBe(2);
    d.ngOnDestroy();
  });

  it('Zurücksetzen (kostet ein Leben) → Fortsetzen lädt das nächste Puzzle', () => {
    const { c, states } = started();
    c.resetPuzzle();
    const state = states.filter(Boolean).pop();
    c.ngOnDestroy();

    expect(state.lives).toBe(2);
    expect(state.chainIndex).toBe(1);
  });

  it('ohne Ergebnis bleibt Fortsetzen beim aktuellen Puzzle', () => {
    const { c, states } = started();
    const state = states.filter(Boolean).pop();
    c.ngOnDestroy();

    expect(state.chainIndex).toBe(0);
    const d = resumed(state);
    expect(d.puzzle.id).toBe(100);
    d.ngOnDestroy();
  });

  it('Analyse nach einem Fehler: Rückkehr beim nächsten Puzzle, dessen Zeit bei 0 beginnt', () => {
    const { c, states } = started();
    const lives: any[] = [];
    c['storage'].saveLiveElapsed = (e: any) => lives.push(e);
    c['loseLife']();
    c['puzzleStopwatch'].start(40);  // 40 s am gescheiterten Puzzle
    c['puzzleStartTime'] = Date.now();
    c.analyzeCurrentPuzzle();
    c.ngOnDestroy();                 // sichert den Live-Zeitstand
    const state = states.filter(Boolean).pop();

    expect(state.chainIndex).toBe(1);                   // nicht 2: Zeiger rückte schon beim Fehler vor
    const d = resumed(state, lives[lives.length - 1]);
    expect(d.puzzle.id).toBe(101);
    expect(d['resumePuzzleSeconds']).toBe(0);           // die 40 s gehören zum alten Puzzle
    d.ngOnDestroy();
  });

  it('Ergebnis am letzten Puzzle der lokalen Kette: Fortsetzen verlängert sie statt neu zu würfeln', () => {
    const d = makeComponent();
    const pool = CHAIN.map(p => ({ ...p }));
    d['storage'].loadChainSeed = () => 'seed-xyz';
    d['offlinePool'] = pool;
    d.activeGameState = { lives: 2, solved: 3, chainIndex: 3, seed: 'seed-xyz', maxRatingReached: 1100 };
    d.resumeGame();
    expect(d['chain'][0]).toBe(pool[0]);                // gespielte Kette bleibt (kein Neuaufbau ab 0)
    expect(d['chain'].length).toBe(6);                  // um einen Block verlängert
    expect(d.chainIndex).toBe(3);
    d.ngOnDestroy();
  });
});

describe('EndlessPuzzleComponent prefetch race (runGeneration)', () => {
  /** Steuerbares Observable: merkt sich den Handler, damit der Test next() später feuert. */
  function controllable() {
    let fire: (v: any) => void = () => {};
    const obs = { subscribe: (h: any) => { fire = (v: any) => (typeof h === 'function' ? h : h.next)(v); return { unsubscribe() {} }; } };
    return { obs, emit: (v: any) => fire(v) };
  }

  it('does NOT overwrite the pool once a run has started', () => {
    const c = makeComponent();
    c.ensureWorstThemes = (cb: any) => cb();
    const ctrl = controllable();
    c.puzzleService.getRandomBatch = () => ctrl.obs;
    c.offlinePool = [];

    c.prefetchRun();        // merkt sich runGeneration
    c.runGeneration++;      // inzwischen ist ein Run gestartet
    ctrl.emit([{ id: 1 }]); // späte Prefetch-Antwort

    expect(c.offlinePool).toEqual([]);   // Pool des Runs bleibt unangetastet
  });

  it('fills the pool when no run started in the meantime', () => {
    const c = makeComponent();
    c.ensureWorstThemes = (cb: any) => cb();
    const ctrl = controllable();
    c.puzzleService.getRandomBatch = () => ctrl.obs;
    c.offlinePool = [];

    c.prefetchRun();
    ctrl.emit([{ id: 9 }]);

    expect(c.offlinePool.length).toBe(1);
  });
});

describe('EndlessPuzzleComponent recordAttempt', () => {
  it('sends the used hint level (regression: endless dropped hintsUsed)', () => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.maxHintLevel = 2;
    const spy = jasmine.createSpy('recordAnonymousAttempt').and.returnValue(sub(null));
    c.puzzleService.recordAnonymousAttempt = spy;

    c.recordAttempt(true, 12);   // anonym (auth.isLoggedIn=false) → recordAnonymousAttempt

    expect(spy).toHaveBeenCalled();
    // Signatur: (id, solved, timeSpentSeconds, moveLog?, viz, evalShown, vizShowCount, hintsUsed)
    expect(spy.calls.mostRecent().args[7]).toBe(2);
  });
});

describe('EndlessPuzzleComponent Live-Zeitstand (Refresh mitten im Puzzle)', () => {
  it('onSolvingBegins setzt eine fortzusetzende Puzzle-Zeit genau einmal fort', () => {
    const c = makeComponent();
    (c as any).resumePuzzleSeconds = 12;
    (c as any).onSolvingBegins();
    expect(c.elapsedSeconds).toBe(12);          // Zwischenzeit fortgesetzt
    (c as any).onSolvingBegins();
    expect(c.elapsedSeconds).toBe(0);           // konsumiert → nächstes Puzzle startet bei 0
    c.ngOnDestroy();
  });

  it('resumeGame übernimmt den Live-Stand desselben Laufs (Session-Maximum + Puzzle-Zeit)', () => {
    const c = makeComponent();
    spyOn(c as any, 'loadCurrent');             // Kettenaufbau hier irrelevant
    c['storage'].loadChainSeed = () => 'seed-xyz';
    c['offlinePool'] = CHAIN.map((p: any) => ({ ...p }));
    c['storage'].loadLiveElapsed = () => ({ seed: 'seed-xyz', chainIndex: 2, session: 95, puzzle: 12 });
    c.activeGameState = { lives: 2, solved: 2, chainIndex: 2, seed: 'seed-xyz', sessionSeconds: 80, maxRatingReached: 1100 };
    c.resumeGame();
    expect(c.sessionSeconds).toBe(95);                    // Live-Stand (jünger) schlägt Sync-Punkt (80)
    expect((c as any).resumePuzzleSeconds).toBe(12);      // gleiches Puzzle → Zwischenzeit wird fortgesetzt
    c.ngOnDestroy();
  });

  it('Live-Stand eines anderen Laufs (Seed) wird ignoriert', () => {
    const c = makeComponent();
    spyOn(c as any, 'loadCurrent');
    c['storage'].loadChainSeed = () => 'seed-xyz';
    c['offlinePool'] = CHAIN.map((p: any) => ({ ...p }));
    c['storage'].loadLiveElapsed = () => ({ seed: 'anderer-lauf', chainIndex: 2, session: 999, puzzle: 50 });
    c.activeGameState = { lives: 2, solved: 2, chainIndex: 2, seed: 'seed-xyz', sessionSeconds: 80, maxRatingReached: 1100 };
    c.resumeGame();
    expect(c.sessionSeconds).toBe(80);
    expect((c as any).resumePuzzleSeconds).toBe(0);
    c.ngOnDestroy();
  });

  it('bei anderer Ketten-Position wird nur die Session-Zeit angehoben, nicht die Puzzle-Zeit', () => {
    const c = makeComponent();
    spyOn(c as any, 'loadCurrent');
    c['storage'].loadChainSeed = () => 'seed-xyz';
    c['offlinePool'] = CHAIN.map((p: any) => ({ ...p }));
    c['storage'].loadLiveElapsed = () => ({ seed: 'seed-xyz', chainIndex: 1, session: 95, puzzle: 12 });
    c.activeGameState = { lives: 2, solved: 2, chainIndex: 2, seed: 'seed-xyz', sessionSeconds: 80, maxRatingReached: 1100 };
    c.resumeGame();
    expect(c.sessionSeconds).toBe(95);
    expect((c as any).resumePuzzleSeconds).toBe(0);       // anderes Puzzle → nicht fortsetzen
    c.ngOnDestroy();
  });

  it('der Sekunden-Tick persistiert den Live-Stand über den Storage', () => {
    const c = makeComponent();
    const saved: any[] = [];
    c['storage'].saveLiveElapsed = (e: any) => saved.push(e);
    (c as any).seed = 'seed-abc';
    (c as any).chainIndex = 3;
    c.sessionSeconds = 41;
    (c as any).saveLiveElapsedNow();
    expect(saved.length).toBe(1);
    expect(saved[0].seed).toBe('seed-abc');
    expect(saved[0].chainIndex).toBe(3);
    expect(saved[0].session).toBe(41);
    (c as any).seed = '';
    (c as any).saveLiveElapsedNow();                      // ohne Lauf-Seed wird nichts geschrieben
    expect(saved.length).toBe(1);
  });
  it('chainPreview: Marken sortiert, dedupliziert und auf den 30er-Block geklemmt', () => {
    const c = makeComponent();
    c.config.startElo = 700;
    (c as any).sessionHistory = [];                       // erster Lauf → Anker 15/30
    const first = c.chainPreview;
    expect(first.map((p: any) => p.puzzle)).toEqual([1, 16, 30]);   // vorher: 1, 16, 31, 30
    // Puzzle-Nummern streng aufsteigend, letzte = Blockende
    expect(first[first.length - 1].puzzle).toBe(30);
    for (let i = 1; i < first.length; i++) expect(first[i].puzzle).toBeGreaterThan(first[i - 1].puzzle);
  });
});


/**
 * Spielweise (Training/Einfach) im Bereich „Endless": Ein LAUF ist die Einheit — gefragt wird beim
 * Start/Fortsetzen eines Laufs (und nur beim ersten Mal), nicht bei jedem Puzzle darin. Bei fester
 * Ansicht aus dem Link (?visualmode=) wird gar nicht gefragt.
 */
describe('EndlessPuzzleComponent Spielweise', () => {
  it('fragt beim ersten Lauf-Start mit dem Bereich „endless"', () => {
    const sm = makeSolveMode('training');
    const c = makeComponent({}, sm);

    c.startGame();

    expect(sm.ensure).toHaveBeenCalled();
    expect(sm.ensure.calls.mostRecent().args[0]).toBe('endless');
    expect(sm.ensure.calls.mostRecent().args[1].scopeLabel).toBe('solveMode.scope.endless');
    expect(sm.dialogCalls).toBe(1);
    c.ngOnDestroy();
  });

  it('fragt beim zweiten Lauf NICHT mehr (gemerkte Wahl)', () => {
    const sm = makeSolveMode('easy');
    const c = makeComponent({}, sm);

    c.startGame();
    c.startGame();

    expect(sm.dialogCalls).toBe(1);
    expect(c.solveModeChoice).toBe('easy');
    c.ngOnDestroy();
  });

  it('wendet die Stufe der gewählten Spielweise an (einfach = 0, Training = eingestellte Stufe)', () => {
    const einfach = makeComponent({}, makeSolveMode('easy', 3));
    einfach.startGame();
    expect(einfach.visualizationMode).toBe(0);
    einfach.ngOnDestroy();

    const training = makeComponent({}, makeSolveMode('training', 3));
    training.startGame();
    expect(training.visualizationMode).toBe(3);
    training.ngOnDestroy();
  });

  it('fragt NICHT bei fester Ansicht aus dem Link (?visualmode=)', () => {
    const sm = makeSolveMode('easy');
    const c = makeComponent({ visualmode: '2' }, sm);
    c.ngOnInit();

    c.startGame();

    expect(sm.ensure).not.toHaveBeenCalled();
    expect(c.solveModeChoice).toBeNull();
    expect(c.visualizationMode).toBe(2);   // Stufe aus der URL bleibt
    c.ngOnDestroy();
  });

  it('Umschalten merkt die neue Spielweise, wirkt aber erst beim nächsten Puzzle', () => {
    const sm = makeSolveMode('training', 3);
    const c = makeComponent({}, sm);
    c.startGame();
    expect(c.visualizationMode).toBe(3);

    c.toggleSolveMode();

    expect(c.solveModeChoice).toBe('easy');
    expect(sm.set).toHaveBeenCalledWith('endless', 'easy');
    expect(c.snackbar.info).toHaveBeenCalledWith('solveMode.switchedEasy', { duration: 3000 });
    expect(c.visualizationMode).toBe(3);   // laufender Versuch behält seine Regeln

    c.loadCurrent();
    expect(c.visualizationMode).toBe(0);   // erst das nächste Puzzle spielt einfach
    c.ngOnDestroy();
  });

  it('direkte Stufenwahl zieht die gemerkte Spielweise mit', () => {
    const sm = makeSolveMode('training', 3);
    const c = makeComponent({}, sm);

    c.setVisualizationLevel(0);
    expect(sm.set).toHaveBeenCalledWith('endless', 'easy');
    expect(c.solveModeChoice).toBe('easy');

    c.setVisualizationLevel(2);
    expect(sm.set).toHaveBeenCalledWith('endless', 'training');
    expect(c.solveModeChoice).toBe('training');
    c.ngOnDestroy();
  });
});

/** Einstellungsdialog-Doppel: `open()` liefert sofort das Ergebnis (Brett/Figuren geändert, Rest wie vorher). */
function stubSettingsDialog(c: any, overrides: Record<string, unknown> = {}): void {
  const p: any = c.prefs;
  Object.assign(p, {
    setBoardTheme: (t: string) => { p.boardTheme = t; }, setPieceSet: (s: string) => { p.pieceSet = s; },
    setThemeMode: (m: string) => { p.themeMode = m; }, setVizArrow: () => {},
    setOffPathWarnMoves: () => {}, setEnPassantForced: (v: boolean) => { p.enPassantForced = v; },
  });
  const result = {
    boardTheme: 'blue', pieceSet: 'alpha', themeMode: c.themeMode, visualizationMode: c.visualizationMode,
    vizArrowEnabled: c.vizArrowEnabled, enPassantForced: true, ...overrides,
  };
  c.dialog.open = () => ({ afterClosed: () => ({ subscribe: (h: any) => h(result) }) });
}

// Codereview F2-009: Speichern im Einstellungsdialog setzte das laufende Puzzle neu auf, auch wenn nur
// Brett/Figuren wechselten; die e.p.-Zeile überschrieb den Link-Zwang (?anarchy=max).
describe('EndlessPuzzleComponent Einstellungen speichern (F2-009)', () => {
  it('nur Brett/Figuren geändert: der laufende Versuch bleibt stehen', () => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.state = 'AWAITING_USER_MOVE';
    spyOn(c as any, 'setupPuzzle');
    stubSettingsDialog(c);

    c.openSettingsDialog();

    expect((c as any).setupPuzzle).not.toHaveBeenCalled();
    expect(c.boardTheme).toBe('blue');
  });

  it('geänderte Stufe während des Lösens setzt das Puzzle neu auf', () => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.state = 'AWAITING_USER_MOVE';
    spyOn(c as any, 'setupPuzzle');
    stubSettingsDialog(c, { visualizationMode: 2 });

    c.openSettingsDialog();

    expect(c.visualizationMode).toBe(2);
    expect((c as any).setupPuzzle).toHaveBeenCalledWith(c.puzzle);
  });

  it('?anarchy=max erzwingt e.p. weiter, auch wenn es in den Einstellungen aus ist', () => {
    const c = makeComponent();
    (c as any).anarchyForcedByUrl = true;      // wie nach ngOnInit mit ?anarchy=max
    c.enPassantForced = true;                  // wie nach onSetupStart
    stubSettingsDialog(c, { enPassantForced: false });

    c.openSettingsDialog();

    expect(c.enPassantForced).toBeTrue();
  });
});

// Codereview F2-011: Der Tiefen-Regler im Spielbildschirm rief saveConfig, und das schickte immer
// activeGameState=null — der Server löschte den offenen Lauf (geräteübergreifendes Fortsetzen weg),
// der Debounce verdrängte sogar einen gerade geplanten Spielstand.
describe('EndlessPuzzleComponent Tiefen-Regler im Lauf (F2-011)', () => {
  const GAME = { lives: 2, solved: 6, level: 6, chainIndex: 6, seed: 's' };

  it('schickt während des Laufs den gesicherten Spielstand mit statt null', () => {
    const c = makeComponent();
    c.state = 'AWAITING_USER_MOVE';
    c.lives = 2;
    c.storage.loadActiveGameLocal = () => ({ ...GAME });
    const save = spyOn(c.storage, 'saveProgressToServer');

    c.config.stockfishDepth = 20;
    c.onDepthChange();

    expect(save).toHaveBeenCalledTimes(1);
    expect(save.calls.mostRecent().args[2]).toEqual(GAME);
  });

  it('außerhalb eines Laufs bleibt es bei null (startGame räumt so den alten Lauf ab)', () => {
    const c = makeComponent();
    c.state = 'CONFIG';
    c.storage.loadActiveGameLocal = () => ({ ...GAME });
    const save = spyOn(c.storage, 'saveProgressToServer');

    c.onDepthChange();

    expect(save.calls.mostRecent().args[2]).toBeNull();
  });
});

// Codereview F2-013: Phasenanzeige und Hilfe stammten aus einem älteren linearen Modell — „Phase 3 (Schritt 20)"
// und „Phase 1 (Puzzle 1–5)", während die Kette bis Puzzle 10/25 log-förmig und danach um 15 stieg.
describe('EndlessPuzzleComponent Phasenanzeige (F2-013)', () => {
  /** Kein Erst-Lauf (eine Session in der Historie) → adaptive Kurve; translate gibt die Parameter mit aus. */
  function adaptive(): any {
    const c = makeComponent();
    c.sessionHistory = [{ timestamp: 1, config: { ...c.config }, totalSolved: 5, maxRating: 1200, durationSeconds: 60, mistakeAtRatings: [1000] }];
    c.translate.instant = (k: string, p?: object) => (p ? `${k} ${JSON.stringify(p)}` : k);
    return c;
  }
  const label = (phase: number, step: number) => `endless.game.phaseLabel ${JSON.stringify({ phase, step })}`;
  const stepAt = (c: any, i: number) => {
    const s = c.config.startElo, t1 = c.fasttrackAvgFirst, t2 = c.fasttrackAvgSecond;
    return chainRatingAt(i + 1, s, t1, t2) - chainRatingAt(i, s, t1, t2);
  };

  it('nennt ab Phase 3 den echten Schritt der Kette (CHAIN_FLAT_STEP, nicht 20)', () => {
    const c = adaptive();
    c.chainIndex = 30;
    expect(c.currentPhaseLabel).toBe(label(3, CHAIN_FLAT_STEP));
  });

  it('Phase 1/2: Schritt = Anstieg der Kette zum nächsten Puzzle, Grenzen bei CHAIN_T1_INDEX/CHAIN_T2_INDEX', () => {
    const c = adaptive();
    for (const [i, phase] of [[0, 1], [CHAIN_T1_INDEX - 1, 1], [CHAIN_T1_INDEX, 2], [CHAIN_T2_INDEX - 1, 2], [CHAIN_T2_INDEX, 3]]) {
      c.chainIndex = i;
      expect(c.currentPhaseLabel).withContext(`chainIndex ${i}`).toBe(label(phase, stepAt(c, i)));
    }
    c.chainIndex = 0;
    expect(stepAt(c, 0)).not.toBe(c.fasttrackPhase1Step);   // die alte „/5"-Anzeige lag daneben
  });

  it('Hilfe-Parameter decken sich mit den Phasengrenzen (1-basierte Puzzle-Nummern)', () => {
    const c = adaptive();
    expect(c.phaseHelp).toEqual({
      p1End: CHAIN_T1_INDEX, p2Start: CHAIN_T1_INDEX + 1, p2End: CHAIN_T2_INDEX,
      p3Start: CHAIN_T2_INDEX + 1, flatStep: CHAIN_FLAT_STEP,
    });
    c.chainIndex = c.phaseHelp.p1End - 1;      // letztes Puzzle von Phase 1 laut Hilfe
    expect(c.currentPhaseLabel).toContain('"phase":1');
    c.chainIndex = c.phaseHelp.p3Start - 1;    // erstes Puzzle von Phase 3 laut Hilfe
    expect(c.currentPhaseLabel).toContain('"phase":3');
  });

  it('die Hilfetexte (en) setzen genau diese Parameter ein, keine festen Zahlen mehr', async () => {
    let en: any = null;
    for (const url of ['/i18n/en.json', '/base/i18n/en.json']) {
      const res = await fetch(url);
      if (res.ok) { en = await res.json(); break; }
    }
    const help = en.endless.help;
    const params = (s: string) => [...s.matchAll(/\{\{(\w+)\}\}/g)].map(m => m[1]).sort();
    expect(params(help.fasttrackPhase1)).toEqual(['p1End']);
    expect(params(help.fasttrackPhase2)).toEqual(['p2End', 'p2Start']);
    expect(params(help.fasttrackPhase3)).toEqual(['flatStep', 'p3Start']);
  });
});

/**
 * Rendert die Endless-Seite (Template) mit ausgeblendeten Kindkomponenten; ngOnInit muss der Aufrufer stilllegen,
 * den Zustand setzt `setup`. Übersetzt ist nur endless.game.lives (für die Leben-Beschriftung).
 */
function renderEndless(setup: (c: any) => void, extra: { providers?: any[]; imports?: any[] } = {}): HTMLElement {
  const fake = makeComponent();   // liefert die Test-Doppel; gerendert wird eine eigene Instanz über TestBed
  TestBed.configureTestingModule({
    imports: [EndlessPuzzleComponent],
    providers: [
      provideTranslateService({ fallbackLang: 'en' }),
      { provide: PuzzleService, useValue: fake.puzzleService },
      { provide: StockfishService, useValue: { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } },
      { provide: EndlessStorageService, useValue: fake.storage },
      { provide: AuthService, useValue: { isLoggedIn: false } },
      { provide: PreferencesService, useValue: fake.prefs },
      { provide: Router, useValue: { navigate: jasmine.createSpy('navigate'), url: '/puzzles/endless' } },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
      { provide: MatDialog, useValue: {} },
      { provide: OfflineService, useValue: { puzzleCount: 0, endlessRuns: 0 } },
      { provide: SnackbarService, useValue: { info: () => {} } },
      { provide: OfflineQueueService, useValue: { enqueue: () => {} } },
      { provide: LongSolveService, useValue: { resolve: (x: number) => sub(x) } },
      { provide: FavoritesService, useValue: { contains: () => sub(false), add: () => sub(true), remove: () => sub(false), count: () => sub(0), list: () => sub([]) } },
      { provide: EndlessChainService, useValue: fake.chainService },
      { provide: SolveModeService, useValue: makeSolveMode() },
      { provide: WorksheetService, useValue: {} },
      ...(extra.providers ?? []),
    ],
  });
  TestBed.overrideComponent(EndlessPuzzleComponent, { set: { imports: [CommonModule, FormsModule, MatAutocompleteModule, TranslatePipe, ...(extra.imports ?? [])], schemas: [NO_ERRORS_SCHEMA] } });
  const translate = TestBed.inject(TranslateService);
  translate.setTranslation('en', { endless: { game: { lives: '{{lives}} of {{max}} lives' } } });
  translate.use('en');
  const fixture = TestBed.createComponent(EndlessPuzzleComponent);
  setup(fixture.componentInstance);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

// Codereview UX-047: „Auto: …" (Schwelle zurücksetzen) war ein <span (click)> ohne Tastaturzugang, und die drei
// Herzen (Leben) waren reine mat-icons (aria-hidden) ohne Textalternative. Gerendert (Template), Kinder ausgeblendet.
describe('EndlessPuzzleComponent a11y: Auto-Knopf und Leben (UX-047)', () => {
  beforeEach(() => spyOn(EndlessPuzzleComponent.prototype, 'ngOnInit'));   // nichts laden — den Zustand setzt der Test

  const render = renderEndless;

  it('„Auto: …" ist ein Knopf (per Tastatur erreichbar) und setzt die Schwelle zurück', () => {
    let c: any;
    const el = render(x => {
      c = x;
      c.showAdvanced = true;
      Object.assign(c.fasttrack, { phase1Step: 50, avgFirst: 1200, autoFirst: 1000, avgSecond: 1500, autoSecond: 1500 });
    });
    const hints = el.querySelectorAll('.auto-hint');
    expect(hints.length).toBe(1);
    expect(hints[0].tagName).toBe('BUTTON');
    expect(hints[0].getAttribute('type')).toBe('button');
    const reset = spyOn(c, 'resetThreshold');
    (hints[0] as HTMLButtonElement).click();
    expect(reset).toHaveBeenCalledWith(1);
  });

  it('Startbildschirm: die Herzen sagen, wie viele Leben es gibt', () => {
    const lives = render(() => {}).querySelector('.config-lives')!;
    expect(lives.getAttribute('role')).toBe('img');
    expect(lives.getAttribute('aria-label')).toBe('3 of 3 lives');
  });

  it('im Lauf: die Herzen nennen die verbleibenden Leben', () => {
    const el = render(c => { c.state = 'AWAITING_USER_MOVE'; c.puzzle = { ...PUZZLE }; c.lives = 2; });
    const hearts = el.querySelector('.qs-hearts')!;
    expect(hearts.getAttribute('role')).toBe('img');
    expect(hearts.getAttribute('aria-label')).toBe('2 of 3 lives');
  });
});

// Codereview UX-044: Das Konfig-Raster hatte am Desktop zwei Spalten — „Alle löschen" (nowrap) drückte die
// Schnellauswahl links auf ~100 px, die Chips brachen mitten im Wort um, das Raster ragte über den Kartenrand.
describe('EndlessPuzzleComponent Konfig-Raster am Desktop (UX-044)', () => {
  beforeEach(() => spyOn(EndlessPuzzleComponent.prototype, 'ngOnInit'));

  it('ist einspaltig: Schnellauswahl und Themenfeld stehen untereinander, Chips ohne Umbruch', () => {
    expect(window.innerWidth).withContext('Desktop-Viewport (karma --window-size)').toBeGreaterThan(768);
    const el = renderEndless(() => {});
    const fields = el.querySelector('.config-fields') as HTMLElement;
    expect(getComputedStyle(fields).gridTemplateColumns.trim().split(/\s+/).length).toBe(1);
    const presets = (el.querySelector('.theme-presets') as HTMLElement).getBoundingClientRect();
    const themes = (el.querySelector('.themes-row') as HTMLElement).getBoundingClientRect();
    expect(themes.top).toBeGreaterThanOrEqual(presets.bottom);
    expect(getComputedStyle(el.querySelector('.theme-preset-chip') as HTMLElement).whiteSpace).toBe('nowrap');
  });
});

// Codereview UX-010: Der Hilfe-Knopf war ein Symbol-Knopf ohne Namen, die Hilfe ein eigenes div-Overlay ohne
// role="dialog", ohne Fokusführung, und Esc schloss es nicht. Jetzt ein echter MatDialog aus dem Template.
describe('EndlessPuzzleComponent Hilfe als Dialog (UX-010)', () => {
  beforeEach(() => spyOn(EndlessPuzzleComponent.prototype, 'ngOnInit'));
  afterEach(() => TestBed.inject(MatDialog).closeAll());

  const renderWithDialog = () => renderEndless(() => {}, {
    providers: [MatDialog, provideNoopAnimations()],
    imports: [MatDialogModule, MatButtonModule],
  });

  it('der Hilfe-Knopf hat einen Namen', () => {
    const btn = renderWithDialog().querySelector('.help-btn')!;
    expect(btn.getAttribute('aria-label')).toBe('endless.help.open');
    expect(btn.getAttribute('type')).toBe('button');
  });

  it('öffnet einen Dialog mit Rolle und Titel, holt den Fokus hinein, Esc schließt ihn', async () => {
    const el = renderWithDialog();
    (el.querySelector('.help-btn') as HTMLButtonElement).click();
    await new Promise(r => setTimeout(r));

    const dialog = document.querySelector('.cdk-overlay-container [role="dialog"]') as HTMLElement;
    expect(dialog).withContext('Dialog-Element im Overlay').not.toBeNull();
    expect(el.closest('[aria-hidden="true"]')).withContext('Seite dahinter für Screenreader ausgeblendet').not.toBeNull();
    const title = dialog.querySelector('h2')!;
    expect(title.textContent).toContain('endless.help.title');
    expect(dialog.getAttribute('aria-labelledby')).toBe(title.id);
    expect(dialog.querySelector('button[aria-label="common.close"]')).withContext('benannter Schließen-Knopf').not.toBeNull();
    expect(dialog.contains(document.activeElement)).withContext('Fokus liegt im Dialog').toBeTrue();

    const esc = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true });
    Object.defineProperty(esc, 'keyCode', { get: () => 27 });
    document.activeElement!.dispatchEvent(esc);
    await new Promise(r => setTimeout(r));
    expect(document.querySelector('.cdk-overlay-container [role="dialog"]')).withContext('nach Esc geschlossen').toBeNull();
  });
});
