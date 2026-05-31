import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from './auth.service';
import { catchError, of } from 'rxjs';

const BOARD_THEME_KEY = 'rookhub_board_theme';
const PIECE_SET_KEY = 'rookhub_piece_set';
const PUZZLE_CONFIG_KEY = 'rookhub_puzzle_config';
const BOOK_PUZZLE_CONFIG_KEY = 'rookhub_book_puzzle_config';

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

  constructor(private http: HttpClient, private authService: AuthService) {
    this.loadFromLocalStorage();
  }

  /** Read all preferences from localStorage (synchronous, instant). */
  private loadFromLocalStorage(): void {
    try { this.boardTheme = localStorage.getItem(BOARD_THEME_KEY) || 'brown'; } catch {}
    try { this.pieceSet = localStorage.getItem(PIECE_SET_KEY) || 'cburnett'; } catch {}
    try {
      const raw = localStorage.getItem(PUZZLE_CONFIG_KEY);
      if (raw) {
        const saved = JSON.parse(raw);
        if (saved.stockfishDepth) this.stockfishDepth = this.clampDepth(saved.stockfishDepth);
        if (saved.difficulty) this.puzzleDifficulty = saved.difficulty;
      }
    } catch {}
    try {
      const raw = localStorage.getItem(BOOK_PUZZLE_CONFIG_KEY);
      if (raw) {
        const saved = JSON.parse(raw);
        if (saved.stockfishDepth) this.bookStockfishDepth = this.clampDepth(saved.stockfishDepth);
      }
    } catch {}
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

  setBookStockfishDepth(depth: number): void {
    this.bookStockfishDepth = this.clampDepth(depth);
    this.saveBookConfigLocal();
    this.saveToServer({ bookStockfishDepth: this.bookStockfishDepth });
  }

  private savePuzzleConfigLocal(): void {
    try {
      localStorage.setItem(PUZZLE_CONFIG_KEY, JSON.stringify({
        stockfishDepth: this.stockfishDepth,
        difficulty: this.puzzleDifficulty
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
