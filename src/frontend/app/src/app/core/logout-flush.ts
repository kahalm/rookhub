/**
 * Letzte Uploads vor dem Abmelden bzw. Kontowechsel (1.019.0): Dienste mit ungesicherten Ständen am Gerät (Anzeige-
 * Vorlieben, E-Book-Kalkulationen …) melden sich hier an; `AuthService` ruft {@link flushBeforeLogout} mit dem Token
 * der ENDENDEN Sitzung, bevor `clearLocalTraces` das Gerät räumt. Ohne Angular, damit `AuthService` die Dienste nicht
 * kennen muss (Modul-Kreis, Bündelgröße).
 */
type Flusher = (token: string) => void;
const flushers = new Set<Flusher>();

/** Anmelden → Abmelden-Funktion. */
export function registerLogoutFlush(fn: Flusher): () => void {
  flushers.add(fn);
  return () => flushers.delete(fn);
}

export function flushBeforeLogout(token: string | null | undefined): void {
  if (!token) return;
  for (const fn of [...flushers]) {
    try { fn(token); } catch { /* ein Abmelden darf an nichts hängen */ }
  }
}
