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
  :host ::ng-deep ${MASKS} .auth-container { min-height: 0; justify-content: flex-start; padding: 8px 16px 24px; }
  :host ::ng-deep ${MASKS} .mat-mdc-card {
    --mat-sys-primary: var(--kid-title);
    --mat-card-elevated-container-color: var(--kid-card);
    --mat-card-elevated-container-elevation: none;
    --mat-card-elevated-container-shape: 24px;
    --mat-card-title-text-size: 1.6rem;
    --mat-card-title-text-weight: 800;
    --mat-card-title-text-line-height: 1.2;
    --mat-form-field-outlined-container-shape: 14px;
    color: #1f2d3d; box-shadow: 0 6px 0 var(--kid-shadow); padding: 6px 4px 10px;
  }
  :host ::ng-deep ${MASKS} .mat-mdc-card-title { color: var(--kid-title); }
  :host ::ng-deep ${MASKS} .auth-form button[type=submit] {
    --mat-button-protected-container-color: var(--kid-green-strong);
    --mat-button-protected-label-text-color: #fff;
    --mat-button-protected-container-shape: 999px;
    --mat-button-protected-container-height: 52px;
    --mat-button-protected-label-text-size: 1.2rem;
    --mat-button-protected-label-text-weight: 800;
    width: 100%; min-height: 52px; box-shadow: 0 5px 0 var(--kid-shadow);
  }
  :host ::ng-deep ${MASKS} .mat-mdc-card-actions { display: flex; flex-wrap: wrap; gap: 10px; justify-content: space-between; }
  :host ::ng-deep ${MASKS} .mat-mdc-card-actions a {
    --mat-button-text-container-shape: 999px;
    --mat-button-text-label-text-color: var(--kid-title);
    --mat-button-text-label-text-weight: 800;
    min-height: 44px; padding: 6px 16px; border: 2px solid var(--kid-title); background: var(--kid-card);
    box-shadow: 0 3px 0 var(--kid-shadow); margin: 0;
  }
  :host ::ng-deep ${MASKS} .auth-required { border-left-color: var(--kid-title); background: var(--kid-sky); border-radius: 12px; }
`;
