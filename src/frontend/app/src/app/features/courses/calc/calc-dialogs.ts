import { MatDialog } from '@angular/material/dialog';
import { Subscription } from 'rxjs';
import { CalcGradeDialogComponent, CalcGradeDialogResult } from './calc-grade-dialog.component';
import { CalcSettingsDialogComponent } from './calc-settings-dialog.component';
import { CalcTimerDialogComponent, CalcTimerDialogData, CalcTimerDialogResult } from './calc-timer-dialog.component';
import { CalcGrade, normalizeGrade } from './calc-review.util';

/**
 * Der Knopf „Ergebnis" öffnet die Auswahl. Bewusst ein Dialog statt fünf Schaltern in der
 * Seitenspalte: der Modus soll ein Brett bleiben, kein Formular (UI-Dichte-Regel).
 * `onGrade` bekommt eine Stufe oder `null` (= Bewertung ausdrücklich zurücknehmen); Weggeklicktes meldet nichts.
 */
export function openCalcGradeDialog(
  dialog: MatDialog, current: { grade: CalcGrade | null; chosenSan: string | null }, onGrade: (grade: CalcGrade | null) => void,
): Subscription {
  const ref = dialog.open<CalcGradeDialogComponent, unknown, CalcGradeDialogResult>(
    CalcGradeDialogComponent, {
      data: { grade: current.grade, chosenSan: current.chosenSan },
      width: '440px',
      autoFocus: false,
    });
  return ref.afterClosed().subscribe(result => {
    // Durchgelassen wird nur ein ECHTES Ergebnis: eine Zahl = Stufe, `null` = Bewertung
    // ausdrücklich zurücknehmen. Alles andere heißt „weggeklickt, nichts ändern".
    //
    // Bewusst NICHT auf `undefined` prüfen: was hier ankommt, befüllt teils ein fremdes
    // Framework (ein `mat-dialog-close`-Attribut schließt mit dem leeren STRING). Ein solcher
    // Wert liefe über `normalizeGrade('') === null` als `clearGrade` an den Server — wer eine
    // bestehende Bewertung nur ansieht und abbricht, verlöre sie. Ein Löschbefehl muss aus
    // einer AUSDRÜCKLICHEN Handlung kommen, nicht aus einem Vorgabewert.
    if (typeof result === 'number' && Number.isFinite(result)) {
      onGrade(normalizeGrade(result));
    } else if (result === null) {
      onGrade(null);
    }
  });
}

/**
 * Kapitelzeit von Hand nachtragen — für den Fall, dass das Training zu spät gestartet wurde.
 * Bewusst NUR die Kapitel-Uhr (Merkhilfe auf diesem Gerät): die je Stellung gemessene Zeit
 * entsteht aus echten Abschnitten und bliebe sonst keine Messung mehr. `onSeconds` bekommt ganze Sekunden ≥ 0.
 */
export function openCalcTimerDialog(dialog: MatDialog, data: CalcTimerDialogData, onSeconds: (seconds: number) => void): void {
  const ref = dialog.open<CalcTimerDialogComponent, CalcTimerDialogData, CalcTimerDialogResult | undefined>(
    CalcTimerDialogComponent, { data, autoFocus: false });
  ref.afterClosed().subscribe(result => {
    // Nur eine echte Zahl zählt: ein Fremdwert (leerer String vom Framework) ist „weggeklickt".
    if (typeof result !== 'number' || !Number.isFinite(result)) return;
    onSeconds(Math.max(0, Math.floor(result)));
  });
}

/** Brett- und Figurenart — alles, was im Kalkulations-Modus etwas bewirkt. */
export function openCalcSettingsDialog(dialog: MatDialog): void {
  dialog.open(CalcSettingsDialogComponent, { autoFocus: false });
}
