import { KID_PAGE_WIDTH } from './kids-layout';

/**
 * Datenschutz in der Kinder-Fassung (UI-Sweep 2026-10-10, k-privacy). Die Seite selbst ist RookHubs geteilte
 * `PrivacyComponent` (Route `/privacy`, Texte und Ruecklink dort) — KidHub aendert daran nichts, sondern nur, wie sie
 * hier aussieht: Fliesstext 16 px, „Das Wichtigste in Kuerze" als hervorgehobene Karte oben, die ausfuehrlichen
 * Abschnitte fuer Eltern zum Aufklappen. Vorher eine 3 500 px lange Karte mit ~13-px-Schrift.
 */

/** Pfad ohne Abfrage und Anker ist `/privacy`. */
export function isPrivacyUrl(url: string): boolean {
  return url.split(/[?#]/)[0].replace(/\/+$/, '') === '/privacy';
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

/**
 * Gestaltung dafuer — in den Stilen der App-Huelle (`:host ::ng-deep`), damit sie nur auf der Kinderseite gilt. Das
 * Inhaltsverzeichnis entfaellt: die zugeklappten Abschnitte sind selbst die Liste.
 */
export const KID_PRIVACY_STYLES = `
  :host ::ng-deep app-privacy .legal-container { padding: 12px 16px 24px; }
  :host ::ng-deep app-privacy mat-card {
    max-width: ${KID_PAGE_WIDTH.legal - 32}px; border-radius: 22px; background: var(--kid-card); color: #23344a;
    box-shadow: 0 5px 0 var(--kid-shadow);
  }
  :host ::ng-deep app-privacy h1 { color: var(--kid-title); font-size: 1.7rem; font-weight: 800; }
  :host ::ng-deep app-privacy mat-card-content { font-size: 16px; line-height: 1.55; }
  :host ::ng-deep app-privacy mat-card-content li { margin: 6px 0; }
  :host ::ng-deep app-privacy mat-card-content .muted { font-size: 14px; color: #4f5d6e; }
  :host ::ng-deep app-privacy mat-card-content[data-kid-folded] .toc { display: none; }
  :host ::ng-deep app-privacy .kid-summary {
    margin: 14px 0; padding: 14px 18px; border-radius: 16px; background: #eef4fb; font-size: 17px;
  }
  :host ::ng-deep app-privacy .kid-summary h2 { margin: 0 0 6px; font-size: 1.2rem; font-weight: 800; color: var(--kid-title); }
  :host ::ng-deep app-privacy .kid-summary ul { margin: 0; padding-left: 1.2rem; }
  :host ::ng-deep app-privacy details.kid-parent { border-top: 1px solid #dbe5f0; }
  :host ::ng-deep app-privacy details.kid-parent > summary {
    display: flex; align-items: center; gap: 8px; min-height: 44px; cursor: pointer; list-style: none;
  }
  :host ::ng-deep app-privacy details.kid-parent > summary::-webkit-details-marker { display: none; }
  :host ::ng-deep app-privacy details.kid-parent > summary::before {
    content: '▸'; color: var(--kid-title); font-weight: 800; transition: transform .15s;
  }
  :host ::ng-deep app-privacy details.kid-parent[open] > summary::before { transform: rotate(90deg); }
  :host ::ng-deep app-privacy details.kid-parent > summary h2 { margin: 0; font-size: 16px; color: var(--kid-title); }
  :host ::ng-deep app-privacy details.kid-parent > summary:focus-visible { outline: 3px solid var(--kid-title); outline-offset: 2px; }
  :host ::ng-deep app-privacy details.kid-parent[open] { padding-bottom: 8px; }
`;
