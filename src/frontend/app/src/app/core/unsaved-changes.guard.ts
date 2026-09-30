import { CanDeactivateFn } from '@angular/router';
import { Observable } from 'rxjs';

/** Eine Seite mit Arbeitsstand, der beim Verlassen verloren ginge — sie entscheidet selbst, ob sie nachfragt. */
export interface LeaveConfirm {
  /** `true` = darf ohne Rückfrage gehen; sonst die Antwort auf die Rückfrage. */
  canLeave(): boolean | Observable<boolean>;
}

/**
 * Fragt vor dem Wegnavigieren innerhalb der App (Zurück-Pfeil, Browser-Zurück, Menü), wenn die Seite ungespeicherte
 * Änderungen hat. Neu laden oder Tab schließen fängt die Seite selbst über `beforeunload` ab.
 *
 * <p><b>Warum:</b> Auf der Partie-Korrektur (`/games/:id/edit`) gingen bestätigte Stellen, gewählte Lesarten und
 * Kopfdaten mit einem Tipp auf den Zurück-Pfeil ohne Rückfrage verloren — `dirty` wurde geführt, aber nirgends
 * gelesen, und einen Zwischenspeicher gibt es nicht (Codereview W3 F4-003).</p>
 */
export const unsavedChangesGuard: CanDeactivateFn<LeaveConfirm> = component => component?.canLeave?.() ?? true;
