import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from './auth.service';
import { catchError, of } from 'rxjs';

import { ThemeMode } from '../features/puzzles/board-theme.util';

const BOARD_THEME_KEY = 'rookhub_board_theme';
const PIECE_SET_KEY = 'rookhub_piece_set';
const PUZZLE_CONFIG_KEY = 'rookhub_puzzle_config';
const BOOK_PUZZLE_CONFIG_KEY = 'rookhub_book_puzzle_config';
const THEME_MODE_KEY = 'rookhub_theme_mode';
const VISUALIZATION_KEY = 'rookhub_visualization';
const VIZ_ARROW_KEY = 'rookhub_viz_arrow';
const OFFPATH_WARN_KEY = 'rookhub_offpath_warn_moves';
const EN_PASSANT_FORCED_KEY = 'rookhub_en_passant_forced';

interface ProfilePreferences {
  boardTheme: string | null;
  pieceSet: string | null;
  stockfishDepth: number | null;
  puzzleDifficulty: string | null;
  bookStockfishDepth: number | null;
}

@Injectable({ providedIn: 'root' })
export class PreferencesService {
  boardTheme = 'brown';
  pieceSet = 'cburnett';
  stockfishDepth = 16;
  puzzleDifficulty = 'normal';
  bookStockfishDepth = 16;
  themeMode: ThemeMode = 'fixed';
  /** Visualisierungs-Level 0-4 (nur lokal, geräteabhängig — kein Server-Sync). */
  visualization = 1;
  /** Standard-Puzzle: „5 schwächste Themen trainieren" (nur lokal, geräteabhängig). */
  puzzleWorstTags = false;
  /** Gegnerzug-Pfeil im Viz-Modus anzeigen (nur lokal). */
  vizArrow = true;
  /** Nach wie vielen off-path-Zügen (gegen Stockfish) gewarnt wird, wenn die Eval nicht mind. +2
   *  für den Spieler ist. 0 = nie warnen. Default 3. Nur lokal (geräteabhängig). */
  offPathWarnMoves = 3;
  /** Anarchy/Crazy-Brett: „en passant forciert" (wenn möglich, ist e.p. Pflicht). Default an;
   *  in den Einstellungen abwählbar. Nur lokal (geräteabhängig). */
  enPassantForced = true;

  constructor(private http: HttpClient, private authService: AuthService) {
    this.loadFromLocalStorage();
  }

  /** Read all preferences from localStorage (synchronous, instant). */
  private loadFromLocalStorage(): void {
    try { this.boardTheme = localStorage.getItem(BOARD_THEME_KEY) || 'brown'; } catch {}
    try { this.pieceSet = localStorage.getItem(PIECE_SET_KEY) || 'cburnett'; } catch {}
    try {
      const tm = localStorage.getItem(THEME_MODE_KEY);
      if (tm === 'fixed' || tm === 'random' || tm === 'crazy') this.themeMode = tm;
    } catch {}
    try {
      const raw = localStorage.getItem(PUZZLE_CONFIG_KEY);
      if (raw) {
        const saved = JSON.parse(raw);
        if (saved.stockfishDepth) this.stockfishDepth = this.clampDepth(saved.stockfishDepth);
        if (saved.difficulty) this.puzzleDifficulty = saved.difficulty;
        if (typeof saved.worstTags === 'boolean') this.puzzleWorstTags = saved.worstTags;
      }
    } catch {}
    try {
      const raw = localStorage.getItem(BOOK_PUZZLE_CONFIG_KEY);
      if (raw) {
        const saved = JSON.parse(raw);
        if (saved.stockfishDepth) this.bookStockfishDepth = this.clampDepth(saved.stockfishDepth);
      }
    } catch {}
    try {
      const viz = localStorage.getItem(VISUALIZATION_KEY);
      if (viz !== null) {
        // Backward-compat: '0'→0, '1'→1, other→parseInt clamped 0-4
        const n = parseInt(viz, 10);
        this.visualization = isNaN(n) ? 1 : Math.max(0, Math.min(4, n));
      }
    } catch {}
    try {
      const arrow = localStorage.getItem(VIZ_ARROW_KEY);
      if (arrow !== null) this.vizArrow = arrow !== 'false';
    } catch {}
    try {
      const w = localStorage.getItem(OFFPATH_WARN_KEY);
      if (w !== null) { const n = parseInt(w, 10); if (!isNaN(n)) this.offPathWarnMoves = Math.max(0, Math.min(20, n)); }
    } catch {}
    try {
      const ep = localStorage.getItem(EN_PASSANT_FORCED_KEY);
      if (ep !== null) this.enPassantForced = ep !== 'false';
    } catch {}
  }

