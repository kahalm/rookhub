import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../core/auth.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { Observable, of, catchError, map, tap } from 'rxjs';
import { PuzzleDto } from './puzzle.service';

export interface EndlessConfig {
  startElo: number;
  themes: string;
  fasttrackThreshold1?: number;
  fasttrackThreshold2?: number;
  stockfishDepth: number;
}

export interface EndlessSession {
  timestamp: number;
  config: EndlessConfig;
  totalSolved: number;
  maxRating: number;
  durationSeconds: number;
  mistakeAtRatings: number[];
}

/** Ein einzelnes Puzzle einer Session (mit Start-/Endzeit als Unix-Millis) — nur fürs serverseitige Logging. */
export interface SessionPuzzleLog {
  puzzleId: number;
  lichessId: string;
  rating: number;
  solved: boolean;
  startedAt: number;
  endedAt: number;
}

export interface EndlessSyncResponse {
  progress: EndlessProgressDto | null;
  sessions: EndlessSessionDto[];
}

export interface EndlessProgressDto {
  startElo: number;
  themes: string;
  fasttrackThreshold1?: number;
  fasttrackThreshold2?: number;
  stockfishDepth: number;
  highscore: number;
  activeGameState?: string;
  updatedAt: string;
}

export interface EndlessSessionDto {
  id: number;
  timestamp: number;
  totalSolved: number;
  maxRating: number;
  durationSeconds: number;
  configJson: string;
  mistakeAtRatings: string;
}

const CONFIG_KEY = 'rookhub_endless_config';
const HIGHSCORE_KEY = 'rookhub_endless_highscore';
const HISTORY_KEY = 'rookhub_endless_history';
const ACTIVE_GAME_KEY = 'rookhub_endless_active_game';
const SYNCED_KEY = 'rookhub_endless_synced';
const OFFLINE_POOL_KEY = 'rookhub_endless_offline_pool';
const CHAIN_TOKEN_KEY = 'rookhub_endless_chain_token';
const MAX_HISTORY_SESSIONS = 50;

@Injectable({ providedIn: 'root' })
export class EndlessStorageService {
  private readonly apiUrl = '/api/endless';
  private saveDebounceTimer: ReturnType<typeof setTimeout> | null = null;
  private lastSaveTime = 0;
  private readonly SAVE_DEBOUNCE_MS = 3000;
  /** Hoechster bekannter Highscore (Server + lokal) — es wird nie ein niedrigerer gesendet. */
  private highestKnownHighscore = 0;

  constructor(private http: HttpClient, private authService: AuthService, private offlineQueue: OfflineQueueService) {}

  // --- localStorage (cache/fallback) ---

  loadConfig(defaults: EndlessConfig): EndlessConfig {
    let config = { ...defaults };
    try {
      const raw = localStorage.getItem(CONFIG_KEY);
      if (raw) {
        const saved = JSON.parse(raw);
        delete saved.rangeWidth;
        config = { ...config, ...saved };
      }
    } catch {}
    if (!config.stockfishDepth || config.stockfishDepth < 1) config.stockfishDepth = 16;
    if (config.stockfishDepth > 24) config.stockfishDepth = 24;
    if (config.fasttrackThreshold1 != null && config.fasttrackThreshold1 <= config.startElo) {
      config.fasttrackThreshold1 = undefined;
    }
    if (config.fasttrackThreshold2 != null && config.fasttrackThreshold2 <= config.startElo) {
      config.fasttrackThreshold2 = undefined;
    }
    return config;
  }

  saveConfig(config: EndlessConfig): void {
    try { localStorage.setItem(CONFIG_KEY, JSON.stringify(config)); } catch {}
  }

  loadHighscore(): number {
    try {
      const raw = localStorage.getItem(HIGHSCORE_KEY);
      if (raw) return parseInt(raw, 10) || 0;
    } catch {}
    return 0;
  }

  checkHighscore(maxRatingReached: number, currentHighscore: number): { highscore: number; isNew: boolean } {
    if (maxRatingReached > currentHighscore) {
      try { localStorage.setItem(HIGHSCORE_KEY, String(maxRatingReached)); } catch {}
      return { highscore: maxRatingReached, isNew: true };
    }
    return { highscore: currentHighscore, isNew: false };
  }

