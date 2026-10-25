import { Observable } from 'rxjs';
import {
  CalcReview, CalcReviewPatch, applyReviewPatch, emptyReview, isNoopPatch, mergeReviewPatch, normalizeGrade,
} from './calc-review.util';
import { CalcPosition, CalcPositionListItem, CalcReviewSaved } from './calculation.service';
import { KeyedSingleFlightQueue, SingleFlight } from './keyed-single-flight-queue';

export interface CalcReviewControllerOptions {
  /** Die Sprunglisten-Zeilen (ALLE Stellungen des Buchs) — dort stehen Wahl, Zeit und Stufe in der Übersicht. */
  items: () => CalcPositionListItem[];
  /** Die angezeigte Stellung (BookPuzzleId) oder null. */
  currentId: () => number | null;
  /** Speichern (Server oder Gerätespeicher). */
  send: (bookPuzzleId: number, patch: CalcReviewPatch) => Observable<CalcReviewSaved>;
  /** Summen sind überholt (eigene Änderung oder Server-Antwort) — die Ansicht rechnet selbst. */
  onChanged: () => void;
  /** Eine Speicherung kam an. */
  onSaved: () => void;
  /** Eine Speicherung ist gescheitert (sie wartet wieder in der Warteschlange). */
  onSaveFailed: () => void;
}

/**
 * Festlegung, Rechenzeit und Selbstbewertung JE STELLUNG (Zerlegung Z20, aus `CalculationComponent`). Anders als die
 * Kapitel-Uhr gehören diese Werte dem Server (eigene Spalten, auswertbar) und hängen an der Zeile (UserId,
 * BookPuzzleId). Hier stehen der Stand der angezeigten Stellung ({@link review}), die optimistische Anzeige samt
 * Spiegel in der Sprungliste und die Sende-Warteschlange; Dialoge, Stoppuhr und Baum bleiben in der Komponente.
 */
export class CalcReviewController {
  /** Stand der ANGEZEIGTEN Stellung (Spiegel der Sprunglisten-Zeile). */
  review: CalcReview = emptyReview();

  /** Je Stellung immer nur EINE Anfrage unterwegs — wie beim Baum-Speichern: der Server-Upsert
   *  kennt kein Versions-Token, zwei parallele Anfragen könnten vertauscht ankommen (und ein
   *  Zeit-Delta doppelt zählen, wenn eine davon wiederholt werden muss). */
  private readonly outbox = new KeyedSingleFlightQueue<number, CalcReviewPatch>(flight => this.dispatch(flight));

  constructor(private readonly options: CalcReviewControllerOptions) {}

  /** Stand der Stellung übernehmen: aus der Sprunglisten-Zeile, ergänzt um das, was die
   *  Einzelstellung mitliefert (falls der Server die Werte auch dort führt). */
  load(pos: CalcPosition): void {
    const item = this.find(pos.id);
    this.review = {
      chosenSan: pos.chosenSan !== undefined ? pos.chosenSan : (item?.chosenSan ?? null),
      chosenUci: pos.chosenUci !== undefined ? pos.chosenUci : (item?.chosenUci ?? null),
      secondsSpent: pos.secondsSpent !== undefined ? pos.secondsSpent : (item?.secondsSpent ?? 0),
      grade: normalizeGrade(pos.grade !== undefined ? pos.grade : (item?.grade ?? null)),
    };
    this.writeToList(pos.id);
  }

  /** Optimistisch anzeigen + an den Server einreihen (nur mit angezeigter Stellung). */
  set(patch: CalcReviewPatch): void {
    const bookPuzzleId = this.options.currentId();
    if (bookPuzzleId == null) return;
    this.review = applyReviewPatch(this.review, patch);
    this.writeToList(bookPuzzleId);
    this.options.onChanged();
    this.queue(bookPuzzleId, patch);
  }

  /**
   * Gemessene Sekunden gutschreiben und als DELTA einreihen. Auch lokal sofort: die Sprungliste soll die Zeit nicht
   * erst nach der Server-Antwort zeigen (die Antwort korrigiert die Zahl gleich darauf). Jedes Delta bringt seine
   * eigene Marke mit (`token`): der Server ADDIERT die Zeit, ein Wiederholversuch darf sie nicht doppelt buchen.
   */
  creditSeconds(bookPuzzleId: number, seconds: number, token: string): void {
    if (this.isCurrent(bookPuzzleId)) {
      this.review = applyReviewPatch(this.review, { secondsDelta: seconds });
    } else {
      const item = this.find(bookPuzzleId);
      if (item) item.secondsSpent = (item.secondsSpent ?? 0) + seconds;
    }
    this.writeToList(bookPuzzleId);
    this.options.onChanged();
    this.queue(bookPuzzleId, { secondsDelta: seconds, secondsToken: token });
  }

