import { DestroyRef, Injectable, inject } from '@angular/core';
import { AuthService } from './auth.service';

/**
 * Hält die Rechte aktuell (0.589.0, Wunsch 2026-09-28: „sollte immer wieder neue Infos holen"): beim Start (vor dem
 * ersten Seitenaufbau, höchstens {@link PermissionRefresher.StartTimeoutMs} gewartet), bei jeder An-/Abmeldung, wenn der
 * Tab wieder sichtbar wird, und alle {@link PermissionRefresher.IntervalMs}, solange er sichtbar ist. So gilt eine eben
 * vergebene Rolle — oder die Rolle einer Gruppe, in die jemand gerade gekommen ist — ohne Abmelden.
 *
 * <p>Bewusst ein eigener Dienst und nicht im {@link AuthService}: der wird in Hunderten Tests mit HttpTestingController
 * gebaut, und eine Anfrage beim Erzeugen fiele dort überall als unerwartet auf.</p>
 */
@Injectable({ providedIn: 'root' })
export class PermissionRefresher {
  static readonly IntervalMs = 2 * 60_000;
  static readonly StartTimeoutMs = 3000;
  /** Mindestabstand beim Zurückkehren in den Tab — hin- und herschalten fragt nicht jedes Mal. */
  static readonly MinGapMs = 30_000;

  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private started = false;
  private last = 0;

  /** Für `provideAppInitializer`: holt den ersten Stand und richtet das Auffrischen ein. */
  start(): Promise<void> {
    if (this.started) return Promise.resolve();
    this.started = true;
    let userId = this.auth.currentUser?.userId ?? null;
    const sub = this.auth.currentUser$.subscribe(u => {
      if ((u?.userId ?? null) === userId) return;
      userId = u?.userId ?? null;
      void this.refresh();
    });
    const onVisible = () => {
      if (document.visibilityState === 'visible' && Date.now() - this.last >= PermissionRefresher.MinGapMs) void this.refresh();
    };
    document.addEventListener('visibilitychange', onVisible);
    const timer = setInterval(() => { if (document.visibilityState === 'visible') void this.refresh(); }, PermissionRefresher.IntervalMs);
    this.destroyRef.onDestroy(() => {
      sub.unsubscribe();
      document.removeEventListener('visibilitychange', onVisible);
      clearInterval(timer);
    });
    // Offline oder träge: nicht den Start aufhalten — die Claims des Tokens tragen bis dahin.
    return Promise.race([this.refresh(), new Promise<void>(r => setTimeout(r, PermissionRefresher.StartTimeoutMs))]);
  }

  private refresh(): Promise<void> {
    this.last = Date.now();
    if (!this.auth.isLoggedIn || (typeof navigator !== 'undefined' && navigator.onLine === false)) return Promise.resolve();
    return this.auth.refreshPermissions();
  }
}
