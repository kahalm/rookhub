import { Injectable, Injector } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { Router } from '@angular/router';

export interface AuthResponse {
  token: string;
  username: string;
  userId: number;
  isAdmin: boolean;
  /** Gesetzt, wenn dieses Token via Admin-„Als Nutzer einsteigen" erzeugt wurde. */
  impersonating?: boolean;
  /** Benutzername des Admins, der eingestiegen ist (nur bei impersonating). */
  impersonatorUsername?: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly apiUrl = '/api/auth';
  private currentUserSubject = new BehaviorSubject<AuthResponse | null>(this.getStoredUser());
  currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient, private router: Router, private injector: Injector) {}

  get isLoggedIn(): boolean {
    return this.getValidUser() !== null;
  }

  get token(): string | null {
    return this.getValidUser()?.token ?? null;
  }

  get currentUser(): AuthResponse | null {
    return this.getValidUser();
  }

  get isAdmin(): boolean {
    return this.getValidUser()?.isAdmin ?? false;
  }

  private readonly adminBackupKey = 'rookhub_admin_user';

  /** Läuft gerade eine Admin-Impersonation? */
  get isImpersonating(): boolean {
    return !!this.getValidUser()?.impersonating && !!localStorage.getItem(this.adminBackupKey);
  }

  /** Benutzername des Admins, der eingestiegen ist (für das Banner). */
  get impersonatorUsername(): string | null {
    return this.getValidUser()?.impersonatorUsername ?? null;
  }

  /**
   * „Als Nutzer einsteigen": sichert die aktuelle (Admin-)Session und übernimmt das
   * vom Server gelieferte Impersonation-Token. Rücksprung via {@link stopImpersonation}.
   */
  impersonate(target: AuthResponse): void {
    const admin = this.currentUserSubject.value;
    // Nur sichern, wenn wir nicht ohnehin schon in einer Impersonation stecken.
    if (admin && !admin.impersonating) {
      localStorage.setItem(this.adminBackupKey, JSON.stringify(admin));
    }
    const user: AuthResponse = { ...target, impersonating: true };
    localStorage.setItem('rookhub_user', JSON.stringify(user));
    this.currentUserSubject.next(user);
    this.loadPreferences();
  }

  /** Impersonation beenden und zur gesicherten Admin-Session zurückkehren. */
  stopImpersonation(): void {
    const stored = localStorage.getItem(this.adminBackupKey);
    if (!stored) return;
    localStorage.removeItem(this.adminBackupKey);
    localStorage.setItem('rookhub_user', stored);
    this.currentUserSubject.next(JSON.parse(stored));
    this.loadPreferences();
  }

  private loadPreferences(): void {
    import('./preferences.service').then(m => {
      this.injector.get(m.PreferencesService).loadFromServer();
    });
  }

  /**
   * Liefert den aktuellen User, loggt aber bei abgelaufenem Token automatisch
   * aus — eine abgelaufene Session gilt damit sofort als ausgeloggt, nicht erst
   * nach dem naechsten 401 vom Server.
   */
  private getValidUser(): AuthResponse | null {
    const user = this.currentUserSubject.value;
    if (user && this.isTokenExpired(user.token)) {
      localStorage.removeItem('rookhub_user');
      this.currentUserSubject.next(null);
      return null;
    }
    return user;
  }

  private isTokenExpired(token: string): boolean {
    try {
      const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const payload = JSON.parse(atob(base64));
      return !!payload.exp && payload.exp * 1000 < Date.now();
    } catch {
      return true; // unparsebares Token -> als abgelaufen behandeln
    }
  }

  register(username: string, email: string | null, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/register`, { username, email, password })
      .pipe(tap(res => this.storeUser(res)));
  }

  login(username: string, password: string, rememberMe = false): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/login`, { username, password, rememberMe })
      .pipe(tap(res => this.storeUser(res)));
  }

  /**
   * „Passwort vergessen", Schritt 1: fordert einen Reset-Link per E-Mail an. Der Server
   * antwortet aus Datenschutzgründen immer mit Erfolg — egal ob die Adresse existiert.
   */
  forgotPassword(email: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/forgot-password`, { email });
  }

  /** „Passwort vergessen", Schritt 2: setzt das neue Passwort mit dem Token aus der E-Mail. */
  resetPassword(token: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/reset-password`, { token, newPassword });
  }

  logout(): void {
    localStorage.removeItem('rookhub_user');
    localStorage.removeItem('rookhub_admin_user');
    this.currentUserSubject.next(null);
    this.router.navigate(['/login']);
  }

  /**
   * Löscht den eigenen Account (DSGVO): die Identität/PII wird serverseitig anonymisiert,
   * die Solve-Statistik bleibt anonym erhalten. Verlangt das aktuelle Passwort. Bei Erfolg
   * wird lokal ausgeloggt.
   */
  deleteAccount(password: string): Observable<void> {
    return this.http.delete<void>('/api/profile/account', { body: { password } })
      .pipe(tap(() => this.logout()));
  }

  private storeUser(user: AuthResponse): void {
    localStorage.setItem('rookhub_user', JSON.stringify(user));
    this.currentUserSubject.next(user);
    this.claimAnonymousPuzzleSession();
    this.consumeStashedDiscordLink();
    // Sync user preferences from server (overwrites localStorage)
    import('./preferences.service').then(m => {
      this.injector.get(m.PreferencesService).loadFromServer();
    });
  }

  /**
   * Löst einen über einen Bot-Link (?dl=) vorgemerkten Discord-Token ein, sobald
   * sich ein anonymer User ein-/registriert — so wird die beim Klick hinterlegte
   * Discord-ID automatisch mit dem neuen Account verknüpft.
   */
  private consumeStashedDiscordLink(): void {
    import('./discord-link.service').then(m => {
      this.injector.get(m.DiscordLinkService).consumeStashed();
    });
  }

  private claimAnonymousPuzzleSession(): void {
    const sessionId = localStorage.getItem('rookhub_puzzle_session');
    if (!sessionId) return;
    // Lazy import to avoid circular dependency
    import('../features/puzzles/puzzle.service').then(m => {
      const puzzleService = this.injector.get(m.PuzzleService);
      puzzleService.claimSession().subscribe();
      puzzleService.claimBookPuzzleSession().subscribe();
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
      if (this.isTokenExpired(user.token)) {
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
