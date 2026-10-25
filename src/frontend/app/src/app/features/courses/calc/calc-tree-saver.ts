import { CalcTree, isEmpty, serializeTree } from './calc-tree.util';
import { CalcBackend, CalcPosition } from './calculation.service';
import { KeyedSingleFlightQueue, SingleFlight } from './keyed-single-flight-queue';

/** Takt des Autosave — austauschbar für Specs; greift erst beim Aufruf auf die globalen Funktionen zu. */
export interface CalcSaveClock {
  setTimeout(run: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

const SYSTEM_SAVE_CLOCK: CalcSaveClock = {
  setTimeout: (run, ms) => setTimeout(run, ms),
  clearTimeout: handle => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

export interface CalcTreeSaverOptions {
  /** Server oder Gerätespeicher (wechselt nicht während der Sitzung, wird aber erst beim Senden gefragt). */
  backend: () => Pick<CalcBackend, 'saveTree' | 'deleteTree'>;
  /** Die angezeigte Stellung (BookPuzzleId) oder null. */
  currentId: () => number | null;
  /** Der Baum der angezeigten Stellung. */
  tree: () => CalcTree;
  /** Sprunglisten-Zeile als bearbeitet/unbearbeitet markieren. */
  markDone: (bookPuzzleId: number, done: boolean) => void;
  onSaved: () => void;
  onFailed: () => void;
  clock?: CalcSaveClock;
}

/**
 * Speichern des Analysebaums im Kalkulations-Modus (Zerlegung Z20, aus `CalculationComponent`): entprellter Autosave,
 * Speicherstand der angezeigten Stellung (`saving`/`savedAt`) und die Sende-Warteschlange je Stellung.
 */
export class CalcTreeSaver {
  static readonly AUTOSAVE_MS = 1200;

  saving = false;
  savedAt: Date | null = null;
  private dirty = false;
  private hadStoredTree = false;
  private timer: unknown;
  private readonly clock: CalcSaveClock;

  /** Noch nicht bestätigte Speicherungen JE STELLUNG (BookPuzzleId → serialisierter Baum, `null` =
   *  löschen). Der Snapshot muss an der Stellung hängen, zu der er gehört: der Baum ist nach
   *  einem Stellungswechsel schon ersetzt, ein gescheiterter Save wäre sonst unwiederbringlich weg.
   *  Je Stellung immer nur EINE Anfrage unterwegs: der Server-Upsert ist ein reines „last write wins"
   *  ohne Versions-Token, zwei parallele Saves derselben Stellung könnten in umgekehrter Reihenfolge
   *  ankommen (Autosave alle 1,2 s, eine langsame Verbindung genügt) — siehe {@link KeyedSingleFlightQueue}. */
  private readonly outbox = new KeyedSingleFlightQueue<number, string | null>(flight =>
    flight.value === null ? this.sendDelete(flight) : this.sendSave(flight, flight.value));

  constructor(private readonly options: CalcTreeSaverOptions) {
    this.clock = options.clock ?? SYSTEM_SAVE_CLOCK;
  }

  /** Neue Stellung angezeigt. Ein noch laufender Save gehört zur ALTEN — sein Spinner darf hier nicht weiterlaufen. */
  reset(pos: CalcPosition): void {
    this.hadStoredTree = !!pos.treeJson;
    this.dirty = false;
    this.saving = false;
    this.savedAt = pos.treeUpdatedAt ? new Date(pos.treeUpdatedAt) : null;
  }

  /** Der Baum hat sich geändert: in {@link AUTOSAVE_MS} speichern (jede weitere Änderung schiebt das hinaus). */
  markDirty(): void {
    this.dirty = true;
    this.cancelAutosave();
    this.timer = this.clock.setTimeout(() => { this.timer = undefined; this.flush(); }, CalcTreeSaver.AUTOSAVE_MS);
  }

  cancelAutosave(): void {
    if (this.timer) { this.clock.clearTimeout(this.timer); this.timer = undefined; }
  }

  /** Speichert sofort (Autosave-Timer, Stellungswechsel, Verlassen der Seite) — und schickt alles Offene. */
  flush(): void {
    const bookPuzzleId = this.options.currentId();
    if (this.dirty && bookPuzzleId != null) {
      this.dirty = false;
      const tree = this.options.tree();
      // Leerer Baum: gespeicherten Stand verwerfen (nicht „{}" ablegen).
      if (isEmpty(tree)) {
        this.options.markDone(bookPuzzleId, false);
        if (this.hadStoredTree) { this.hadStoredTree = false; this.outbox.set(bookPuzzleId, null); }
        // Sonst nur einen noch offenen Stand entwerten: ohne das würde ein zuvor
        // fehlgeschlagener Save (Eintrag liegt noch in der Outbox) den gerade bewusst
        // GELEERTEN Baum beim nächsten Senden doch wieder hochschreiben.
        else this.outbox.delete(bookPuzzleId);
      } else {
        this.outbox.set(bookPuzzleId, serializeTree(tree));
      }
    }
    // Alles Offene rausschicken — auch Stände von Stellungen, die inzwischen verlassen wurden.
    this.outbox.flush();
  }

  /** GENAU diesen Stand nach einem Fehlschlag erneut einreihen — aber nur, wenn er noch der jüngste ist: weder ein
   *  inzwischen aufgelaufener neuerer Stand in der Outbox noch ein bereits gesendeter (die Warteschlange prüft das). */
  private static keepUnlessNewer(json: string | null): (waiting: string | null | undefined) => string | null | undefined {
    return waiting => (waiting === undefined ? json : undefined);
  }

  private sendSave(flight: SingleFlight<number, string | null>, json: string): void {
    const bookPuzzleId = flight.key;
    if (this.isCurrent(bookPuzzleId)) this.saving = true;
    this.options.backend().saveTree(bookPuzzleId, json).subscribe({
      next: res => {
        this.options.onSaved();
        if (this.isCurrent(bookPuzzleId)) {
          this.saving = false;
          this.hadStoredTree = true;
          this.savedAt = new Date(res.updatedAt);
        }
        this.options.markDone(bookPuzzleId, true);
        this.outbox.succeeded(flight);
      },
      error: () => {
        if (this.isCurrent(bookPuzzleId)) this.saving = false;
        this.outbox.failed(flight, CalcTreeSaver.keepUnlessNewer(json));
        this.options.onFailed();
      },
    });
  }

  private sendDelete(flight: SingleFlight<number, string | null>): void {
    const bookPuzzleId = flight.key;
    this.options.backend().deleteTree(bookPuzzleId).subscribe({
      next: () => {
        if (this.isCurrent(bookPuzzleId)) this.savedAt = null;
        this.outbox.succeeded(flight);
      },
      error: () => {
        this.outbox.failed(flight, CalcTreeSaver.keepUnlessNewer(null));
        this.options.onFailed();
      },
    });
  }

  /** Gehört die Antwort noch zur angezeigten Stellung? Sonst dürfen `saving`/`savedAt` nicht angefasst
   *  werden — eine spät eintreffende Antwort der ALTEN Stellung schriebe sonst in die neue Ansicht. */
  private isCurrent(bookPuzzleId: number): boolean {
    return this.options.currentId() === bookPuzzleId;
  }
}
