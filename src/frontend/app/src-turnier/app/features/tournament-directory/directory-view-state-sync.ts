import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@rh/core/auth.service';
import { ViewStateService } from '@rh/core/view-state.service';
import { DirectoryViewSync } from './directory-view-state';

/** Was der Abgleich vom Turnierkalender braucht — der Zustand selbst bleibt in der Komponente. */
export interface DirectoryViewStateHost {
  /** Was die Filterleiste zeigt (`snapshotView`). */
  snapshot(): Record<string, unknown>;
  /** Einen gemerkten Zustand auf die Leiste legen; `null` = nichts Brauchbares gemerkt. */
  apply(stored: Record<string, unknown> | null): void;
  /** Sind Suchprofile und Deep-Links ausgewertet (`TournamentDirectoryComponent.ready`)? */
  ready(): boolean;
  reload(): void;
}

/** Die Teile des AuthService, die der Abgleich liest. */
export type DirectoryViewAuth = Pick<AuthService, 'isLoggedIn' | 'isImpersonating' | 'currentUser'>;

/**
 * Der gemerkte Anzeigezustand des Turnierkalenders: geraetelokal SOFORT, beim Nutzer (Server)
 * gedrosselt, und beim Aufbau in dieser Reihenfolge zurueckgeholt. Herausgezogen aus
 * `TournamentDirectoryComponent` (Zerlegung Z28) — die Felder, die der Zustand traegt, und wie sie
 * gelesen werden, stehen weiter in `directory-view-state.ts` (`snapshotView`/`applyStoredView`).
 */
export class DirectoryViewStateSync {
  /**
   * Schluessel der gemerkten Ansicht. Ohne sie faellt der Weg „Turnier oeffnen → zurueck" auf die
   * Vorgabefilter zurueck — man muesste Zeitraum, Reiter, Ort und Monat jedes Mal neu einstellen.
   *
   * <p>Das ist nur der Stamm: gespeichert wird je NUTZER (`viewKeyFor`). Der nackte Schluessel war
   * nutzerlos — auf einem geteilten Geraet erbte der naechste Nutzer Umkreis und Standort-Koordinaten
   * des vorigen und schob sie beim ersten Aufruf in SEIN Konto. Ein noch liegender alter Eintrag wird
   * beim Lesen entfernt.</p>
   */
  static readonly ViewKey = 'rh.turnier.directoryView';

  /** Dieselbe Ansicht beim NUTZER (Server) — die Kennung muss in `ViewStateService.AllowedKeys` stehen. */
  static readonly StateKey = 'turnier.directory';

  /**
   * Der geraetelokale Schluessel dieses Nutzers; ohne Anmeldung ein eigener Gast-Schluessel. Seit 0.643.0 ist der
   * Kalender ohne Konto offen, und ein Gast soll „Turnier oeffnen → zurueck" ebenso ohne neu Filtern schaffen. Der
   * Gast-Stand geht nie zum Server (`queuePersist`) und nie in ein Konto — angemeldet gilt der eigene Schluessel.
   */
  static viewKeyFor(userId: number | null | undefined): string {
    return `${DirectoryViewStateSync.ViewKey}.${userId == null ? 'guest' : userId}`;
  }

  /** Der gedrosselte Weg zum Server samt Abgleich-Stand. */
  private readonly sync: DirectoryViewSync;

  constructor(
    private readonly auth: DirectoryViewAuth,
    private readonly viewStates: ViewStateService,
    private readonly destroyRef: DestroyRef,
    private readonly host: DirectoryViewStateHost,
  ) {
    this.sync = new DirectoryViewSync(viewStates, DirectoryViewStateSync.StateKey, destroyRef);
  }

  start(): void {
    this.sync.start();
  }

  /**
   * Zustand sichern — geraetelokal SOFORT und beim Nutzer (Server) verzoegert.
   *
   * <p>Beides, und zwar aus zwei verschiedenen Gruenden: der lokale Speicher traegt den Weg
   * „Turnier oeffnen → zurueck" ohne einen einzigen Abruf, der Server traegt ihn ueber GERAETE —
   * der Umkreis, den man am Rechner eingestellt hat, war am Handy weg.</p>
   *
   * <p>Der Server-Schreibvorgang ist gedrosselt: `store()` laeuft bei jeder Aenderung der
   * Leiste, und eine Tastatureingabe im Suchfeld sind ein Dutzend davon.</p>
   */
  store(): void {
    const state = this.host.snapshot();
    const key = DirectoryViewStateSync.viewKeyFor(this.auth.currentUser?.userId);
    try {
      localStorage.setItem(key, JSON.stringify(state));
    } catch {
      // Gesperrter oder voller Speicher (Privatmodus) ist kein Grund, die Seite scheitern zu
      // lassen — dann faengt man eben wieder bei der Vorgabe an.
    }
    this.queuePersist(state);
  }