  /** Alles Wartende rausschicken (Stellungswechsel, Verlassen der Seite). */
  flush(): void {
    this.outbox.flush();
  }

  private queue(bookPuzzleId: number, patch: CalcReviewPatch): void {
    const merged = mergeReviewPatch(this.outbox.get(bookPuzzleId) ?? {}, patch);
    if (isNoopPatch(merged)) this.outbox.delete(bookPuzzleId);
    else this.outbox.set(bookPuzzleId, merged);
    this.flush();
  }

  private dispatch(flight: SingleFlight<number, CalcReviewPatch>): void {
    const { key: bookPuzzleId, value: patch } = flight;
    this.options.send(bookPuzzleId, patch).subscribe({
      next: res => {
        this.options.onSaved();
        this.applyServer(bookPuzzleId, res);
        this.outbox.succeeded(flight);
      },
      error: () => {
        // Genau diesen Stand wieder einreihen: ein inzwischen aufgelaufener neuerer gewinnt bei
        // Wahl/Punkten, die Zeit-Deltas addieren sich (gemessene Zeit geht nie verloren).
        // WICHTIG: `patch` steht vorn, damit SEINE Zeit-Marke erhalten bleibt — der Fehler kann
        // ein Timeout sein, die Anfrage also angekommen. Mit einer neuen Marke würde der Server
        // die schon verbuchte Zeit ein zweites Mal addieren.
        this.outbox.failed(flight, waiting => {
          const merged = mergeReviewPatch(patch, waiting ?? {});
          return isNoopPatch(merged) ? undefined : merged;
        });
        // Bewusst kein sofortiger Neuversuch (sonst heiße Schleife bei totem Server) —
        // der nächste Stellungswechsel/Verlassen schickt es mit.
        this.options.onSaveFailed();
      },
    });
  }

  /** Antwort des Servers ist die Wahrheit — vor allem bei `secondsSpent` (dort wird addiert). */
  private applyServer(bookPuzzleId: number, res: Partial<CalcReviewSaved> | null | undefined): void {
    if (!res) return;
    // Ein noch offener Änderungswunsch derselben Stellung gehört NICHT überschrieben — die
    // Server-Antwort kennt ihn noch nicht.
    const pending = this.outbox.get(bookPuzzleId);
    const item = this.find(bookPuzzleId);
    if (item) {
      if (res.chosenSan !== undefined) item.chosenSan = res.chosenSan;
      if (res.chosenUci !== undefined) item.chosenUci = res.chosenUci;
      // Liegt schon ein neueres Zeit-Delta in der Warteschlange, ist die Server-Zahl überholt:
      // die Zeile würde sonst kurz zu klein anzeigen und erst nach der nächsten Antwort springen.
      if (typeof res.secondsSpent === 'number' && !pending?.secondsDelta) {
        item.secondsSpent = res.secondsSpent;
      }
      if (res.grade !== undefined) item.grade = normalizeGrade(res.grade);
    }
    if (this.isCurrent(bookPuzzleId) && !pending) {
      this.review = {
        chosenSan: res.chosenSan ?? null,
        chosenUci: res.chosenUci ?? null,
        secondsSpent: res.secondsSpent ?? this.review.secondsSpent,
        grade: normalizeGrade(res.grade),
      };
    }
    this.options.onChanged();
  }

  /** Sprunglisten-Zeile nachziehen (Zeit/Stufe/Wahl stehen dort in der Übersicht). */
  private writeToList(bookPuzzleId: number): void {
    const item = this.find(bookPuzzleId);
    if (!item) return;
    item.chosenSan = this.review.chosenSan;
    item.chosenUci = this.review.chosenUci;
    item.secondsSpent = this.review.secondsSpent;
    item.grade = this.review.grade;
  }

  private isCurrent(bookPuzzleId: number): boolean {
    return this.options.currentId() === bookPuzzleId;
  }

  private find(bookPuzzleId: number): CalcPositionListItem | undefined {
    return this.options.items().find(p => p.id === bookPuzzleId);
  }
}
