/**
 * Anmelden, Registrieren und Passwort-Seiten im Kinder-Stil (UI-Sweep 2026-10-11, r2-k-login). Die Masken selbst sind
 * RookHubs geteilte Komponenten (`@rh/features/auth`) — KidHub ändert an ihnen nichts, sondern kleidet sie nur ein:
 * weiße runde Karte, Überschrift in `--kid-title`, großer runder Anmelden-Knopf, die Links darunter als Pillen. Dazu
 * steht oben derselbe „← Start"-Knopf wie auf den übrigen Seiten (App-Hülle, {@link isAuthUrl}).
 */

const AUTH_PATHS = ['/login', '/register', '/forgot-password', '/reset-password'];

/** Pfad ohne Abfrage und Anker ist eine der Anmelde-Masken. */
export function isAuthUrl(url: string): boolean {
  return AUTH_PATHS.includes(url.split(/[?#]/)[0].replace(/\/+$/, ''));
}

const MASKS = ':is(app-login, app-register, app-forgot-password, app-reset-password)';

/** In den Stilen der App-Hülle (`:host ::ng-deep`) — gilt nur auf der Kinderseite. */
export const KID_AUTH_STYLES = `
  :host ::ng-deep ${MASKS} .auth-container { min-height: 0; flex-direction: column; align-items: center; justify-content: flex-start; padding: 8px 16px 24px; }
  /* UI-Sweep 2026-10-11 (d1-k-kid-auth-card-left-wide): Registrieren/Passwort-Masken sind Zeilen — als Spalte mit
     align-items: center steht die Karte wie bei der Anmeldung mittig. */
  :host ::ng-deep ${MASKS} .mat-mdc-card {
    --mat-sys-primary: var(--kid-title);
    --mat-card-elevated-container-color: var(--kid-card);
    --mat-card-elevated-container-elevation: none;
    --mat-card-elevated-container-shape: 24px;
    --mat-card-title-text-size: 1.6rem;
    --mat-card-title-text-weight: 800;
    --mat-card-title-text-line-height: 1.2;
    --mat-form-field-outlined-container-shape: 14px;
    /* z2-k-register-hinweise: Feldhinweise („Ein Spitzname …") in Kinderschrift statt Materials 12 px. */
    --mat-form-field-subscript-text-size: 1rem;
    --mat-form-field-subscript-text-line-height: 1.35;
    color: #1f2d3d; box-shadow: 0 6px 0 var(--kid-shadow); padding: 6px 4px 10px;
  }
  :host ::ng-deep ${MASKS} .mat-mdc-card-title { color: var(--kid-title); }
  /* z2-k-register-hinweise: „Für Eltern: Was wir speichern …" nicht in 0.8rem. */
  :host ::ng-deep ${MASKS} .privacy-note { font-size: 1rem; }
  /* UI-Sweep 2026-10-11 (d1-k-kid-primary-button-restyled): die geteilten Masken nutzen seit 0.752.0 mat-flat-button —
     die Variablen heißen dort --mat-button-filled-*, die alten protected-* griffen nicht mehr (Knopf war blau und klein). */
  :host ::ng-deep ${MASKS} .auth-form button[type=submit] {
    --mat-button-filled-container-color: var(--kid-green-strong);
    --mat-button-filled-label-text-color: #fff;
    --mat-button-filled-container-shape: 999px;
    --mat-button-filled-container-height: 52px;
    --mat-button-filled-label-text-size: 1.2rem;
    --mat-button-filled-label-text-weight: 800;
    width: 100%; min-height: 52px; box-shadow: 0 5px 0 var(--kid-shadow);
  }
  :host ::ng-deep ${MASKS} .mat-mdc-card-actions { display: flex; flex-wrap: wrap; gap: 10px; justify-content: space-between; padding: 8px 16px; }
  /* UI-Sweep 2026-10-11 (d1-k-kid-auth-pills-offset): 16 px seitlich wie mat-card-content — die Pillen fluchten mit Feldern und Knopf. */
  :host ::ng-deep ${MASKS} .mat-mdc-card-actions a {
    --mat-button-text-container-shape: 999px;
    --mat-button-text-label-text-color: var(--kid-title);
    --mat-button-text-label-text-weight: 800;
    min-height: 44px; padding: 6px 16px; border: 2px solid var(--kid-title); background: var(--kid-card);
    box-shadow: 0 3px 0 var(--kid-shadow); margin: 0;
  }
  :host ::ng-deep ${MASKS} .auth-required { border-left-color: var(--kid-title); background: var(--kid-sky); border-radius: 12px; }
`;