  /** Den geraetelokalen Zustand dieses Nutzers auf die Leiste legen (sofort, ohne Abruf). */
  restore(): void {
    let stored: Record<string, unknown> | null = null;
    try {
      // Der alte, nutzerlose Eintrag gehoert irgendwem, der hier einmal angemeldet war — weg damit.
      localStorage.removeItem(DirectoryViewStateSync.ViewKey);
      const key = DirectoryViewStateSync.viewKeyFor(this.auth.currentUser?.userId);
      const raw = localStorage.getItem(key);
      stored = raw ? JSON.parse(raw) : null;
    } catch {
      stored = null;                       // unlesbar/kaputt: Vorgabe bleibt stehen
    }
    this.host.apply(stored);
  }

  /**
   * Den beim NUTZER gespeicherten Zustand nachziehen (siehe `ViewStateService`).
   *
   * <p>Reihenfolge mit Absicht: erst der geraetelokale Zustand (sofort, ohne Abruf — kein
   * Aufblitzen der Vorgabefilter), dann der vom Server. Weicht der ab, ist ER der juengere:
   * geschrieben hat ihn das Geraet, an dem zuletzt gefiltert wurde. Nur dann wird neu geladen —
   * sonst kostete jeder Seitenaufruf zwei Abfragen.</p>
   *
   * <p>Hat der Server nichts, wird der lokale Zustand hinaufgeschoben: beim ersten Aufruf nach
   * dieser Aenderung steht dort noch nichts, und die bestehende Einstellung soll nicht verloren
   * gehen. Der lokale Zustand ist der DIESES Nutzers (`viewKeyFor`), und beim Einstieg als Nutzer
   * geht nichts hinauf (`queuePersist`).</p>
   */
  syncRemote(): void {
    this.viewStates.get<Record<string, unknown>>(DirectoryViewStateSync.StateKey)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(remote => {
        // Server nicht erreichbar (nicht „hat nichts"): weder uebernehmen noch hinaufschieben — sein
        // Stand kann juenger sein. Spaetere Aenderungen des Nutzers gehen trotzdem hinauf (remoteSync).
        if (remote === undefined) {
          // Vor dem fertigen Aufbau zieht `applyQueryParams` die Basis nach.
          this.sync.markFailed(this.host.ready() ? JSON.stringify(this.host.snapshot()) : null);
          return;
        }
        this.sync.markDone();
        if (!remote) {
          this.queuePersist(this.host.snapshot());
          return;
        }
        if (JSON.stringify(remote) === JSON.stringify(this.host.snapshot())) return;

        this.host.apply(remote);
        if (this.host.ready()) this.host.reload();
      });
  }

  /**
   * Scheiterte der Abruf schon vor dem fertigen Aufbau, ist erst JETZT der Stand da, an dem eine
   * Aenderung des Nutzers zu messen ist (siehe `DirectoryViewSync.stateAtSyncFailure`).
   */
  captureBaselineIfFailed(): void {
    this.sync.captureBaselineIfFailed(() => JSON.stringify(this.host.snapshot()));
  }

  /**
   * Zum Server — ausser beim Einstieg als Nutzer: dann sieht sich ein Admin nur um, und was er an
   * der Leiste dreht (oder die Vorgabe, die der erste Aufbau speichert), gehoert nicht in das fremde
   * Konto. Der Nutzer saehe sonst beim naechsten Besuch einen Umkreis, den er nie eingestellt hat.
   */
  private queuePersist(state: Record<string, unknown>): void {
    // Gaeste haben keinen Server-Stand; Admins beim Einstieg als Nutzer schreiben nicht ins fremde Konto.
    if (!this.auth.isLoggedIn || this.auth.isImpersonating) return;
    this.sync.queue(state);
  }
}
