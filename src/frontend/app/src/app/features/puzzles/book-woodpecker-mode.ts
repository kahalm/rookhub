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
      },
      error: (e: HttpErrorResponse) => {
        if (epoch !== h.loadEpoch) return;
        if (e?.status === 404 || e?.status === 403) { this.missing = true; h.puzzle = null; h.state = 'COURSE_DONE'; return; }
        h.state = 'LOADING';
        h.loadError = true;
      },
    });
  }

  next(): void { this.loadNext(); }

  backToOverview(): void { this.h.router.navigate(['/woodpecker']); }

  /** Meldet den Versuch einmal je Aufgabe; der Fortschritt im Kopf kommt aus der Antwort. */
  recordAttempt(solved: boolean): void {
    const h = this.h;
    const svc = h.woodpeckerService;
    if (!h.inWoodpecker || this.planId == null || !h.puzzle || this.attemptRecorded) return;
    this.attemptRecorded = true;
    svc.recordAttempt(this.planId, h.puzzle.id, solved, h.solveSeconds).subscribe({
      next: plan => { this.plan = plan; },
      error: err => {
        this.attemptRecorded = false;
        h.snackbar.warn(apiErrorText(err, h.translate, 'woodpecker.saveFailed'));
      },
    });
  }
}
