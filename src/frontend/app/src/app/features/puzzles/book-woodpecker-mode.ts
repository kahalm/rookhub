import { HttpErrorResponse } from '@angular/common/http';
import { apiErrorText } from '../../core/api-error';
import { daysLeft, WoodpeckerCycle, WoodpeckerPlan, WoodpeckerService } from '../woodpecker/woodpecker.service';
import { BookModeHost, BookModeStrategy } from './book-mode-host';

/** Was der Woodpecker-Modus zusätzlich zum {@link BookModeHost} braucht. */
export interface BookWoodpeckerHost extends BookModeHost {
  readonly inWoodpecker: boolean;
  readonly woodpeckerService: WoodpeckerService;
}

/**
 * Woodpecker-Modus des Buch-Lösers (/woodpecker/:planId/solve, Wunsch 2026-11-02: „egal ob über Woodpecker oder
 * Kurs — die UI soll die gleiche sein“). Bis 0.881 hatte Woodpecker einen eigenen, schlankeren Löser; jetzt läuft er
 * im Kurs-Löser mit Kommentaren, Tipps, Spielweise und Aktionszeile, nur Laden und Melden gehen an `/api/woodpecker`.
 * Der Server bestimmt die Aufgabe (Zyklus 1 in Kursreihenfolge, danach zufällig aus dem Satz) und wertet je Zyklus
 * nur den ersten Versuch — Wiederholen nach einem Fehler ist erlaubt, ändert aber nichts mehr.
 */
export class BookWoodpeckerMode implements BookModeStrategy {
  planId: number | null = null;
  plan: WoodpeckerPlan | null = null;
  /** Training gibt es nicht (mehr) oder gehört jemand anderem (404/403) — kein Verbindungsfehler. */
  missing = false;
  /** Dieser Aufgabe wurde schon ein Versuch gemeldet (der Server wertet ohnehin nur den ersten). */
  attemptRecorded = false;
  /** Nach einem Wiederholen: der erste Versuch ist schon gewertet. */
  repeatHint = false;
  /** Aufgaben, deren Versuch in der Offline-Warteschlange liegt (noch nicht beim Server, F1-013). Liefert der Server
   *  sie noch einmal (in Zyklus 1 schließt erst der gespeicherte Versuch sie aus), wird nichts zweites gemeldet. */
  private readonly queuedIds = new Set<number>();
  /** Meldung unterwegs: „Weiter“ wartet darauf, sonst käme GET next vor dem POST an und brächte dieselbe Aufgabe. */
  private recording = false;
  private nextAfterRecord = false;

  constructor(private readonly h: BookWoodpeckerHost) {}

  /** Laufender (letzter) Zyklus. */
  get cycle(): WoodpeckerCycle | null {
    const p = this.plan;
    return p?.cycles.length ? p.cycles[p.cycles.length - 1] : null;
  }

  get daysLeft(): number { const c = this.cycle; return c ? daysLeft(c.deadline) : 0; }

  /** Fortschritt im Zyklus (ab Zyklus 2; in Zyklus 1 wächst der Satz noch). */
  get percent(): number {
    const p = this.plan;
    const c = this.cycle;
    return p && c && p.setSize ? Math.round(100 * c.attempted / p.setSize) : 0;
  }

  /** Abschlusstext je nach Stand: Methode fertig oder Zyklus geschafft (Pause/nächster Zyklus). */
  get doneText(): string {
    const p = this.plan;
    if (!p) return '';
    if (p.status === 'completed') return this.h.translate.instant('woodpecker.doneCompleted');
    return this.h.translate.instant('woodpecker.doneCycle', { n: p.currentCycle });
  }

  start(planId: number): void {
    this.planId = planId;
    this.loadNext();
  }

  loadNext(): void {
    const h = this.h;
    const svc = h.woodpeckerService;
    if (this.planId == null) return;
    const epoch = ++h.loadEpoch;
    h.loadError = false;
    h.retryFn = () => this.loadNext();
    h.state = 'LOADING';
    const lang = h.requestLang;
    svc.next(this.planId, lang).subscribe({
      next: r => {
        if (epoch !== h.loadEpoch) return;
        this.plan = r.plan;
        this.missing = false;
        this.repeatHint = false;
        this.attemptRecorded = false;
        if (!r.puzzle) { h.puzzle = null; h.state = 'COURSE_DONE'; return; }
        h.gaveUp = false;
        h.puzzle = r.puzzle;
        h.afterLineLoaded(r.puzzle, lang);
        h.setupPuzzle(r.puzzle);
        // Versuch liegt noch in der Warteschlange: der zählt — Wiederholen ändert nichts mehr.
        if (this.queuedIds.has(r.puzzle.id)) this.repeatHint = true;
      },
      error: (e: HttpErrorResponse) => {
        if (epoch !== h.loadEpoch) return;
        if (e?.status === 404 || e?.status === 403) { this.missing = true; h.puzzle = null; h.state = 'COURSE_DONE'; return; }
        h.state = 'LOADING';
        h.loadError = true;
      },
    });
  }

  next(): void {
    if (this.recording) { this.nextAfterRecord = true; return; }
    this.loadNext();
  }

  /** Neuer Durchgang an `puzzleId` (setupPuzzle ohne `sameAttempt`): meldbar, außer sein Versuch wartet noch. */
  resetAttempt(puzzleId: number): void {
    this.attemptRecorded = this.queuedIds.has(puzzleId);
  }

  backToOverview(): void { this.h.router.navigate(['/woodpecker']); }

  /**
   * Meldet den Versuch einmal je Aufgabe; der Fortschritt im Kopf kommt aus der Antwort. Offline oder bei Netz-/5xx-
   * Fehler geht er in die Offline-Warteschlange (gleicher Body) statt verloren — und bleibt gemeldet: sonst würde ein
   * späteres Lösen nach „Nochmal“ als erster (gewerteter) Versuch zählen (F1-013).
   */
  recordAttempt(solved: boolean): void {
    const h = this.h;
    const svc = h.woodpeckerService;
    if (!h.inWoodpecker || this.planId == null || !h.puzzle || this.attemptRecorded) return;
    this.attemptRecorded = true;
    const planId = this.planId;
    const puzzleId = h.puzzle.id;
    const timeSeconds = h.solveSeconds;
    const hintsUsed = h.hintsUsed;
    const queue = () => {
      if (h.offlineQueue.enqueue('POST', `/api/woodpecker/${planId}/attempts`, { bookPuzzleId: puzzleId, solved, timeSeconds, hintsUsed }))
        this.queuedIds.add(puzzleId);
      else h.snackbar.warn(h.translate.instant('woodpecker.saveFailed'));
    };
    if (!navigator.onLine) { queue(); return; }
    this.recording = true;
    svc.recordAttempt(planId, puzzleId, solved, timeSeconds, hintsUsed).subscribe({
      next: plan => { this.plan = plan; this.settled(); },
      error: err => {
        const status = (err as HttpErrorResponse | null)?.status ?? 0;
        // 4xx (außer 408/429) = der Server lehnt ab (Zyklus vorbei, Aufgabe nicht im Satz): Nachsenden hilft nicht.
        if (status >= 400 && status < 500 && status !== 408 && status !== 429)
          h.snackbar.warn(apiErrorText(err, h.translate, 'woodpecker.saveFailed'));
        else queue();
        this.settled();
      },
    });
  }

  private settled(): void {
    this.recording = false;
    if (!this.nextAfterRecord) return;
    this.nextAfterRecord = false;
    this.loadNext();
  }
}
