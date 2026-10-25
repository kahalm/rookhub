/** Ein laufender Versand: was für welchen Schlüssel unterwegs ist, und als wievielter. */
export interface SingleFlight<K, T> {
  readonly key: K;
  readonly value: T;
  /** Laufende Nummer je Schlüssel — ein später Fehlschlag eines überholten Versands reiht nichts mehr ein. */
  readonly ticket: number;
}

/**
 * Sende-Warteschlange „je Schlüssel höchstens EINE Anfrage unterwegs" (Codereview F3-001, Zerlegung Z20 — ersetzt die
 * zwei parallel gebauten Outboxen des Kalkulations-Modus für Baum und Bewertung).
 *
 * Warum: die Server-Upserts sind „last write wins" ohne Versions-Token. Zwei parallele Anfragen derselben Stellung
 * könnten vertauscht ankommen (der ältere Stand bliebe gespeichert, ein Zeit-Delta zählte beim Wiederholen doppelt).
 * Also wartet je Schlüssel ein jüngerer Stand, bis die laufende Anfrage beantwortet ist; verschiedene Schlüssel gehen
 * unabhängig voneinander raus, in der Reihenfolge, in der sie eingereiht wurden.
 *
 * Ablauf: {@link set}/{@link delete} pflegen den wartenden Stand, {@link flush} schickt alles Freie über `dispatch`,
 * der Aufrufer meldet die Antwort mit {@link succeeded} (dann geht ein inzwischen aufgelaufener Stand sofort hinterher)
 * oder {@link failed} (dann wird der Stand nach seiner Regel wieder eingereiht, aber bewusst NICHT sofort neu gesendet —
 * bei totem Server drehte sich sonst eine heiße Schleife; der nächste `flush` nimmt ihn mit).
 *
 * Werte dürfen nicht `undefined` sein (`undefined` heißt in {@link failed} „nichts einreihen").
 */
export class KeyedSingleFlightQueue<K, T> {
  private readonly pending = new Map<K, T>();
  private readonly inFlight = new Set<K>();
  private readonly tickets = new Map<K, number>();

  constructor(private readonly dispatch: (flight: SingleFlight<K, T>) => void) {}

  /** Anzahl der WARTENDEN Stände (laufende zählen nicht). */
  get size(): number { return this.pending.size; }

  /** Wartet für diesen Schlüssel ein Stand? */
  has(key: K): boolean { return this.pending.has(key); }

  /** Der wartende Stand (oder `undefined`). */
  get(key: K): T | undefined { return this.pending.get(key); }

  /** Wartenden Stand setzen bzw. ersetzen (ein vorhandener Eintrag behält seinen Platz in der Reihenfolge). */
  set(key: K, value: T): void { this.pending.set(key, value); }

  /** Wartenden Stand verwerfen (eine laufende Anfrage bleibt davon unberührt). */
  delete(key: K): void { this.pending.delete(key); }

  /** Ist für diesen Schlüssel gerade eine Anfrage unterwegs? */
  isInFlight(key: K): boolean { return this.inFlight.has(key); }

  /** Alles Wartende senden, dessen Schlüssel frei ist — auch Stände längst verlassener Stellungen. */
  flush(): void {
    for (const key of [...this.pending.keys()]) {
      if (this.inFlight.has(key) || !this.pending.has(key)) continue;
      const value = this.pending.get(key) as T;
      this.pending.delete(key);
      this.inFlight.add(key);
      const ticket = (this.tickets.get(key) ?? 0) + 1;
      this.tickets.set(key, ticket);
      this.dispatch({ key, value, ticket });
    }
  }

  /** Antwort da: Schlüssel frei und einen inzwischen aufgelaufenen jüngeren Stand gleich hinterherschicken. */
  succeeded(flight: SingleFlight<K, T>): void {
    this.inFlight.delete(flight.key);
    if (this.pending.has(flight.key)) this.flush();
  }

  /**
   * Anfrage gescheitert: `requeue(wartend)` entscheidet, was wieder wartet (Rückgabe `undefined` = den wartenden Stand
   * lassen, wie er ist). Nur für den JÜNGSTEN Versand des Schlüssels — ein überholter darf nichts zurückrollen.
   * Kein sofortiger Neuversuch.
   */
  failed(flight: SingleFlight<K, T>, requeue: (waiting: T | undefined) => T | undefined): void {
    if (this.tickets.get(flight.key) === flight.ticket) {
      const next = requeue(this.pending.get(flight.key));
      if (next !== undefined) this.pending.set(flight.key, next);
    }
    this.inFlight.delete(flight.key);
  }
}
