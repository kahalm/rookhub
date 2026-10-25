import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { jwtPayload } from './auth-response';
import { SessionStore } from './session-store';

/**
 * Rechte des angemeldeten Nutzers (RBAC) — Gegenstück zum `PermissionResolver` des Servers. Aus `AuthService`
 * herausgelöst (Zerlegung Z34); die Oberfläche fragt weiter `AuthService.has()` (Fassade), aufgefrischt vom
 * `PermissionRefresher`.
 */
@Injectable({ providedIn: 'root' })
export class PermissionService {
  private readonly apiUrl = '/api/auth';
  private readonly session = inject(SessionStore);
  private readonly http = inject(HttpClient);

  /**
   * Die Rechte, die JETZT gelten — vom Server (`GET /api/auth/permissions`, eigene Rollen + Rollen der Gruppen), nicht
   * der Stand beim Anmelden (0.589.0; eine neue Rolle wirkte vorher erst nach dem nächsten Anmelden). `null`, solange
   * noch nicht geholt — dann gelten die Claims des Tokens. Ein Signal: Menüs und Seiten, die `has` in einem `computed`
   * oder im Template lesen, ziehen von selbst nach.
   */
  private readonly live = signal<{ userId: number; isAdmin: boolean; permissions: ReadonlySet<string> } | null>(null);

  /** Admin laut Token (Stand beim Anmelden). */
  get isAdmin(): boolean {
    return this.session.validUser()?.isAdmin ?? false;
  }

  /**
   * Effektive Permissions des aktuellen Tokens (RBAC), aus den `perm`-Claims des JWT dekodiert.
   * Mehrere Claims desselben Namens landen im JWT als Array, ein einzelner als String — beides wird
   * normalisiert. Admins tragen die Admin-Rolle separat (siehe `has`), daher hier ggf. leer.
   */
  get permissions(): ReadonlySet<string> {
    const token = this.session.validUser()?.token ?? null;
    if (!token) return new Set();
    try {
      const p = jwtPayload(token)['perm'];
      if (Array.isArray(p)) return new Set(p.map(String));
      if (typeof p === 'string') return new Set([p]);
      return new Set();
    } catch {
      return new Set();
    }
  }

  /** Darf der aktuelle Nutzer die Aktion? Admin erfüllt jede Permission (Superuser). */
  has(permission: string): boolean {
    const live = this.live();
    const user = this.session.validUser();
    if (live && user && live.userId === user.userId) return live.isAdmin || live.permissions.has(permission);
    return this.isAdmin || this.permissions.has(permission);
  }

  /**
   * Ist der Nutzer JETZT Admin? Live-Stand aus `GET /api/auth/permissions` (wie {@link has}), sonst die Rolle am Token.
   * Liest das Signal — Templates/`computed` ziehen nach, ein Entzug wirkt ohne neues Anmelden (Foto-Link 0.727.5).
   */
  get isAdminLive(): boolean {
    const live = this.live();
    const user = this.session.validUser();
    if (live && user && live.userId === user.userId) return live.isAdmin;
    return this.isAdmin;
  }

  /** Den Live-Stand der Rechte holen. Still bei Fehlern (offline, Server weg) — dann bleibt der bisherige Stand. */
  async refresh(): Promise<void> {
    const user = this.session.validUser();
    if (!user) { this.live.set(null); return; }
    try {
      const r = await firstValueFrom(this.http.get<{ isAdmin: boolean; permissions: string[] }>(`${this.apiUrl}/permissions`));
      if (this.session.validUser()?.userId === user.userId)
        this.live.set({ userId: user.userId, isAdmin: !!r.isAdmin, permissions: new Set(r.permissions ?? []) });
    } catch { /* bisheriger Stand (bzw. die Claims des Tokens) gilt weiter */ }
  }
}
