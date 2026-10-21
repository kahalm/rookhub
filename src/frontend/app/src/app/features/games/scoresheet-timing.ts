import { signal } from '@angular/core';

/**
 * Uhr für das Einlesen eines Partieformulars (Wunsch 2026-09-28: „ein Timer, der raufzählt, damit man eine Vorstellung
 * hat"). Die frühere Schätzung „1–2 Sekunden pro Zug" (`SECONDS_PER_MOVE`, gemessen mit Claude Opus 5.5) galt fürs Modell;
 * seit 0.687.0 liest der Watcher auf dem Server alle 5 min, die Seiten sagen „ein paar Minuten" — die Konstante ist weg
 * (0.815.2).
 */

/**
 * Zeitpunkt aus der API in ms. Aus der Datenbank gelesene Zeiten kommen OHNE Zone („2026-09-28T08:52:00" — EF liest sie
 * als unbestimmt), frisch angelegte mit „Z"; gemeint ist immer UTC. `new Date()` läse die erste Form als ORTSZEIT, die Uhr
 * stünde dann zwei Stunden daneben.
 */
export function serverTime(iso: string | null | undefined): number {
  if (!iso) return NaN;
  return Date.parse(/(?:[zZ]|[+-]\d\d:?\d\d)$/.test(iso) ? iso : iso + 'Z');
}

/** Sekunden seit dem Hochladen, nie negativ (Uhr des Geräts etwas voraus); unbekannt = 0. */
export function readingSeconds(createdAt: string | null | undefined, now = Date.now()): number {
  const t = serverTime(createdAt);
  return Number.isFinite(t) ? Math.max(0, Math.floor((now - t) / 1000)) : 0;
}

/** „0:07", „1:23", „62:05". */
export function formatClock(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

/** Eine Sekunden-Uhr, die nur läuft, solange etwas gelesen wird (ein Intervall ohne Grund hielte auch Tests auf). */
export class SecondsTicker {
  readonly now = signal(Date.now());
  private id: ReturnType<typeof setInterval> | null = null;

  run(on: boolean): void {
    if (on && !this.id) {
      this.now.set(Date.now());
      this.id = setInterval(() => this.now.set(Date.now()), 1000);
    } else if (!on) this.stop();
  }

  stop(): void {
    if (this.id) clearInterval(this.id);
    this.id = null;
  }
}
