import { Injectable, Injector } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { Router } from '@angular/router';

export interface AuthResponse {
  token: string;
  username: string;
  userId: number;
  isAdmin: boolean;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly apiUrl = '/api/auth';
  private currentUserSubject = new BehaviorSubject<AuthResponse | null>(this.getStoredUser());
  currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient, private router: Router, private injector: Injector) {}

  get isLoggedIn(): boolean {
    return !!this.currentUserSubject.value;
  }

  get token(): string | null {
    return this.currentUserSubject.value?.token ?? null;
  }

  get currentUser(): AuthResponse | null {
    return this.currentUserSubject.value;
  }

  get isAdmin(): boolean {
    return this.currentUserSubject.value?.isAdmin ?? false;
  }

  register(username: string, email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/register`, { username, email, password })
      .pipe(tap(res => this.storeUser(res)));
  }

  login(username: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/login`, { username, password })
      .pipe(tap(res => this.storeUser(res)));
  }

  logout(): void {
    localStorage.removeItem('rookhub_user');
    this.currentUserSubject.next(null);
    this.router.navigate(['/login']);
  }

  private storeUser(user: AuthResponse): void {
    localStorage.setItem('rookhub_user', JSON.stringify(user));
    this.currentUserSubject.next(user);
    this.claimAnonymousPuzzleSession();
    // Sync user preferences from server (overwrites localStorage)
    import('./preferences.service').then(m => {
      this.injector.get(m.PreferencesService).loadFromServer();
    });
  }

  private claimAnonymousPuzzleSession(): void {
    const sessionId = localStorage.getItem('rookhub_puzzle_session');
    if (!sessionId) return;
    // Lazy import to avoid circular dependency
    import('../features/puzzles/puzzle.service').then(m => {
      const puzzleService = this.injector.get(m.PuzzleService);
      puzzleService.claimSession().subscribe();
    });
    // Also claim endless puzzle progress
    import('../features/puzzles/endless-storage.service').then(m => {
      const endlessStorage = this.injector.get(m.EndlessStorageService);
      endlessStorage.claimEndlessSession().subscribe();
    });
  }

  private getStoredUser(): AuthResponse | null {
    try {
      const stored = localStorage.getItem('rookhub_user');
      if (!stored) return null;
      const user: AuthResponse = JSON.parse(stored);
      const base64Url = user.token.split('.')[1];
      const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
      const payload = JSON.parse(atob(base64));
      if (payload.exp && payload.exp * 1000 < Date.now()) {
        localStorage.removeItem('rookhub_user');
        return null;
      }
      return user;
    } catch {
      try { localStorage.removeItem('rookhub_user'); } catch { }
      return null;
    }
  }
}
