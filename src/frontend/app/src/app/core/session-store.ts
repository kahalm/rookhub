import { Injectable, Injector, inject } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { TranslateService } from '@ngx-translate/core';
import { AuthResponse, isTokenExpired } from './auth-response';
import { OfflineService } from './offline.service';
import { SnackbarService } from './snackbar.service';
import { ClientLogService } from './client-log.service';
import { localStore, removeKey } from './local-json-store';

/**
 * Die Sitzung dieses Tabs: das Token im Speicher (`rookhub_user`), sein Ablauf und das lokale Aufräumen beim
 * Nutzerwechsel. Aus `AuthService` herausgelöst (Zerlegung Z34) — die Fassade bleibt der einzige Einstieg für die
 * Oberfläche; dieser Dienst kennt weder Router noch HTTP.
 *
 * <p>Abhängige Dienste (`OfflineService`, Übersetzung, Snackbar, Client-Log) holt er erst bei Bedarf aus dem
 * Injektor: sie hängen selbst am `AuthService` (Zirkelbezug beim Aufbau).</p>
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  static readonly UserKey = 'rookhub_user';
  static readonly AdminBackupKey = 'rookhub_admin_user';
  /** Merker: ein Abmelden hat die GETEILTE Anmeldung (Cookie) noch nicht beim Server beendet. */
  static readonly SessionEndPendingKey = 'rookhub_session_end_pending';

  private readonly injector = inject(Injector);
  private readonly subject = new BehaviorSubject<AuthResponse | null>(this.readStored());
  readonly user$ = this.subject.asObservable();

  /** Konnte die Sitzung auch nach dem Räumen der Offline-Caches nicht gespeichert werden (Quota voll
   *  / Privatmodus / gesperrter Speicher)? Sie läuft dann nur im Speicher des Tabs: ein Neuladen loggt
   *  aus. Der Nutzer erfährt es sofort per Snackbar (siehe {@link persist}). */
  storageFull = false;

  /** Die Sitzung so, wie sie im Tab steht — OHNE Ablaufprüfung (siehe {@link validUser}). */
  get value(): AuthResponse | null {
    return this.subject.value;
  }

  /** Die Sitzung dieses Tabs setzen (und melden), ohne sie zu speichern. */
  set(user: AuthResponse | null): void {
    this.subject.next(user);
  }

  /**
   * Liefert den aktuellen User, loggt aber bei abgelaufenem Token automatisch
   * aus — eine abgelaufene Session gilt damit sofort als ausgeloggt, nicht erst
   * nach dem naechsten 401 vom Server.
   */
  validUser(): AuthResponse | null {
    const user = this.subject.value;
    if (user && isTokenExpired(user.token)) {
      this.endExpiredSession(user.token);
      this.subject.next(null);
      return null;
    }
    return user;
  }

  /**
   * Eine abgelaufene Sitzung endet lokal wie ein Abmelden: Sitzung, Offline-Inhalte,
   * Nutzer-Spuren und Admin-Backup weg. Der Ablauf ist der NORMALFALL jeder Sitzung, die niemand
   * aktiv beendet (der Interceptor schickt ein abgelaufenes Token gar nicht erst, es kommt also auch
   * kein 401 → logout). Raeumte nur logout() auf, erbte der naechste Nutzer am Geraet Kursliste und
   * Downloads, die anonyme Sitzungs-Id — und der Endless-Modus schob Laufhistorie und Highscore des
   * vorigen beim ersten Oeffnen ins neue Konto, bis in die Bestenliste. Nach einer abgelaufenen
   * Impersonation (2 h) laege das Admin-Token (30/90 Tage) unbegrenzt im Speicher.
   *
   * <p>Bewusst OHNE `session/end` und ohne Navigation: das geteilte Cookie kann laengst einer anderen,
   * aktiven Anmeldung gehoeren (eine andere Oberflaeche haelt es frisch, womoeglich fuer ein anderes
   * Konto) — und beim Start laeuft das hier im Konstruktor der Fassade, wo ein HTTP-Aufruf ueber den
   * Auth-Interceptor auf den noch unfertigen `AuthService` zurueckliefe.</p>
   *
   * <p>Hat ein ANDERER Tab inzwischen eine andere Anmeldung gespeichert, gehoeren Speicher und
   * Offline-Inhalte ihr — dann endet nur die Sitzung dieses Tabs.</p>
   */
  private endExpiredSession(token: string): void {
    try {
      const stored = localStorage.getItem(SessionStore.UserKey);
      if (stored && (JSON.parse(stored) as Partial<AuthResponse> | null)?.token !== token) return;
    } catch { /* unlesbar → wie die eigene behandeln */ }
    try { localStorage.removeItem(SessionStore.UserKey); } catch { /* Storage gesperrt */ }
    this.clearLocalTraces();
  }

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
  persist(user: AuthResponse): boolean {
    const json = JSON.stringify(user);
    try { localStorage.setItem(SessionStore.UserKey, json); this.storageFull = false; return true; }
    catch { /* voll oder gesperrt → einmal räumen und erneut versuchen */ }

    try { this.injector.get(OfflineService).clearAll(); } catch { /* Storage/DI nicht verfügbar */ }
    try {
      localStorage.setItem(SessionStore.UserKey, json);
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

  /** Was beim Nutzerwechsel am Geraet lokal verschwinden muss — beim Abmelden, beim Wechsel auf die geteilte
   *  Anmeldung eines anderen Kontos, beim Ablauf ({@link endExpiredSession}) und beim Kontowechsel ueber die
   *  Anmeldemaske (alles in `AuthService`). */
  clearLocalTraces(): void {
    removeKey(localStore(), SessionStore.AdminBackupKey);
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

  /** Steht ein Ende der geteilten Anmeldung noch aus (siehe `AuthService.logout`)? */
  get sessionEndPending(): boolean {
    try { return localStorage.getItem(SessionStore.SessionEndPendingKey) !== null; } catch { return false; }
  }

  setSessionEndPending(pending: boolean): void {
    try {
      if (pending) localStorage.setItem(SessionStore.SessionEndPendingKey, '1');
      else localStorage.removeItem(SessionStore.SessionEndPendingKey);
    } catch { /* Speicher voll/gesperrt: dann bleibt es beim einmaligen Versuch */ }
  }

  /** Die gespeicherte Sitzung lesen; unlesbar → entfernen. */
  readStored(): AuthResponse | null {
    try {
      const stored = localStorage.getItem(SessionStore.UserKey);
      if (!stored) return null;
      // Auch ein abgelaufenes Token zurueckgeben: die Fassade beendet es im Konstruktor per validUser() wie
      // einen Ablauf mitten in der Sitzung (endExpiredSession).
      return JSON.parse(stored);
    } catch {
      try { localStorage.removeItem(SessionStore.UserKey); } catch { }
      return null;
    }
  }
}
