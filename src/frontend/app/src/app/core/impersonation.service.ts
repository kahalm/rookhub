import { Injectable, Injector, inject } from '@angular/core';
import { AuthResponse } from './auth-response';
import { OfflineService } from './offline.service';
import { SessionStore } from './session-store';
import { localStore, readRaw, removeKey, writeJson } from './local-json-store';

/** Ausgang von {@link ImpersonationService.stop}: Admin-Sitzung zurück, oder ohne brauchbare Sicherung abmelden. */
export type ImpersonationStop = 'restored' | 'logout';

/**
 * Admin-„Als Nutzer einsteigen": Admin-Sitzung sichern (`rookhub_admin_user`), fremdes Token übernehmen und zurück.
 * Aus `AuthService` herausgelöst (Zerlegung Z34); Verhalten unverändert. Aufrufer gehen über die Fassade
 * (`AuthService.impersonate`/`stopImpersonation`) — sie lädt danach die Präferenzen bzw. meldet ab, damit dieser
 * Dienst nicht vom `AuthService` abhängt.
 */
@Injectable({ providedIn: 'root' })
export class ImpersonationService {
  private readonly session = inject(SessionStore);
  private readonly injector = inject(Injector);

  /** Läuft gerade eine Admin-Impersonation? Entscheidend ist allein das Token dieses Tabs — NICHT die
   *  Admin-Sicherung im (mit allen Tabs geteilten) Speicher: fehlte sie, verschwand der rote Streifen,
   *  während das fremde Token weiter galt (Codereview F1-005). */
  get isImpersonating(): boolean {
    return !!this.session.validUser()?.impersonating;
  }

  /** Benutzername des Admins, der eingestiegen ist (für das Banner). */
  get impersonatorUsername(): string | null {
    return this.session.validUser()?.impersonatorUsername ?? null;
  }

  /**
   * Sichert die aktuelle (Admin-)Session und übernimmt das vom Server gelieferte Impersonation-Token.
   *
   * @returns `false`, wenn die Admin-Sicherung auch nach dem Räumen der Offline-Caches nicht geschrieben werden
   *          konnte (Speicher gesperrt/voll) — dann bleibt alles beim Alten.
   */
  start(target: AuthResponse): boolean {
    const admin = this.session.value;
    // Nur sichern, wenn wir nicht ohnehin schon in einer Impersonation stecken.
    if (admin && !admin.impersonating && !this.writeAdminBackup(admin)) return false;
    const user: AuthResponse = { ...target, impersonating: true };
    this.session.persist(user);
    this.session.set(user);
    return true;
  }

  /**
   * Die Admin-Sitzung für den Rücksprung sichern — bei vollem Speicher wie `SessionStore.persist`: erst die
   * Offline-Caches räumen (jederzeit wieder ladbar), dann noch einmal. Der häufige Grund für ein gescheitertes
   * `setItem` ist nicht der gesperrte Speicher, sondern die Quota, die genau diese Caches füllen.
   */
  private writeAdminBackup(admin: AuthResponse): boolean {
    if (writeJson(localStore(), SessionStore.AdminBackupKey, admin)) return true;
    try { this.injector.get(OfflineService).clearAll(); } catch { /* Storage/DI nicht verfügbar */ }
    return writeJson(localStore(), SessionStore.AdminBackupKey, admin);
  }

  /**
   * Zur gesicherten Admin-Session zurückkehren. `'logout'`: keine oder eine beschädigte Sicherung — die Fassade
   * meldet dann ab, statt im fremden Konto hängenzubleiben (der Streifen stünde, sein Knopf täte sonst nichts).
   */
  stop(): ImpersonationStop {
    const stored = readRaw(localStore(), SessionStore.AdminBackupKey);
    if (!stored) return 'logout';
    let admin: AuthResponse;
    try {
      admin = JSON.parse(stored);
    } catch {
      // Beschädigtes Admin-Backup: nicht in einem halben Zustand hängenbleiben —
      // Reste verwerfen und sauber ausloggen.
      removeKey(localStore(), SessionStore.AdminBackupKey);
      return 'logout';
    }
    // Reihenfolge: ERST die Admin-Sitzung zurückschreiben, DANN das Backup löschen. Vorher war es
    // umgekehrt — warf `setItem` (voller/gesperrter Speicher), war das Backup unwiederbringlich weg,
    // `rookhub_user` trug weiter das Impersonation-Token und das rote Banner verschwand: der Admin
    // arbeitete unbemerkt im fremden Konto weiter, ohne Rücksprung.
    try { localStorage.setItem(SessionStore.UserKey, stored); }
    catch {
      this.session.storageFull = true;
      // Backup NICHT löschen — der Rücksprung bleibt so nach einem Neuladen möglich.
      this.session.set(admin);
      return 'restored';
    }
    removeKey(localStore(), SessionStore.AdminBackupKey);
    this.session.set(admin);
    return 'restored';
  }
}
