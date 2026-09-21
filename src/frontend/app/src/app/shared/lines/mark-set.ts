import { Observable } from 'rxjs';

/**
 * Die Flashcard-Markierungen einer Linien-Liste: eine Menge von Schlüsseln plus das Umschalten
 * einer einzelnen Markierung — OPTIMISTISCH (die Checkbox reagiert sofort) und mit Rollback, wenn
 * der Server das Speichern verweigert.
 *
 * Der Schlüssel ist generisch, weil die beiden Listen verschieden zählen: ein Kurs markiert die
 * Linien-Id (`number`), ein Repertoire den Linien-Hash (`string`, siehe `repertoire-line-key.util`).
 *
 * Verhält sich nach außen wie ein `Set` (`has`, `size`, iterierbar) — die Vorlagen greifen
 * unverändert mit `marked.has(...)` und `marked.size` darauf zu.
 */
export class MarkSet<K> {
  private keys = new Set<K>();

  has(key: K): boolean { return this.keys.has(key); }

  get size(): number { return this.keys.size; }

  [Symbol.iterator](): Iterator<K> { return this.keys[Symbol.iterator](); }

  /** Den ganzen Stand ersetzen (nach dem Laden vom Server). */
  replace(keys: Iterable<K>): void { this.keys = new Set(keys ?? []); }

  /**
   * Markierung umschalten und das Ergebnis speichern lassen. Die Menge ändert sich SOFORT; scheitert
   * der Aufruf, wird genau diese eine Änderung zurückgenommen. Liefert den angestrebten Zustand
   * (`true` = jetzt markiert) — den braucht der Aufrufer für Meldungen.
   */
  toggle(key: K, persist: (key: K, next: boolean) => Observable<unknown>): boolean {
    const next = !this.keys.has(key);
    this.apply(key, next);
    persist(key, next).subscribe({
      error: () => this.apply(key, !next),
    });
    return next;
  }

  private apply(key: K, marked: boolean): void {
    if (marked) this.keys.add(key); else this.keys.delete(key);
  }
}
