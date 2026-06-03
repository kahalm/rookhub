import { Injectable } from '@angular/core';
import { MatSnackBar, MatSnackBarRef, TextOnlySnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';

/**
 * Optionen für ein Snackbar. Die Nachricht selbst übergibt der Aufrufer fertig
 * übersetzt (oft mit Parametern oder als Server-Fehlertext); hier wird nur die
 * Aktions-Schaltfläche verwaltet.
 */
export interface SnackbarOptions {
  /** i18n-Key der Aktions-Schaltfläche (Default `'common.close'`); `''` = keine Schaltfläche. */
  action?: string;
  /** Auto-Ausblenden in ms (Default 3000); `0` = bleibt bis zur Aktion stehen. */
  duration?: number;
  /** `action` als wörtliches Label statt i18n-Key behandeln (z.B. „OK" im Analyse-Modus). */
  rawAction?: boolean;
}

/**
 * Dünner Wrapper um {@link MatSnackBar} — bündelt die zuvor app-weit ~100× wiederholte
 * `snackBar.open(msg, translate.instant('common.close'), { duration })`-Boilerplate.
 *
 * Die Nachricht übergibt der Aufrufer bereits übersetzt; die Aktions-Beschriftung
 * (meist „Schließen"/„OK") übersetzt der Service. Die Methoden unterscheiden sich nur
 * in der Standard-Anzeigedauer und geben die `MatSnackBarRef` zurück (z.B. für `onAction()`).
 */
@Injectable({ providedIn: 'root' })
export class SnackbarService {
  constructor(private snackBar: MatSnackBar, private translate: TranslateService) {}

  /** Allgemeines Snackbar (Default 3000 ms, Aktion „Schließen"). */
  show(message: string, opts: SnackbarOptions = {}): MatSnackBarRef<TextOnlySnackBar> {
    const { action = 'common.close', duration = 3000, rawAction = false } = opts;
    const label = action ? (rawAction ? action : this.translate.instant(action)) : '';
    return this.snackBar.open(message, label, { duration });
  }

  /** Fehler/Info-Hinweis, 3000 ms. */
  info(message: string, opts: SnackbarOptions = {}): MatSnackBarRef<TextOnlySnackBar> {
    return this.show(message, opts);
  }

  /** Bestätigung/Erfolg, 2000 ms. */
  success(message: string, opts: SnackbarOptions = {}): MatSnackBarRef<TextOnlySnackBar> {
    return this.show(message, { duration: 2000, ...opts });
  }

  /** Schnelles Toggle-Feedback, 1500 ms. */
  quick(message: string, opts: SnackbarOptions = {}): MatSnackBarRef<TextOnlySnackBar> {
    return this.show(message, { duration: 1500, ...opts });
  }

  /** Wichtige/Offline-Hinweise, 5000 ms. */
  warn(message: string, opts: SnackbarOptions = {}): MatSnackBarRef<TextOnlySnackBar> {
    return this.show(message, { duration: 5000, ...opts });
  }

  /** Kopier-Feedback: 2000 ms, ohne Aktions-Schaltfläche. */
  copy(message: string): MatSnackBarRef<TextOnlySnackBar> {
    return this.show(message, { duration: 2000, action: '' });
  }
}
