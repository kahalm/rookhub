import { DestroyRef, Injectable, Injector, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, firstValueFrom, tap } from 'rxjs';
import { Router } from '@angular/router';
import { AuthResponse } from './auth-response';
import { SessionStore } from './session-store';
import { PermissionService } from './permission.service';
import { ImpersonationService } from './impersonation.service';
import { LoginFollowUpService } from './login-follow-up.service';
import { localStore, removeKey } from './local-json-store';
import { PushService } from './push.service';

export type { AuthResponse } from './auth-response';

/**
 * Anmeldung der Oberfläche — die EINE Fassade, die alle fünf Oberflächen nutzen (Oberfläche unverändert seit vor der
 * Zerlegung Z34). Dahinter: {@link SessionStore} (Token, Speicher, Ablauf, lokales Aufräumen),
 * {@link PermissionService} (`has`, Live-Rechte), {@link ImpersonationService} („Als Nutzer einsteigen") und
 * {@link LoginFollowUpService} (Nachlauf nach dem Anmelden). Hier bleiben die HTTP-Abläufe (Anmelden, Registrieren,
 * Passwort, Abmelden mit dem Ende der geteilten Anmeldung) und der Abgleich mit anderen Tabs.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly apiUrl = '/api/auth';
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = inject(SessionStore);
  private readonly perms = inject(PermissionService);
  private readonly impersonation = inject(ImpersonationService);
  private readonly followUp = inject(LoginFollowUpService);
  private readonly injector = inject(Injector);
  currentUser$ = this.session.user$;

  constructor() {
    const destroyRef = inject(DestroyRef);
    // Ein Abmelden ohne Netz holt das Ende der geteilten Anmeldung nach, sobald das Netz zurueck ist
    // (siehe sessionEndPending) — nicht erst beim naechsten Start.
    if (typeof window !== 'undefined') {
      const onOnline = () => { if (this.sessionEndPending && !this.isLoggedIn) void this.flushSessionEnd(); };
      window.addEventListener('online', onOnline);
      destroyRef.onDestroy(() => window.removeEventListener('online', onOnline));
      // Anmelden, Abmelden und Impersonation in einem ANDEREN Tab gelten hier mit (siehe followOtherTab).
      const onStorage = (e: StorageEvent) => this.followOtherTab(e);
      window.addEventListener('storage', onStorage);
      destroyRef.onDestroy(() => window.removeEventListener('storage', onStorage));
    }
    // Beim Start schon abgelaufen: hier beenden (wie einen Ablauf mitten in der Sitzung) — das Aufraeumen braucht
    // den Injektor, und der ist erst nach dem Aufbau der Felder vollstaendig.
    this.session.validUser();
  }

  get isLoggedIn(): boolean {
    return this.session.validUser() !== null;
  }

  get token(): string | null {
    return this.session.validUser()?.token ?? null;
  }

  get currentUser(): AuthResponse | null {
    return this.session.validUser();
  }

  get isAdmin(): boolean {
    return this.perms.isAdmin;
  }

  /** Effektive Permissions des aktuellen Tokens (siehe {@link PermissionService.permissions}). */
  get permissions(): ReadonlySet<string> {
    return this.perms.permissions;
  }

  /** Darf der aktuelle Nutzer die Aktion? Admin erfüllt jede Permission (Superuser). Live-Stand vor Token-Claims. */
  has(permission: string): boolean {
    return this.perms.has(permission);
  }

  /** Ist der Nutzer JETZT Admin? (siehe {@link PermissionService.isAdminLive}) */
  get isAdminLive(): boolean {
    return this.perms.isAdminLive;
  }

  /** Den Live-Stand der Rechte holen. Still bei Fehlern (offline, Server weg) — dann bleibt der bisherige Stand. */
  refreshPermissions(): Promise<void> {
    return this.perms.refresh();
  }

  /** Läuft gerade eine Admin-Impersonation? Allein das Token dieses Tabs entscheidet (Codereview F1-005). */
  get isImpersonating(): boolean {
    return this.impersonation.isImpersonating;
  }

  /** Benutzername des Admins, der eingestiegen ist (für das Banner). */
  get impersonatorUsername(): string | null {
    return this.impersonation.impersonatorUsername;
  }

  /**
   * Uebernimmt eine anderswo entstandene Anmeldung als die eigene — heute der Sprung zwischen
   * RookHub und der Turnierseite (siehe `HandoffService`). Bewusst getrennt von `impersonate`:
   * hier wird nichts gesichert und nichts markiert, es ist eine ganz normale Anmeldung, die nur
   * nicht ueber die Anmeldemaske kam. Ob sie an der geteilten Anmeldung haengt (`adopted`),
   * entscheidet der Aufrufer.
   */
  adoptSession(user: AuthResponse): void {
    this.session.setSessionEndPending(false);   // neue Anmeldung: ein altes ausstehendes Abmelden gilt nicht mehr
    this.session.persist(user);
    this.session.set(user);
    this.followUp.loadPreferences();
  }

  /**
   * Wechselt auf die GETEILTE Anmeldung eines ANDEREN Kontos — der Abgleich einer uebernommenen
   * Anmeldung hat festgestellt, dass sich dort inzwischen jemand anderes angemeldet hat (siehe
   * `HandoffService.verifyAdoptedSession`).
   *
   * <p>Das ist ein Nutzerwechsel am selben Geraet und raeumt deshalb lokal auf wie {@link logout}
   * (Offline-Inhalte, Nutzer-Spuren, Admin-Sicherung). Ohne das erbte das neue Konto Kursliste,
   * heruntergeladene Kurse und Repertoires, Kalkulations-Notizen und den Menue-Snapshot des vorigen —
   * und der Endless-Modus uebertrug dessen lokale Laeufe samt Highscore ins neue Konto.</p>
   *
   * <p>Bewusst OHNE `session/end`: das Cookie gehoert jetzt dem neuen Konto, das Abmelden loeschte
   * es gleich mit. Und ohne Navigation — wohin es danach geht, entscheidet der Aufrufer.</p>
   */
  switchToSharedSession(user: AuthResponse): void {
    this.session.clearLocalTraces();
    this.adoptSession({ ...user, adopted: true });
  }

  /**
   * „Als Nutzer einsteigen": sichert die aktuelle (Admin-)Session und übernimmt das
   * vom Server gelieferte Impersonation-Token. Rücksprung via {@link stopImpersonation}.
   *
   * @returns `false`, wenn die Admin-Sicherung auch nach dem Räumen der Offline-Caches nicht geschrieben werden
   *          konnte (Speicher gesperrt/voll) — dann bleibt alles beim Alten: ohne Sicherung gäbe es keinen
   *          Rücksprung, der Ausstieg meldete ab. Jeder Aufrufer MUSS das auswerten (sonst „Eingestiegen als X",
   *          obwohl die Sitzung die des Admins bleibt).
   */
  impersonate(target: AuthResponse): boolean {
    if (!this.impersonation.start(target)) return false;
    this.followUp.loadPreferences();
    return true;
  }

  /** Impersonation beenden und zur gesicherten Admin-Session zurückkehren (ohne brauchbare Sicherung: abmelden). */
  stopImpersonation(): void {
    if (this.impersonation.stop() === 'logout') { this.logout(); return; }
    this.followUp.loadPreferences();
  }

  /**
   * Ein ANDERER Tab hat die gespeicherte Sitzung geändert (das storage-Ereignis feuert nur in den übrigen
   * Tabs). Ohne Abgleich hielt jeder Tab seine Sitzung im Speicher, solange er offen war: stieg der Admin
   * in Tab 1 aus einer Impersonation aus, arbeitete Tab 2 mit dem fremden Token weiter; meldete man sich
   * in Tab 1 ab, schickte Tab 2 weiter das gültige Token (Codereview F1-005).
   *
   * <p>Aufgeräumt (Offline-Inhalte, Spuren, geteilte Anmeldung) hat der andere Tab schon — hier wird nur
   * die Sitzung dieses Tabs nachgezogen, ohne erneutes `session/end`.</p>
   */
  private followOtherTab(e: StorageEvent): void {
    if (e.key !== null && e.key !== SessionStore.UserKey) return;   // null: der Speicher wurde ganz geleert
    const current = this.session.value;
    const stored = this.session.readStored();
    if ((stored?.token ?? null) === (current?.token ?? null)) return;
    this.session.set(stored);
    if (!stored) { this.router.navigate(['/login']); return; }
    if (stored.userId !== current?.userId) this.followUp.loadPreferences();
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
   *
   * <p>`site` (nur `kidhub`/`leaguehub`/`turnier`, nie eine URL) und `lang` bestimmen Link-Ziel, Betreff, Absender
   * und Sprache der Mail (UX-031); ohne beide bleibt die Mail wie bisher (RookHub, Deutsch). Fehlende Werte gehen
   * gar nicht erst mit (undefined faellt beim Serialisieren weg).</p>
   */
  forgotPassword(email: string, site?: string | null, lang?: string | null): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/forgot-password`,
      { email, site: site || undefined, lang: lang || undefined });
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
    this.session.persist(user);
    this.session.set(user);
  }

  /** Konnte die Sitzung auch nach dem Räumen der Offline-Caches nicht gespeichert werden (siehe
   *  {@link SessionStore.storageFull})? Ein Neuladen loggt dann aus. */
  get storageFull(): boolean {
    return this.session.storageFull;
  }

  logout(): void {
    this.dropPushSubscription(this.token);
    removeKey(localStore(), SessionStore.UserKey);
    this.session.clearLocalTraces();
    // Die GETEILTE Anmeldung mit beenden: sie liegt als Cookie auf der Elterndomaene und ist der
    // einzige Teil dieser Sitzung, den localStorage.removeItem nicht erreicht. Bliebe sie stehen,
    // holte sich die Seite beim naechsten Aufruf genau die Anmeldung zurueck, die man gerade
    // beendet hat. Ohne Rueckmeldung abschicken — ein Abmelden darf an nichts haengen.
    // `rh-session/end`: das Cookie liegt seit N6-001 unter genau diesem Pfad (der Server raeumt dabei
    // auch das alte unter `/api/auth` ab).
    // Scheitert der Aufruf (offline, API-Neustart), bliebe das 30-Tage-Cookie gueltig, und der naechste
    // Start ohne Sitzung uebernaehme genau diese Anmeldung — womoeglich fuer den naechsten Nutzer am
    // Geraet. Deshalb ein Merker, der erst faellt, wenn der Server geantwortet hat; solange er steht,
    // holt HandoffService.adoptSharedSession das Ende nach, statt zu uebernehmen (Codereview F1-004).
    this.session.setSessionEndPending(true);
    void this.flushSessionEnd();
    this.session.set(null);
    this.router.navigate(['/login']);
  }

  /**
   * Das Push-Abo dieses Geräts beim Abmelden mit abmelden (Codereview F1-011) — sonst bekäme das Gerät
   * weiter die Benachrichtigungen des abgemeldeten Kontos. Das Token wird mitgegeben, weil die Sitzung
   * gleich danach geleert ist. Best effort: PushService lazy holen (Apps ohne Service Worker haben kein
   * SwPush), jeder Fehler wird geschluckt — ein Abmelden darf daran nie scheitern.
   */
  private dropPushSubscription(token: string | null): void {
    if (!token) return;
    try {
      void this.injector.get(PushService).removeSubscription(token).catch(() => undefined);
    } catch { /* kein Service Worker/Push in dieser App → nichts abzumelden */ }
  }

  /** Merker: ein Abmelden hat die GETEILTE Anmeldung (Cookie) noch nicht beim Server beendet. */
  static readonly SessionEndPendingKey = SessionStore.SessionEndPendingKey;

  /** Steht ein Ende der geteilten Anmeldung noch aus (siehe {@link logout})? */
  get sessionEndPending(): boolean {
    return this.session.sessionEndPending;
  }

  /**
   * Die geteilte Anmeldung beim Server beenden und danach den Merker abraeumen. `true`, wenn der Server
   * geantwortet hat. Ohne Antwort (offline, 408/429, 5xx) bleibt der Merker stehen — nachgeholt wird beim
   * naechsten Start ({@link HandoffService.adoptSharedSession}) bzw. sobald das Netz zurueck ist. Eine
   * endgueltige Antwort (andere 4xx) raeumt ihn ebenfalls ab: ein Wiederholen aenderte nichts, und ein
   * stehender Merker sperrte die geteilte Anmeldung in diesem Browser auf Dauer.
   */
  async flushSessionEnd(): Promise<boolean> {
    try {
      await firstValueFrom(this.http.post('/api/auth/rh-session/end', {}), { defaultValue: null });
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      if (status === 0 || status === 408 || status === 429 || status >= 500) return false;
    }
    this.session.setSessionEndPending(false);
    return true;
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
    // gilt jetzt in jedem Fall (in-memory); persist räumt bei vollem Speicher die Caches und
    // sagt es, wenn auch das nicht reicht.
    // Kontowechsel ohne Abmelden (Anmeldemaske mit ?switch=1): vorher aufraeumen wie beim Abmelden,
    // sonst erbte das neue Konto Offline-Inhalte, Admin-Backup und Endless-Laeufe des vorigen (die der
    // Endless-Modus beim ersten Oeffnen ins Konto uebertraegt). Dasselbe Konto erneut: nichts raeumen —
    // und ohne vorige Sitzung auch nicht, denn dann gehoeren die lokalen Laeufe dem, der sich gerade
    // anmeldet (anonym gespielt, jetzt registriert). Kein session/end: das Cookie gehoert schon dem neuen.
    const previous = this.session.validUser();
    if (previous && previous.userId !== user.userId) this.session.clearLocalTraces();
    this.session.setSessionEndPending(false);   // die Anmeldung hat das Cookie eben neu geschrieben
    this.session.persist(user);
    this.session.set(user);
    // Nachlauf: anonyme Sitzungen übernehmen, Discord-Link einlösen, Präferenzen laden (in dieser Reihenfolge).
    this.followUp.afterLogin();
  }
}