  loadSessionHistory(): EndlessSession[] {
    try {
      const raw = localStorage.getItem(HISTORY_KEY);
      if (raw) return JSON.parse(raw) || [];
    } catch {}
    return [];
  }

  saveSessionHistory(history: EndlessSession[]): void {
    try { localStorage.setItem(HISTORY_KEY, JSON.stringify(history)); } catch {}
  }

  // --- Offline-Puzzle-Pool (für Endless ohne Internet) ---
  saveOfflinePool(puzzles: PuzzleDto[]): void {
    try { localStorage.setItem(OFFLINE_POOL_KEY, JSON.stringify(puzzles ?? [])); } catch {}
  }

  loadOfflinePool(): PuzzleDto[] {
    try {
      const raw = localStorage.getItem(OFFLINE_POOL_KEY);
      if (raw) return JSON.parse(raw) || [];
    } catch {}
    return [];
  }

  clearOfflinePool(): void {
    try { localStorage.removeItem(OFFLINE_POOL_KEY); } catch {}
  }

  /**
   * Token der lokal gecachten Kette (= Run-Start-Zeitstempel). Wird beim Fortsetzen mit dem
   * Token im Spielstand verglichen: stimmt es überein, ist der lokale Pool DIESE Run-Kette
   * (Refresh auf demselben Gerät → exakt dasselbe Puzzle). Sonst (anderes Gerät / überschrieben
   * durch einen Config-Prefetch) wird die Kette neu generiert. `0` = kein gültiger Ketten-Cache.
   */
  saveChainToken(token: number): void {
    try { localStorage.setItem(CHAIN_TOKEN_KEY, String(token || 0)); } catch {}
  }

  loadChainToken(): number {
    try {
      const raw = localStorage.getItem(CHAIN_TOKEN_KEY);
      if (raw) return parseInt(raw, 10) || 0;
    } catch {}
    return 0;
  }

  recordSession(history: EndlessSession[], session: EndlessSession): EndlessSession[] {
    const updated = [...history, session];
    const trimmed = updated.length > MAX_HISTORY_SESSIONS
      ? updated.slice(-MAX_HISTORY_SESSIONS)
      : updated;
    this.saveSessionHistory(trimmed);
    return trimmed;
  }

  saveActiveGameLocal(state: object | null): void {
    try {
      if (state) {
        localStorage.setItem(ACTIVE_GAME_KEY, JSON.stringify(state));
      } else {
        localStorage.removeItem(ACTIVE_GAME_KEY);
      }
    } catch {}
  }

  loadActiveGameLocal(): object | null {
    try {
      const raw = localStorage.getItem(ACTIVE_GAME_KEY);
      if (raw) return JSON.parse(raw);
    } catch {}
    return null;
  }

  // --- Server Sync ---

  private getSessionId(): string | null {
    try { return localStorage.getItem('rookhub_puzzle_session'); } catch { return null; }
  }

  loadFromServer(): Observable<EndlessSyncResponse | null> {
    // Server-Highscore merken, damit ein spaeterer Save ihn nicht unterbietet.
    const remember = tap((res: EndlessSyncResponse | null) => {
      const hs = res?.progress?.highscore;
      if (typeof hs === 'number') this.highestKnownHighscore = Math.max(this.highestKnownHighscore, hs);
    });
    if (this.authService.isLoggedIn) {
      return this.http.get<EndlessSyncResponse>(`${this.apiUrl}/progress`).pipe(
        remember, catchError(() => of(null))
      );
    }
    const sessionId = this.getSessionId();
    if (sessionId) {
      return this.http.get<EndlessSyncResponse>(`${this.apiUrl}/progress/anonymous`, {
        params: { sessionId }
      }).pipe(remember, catchError(() => of(null)));
    }
    return of(null);
  }

