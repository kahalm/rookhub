import { DestroyRef, Injectable, Injector, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { BehaviorSubject, Observable, firstValueFrom, tap } from 'rxjs';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { OfflineService } from './offline.service';
import { SnackbarService } from './snackbar.service';
import { ClientLogService } from './client-log.service';
import { localStore, readRaw, removeKey, writeJson } from './local-json-store';
import { ANON_PUZZLE_SESSION_KEY, readAnonSessionId } from './anon-session';

export interface AuthResponse {
  token: string;
  username: string;
  userId: number;
  isAdmin: boolean;
  /** Gesetzt, wenn dieses Token via Admin-„Als Nutzer einsteigen" erzeugt wurde. */
  impersonating?: boolean;
  /** Benutzername des Admins, der eingestiegen ist (nur bei impersonating). */
  impersonatorUsername?: string;
  /**
   * Nur im Client gesetzt: diese Anmeldung wurde aus der GETEILTEN Anmeldung einer anderen Oberflaeche
   * uebernommen (Cookie auf der Elterndomaene) und endet mit ihr — `HandoffService.verifyAdoptedSession`
   * gleicht sie beim Start und beim Zurueckkehren in den Tab ab. Eine eigene Anmeldung (Maske,
   * Registrierung) und ein frisches Token nach „Passwort aendern" tragen das Feld nicht.
   */
  adopted?: boolean;
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
    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => this.destroyed = true);
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
    // Beim Start schon abgelaufen: hier (nicht in getStoredUser) beenden — das Aufraeumen braucht den
    // Injektor, und der ist erst im Konstruktor gesetzt, nicht schon beim Initialisieren der Felder.
    this.getValidUser();
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

  /**
   * Die Rechte, die JETZT gelten — vom Server (`GET /api/auth/permissions`, eigene Rollen + Rollen der Gruppen), nicht
   * der Stand beim Anmelden (0.589.0; eine neue Rolle wirkte vorher erst nach dem nächsten Anmelden). `null`, solange
   * noch nicht geholt — dann gelten die Claims des Tokens. Ein Signal: Menüs und Seiten, die `has` in einem `computed`
   * oder im Template lesen, ziehen von selbst nach. Aufgefrischt vom {@link PermissionRefresher}.
   */
  private readonly live = signal<{ userId: number; isAdmin: boolean; permissions: ReadonlySet<string> } | null>(null);

  /** Darf der aktuelle Nutzer die Aktion? Admin erfüllt jede Permission (Superuser). */
  has(permission: string): boolean {
    const live = this.live();
    const user = this.getValidUser();
    if (live && user && live.userId === user.userId) return live.isAdmin || live.permissions.has(permission);
    return this.isAdmin || this.permissions.has(permission);
  }

  /** Den Live-Stand der Rechte holen. Still bei Fehlern (offline, Server weg) — dann bleibt der bisherige Stand. */
  async refreshPermissions(): Promise<void> {
    const user = this.getValidUser();
    if (!user) { this.live.set(null); return; }
    try {
      const r = await firstValueFrom(this.http.get<{ isAdmin: boolean; permissions: string[] }>(`${this.apiUrl}/permissions`));
      if (this.getValidUser()?.userId === user.userId)
        this.live.set({ userId: user.userId, isAdmin: !!r.isAdmin, permissions: new Set(r.permissions ?? []) });
    } catch { /* bisheriger Stand (bzw. die Claims des Tokens) gilt weiter */ }
  }

  private readonly adminBackupKey = 'rookhub_admin_user';

  /** Läuft gerade eine Admin-Impersonation? Entscheidend ist allein das Token dieses Tabs — NICHT die
   *  Admin-Sicherung im (mit allen Tabs geteilten) Speicher: fehlte sie, verschwand der rote Streifen,
   *  während das fremde Token weiter galt (Codereview F1-005). */
  get isImpersonating(): boolean {
    return !!this.getValidUser()?.impersonating;
  }

  /** Benutzername des Admins, der eingestiegen ist (für das Banner). */
  get impersonatorUsername(): string | null {
    return this.getValidUser()?.impersonatorUsername ?? null;
  }

  /**
   * Uebernimmt eine anderswo entstandene Anmeldung als die eigene — heute der Sprung zwischen
   * RookHub und der Turnierseite (siehe `HandoffService`). Bewusst getrennt von `impersonate`:
   * hier wird nichts gesichert und nichts markiert, es ist eine ganz normale Anmeldung, die nur
   * nicht ueber die Anmeldemaske kam. Ob sie an der geteilten Anmeldung haengt (`adopted`),
   * entscheidet der Aufrufer.
   */
  adoptSession(user: AuthResponse): void {
    this.setSessionEndPending(false);   // neue Anmeldung: ein altes ausstehendes Abmelden gilt nicht mehr
    this.persistSession(user);
    this.currentUserSubject.next(user);
    this.loadPreferences();
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
    this.clearLocalTraces();
    this.adoptSession({ ...user, adopted: true });
  }

  /**
   * „Als Nutzer einsteigen": sichert die aktuelle (Admin-)Session und übernimmt das
   * vom Server gelieferte Impersonation-Token. Rücksprung via {@link stopImpersonation}.
   *
   * @returns `false`, wenn die Admin-Sicherung nicht geschrieben werden konnte (Speicher voll/gesperrt) —
   *          dann bleibt alles beim Alten: ohne Sicherung gäbe es keinen Rücksprung, der Ausstieg meldete ab.
   */
  impersonate(target: AuthResponse): boolean {
    const admin = this.currentUserSubject.value;
    // Nur sichern, wenn wir nicht ohnehin schon in einer Impersonation stecken.
    if (admin && !admin.impersonating) {
      if (!writeJson(localStore(), this.adminBackupKey, admin)) return false;
    }
    const user: AuthResponse = { ...target, impersonating: true };
    this.persistSession(user);
    this.currentUserSubject.next(user);
    this.loadPreferences();
    return true;
  }

  /** Impersonation beenden und zur gesicherten Admin-Session zurückkehren. */
  stopImpersonation(): void {
    const stored = readRaw(localStore(), this.adminBackupKey);
    // Ohne Sicherung gibt es keinen Rücksprung — dann nicht im fremden Konto hängenbleiben (der Streifen
    // steht, sein Knopf täte sonst nichts), sondern abmelden wie bei einer beschädigten Sicherung.
    if (!stored) { this.logout(); return; }
    let admin: AuthResponse;
    try {
      admin = JSON.parse(stored);
    } catch {
      // Beschädigtes Admin-Backup: nicht in einem halben Zustand hängenbleiben —
      // Reste verwerfen und sauber ausloggen.
      removeKey(localStore(), this.adminBackupKey);
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
    removeKey(localStore(), this.adminBackupKey);
    this.currentUserSubject.next(admin);
    this.loadPreferences();
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
    if (e.key !== null && e.key !== 'rookhub_user') return;   // null: der Speicher wurde ganz geleert
    const current = this.currentUserSubject.value;
    const stored = this.getStoredUser();
    if ((stored?.token ?? null) === (current?.token ?? null)) return;
    this.currentUserSubject.next(stored);
    if (!stored) { this.router.navigate(['/login']); return; }
    if (stored.userId !== current?.userId) this.loadPreferences();
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
      this.endExpiredSession(user.token);
      this.currentUserSubject.next(null);
      return null;
    }
    return user;
  }

  /**
   * Eine abgelaufene Sitzung endet lokal wie ein {@link logout}: Sitzung, Offline-Inhalte,
   * Nutzer-Spuren und Admin-Backup weg. Der Ablauf ist der NORMALFALL jeder Sitzung, die niemand
   * aktiv beendet (der Interceptor schickt ein abgelaufenes Token gar nicht erst, es kommt also auch
   * kein 401 → logout). Raeumte nur logout() auf, erbte der naechste Nutzer am Geraet Kursliste und
   * Downloads, die anonyme Sitzungs-Id — und der Endless-Modus schob Laufhistorie und Highscore des
   * vorigen beim ersten Oeffnen ins neue Konto, bis in die Bestenliste. Nach einer abgelaufenen
   * Impersonation (2 h) laege das Admin-Token (30/90 Tage) unbegrenzt im Speicher.
   *
   * <p>Bewusst OHNE `session/end` und ohne Navigation: das geteilte Cookie kann laengst einer anderen,
   * aktiven Anmeldung gehoeren (eine andere Oberflaeche haelt es frisch, womoeglich fuer ein anderes
   * Konto) — und beim Start laeuft das hier im Konstruktor, wo ein HTTP-Aufruf ueber den
   * Auth-Interceptor auf diesen noch unfertigen Dienst zurueckliefe.</p>
   *
   * <p>Hat ein ANDERER Tab inzwischen eine andere Anmeldung gespeichert, gehoeren Speicher und
   * Offline-Inhalte ihr — dann endet nur die Sitzung dieses Tabs.</p>
   */
  private endExpiredSession(token: string): void {
    try {
      const stored = localStorage.getItem('rookhub_user');
      if (stored && (JSON.parse(stored) as Partial<AuthResponse> | null)?.token !== token) return;
    } catch { /* unlesbar → wie die eigene behandeln */ }
    try { localStorage.removeItem('rookhub_user'); } catch { /* Storage gesperrt */ }
    this.clearLocalTraces();
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
    this.persistSession(user);
    this.currentUserSubject.next(user);
  }

  /** Konnte die Sitzung auch nach dem Räumen der Offline-Caches nicht gespeichert werden (Quota voll
   *  / Privatmodus / gesperrter Speicher)? Sie läuft dann nur im Speicher des Tabs: ein Neuladen loggt
   *  aus. Der Nutzer erfährt es sofort per Snackbar (siehe {@link persistSession}). */
  storageFull = false;

  /**
   * Die Sitzung in den localStorage schreiben — und wenn der voll ist, Platz schaffen statt aufgeben.
   *
   * <p>Der Speicher ist je Origin auf wenige MB begrenzt, und genau dorthin schreiben die
   * Offline-Caches (Bücher, Repertoires, Puzzle-Pools) bewusst viel. Scheiterte `setItem`, lebte die
   * Anmeldung nur noch im Tab: nach jedem Neuladen stand die Anmeldemaske, ohne jeden Hinweis — das
   * Flag `storageFull` wurde gesetzt und von niemandem gelesen. Die Offline-Daten sind jederzeit
   * erneut herunterladbar, die Anmeldung nicht — also weichen sie. Klappt es auch danach nicht, sagt
   * es die App wenigstens, statt den Nutzer beim nächsten Neuladen rätseln zu lassen.</p>
   *
   * @returns `true`, wenn die Sitzung jetzt im Speicher liegt.
   */
  private persistSession(user: AuthResponse): boolean {
    const json = JSON.stringify(user);
    try { localStorage.setItem('rookhub_user', json); this.storageFull = false; return true; }
    catch { /* voll oder gesperrt → einmal räumen und erneut versuchen */ }

    try { this.injector.get(OfflineService).clearAll(); } catch { /* Storage/DI nicht verfügbar */ }
    try {
      localStorage.setItem('rookhub_user', json);
      this.storageFull = false;
      this.notifyStorage('auth.storage.evicted', 'storage_full_evicted', false);
      return true;
    } catch {
      this.storageFull = true;
      this.notifyStorage('auth.storage.full', 'storage_full', true);
      return false;
    }
  }

  /** Nutzer informieren + Diagnose-Event für Kibana — beides best-effort, hier darf nichts werfen
   *  (die Anmeldung selbst ist längst gültig). */
  private notifyStorage(i18nKey: string, kind: string, warn: boolean): void {
    try {
      const text = this.injector.get(TranslateService).instant(i18nKey);
      const snack = this.injector.get(SnackbarService);
      if (warn) snack.warn(text, { duration: 10000 }); else snack.info(text, { duration: 8000 });
    } catch { /* ohne Übersetzung/Snackbar (Tests, sehr früher Start) bleibt es still */ }
    try { this.injector.get(ClientLogService).report(kind); } catch { /* egal */ }
  }

  logout(): void {
    removeKey(localStore(), 'rookhub_user');
    this.clearLocalTraces();
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
    this.setSessionEndPending(true);
    void this.flushSessionEnd();
    this.currentUserSubject.next(null);
    this.router.navigate(['/login']);
  }

  /** Merker: ein Abmelden hat die GETEILTE Anmeldung (Cookie) noch nicht beim Server beendet. */
  static readonly SessionEndPendingKey = 'rookhub_session_end_pending';

  /** Steht ein Ende der geteilten Anmeldung noch aus (siehe {@link logout})? */
  get sessionEndPending(): boolean {
    try { return localStorage.getItem(AuthService.SessionEndPendingKey) !== null; } catch { return false; }
  }

  private setSessionEndPending(pending: boolean): void {
    try {
      if (pending) localStorage.setItem(AuthService.SessionEndPendingKey, '1');
      else localStorage.removeItem(AuthService.SessionEndPendingKey);
    } catch { /* Speicher voll/gesperrt: dann bleibt es beim einmaligen Versuch */ }
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
    this.setSessionEndPending(false);
    return true;
  }

  /** Was beim Nutzerwechsel am Geraet lokal verschwinden muss — beim Abmelden ({@link logout}), beim
   *  Wechsel auf die geteilte Anmeldung eines anderen Kontos ({@link switchToSharedSession}), beim
   *  Ablauf ({@link endExpiredSession}) und beim Kontowechsel ueber die Anmeldemaske ({@link storeUser}). */
  private clearLocalTraces(): void {
    removeKey(localStore(), 'rookhub_admin_user');
    // Geräte-lokale Offline-Inhalte (heruntergeladene Repertoires/Kurse, Kursliste, Tagespuzzle,
    // Pools) beim Abmelden löschen — sonst blieben sie für den NÄCHSTEN Nutzer desselben Geräts
    // les-/sichtbar. Die Offline-Schreib-Queue bleibt bewusst bestehen (sie ist user-gestempelt und
    // geht nur unter demselben Konto raus) → gemerkte Lösungen überstehen ein versehentliches Logout.
    // clearOnLogout (nicht clearAll): räumt zusätzlich die lokalen Nutzer-SPUREN ab. Der
    // Endless-Modus überträgt lokale Läufe beim ersten Öffnen ins Konto — auf einem geteilten Gerät
    // erbte der nächste Nutzer sonst Laufhistorie und Highscore des vorigen, sichtbar bis in die
    // Bestenliste; ebenso Kalkulations-Notizen, lokalen Kursfortschritt und den Menü-Snapshot.
    try { this.injector.get(OfflineService).clearOnLogout(); } catch { /* Storage/DI nicht verfügbar */ }
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
    // gilt jetzt in jedem Fall (in-memory); persistSession räumt bei vollem Speicher die Caches und
    // sagt es, wenn auch das nicht reicht.
    // Kontowechsel ohne Abmelden (Anmeldemaske mit ?switch=1): vorher aufraeumen wie beim Abmelden,
    // sonst erbte das neue Konto Offline-Inhalte, Admin-Backup und Endless-Laeufe des vorigen (die der
    // Endless-Modus beim ersten Oeffnen ins Konto uebertraegt). Dasselbe Konto erneut: nichts raeumen —
    // und ohne vorige Sitzung auch nicht, denn dann gehoeren die lokalen Laeufe dem, der sich gerade
    // anmeldet (anonym gespielt, jetzt registriert). Kein session/end: das Cookie gehoert schon dem neuen.
    const previous = this.getValidUser();
    if (previous && previous.userId !== user.userId) this.clearLocalTraces();
    this.setSessionEndPending(false);   // die Anmeldung hat das Cookie eben neu geschrieben
    this.persistSession(user);
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
    // Punktepartie: eigene Kennung (GuessService.AnonKey), unabhängig von der Puzzle-Sitzung — ein Besucher
    // kann nur geraten und nie ein Puzzle gelöst haben (Codereview N11-003).
    if (readRaw(localStore(), 'rookhub_guess_session'))
      import('../features/guess/guess.service').then(m =>
        this.withService(() => this.injector.get(m.GuessService), guess => guess.claimAnonymous().subscribe()));
    // Mit der Rückfallebene von anon-session: bei gesperrtem Speicher liefen die Versuche unter einer
    // Speicher-Kennung — auch die gehören jetzt ins Konto.
    const sessionId = readAnonSessionId(ANON_PUZZLE_SESSION_KEY);
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
      // Auch ein abgelaufenes Token zurueckgeben: der Konstruktor beendet es per getValidUser() wie
      // einen Ablauf mitten in der Sitzung (endExpiredSession).
      return JSON.parse(stored);
    } catch {
      try { localStorage.removeItem('rookhub_user'); } catch { }
      return null;
    }
  }
}
