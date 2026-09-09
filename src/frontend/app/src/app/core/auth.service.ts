import { DestroyRef, Injectable, Injector, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { Router } from '@angular/router';
import { OfflineService } from './offline.service';

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

  /**
   * Ob der Injektor schon abgeraeumt ist. Gebraucht, weil mehrere Stellen hier einen DYNAMISCHEN
   * Import machen und den Dienst erst im `then` aus dem Injektor holen — bis dahin kann der
   * Injektor weg sein.
   */
  private destroyed = false;

  constructor(private http: HttpClient, private router: Router, private injector: Injector) {
    inject(DestroyRef).onDestroy(() => this.destroyed = true);
  }

  /**
   * Einen Dienst holen, der hinter einem dynamischen Import liegt — und dabei aushalten, dass der
   * Injektor in der Zwischenzeit abgeraeumt wurde.
   *
   * <p>Ein `import(...)` loest asynchron auf, und niemand bricht es ab. Meldet sich jemand an und
   * verlaesst die Seite sofort wieder, kommt das `then` NACH dem Abbau des Injektors an, und
   * `injector.get` wirft NG0205. Latent seit 0.413.0, an fuenf Stellen dieser Datei.</p>
   *
   * <p><b>Was hier NICHT der Beleg ist, obwohl es danach aussieht:</b> im Karma-Protokoll steht
   * NG0205 zu Dutzenden — auch in gruenen Laeufen, gezaehlt 72-mal in einem. Das ist der
   * TestBed-Abbau, kein Fehlschlag, und es hat mit dieser Reparatur nichts zu tun. Wer den Race
   * beheben will, sucht ihn im Browser (Anmelden, sofort weg), nicht im Testprotokoll.</p>
   *
   * <p>Bewusst kein `try/catch` um den ganzen Aufruf: das verschluckte auch echte Fehler AUS dem
   * geholten Dienst. Geprueft wird nur das eine, was hier schiefgehen kann.</p>
   */
  private withService<T>(get: () => T, use: (service: T) => void): void {
    if (this.destroyed) return;
    use(get());
  }

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

  /**
   * Effektive Permissions des aktuellen Tokens (RBAC), aus den `perm`-Claims des JWT dekodiert.
   * Mehrere Claims desselben Namens landen im JWT als Array, ein einzelner als String — beides wird
   * normalisiert. Admins tragen die Admin-Rolle separat (siehe `has`), daher hier ggf. leer.
   */
  get permissions(): ReadonlySet<string> {
    const token = this.token;
    if (!token) return new Set();
    try {
      const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const payload = JSON.parse(atob(base64));
      const p = payload['perm'];
      if (Array.isArray(p)) return new Set(p.map(String));
      if (typeof p === 'string') return new Set([p]);
      return new Set();
    } catch {
      return new Set();
    }
  }

  /** Darf der aktuelle Nutzer die Aktion? Admin erfüllt jede Permission (Superuser). */
  has(permission: string): boolean {
    return this.isAdmin || this.permissions.has(permission);
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
   * Uebernimmt eine anderswo entstandene Anmeldung als die eigene — heute der Sprung zwischen
   * RookHub und der Turnierseite (siehe `HandoffService`). Bewusst getrennt von `impersonate`:
   * hier wird nichts gesichert und nichts markiert, es ist eine ganz normale Anmeldung, die nur
   * nicht ueber die Anmeldemaske kam.
   */
  adoptSession(user: AuthResponse): void {
    localStorage.setItem('rookhub_user', JSON.stringify(user));
    this.currentUserSubject.next(user);
    this.loadPreferences();
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
    let admin: AuthResponse;
    try {
      admin = JSON.parse(stored);
    } catch {
      // Beschädigtes Admin-Backup: nicht in einem halben Zustand hängenbleiben —
      // Reste verwerfen und sauber ausloggen.
      localStorage.removeItem(this.adminBackupKey);
      this.logout();
      return;
    }
    // Reihenfolge: ERST die Admin-Sitzung zurückschreiben, DANN das Backup löschen. Vorher war es
    // umgekehrt — warf `setItem` (voller/gesperrter Speicher), war das Backup unwiederbringlich weg,
    // `rookhub_user` trug weiter das Impersonation-Token und das rote Banner verschwand: der Admin
    // arbeitete unbemerkt im fremden Konto weiter, ohne Rücksprung.
    try { localStorage.setItem('rookhub_user', stored); }
    catch {
      this.storageFull = true;
      // Backup NICHT löschen — der Rücksprung bleibt so nach einem Neuladen möglich.
      this.currentUserSubject.next(admin);
      this.loadPreferences();
      return;
    }
    localStorage.removeItem(this.adminBackupKey);
    this.currentUserSubject.next(admin);
    this.loadPreferences();
  }

  private loadPreferences(): void {
    import('./preferences.service').then(m =>
      this.withService(() => this.injector.get(m.PreferencesService), p => p.loadFromServer()));
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

  /** Passwort des eingeloggten Users ändern (aktuelles + neues Passwort). */
  /** Passwort ändern. Die Antwort trägt ein FRISCHES Token: der Server rotiert dabei den
   *  Security-Stamp und entwertet damit auch das Token DIESER Sitzung — ohne den Austausch flöge
   *  man eine Minute später kommentarlos raus (der Interceptor loggt bei 401 aus). Nur das Token
   *  wird ersetzt; `storeUser` würde Präferenzen neu laden und die anonyme Sitzung claimen, was
   *  hier nichts zu suchen hat. */
  changePassword(currentPassword: string, newPassword: string): Observable<AuthResponse> {
    return this.http.put<AuthResponse>(`${this.apiUrl}/change-password`, { currentPassword, newPassword })
      .pipe(tap(res => this.replaceToken(res)));
  }

  /** Gespeichertes Token gegen ein neues tauschen, ohne den restlichen Anmelde-Nachlauf. */
  private replaceToken(user: AuthResponse): void {
    try { localStorage.setItem('rookhub_user', JSON.stringify(user)); }
    catch { /* voller/gesperrter Speicher: das Token im Zustand hält die Sitzung bis zum Neuladen */ }
    this.currentUserSubject.next(user);
  }

  /** Wurde ein Schreibversuch in den localStorage abgelehnt (Quota voll / Privatmodus)? Die Sitzung
   *  läuft dann nur im Speicher: ein Neuladen der Seite loggt aus. Die Profilseite kann darauf
   *  hinweisen („Offline-Speicher voll — Caches leeren"), statt den Nutzer rätseln zu lassen. */
  storageFull = false;

  logout(): void {
    localStorage.removeItem('rookhub_user');
    localStorage.removeItem('rookhub_admin_user');
    // Geräte-lokale Offline-Inhalte (heruntergeladene Repertoires/Kurse, Kursliste, Tagespuzzle,
    // Pools) beim Abmelden löschen — sonst blieben sie für den NÄCHSTEN Nutzer desselben Geräts
    // les-/sichtbar. Die Offline-Schreib-Queue bleibt bewusst bestehen (sie ist user-gestempelt und
    // geht nur unter demselben Konto raus) → gemerkte Lösungen überstehen ein versehentliches Logout.
    // clearOnLogout (nicht clearAll): räumt zusätzlich die lokalen Nutzer-SPUREN ab. Der
    // Endless-Modus überträgt lokale Läufe beim ersten Öffnen ins Konto — auf einem geteilten Gerät
    // erbte der nächste Nutzer sonst Laufhistorie und Highscore des vorigen, sichtbar bis in die
    // Bestenliste; ebenso Kalkulations-Notizen, lokalen Kursfortschritt und den Menü-Snapshot.
    try { this.injector.get(OfflineService).clearOnLogout(); } catch { /* Storage/DI nicht verfügbar */ }
    // Die GETEILTE Anmeldung mit beenden: sie liegt als Cookie auf der Elterndomaene und ist der
    // einzige Teil dieser Sitzung, den localStorage.removeItem nicht erreicht. Bliebe sie stehen,
    // holte sich die Seite beim naechsten Aufruf genau die Anmeldung zurueck, die man gerade
    // beendet hat. Ohne Rueckmeldung abschicken — ein Abmelden darf an nichts haengen.
    this.http.post('/api/auth/session/end', {}).subscribe({ error: () => { /* egal */ } });
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
    // FALLE: `setItem` läuft im tap() des Login-Streams. Genau dieser Speicher wird von den
    // Offline-Caches absichtlich vollgeschrieben — ein QuotaExceededError riss damit den
    // Login-Stream, die Maske meldete „Login fehlgeschlagen" und die Sitzung war auch im Speicher
    // nicht gesetzt. Der Nutzer kam nicht mehr herein und erfuhr den echten Grund nie. Die Sitzung
    // gilt jetzt in jedem Fall (in-memory), der Speicherfehler ist nur ein Komfortverlust.
    try { localStorage.setItem('rookhub_user', JSON.stringify(user)); }
    catch { this.storageFull = true; }
    this.currentUserSubject.next(user);
    this.claimAnonymousPuzzleSession();
    this.consumeStashedDiscordLink();
    // Sync user preferences from server (overwrites localStorage)
    import('./preferences.service').then(m =>
      this.withService(() => this.injector.get(m.PreferencesService), p => p.loadFromServer()));
  }

  /**
   * Löst einen über einen Bot-Link (?dl=) vorgemerkten Discord-Token ein, sobald
   * sich ein anonymer User ein-/registriert — so wird die beim Klick hinterlegte
   * Discord-ID automatisch mit dem neuen Account verknüpft.
   */
  private consumeStashedDiscordLink(): void {
    import('./discord-link.service').then(m =>
      this.withService(() => this.injector.get(m.DiscordLinkService), d => d.consumeStashed()));
  }

  private claimAnonymousPuzzleSession(): void {
    const sessionId = localStorage.getItem('rookhub_puzzle_session');
    if (!sessionId) return;
    // Lazy import to avoid circular dependency
    import('../features/puzzles/puzzle.service').then(m =>
      this.withService(() => this.injector.get(m.PuzzleService), puzzleService => {
        puzzleService.claimSession().subscribe();
        puzzleService.claimBookPuzzleSession().subscribe();
      }));
    // Also claim endless puzzle progress
    import('../features/puzzles/endless-storage.service').then(m =>
      this.withService(() => this.injector.get(m.EndlessStorageService),
        endlessStorage => endlessStorage.claimEndlessSession().subscribe()));
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
