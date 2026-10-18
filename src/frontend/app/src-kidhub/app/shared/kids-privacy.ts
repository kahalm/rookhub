import { KID_PAGE_WIDTH } from './kids-layout';

/**
 * Datenschutz in der Kinder-Fassung (UI-Sweep 2026-10-10, k-privacy). Die Seite selbst ist RookHubs geteilte
 * `PrivacyComponent` (Route `/privacy`, Texte und Ruecklink dort) — KidHub aendert daran nichts, sondern nur, wie sie
 * hier aussieht: Fliesstext 16 px, „Das Wichtigste in Kuerze" als hervorgehobene Karte oben, die ausfuehrlichen
 * Abschnitte fuer Eltern zum Aufklappen. Vorher eine 3 500 px lange Karte mit ~13-px-Schrift.
 */

/** Pfad ohne Abfrage und Anker. */
function pathOf(url: string): string {
  return url.split(/[?#]/)[0].replace(/\/+$/, '');
}

/** Pfad ohne Abfrage und Anker ist `/privacy`. */
export function isPrivacyUrl(url: string): boolean {
  return pathOf(url) === '/privacy';
}

/** Pfad ohne Abfrage und Anker ist `/account-deletion` (UI-Sweep 2026-10-11, d1-k-account-deletion-style). */
export function isAccountDeletionUrl(url: string): boolean {
  return pathOf(url) === '/account-deletion';
}

/** Rechtsseite in der Kinder-Huelle: Datenschutz oder Konto loeschen — gleiche Karte, Breite und „← Start". */
export function isLegalUrl(url: string): boolean {
  return isPrivacyUrl(url) || isAccountDeletionUrl(url);
}

/** Markiert eine schon umgebaute Seite — ein zweiter Aufruf aendert nichts. */
const FOLDED = 'data-kid-folded';

/**
 * Baut die gezeichnete Datenschutzerklaerung unter `root` um: die Abschnitte nach dem Kinder-Teil (je `h2` mit dem
 * Inhalt bis zur naechsten `h2`) kommen in `<details>`, die Ueberschrift wird ihr `<summary>`; die Kinder-Ueberschrift
 * (`#privacy-kid`) und ihre Liste kommen in eine hervorgehobene Karte. Der Ruecklink am Ende (`.back`) bleibt draussen.
 * Verschoben werden die von Angular gezeichneten Elemente selbst — ihre Bindungen (Texte, Links) bleiben, die
 * Bedingungen der Seite haengen nur an `LEGAL_SITE` und aendern sich nie. Gibt zurueck, ob umgebaut wurde.
 */
export function foldPrivacy(root: ParentNode): boolean {
  const content = root.querySelector('app-privacy mat-card-content');
  if (!content || content.hasAttribute(FOLDED)) return false;
  const doc = content.ownerDocument;
  const heads = Array.from(content.children).filter(el => el.tagName === 'H2');
  if (heads.length === 0) return false;
  content.setAttribute(FOLDED, '');
  for (const head of heads) {
    const body: Element[] = [];
    for (let n = head.nextElementSibling; n && n.tagName !== 'H2' && !n.classList.contains('back'); n = n.nextElementSibling) {
      body.push(n);
      if (head.id === 'privacy-kid') break;          // nur die Liste; der Satz „Fuer Eltern: …" bleibt darunter stehen
    }
    if (head.id === 'privacy-kid') {
      const card = doc.createElement('section');
      card.className = 'kid-summary';
      head.before(card);
      card.append(head, ...body);
    } else {
      const details = doc.createElement('details');
      details.className = 'kid-parent';
      const summary = doc.createElement('summary');
      head.before(details);
      summary.append(head);
      details.append(summary, ...body);
    }
  }
  return true;
}

/** Texte, die KidHub auf „Konto loeschen" selbst einsetzt (uebersetzt von der Huelle). */
export interface KidDeletionTexts {
  /** Kurzer Kindersatz direkt unter der Einleitung (`kids.accountDeletion.short`). */
  short: string;
  /** Beschriftung des Eltern-Aufklappers ueber der Liste „Was entfernt wird" (`kids.accountDeletion.parents`). */
  parents: string;
  /** Beschriftung des Eltern-Aufklappers ueber der Anleitung (`kids.accountDeletion.parentsHowTo`). */
  howTo: string;
}

/** Ein zugeklapptes `<details class="kid-parent">` mit leerem Beschriftungs-Span der Klasse `labelClass`. */
function parentDetails(doc: Document, labelClass: string): HTMLDetailsElement {
  const details = doc.createElement('details');
  details.className = 'kid-parent';
  const summary = doc.createElement('summary');
  const label = doc.createElement('span');
  label.className = labelClass;
  summary.append(label);
  details.append(summary);
  return details;
}

/**
 * „Konto loeschen" in der Kinder-Fassung (UI-Sweep 2026-10-11, d1-k-account-deletion-jargon): die Liste unter „Was
 * entfernt wird" (`ul.removed`) nennt API-Tokens, Chessable, eigene Engines, PGN und Pfade — rechtlich richtig, aber
 * nichts fuer Kinder. Sie kommt wie beim Datenschutz in ein zugeklapptes `<details class="kid-parent">` „Fuer Eltern".
 * a1-k-deletion-fachjargon: ebenso die Anleitung ueber dem Knopf (erstes `h2` „So loeschst du dein Konto" samt Absatz,
 * `p.backup` und `p.backup-links` — ☰ → Konto → Profil, PGN, Repertoires) in ein zweites Eltern-Aufklapp „so wird das
 * Konto geloescht". d3-k-delete-button-before-info: der Kindersatz (`p.kid-short`) steht unter „Was entfernt wird" vor
 * dessen Eltern-Aufklapper, der Knopf (`p.action`) bleibt sichtbar, steht aber hinter „Was anonym erhalten bleibt". Die geteilte Komponente bleibt unberuehrt; verschoben werden nur die von Angular gezeichneten
 * Elemente (ihre Bindungen bleiben). Gibt zurueck, ob umgebaut wurde.
 */
export function foldAccountDeletion(root: ParentNode, texts: KidDeletionTexts): boolean {
  const content = root.querySelector('app-account-deletion mat-card-content');
  const list = content?.querySelector(':scope > ul.removed');
  if (!content || !list || content.hasAttribute(FOLDED)) return false;
  const doc = content.ownerDocument;
  content.setAttribute(FOLDED, '');

  // Anleitung: erstes h2 bis vor den Knopf (bzw. die naechste Ueberschrift / Liste).
  const children = Array.from(content.children);
  const howHead = children.find(el => el.tagName === 'H2');
  const how: Element[] = [];
  if (howHead) {
    for (let n: Element | null = howHead; n; n = n.nextElementSibling) {
      if (n !== howHead && (n.tagName === 'H2' || n.classList.contains('action') || n.matches('ul.removed'))) break;
      how.push(n);
    }
  }
  const short = doc.createElement('p');
  short.className = 'kid-short';
  if (how.length > 0) {
    const howDetails = parentDetails(doc, 'kid-parent-howto-label');
    how[0].before(howDetails);
    howDetails.append(...how);
  }

  const details = parentDetails(doc, 'kid-parent-label');
  list.before(details);
  details.append(list);
  // d3-k-delete-button-before-info: der Kindersatz („Alles von dir wird gelöscht …") gehört zu „Was entfernt wird" — er
  // steht jetzt unter dieser Überschrift, vor dem Eltern-Aufklapper (vorher oben unter der Einleitung, die Überschrift
  // hatte keinen sichtbaren Inhalt). Der Knopf rückt hinter „Was anonym erhalten bleibt": erst was verloren geht, dann
  // die Aktion (vorher stand er vor der Erklärung, die Überschrift klebte an ihm).
  details.before(short);
  const action = content.querySelector(':scope > p.action');
  if (action) {
    const keptHead = nextHeading(details);
    const anchor = (keptHead && nextHeading(keptHead)) ?? content.querySelector(':scope > p.back');
    if (anchor) anchor.before(action); else content.append(action);
  }
  setAccountDeletionTexts(root, texts);
  return true;
}

/** Nächstes Geschwister, das eine `h2` oder der Rücklink (`p.back`) ist — Grenze eines Abschnitts. */
function nextHeading(from: Element): Element | null {
  for (let n = from.nextElementSibling; n; n = n.nextElementSibling) {
    if (n.tagName === 'H2' || n.classList.contains('back')) return n.tagName === 'H2' ? n : null;
  }
  return null;
}

/** Setzt die Kindertexte neu (Sprachwechsel); ohne umgebaute Seite passiert nichts. */
export function setAccountDeletionTexts(root: ParentNode, texts: KidDeletionTexts): void {
  const short = root.querySelector('app-account-deletion .kid-short');
  const label = root.querySelector('app-account-deletion .kid-parent-label');
  const howTo = root.querySelector('app-account-deletion .kid-parent-howto-label');
  if (short) short.textContent = texts.short;
  if (label) label.textContent = texts.parents;
  if (howTo) howTo.textContent = texts.howTo;
}

/**
 * Gestaltung dafuer — in den Stilen der App-Huelle (`:host ::ng-deep`), damit sie nur auf der Kinderseite gilt. Das
 * Inhaltsverzeichnis entfaellt: die zugeklappten Abschnitte sind selbst die Liste.
 */
export const KID_PRIVACY_STYLES = `
  :host ::ng-deep :is(app-privacy, app-account-deletion) .legal-container { padding: 12px 16px 24px; }
  :host ::ng-deep :is(app-privacy, app-account-deletion) mat-card {
    max-width: ${KID_PAGE_WIDTH.legal - 32}px; border-radius: 22px; background: var(--kid-card); color: #23344a;
    box-shadow: 0 5px 0 var(--kid-shadow);
  }
  :host ::ng-deep :is(app-privacy, app-account-deletion) h1 { color: var(--kid-title); font-size: 1.7rem; font-weight: 800; }
  :host ::ng-deep :is(app-privacy, app-account-deletion) mat-card-content { font-size: 16px; line-height: 1.55; }
  :host ::ng-deep :is(app-privacy, app-account-deletion) mat-card-content li { margin: 6px 0; }
  :host ::ng-deep app-privacy mat-card-content .muted { font-size: 14px; color: #4f5d6e; }
  :host ::ng-deep app-privacy mat-card-content[data-kid-folded] .toc { display: none; }
  :host ::ng-deep app-privacy .kid-summary {
    margin: 14px 0; padding: 14px 18px; border-radius: 16px; background: #eef4fb; font-size: 17px;
  }
  :host ::ng-deep app-privacy .kid-summary h2 { margin: 0 0 6px; font-size: 1.2rem; font-weight: 800; color: var(--kid-title); }
  :host ::ng-deep app-privacy .kid-summary ul { margin: 0; padding-left: 1.2rem; }
  :host ::ng-deep :is(app-privacy, app-account-deletion) details.kid-parent { border-top: 1px solid #dbe5f0; }
  :host ::ng-deep :is(app-privacy, app-account-deletion) details.kid-parent > summary {
    display: flex; align-items: center; gap: 8px; min-height: 44px; cursor: pointer; list-style: none;
  }
  :host ::ng-deep :is(app-privacy, app-account-deletion) details.kid-parent > summary::-webkit-details-marker { display: none; }
  :host ::ng-deep :is(app-privacy, app-account-deletion) details.kid-parent > summary::before {
    content: '▸'; color: var(--kid-title); font-weight: 800; transition: transform .15s;
  }
  :host ::ng-deep :is(app-privacy, app-account-deletion) details.kid-parent[open] > summary::before { transform: rotate(90deg); }
  :host ::ng-deep app-privacy details.kid-parent > summary h2 { margin: 0; font-size: 16px; color: var(--kid-title); }
  :host ::ng-deep :is(app-privacy, app-account-deletion) details.kid-parent > summary:focus-visible { outline: 3px solid var(--kid-title); outline-offset: 2px; }
  :host ::ng-deep :is(app-privacy, app-account-deletion) details.kid-parent[open] { padding-bottom: 8px; }
  @media (prefers-reduced-motion: reduce) {
    :host ::ng-deep :is(app-privacy, app-account-deletion) details.kid-parent > summary::before { transition: none; }
  }

  /* „Konto loeschen" (UI-Sweep 2026-10-11, d1-k-account-deletion-style): Abschnittskoepfe und Links in --kid-title
     statt Material-Blau, Kindersatz etwas groesser, der Eltern-Aufklapper fett in --kid-title. */
  :host ::ng-deep app-account-deletion mat-card-content h2 { font-size: 1.05rem; font-weight: 800; color: var(--kid-title); }
  :host ::ng-deep app-account-deletion mat-card-content a:not(.mat-mdc-button-base) { color: var(--kid-title); font-weight: 700; }
  :host ::ng-deep app-account-deletion .kid-short { font-size: 17px; }
  /* d3-k-delete-button-before-info: der Knopf steht nach „Was anonym erhalten bleibt" — mit Luft nach oben und unten. */
  :host ::ng-deep app-account-deletion .action { margin: 24px 0 8px; }
  :host ::ng-deep app-account-deletion :is(.kid-parent-label, .kid-parent-howto-label) { font-weight: 700; color: var(--kid-title); }
  /* a1-k-deletion-fachjargon: Ueberschrift der zugeklappten Anleitung nicht so gross wie ein Abschnittskopf. */
  :host ::ng-deep app-account-deletion details.kid-parent > h2 { margin-top: 8px; }
  /* Loeschknopf wie ein KidHub-Hauptknopf (.big: Radius 18, 52 px, 800, Schatten) in der Gefahrfarbe —
     #9b1c1c auf Weiss 8,3:1 — statt der kleinen blauen Material-Pille. */
  :host ::ng-deep app-account-deletion .delete-now {
    --mat-button-filled-container-color: var(--kid-bad-fg);
    --mat-button-filled-label-text-color: #fff;
    --mat-button-filled-state-layer-color: #fff;
    --mat-button-filled-container-shape: 18px;
    --mat-button-filled-container-height: 52px;
    --mat-button-filled-horizontal-padding: 22px;
    --mat-button-filled-label-text-size: 1.15rem;
    --mat-button-filled-label-text-weight: 800;
    box-shadow: 0 5px 0 var(--kid-shadow);
  }
  :host ::ng-deep app-account-deletion .delete-now:active { transform: translateY(3px); box-shadow: 0 2px 0 var(--kid-shadow); }
`;
