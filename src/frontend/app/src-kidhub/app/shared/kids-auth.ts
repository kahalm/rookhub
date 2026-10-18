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

/** Pfad ohne Abfrage und Anker ist `/forgot-password`. */
export function isForgotPasswordUrl(url: string): boolean {
  return url.split(/[?#]/)[0].replace(/\/+$/, '') === '/forgot-password';
}

/**
 * „Passwort vergessen" in der Kinder-Fassung (d3-k-forgot-stacked-muted-paragraphs): unter dem Knopf standen zwei leise
 * Erwachsenen-Absätze offen übereinander (`p.kids-mail` „die Mail kann ›RookHub‹ zeigen", `p.no-email` „Keine E-Mail im
 * Konto? … Adresse"). Beide richten sich an Eltern — sie kommen wie auf den Rechtsseiten in EIN zugeklapptes
 * `<details class="kid-parent">` mit der Beschriftung `label` („Für Eltern: keine Mail bekommen?"). Die geteilte
 * Komponente bleibt unberührt (DESIGN 8.6); verschoben werden die von Angular gezeichneten Absätze selbst (ihre Bindungen
 * bleiben, ihre Bedingungen hängen nur an der Oberfläche). Gibt zurück, ob umgebaut wurde.
 */
export function foldForgotPassword(root: ParentNode, label: string): boolean {
  const content = root.querySelector('app-forgot-password mat-card-content');
  if (!content || content.querySelector(':scope > details.kid-parent')) return false;
  const notes = Array.from(content.querySelectorAll(':scope > p.kids-mail, :scope > p.no-email'));
  if (notes.length === 0) return false;
  const doc = content.ownerDocument;
  const details = doc.createElement('details');
  details.className = 'kid-parent';
  const summary = doc.createElement('summary');
  const text = doc.createElement('span');
  text.className = 'kid-parent-label';
  text.textContent = label;
  summary.append(text);
  details.append(summary);
  notes[0].before(details);
  details.append(...notes);
  return true;
}

/** Setzt die Beschriftung neu (Sprachwechsel); ohne umgebaute Seite passiert nichts. */
export function setForgotPasswordTexts(root: ParentNode, label: string): void {
  const text = root.querySelector('app-forgot-password details.kid-parent .kid-parent-label');
  if (text) text.textContent = label;
}

/** Farbe der Feldhinweise auf der weißen Karte — leiser als der Text (#1f2d3d), aber ≥ 4,5:1 (b1-k-register-hinweise-abstand). */
export const KID_AUTH_HINT_COLOR = '#4a6380';

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
  /* b1-k-register-hinweise-abstand: die 1rem großen Hinweise klebten dunkel und 16 px eingerückt unter dem Feld, nur
     4–6 px über dem nächsten Feld bzw. dem Knopf — sie wirkten wie eigener Text. Jetzt an der Feldkante, leiser
     (KID_AUTH_HINT_COLOR, 6,1:1 auf Weiß) und mit 14 px Luft nach unten: klar der Hinweis zum Feld darüber. */
  /* d3-k-kid-login-feldluecken-ungleich: Zeile + 14 px galten für JEDES Feld — auf der Anmeldung (keine Hinweise) blieben
     44–50 px leer zwischen den Feldern. Die 14 px nur unter Feldern mit Hinweis; ohne Hinweis und ohne Fehler bleibt die
     leere Unterzeile 8 px flach (:has ab Chrome 105, Boox 111 ok). Mit Fehler gilt Materials Höhe — die Meldung steht. */
  :host ::ng-deep ${MASKS} .mat-mdc-form-field:has(.mat-mdc-form-field-hint) .mat-mdc-form-field-subscript-wrapper { margin-bottom: 14px; }
  :host ::ng-deep ${MASKS} .mat-mdc-form-field:not(:has(.mat-mdc-form-field-hint, .mat-mdc-form-field-error)) .mat-mdc-form-field-subscript-wrapper { height: 8px; }
  :host ::ng-deep ${MASKS} .mat-mdc-form-field-hint-wrapper,
  :host ::ng-deep ${MASKS} .mat-mdc-form-field-error-wrapper { padding: 4px 4px 0; }
  :host ::ng-deep ${MASKS} .mat-mdc-form-field-hint { color: ${KID_AUTH_HINT_COLOR}; }
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
  :host ::ng-deep ${MASKS} .mat-mdc-card-actions { display: flex; flex-wrap: wrap; gap: 10px; justify-content: space-between; padding: 16px 16px 8px; }
  /* a1-k-auth-mail-link-button-gap: oben 16 px statt 8 — die Pillenreihe klebte an der Kontaktzeile („Keine E-Mail …“)
     bzw. am Hauptknopf, Abschnittsabstand 16–24 px (DESIGN 3.2). */
  /* UI-Sweep 2026-10-11 (d1-k-kid-auth-pills-offset): 16 px seitlich wie mat-card-content — die Pillen fluchten mit Feldern und Knopf. */
  :host ::ng-deep ${MASKS} .mat-mdc-card-actions a {
    --mat-button-text-container-shape: 999px;
    --mat-button-text-label-text-color: var(--kid-title);
    --mat-button-text-label-text-weight: 800;
    min-height: 44px; padding: 6px 16px; border: 2px solid var(--kid-title); background: var(--kid-card);
    box-shadow: 0 3px 0 var(--kid-shadow); margin: 0;
  }
  :host ::ng-deep ${MASKS} .auth-required { border-left-color: var(--kid-title); background: var(--kid-sky); border-radius: 12px; }
  /* d3-k-forgot-stacked-muted-paragraphs: Eltern-Hinweise zugeklappt — Aufklapper wie auf den Rechtsseiten
     (KID_PRIVACY_STYLES): Trennlinie, 44-px-Zeile, ▸-Marke, Beschriftung fett in --kid-title. */
  :host ::ng-deep ${MASKS} details.kid-parent { margin-top: 12px; border-top: 1px solid #dbe5f0; }
  :host ::ng-deep ${MASKS} details.kid-parent > summary {
    display: flex; align-items: center; gap: 8px; min-height: 44px; cursor: pointer; list-style: none;
  }
  :host ::ng-deep ${MASKS} details.kid-parent > summary::-webkit-details-marker { display: none; }
  :host ::ng-deep ${MASKS} details.kid-parent > summary::before {
    content: '▸'; color: var(--kid-title); font-weight: 800; transition: transform .15s;
  }
  :host ::ng-deep ${MASKS} details.kid-parent[open] > summary::before { transform: rotate(90deg); }
  :host ::ng-deep ${MASKS} details.kid-parent > summary:focus-visible { outline: 3px solid var(--kid-title); outline-offset: 2px; }
  :host ::ng-deep ${MASKS} details.kid-parent .kid-parent-label { font-weight: 700; color: var(--kid-title); }
  :host ::ng-deep ${MASKS} details.kid-parent > p { margin: 0 0 8px; }
  @media (prefers-reduced-motion: reduce) {
    :host ::ng-deep ${MASKS} details.kid-parent > summary::before { transition: none; }
  }
`;
