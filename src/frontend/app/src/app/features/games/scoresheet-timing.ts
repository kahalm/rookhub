import { signal } from '@angular/core';

/**
 * Wie lange das Einlesen eines Partieformulars dauert — für den Hinweis und die mitlaufende Uhr (Wunsch 2026-09-28: „ca.
 * 1–2 Sekunden pro Zug und ein Timer, der raufzählt, damit man eine Vorstellung hat"). Gemessen auf Prod (Claude Opus 5.5
 * „nur abschreiben", 26.–28.09.2026): 43 Einträge 22 s, 59 Einträge 31 s, 104 Einträge 73 s — rund 1–1,5 s je Zug.
 * Seit 0.687.0 liest der Watcher auf dem Server (alle 5 min), die Seiten sagen deshalb „ein paar Minuten" (0.688.1).
 */
export const SECONDS_PER_MOVE = '1–2';

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
