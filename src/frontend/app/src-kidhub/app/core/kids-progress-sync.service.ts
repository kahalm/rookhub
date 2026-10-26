import { DestroyRef, Injectable, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@rh/core/auth.service';
import { KidsApiService } from './kids-api.service';
import { KidsEndlessStore } from './kids-endless.store';
import { KidsProgressStore } from './kids-progress.store';
import { KidsStarsStore } from './kids-stars.store';

/** So lange nach der letzten Aenderung, bevor der Stand hinaufgeht — ein Durchgang sind viele kleine Schritte. */
export const SYNC_DEBOUNCE_MS = 800;

export type KidsSyncState = 'off' | 'saving' | 'saved' | 'offline';

/**
 * Gleicht den KidHub-Fortschritt mit dem Konto ab, solange jemand angemeldet ist:
 * <ul>
 *   <li>beim Anmelden (und beim Start angemeldet) den ganzen hiesigen Stand hinauf — der Server fuehrt
 *       ihn mit dem Konto zusammen, also auch, was das Kind vorher OHNE Konto gespielt hat;</li>
 *   <li>nach jeder Aenderung (gebuendelt), wenn der Tab wieder sichtbar wird (anderes Geraet) und wenn das
 *       Netz zurueck ist;</li>
 *   <li>beim Abmelden bleibt der Stand im Konto, hier wird er geleert — auf einem geteilten Tablet soll das
 *       naechste Kind nicht mit fremden Sternen anfangen (und die nicht in SEIN Konto uebernehmen). Dazu gehen die
 *       Geraete-Staende, die (noch) nicht im Konto liegen: Endlos-Laeufe samt Rekord und Kurve, Sternenjagd (F7-013). Liegt
 *       beim Anmelden noch der Stand eines ANDEREN Kontos hier, wird er verworfen statt uebernommen.</li>
 * </ul>
 * Scheitert ein Abgleich, bleibt alles lokal; der naechste Anlass holt ihn nach.
 */
@Injectable({ providedIn: 'root' })
export class KidsProgressSync {
  private readonly auth = inject(AuthService);
  private readonly api = inject(KidsApiService);
  private readonly store = inject(KidsProgressStore);
  private readonly endless = inject(KidsEndlessStore);
  private readonly stars = inject(KidsStarsStore);

  readonly state = signal<KidsSyncState>('off');

  private userId: number | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private inFlight = false;
  private again = false;

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.auth.currentUser$.pipe(takeUntilDestroyed(destroyRef)).subscribe(user => this.onUser(user?.userId ?? null));
    effect(() => {
      this.store.revision();
      untracked(() => this.schedule());
    });
    const wake = () => { if (document.visibilityState !== 'hidden') this.push(); };
    document.addEventListener('visibilitychange', wake);
    window.addEventListener('online', wake);
    destroyRef.onDestroy(() => {
      document.removeEventListener('visibilitychange', wake);
      window.removeEventListener('online', wake);
      clearTimeout(this.timer);
    });
  }

  private onUser(id: number | null): void {
    if (id === this.userId) return;
    this.userId = id;
    if (id === null) {
      clearTimeout(this.timer);
      this.state.set('off');
      if (this.store.owner() !== null) this.store.clear();
      // Nur beim Wechsel angemeldet → abgemeldet (der Start ohne Konto kommt hier nicht an: userId war schon null).
      this.endless.clear();
      this.stars.clear();
      return;
    }
    const owner = this.store.owner();
    if (owner !== null && owner !== id) this.store.clear();
    this.push();
  }

  private schedule(): void {
    if (this.userId === null) return;
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.push(), SYNC_DEBOUNCE_MS);
  }

  /** Den Stand jetzt hinaufschicken; laeuft schon einer, direkt danach noch einmal. */
  push(): void {
    const id = this.userId;
    if (id === null) return;
    if (this.inFlight) { this.again = true; return; }
    clearTimeout(this.timer);
    this.inFlight = true;
    this.state.set('saving');
    this.api.syncProgress(this.store.toDto()).subscribe({
      next: merged => {
        // Inzwischen abgemeldet oder ein anderes Konto: die Antwort gehoert nicht mehr hierher.
        if (this.userId === id) {
          this.store.adopt(merged, id);
          this.state.set('saved');
        }
        this.done();
      },
      error: () => {
        if (this.userId === id) this.state.set('offline');
        this.done();
      },
    });
  }

  private done(): void {
    this.inFlight = false;
    if (this.again) {
      this.again = false;
      this.push();
    }
  }
}