  saveProgressToServer(config: EndlessConfig, highscore: number, activeGameState: object | null): void {
    const now = Date.now();
    if (now - this.lastSaveTime < this.SAVE_DEBOUNCE_MS) {
      if (this.saveDebounceTimer) clearTimeout(this.saveDebounceTimer);
      this.saveDebounceTimer = setTimeout(() => {
        this.doSaveProgress(config, highscore, activeGameState);
      }, this.SAVE_DEBOUNCE_MS);
      return;
    }
    this.doSaveProgress(config, highscore, activeGameState);
  }

  private doSaveProgress(config: EndlessConfig, highscore: number, activeGameState: object | null): void {
    this.lastSaveTime = Date.now();
    // Highscore nie absenken: ein parallel (anderes Geraet/Tab) hoeher gemeldeter
    // Wert darf nicht durch einen lokal niedrigeren ueberschrieben werden.
    const safeHighscore = Math.max(highscore, this.highestKnownHighscore);
    this.highestKnownHighscore = safeHighscore;
    const body: any = {
      startElo: config.startElo,
      themes: config.themes || '',
      fasttrackThreshold1: config.fasttrackThreshold1 ?? null,
      fasttrackThreshold2: config.fasttrackThreshold2 ?? null,
      stockfishDepth: config.stockfishDepth,
      highscore: safeHighscore,
      activeGameState: activeGameState ? JSON.stringify(activeGameState) : null
    };

    if (this.authService.isLoggedIn) {
      this.http.put(`${this.apiUrl}/progress`, body).pipe(catchError(() => of(null))).subscribe();
    } else {
      const sessionId = this.getSessionId();
      if (sessionId) {
        this.http.put(`${this.apiUrl}/progress/anonymous`, { ...body, sessionId })
          .pipe(catchError(() => of(null))).subscribe();
      }
    }
  }

  saveProgressImmediate(config: EndlessConfig, highscore: number, activeGameState: object | null): void {
    if (this.saveDebounceTimer) clearTimeout(this.saveDebounceTimer);
    this.doSaveProgress(config, highscore, activeGameState);
  }

  recordSessionToServer(session: EndlessSession, puzzles: SessionPuzzleLog[] = []): Observable<number | null> {
    const body: any = {
      timestamp: session.timestamp,
      totalSolved: session.totalSolved,
      maxRating: session.maxRating,
      durationSeconds: session.durationSeconds,
      configJson: JSON.stringify(session.config),
      mistakeAtRatings: session.mistakeAtRatings.join(','),
      // Nur fürs serverseitige Logging (Start-/Lösungszeit je Puzzle), nicht persistiert.
      puzzles: (puzzles ?? []).map(p => ({
        puzzleId: p.puzzleId,
        lichessId: p.lichessId,
        rating: p.rating,
        solved: p.solved,
        startedAt: p.startedAt,
        endedAt: p.endedAt,
      }))
    };

    if (this.authService.isLoggedIn) {
      const url = `${this.apiUrl}/sessions`;
      if (!navigator.onLine) { this.offlineQueue.enqueue('POST', url, body); return of(null); }
      return this.http.post<any>(url, body).pipe(
        map(res => res?.id ?? null),
        catchError(() => { this.offlineQueue.enqueue('POST', url, body); return of(null); })
      );
    } else {
      const sessionId = this.getSessionId();
      if (sessionId) {
        const url = `${this.apiUrl}/sessions/anonymous`;
        const anonBody = { ...body, sessionId };
        if (!navigator.onLine) { this.offlineQueue.enqueue('POST', url, anonBody); return of(null); }
        return this.http.post<any>(url, anonBody).pipe(
          map(res => res?.id ?? null),
          catchError(() => { this.offlineQueue.enqueue('POST', url, anonBody); return of(null); })
        );
      }
      return of(null);
    }
  }

  archiveSession(sessionId: number): Observable<any> {
    return this.http.post(`${this.apiUrl}/archive`, { sessionIds: [sessionId], archive: true }).pipe(
      catchError(() => of(null))
    );
  }