  setOffPathWarnMoves(n: number): void {
    this.offPathWarnMoves = Math.max(0, Math.min(20, Math.round(n) || 0));
    try { localStorage.setItem(OFFPATH_WARN_KEY, String(this.offPathWarnMoves)); } catch {}
  }

  setEnPassantForced(val: boolean): void {
    this.enPassantForced = val;
    try { localStorage.setItem(EN_PASSANT_FORCED_KEY, String(val)); } catch {}
  }

  setVisualization(level: number): void {
    this.visualization = Math.max(0, Math.min(4, level));
    try { localStorage.setItem(VISUALIZATION_KEY, String(this.visualization)); } catch {}
  }

  setVizArrow(val: boolean): void {
    this.vizArrow = val;
    try { localStorage.setItem(VIZ_ARROW_KEY, String(val)); } catch {}
  }

  /** Fetch preferences from server profile and overwrite localStorage. Called after login. */
  loadFromServer(): void {
    if (!this.authService.isLoggedIn) return;
    this.http.get<ProfilePreferences>('/api/profile').pipe(
      catchError(() => of(null))
    ).subscribe(profile => {
      if (!profile) return;
      if (profile.boardTheme) {
        this.boardTheme = profile.boardTheme;
        try { localStorage.setItem(BOARD_THEME_KEY, profile.boardTheme); } catch {}
      }
      if (profile.pieceSet) {
        this.pieceSet = profile.pieceSet;
        try { localStorage.setItem(PIECE_SET_KEY, profile.pieceSet); } catch {}
      }
      if (profile.stockfishDepth != null) {
        this.stockfishDepth = this.clampDepth(profile.stockfishDepth);
        this.savePuzzleConfigLocal();
      }
      if (profile.puzzleDifficulty) {
        this.puzzleDifficulty = profile.puzzleDifficulty;
        this.savePuzzleConfigLocal();
      }
      if (profile.bookStockfishDepth != null) {
        this.bookStockfishDepth = this.clampDepth(profile.bookStockfishDepth);
        this.saveBookConfigLocal();
      }
    });
  }

  setBoardTheme(theme: string): void {
    this.boardTheme = theme;
    try { localStorage.setItem(BOARD_THEME_KEY, theme); } catch {}
    this.saveToServer({ boardTheme: theme });
  }

  setPieceSet(set: string): void {
    this.pieceSet = set;
    try { localStorage.setItem(PIECE_SET_KEY, set); } catch {}
    this.saveToServer({ pieceSet: set });
  }

  setThemeMode(mode: ThemeMode): void {
    this.themeMode = mode;
    try { localStorage.setItem(THEME_MODE_KEY, mode); } catch {}
  }

  setStockfishDepth(depth: number): void {
    this.stockfishDepth = this.clampDepth(depth);
    this.savePuzzleConfigLocal();
    this.saveToServer({ stockfishDepth: this.stockfishDepth });
  }

  setPuzzleDifficulty(difficulty: string): void {
    this.puzzleDifficulty = difficulty;
    this.savePuzzleConfigLocal();
    this.saveToServer({ puzzleDifficulty: difficulty });
  }

  setPuzzleWorstTags(enabled: boolean): void {
    this.puzzleWorstTags = enabled;
    this.savePuzzleConfigLocal();
  }

  setBookStockfishDepth(depth: number): void {
    this.bookStockfishDepth = this.clampDepth(depth);
    this.saveBookConfigLocal();
    this.saveToServer({ bookStockfishDepth: this.bookStockfishDepth });
  }

  private savePuzzleConfigLocal(): void {
    try {
      localStorage.setItem(PUZZLE_CONFIG_KEY, JSON.stringify({
        stockfishDepth: this.stockfishDepth,
        difficulty: this.puzzleDifficulty,
        worstTags: this.puzzleWorstTags
      }));
    } catch {}
  }

  private saveBookConfigLocal(): void {
    try {
      localStorage.setItem(BOOK_PUZZLE_CONFIG_KEY, JSON.stringify({
        stockfishDepth: this.bookStockfishDepth
      }));
    } catch {}
  }

  /** Fire-and-forget PUT to server (only when logged in). */
  private saveToServer(partial: Partial<ProfilePreferences>): void {
    if (!this.authService.isLoggedIn) return;
    this.http.put('/api/profile', partial).pipe(
      catchError(() => of(null))
    ).subscribe();
  }

  private clampDepth(d: number): number {
    return Math.max(1, Math.min(24, d));
  }
}
