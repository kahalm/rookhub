import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable, of } from 'rxjs';
import { map } from 'rxjs/operators';
import { PreferencesService } from './preferences.service';
import { SolveModeDialogComponent, SolveModeDialogData } from '../shared/solve-mode/solve-mode-dialog.component';

/** Die beiden Spielweisen. Die Zeichenketten sind der Vertrag zum Server (Spalte `Mode`
 *  auf PuzzleAttempts/CourseAttempts/BookPuzzleAttempts/EndlessSessions/WeeklyPostAttempts)
 *  — nicht umbenennen ohne Migration. */
export type SolveMode = 'training' | 'easy';

/** Speicher-Schlüssel im localStorage — Namensschema wie die übrigen Einstellungen. */
const STORE_KEY = 'rookhub_solve_modes';

/**
 * Deckel gegen unbegrenztes Wachstum: pro Kurs entsteht ein Eintrag, und die Zahl der Kurse
 * wächst. Bei Überschreitung fliegen die ältesten Einträge raus — die werden ohnehin am
 * ehesten neu erfragt.
 */
const MAX_EINTRAEGE = 200;

interface Eintrag { mode: SolveMode; at: number; }

/**
 * Spielweise je Bereich: „Einfachmodus" (Figuren normal ziehbar) oder „Trainingsmodus"
 * (Brett eingefroren bzw. die vom Nutzer eingestellte Visualisierungsstufe).
 *
 * Gefragt wird **einmalig je Bereich** — für Kurse zusätzlich je Kurs, weil sich ein
 * Taktikbuch anders trainiert als ein Eröffnungskurs. Danach gilt die gemerkte Wahl
 * wortlos; umschalten kann man jederzeit über die Aktionsleiste des Solvers.
 *
 * Die Wahl liegt im localStorage, nicht auf dem Server: sie steht in einer Reihe mit den
 * übrigen Anzeige-Einstellungen (Brett-Thema, Figurensatz, Visualisierungsstufe), gilt wie
 * diese je Gerät und funktioniert auch für anonyme Besucher — die lösen Tagespuzzles und
 * geteilte Puzzles, hätten serverseitig aber kein Konto, an dem die Wahl hängen könnte.
 */
@Injectable({ providedIn: 'root' })
export class SolveModeService {
  private readonly dialog = inject(MatDialog);
  private readonly prefs = inject(PreferencesService);

  /** Bereichs-Schlüssel. Kurse/Bücher bekommen ihre Id angehängt. */
  static scopeCourse(bookId: number): string { return `course:${bookId}`; }
  static scopeWeekly(weeklyId: number): string { return `weekly:${weeklyId}`; }

  /** Gemerkte Wahl, oder null wenn für diesen Bereich noch nie gefragt wurde. */
  get(scope: string): SolveMode | null {
    const alle = this.lesen();
    const e = alle[scope];
    return e && (e.mode === 'easy' || e.mode === 'training') ? e.mode : null;
  }

  set(scope: string, mode: SolveMode): void {
    const alle = this.lesen();
    alle[scope] = { mode, at: Date.now() };
    this.schreiben(alle);
  }

  /** Wahl für einen Bereich vergessen — der nächste Einstieg fragt wieder. */
  clear(scope: string): void {
    const alle = this.lesen();
    if (!(scope in alle)) return;
    delete alle[scope];
    this.schreiben(alle);
  }

  /**
   * Liefert die Spielweise für einen Bereich und fragt nur, wenn es noch keine gibt.
   * `data.scopeLabel` benennt im Dialog, worum es geht (Kursname, „Tagespuzzle", …).
   *
   * Bewusst ein blockierender Dialog: die Wahl entscheidet, wie gelöst UND wie gewertet wird,
   * und lässt sich hinterher nicht rückwirkend korrigieren. Sie kommt aber nur EINMAL je
   * Bereich — wer schon gewählt hat, sieht nie wieder einen Dialog.
   */
  ensure(scope: string, data: SolveModeDialogData = {}): Observable<SolveMode> {
    const gemerkt = this.get(scope);
    if (gemerkt) return of(gemerkt);
    return this.dialog
      .open(SolveModeDialogComponent, { width: '460px', maxWidth: '94vw', disableClose: true, data })
      .afterClosed()
      .pipe(map((gewaehlt: SolveMode | undefined) => {
        // Abgebrochen (Escape ist durch disableClose aus, aber defensiv) → Trainingsmodus,
        // das bisherige Verhalten. Gemerkt wird nur eine echte Wahl.
        const mode: SolveMode = gewaehlt === 'easy' ? 'easy' : 'training';
        if (gewaehlt) this.set(scope, mode);
        return mode;
      }));
  }

  /**
   * Visualisierungsstufe zur Spielweise. „Einfach" ist immer Stufe 0 (Drag & Drop);
   * „Training" ist die GLOBAL eingestellte Stufe, mindestens 1 — wer Blindspiel, Dunkel oder
   * Unsichtbar gewählt hat, behält das. Ein hartes Erzwingen von Stufe 1 würde diese Nutzer
   * stillschweigend herabstufen.
   */
  levelFor(mode: SolveMode): number {
    return mode === 'easy' ? 0 : Math.max(1, this.prefs.visualization);
  }

  /** Umkehrung: aus einer Stufe die Spielweise ableiten — für den Fall, dass der Nutzer die
   *  Stufe direkt über die Einstellungen ändert und die gemerkte Wahl mitziehen soll. */
  modeForLevel(level: number): SolveMode {
    return level > 0 ? 'training' : 'easy';
  }

  private lesen(): Record<string, Eintrag> {
    try {
      const roh = localStorage.getItem(STORE_KEY);
      if (!roh) return {};
      const o = JSON.parse(roh);
      return o && typeof o === 'object' && !Array.isArray(o) ? o : {};
    } catch {
      return {};   // kaputter/gesperrter Speicher: lieber neu fragen als abstürzen
    }
  }

  private schreiben(alle: Record<string, Eintrag>): void {
    const keys = Object.keys(alle);
    if (keys.length > MAX_EINTRAEGE) {
      // Älteste zuerst wegwerfen; sie werden beim nächsten Einstieg einfach neu erfragt.
      keys.sort((a, b) => (alle[a]?.at || 0) - (alle[b]?.at || 0));
      for (const k of keys.slice(0, keys.length - MAX_EINTRAEGE)) delete alle[k];
    }
    try { localStorage.setItem(STORE_KEY, JSON.stringify(alle)); } catch { /* Privatmodus */ }
  }
}