  bulkImportSessionsToServer(sessions: EndlessSession[]): void {
    if (sessions.length === 0) return;
    const mapped = sessions.map(s => ({
      timestamp: s.timestamp,
      totalSolved: s.totalSolved,
      maxRating: s.maxRating,
      durationSeconds: s.durationSeconds,
      configJson: JSON.stringify(s.config),
      mistakeAtRatings: s.mistakeAtRatings.join(',')
    }));

    if (this.authService.isLoggedIn) {
      this.http.post(`${this.apiUrl}/sessions/bulk`, { sessions: mapped })
        .pipe(catchError(() => of(null))).subscribe();
    } else {
      const sessionId = this.getSessionId();
      if (sessionId) {
        this.http.post(`${this.apiUrl}/sessions/bulk/anonymous`, { sessionId, sessions: mapped })
          .pipe(catchError(() => of(null))).subscribe();
      }
    }
  }

  claimEndlessSession(): Observable<any> {
    const sessionId = this.getSessionId();
    if (!sessionId) return of(null);
    return this.http.post(`${this.apiUrl}/claim-session`, { anonymousSessionId: sessionId })
      .pipe(catchError(() => of(null)));
  }

  // --- Merge helpers ---

  mergeServerData(
    localConfig: EndlessConfig,
    localHighscore: number,
    localHistory: EndlessSession[],
    serverData: EndlessSyncResponse
  ): { config: EndlessConfig; highscore: number; history: EndlessSession[] } {
    let config = localConfig;
    let highscore = localHighscore;
    let history = localHistory;

    if (serverData.progress) {
      const sp = serverData.progress;
      // Server wins if it has data
      config = {
        startElo: sp.startElo,
        themes: sp.themes,
        fasttrackThreshold1: sp.fasttrackThreshold1 ?? undefined,
        fasttrackThreshold2: sp.fasttrackThreshold2 ?? undefined,
        stockfishDepth: sp.stockfishDepth
      };
      highscore = Math.max(localHighscore, sp.highscore);
    }

    if (serverData.sessions.length > 0) {
      history = serverData.sessions.map(s => this.mapServerSession(s));
    }

    // Persist merged data locally
    this.saveConfig(config);
    try { localStorage.setItem(HIGHSCORE_KEY, String(highscore)); } catch {}
    this.saveSessionHistory(history);

    return { config, highscore, history };
  }

  /** Migrations-Flag pro Identitaet (User-Id bzw. anonyme Session), nicht global. */
  private syncedKey(): string {
    if (this.authService.isLoggedIn) {
      const uid = this.authService.currentUser?.userId;
      return uid != null ? `${SYNCED_KEY}:u${uid}` : SYNCED_KEY;
    }
    const sid = this.getSessionId();
    return sid ? `${SYNCED_KEY}:a${sid}` : SYNCED_KEY;
  }

  migrateLocalToServer(config: EndlessConfig, highscore: number, history: EndlessSession[]): void {
    const key = this.syncedKey();
    try {
      // Legacy: frueher existierte nur ein globaler Flag. Ist er gesetzt und ein User
      // eingeloggt, uebernehmen wir ihn fuer DIESE Identitaet (keine Doppel-Migration)
      // und entfernen den globalen Flag, damit andere Identitaeten kuenftig migrieren.
      if (key !== SYNCED_KEY && localStorage.getItem(SYNCED_KEY)) {
        localStorage.setItem(key, '1');
        localStorage.removeItem(SYNCED_KEY);
        return;
      }
      if (localStorage.getItem(key)) return;
      localStorage.setItem(key, '1');
    } catch {}

    // Push config + highscore
    this.saveProgressImmediate(config, highscore, null);

    // Push existing sessions
    if (history.length > 0) {
      this.bulkImportSessionsToServer(history);
    }
  }

  private mapServerSession(s: EndlessSessionDto): EndlessSession {
    let config: EndlessConfig = { startElo: 700, themes: '', stockfishDepth: 16 };
    try { config = JSON.parse(s.configJson); } catch {}
    return {
      timestamp: s.timestamp,
      config,
      totalSolved: s.totalSolved,
      maxRating: s.maxRating,
      durationSeconds: s.durationSeconds,
      mistakeAtRatings: s.mistakeAtRatings ? s.mistakeAtRatings.split(',').map(Number).filter(n => !isNaN(n)) : []
    };
  }
}
