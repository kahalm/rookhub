import { DOCUMENT, EnvironmentProviders, inject, provideAppInitializer } from '@angular/core';

/** So lange nach einer Berührung gelten Mausereignisse als vom Finger simuliert (Browser schicken sie nach touchend). */
export const TOUCH_MOUSE_WINDOW_MS = 1000;

const MENU_ITEM = '.mat-mdc-menu-item';

type WithCaps = Event & { sourceCapabilities?: { firesTouchEvents?: boolean } | null };

/**
 * Untermenüs (mat-menu im mat-menu) am Handy nicht schon beim Antippen per „Hover“ öffnen.
 *
 * Material öffnet ein Untermenü bei `mouseenter` auf seinem Eintrag. Nach einem Fingertipp schicken Browser simulierte
 * Mausereignisse in der Reihenfolge mouseenter → mousedown → click, und mousedown/click werden ERST NACH dem mouseenter
 * an der Fingerstelle gesucht. Das Untermenü ist dann schon offen, liegt am schmalen Schirm oft genau über dem Finger,
 * und der Tipp wählt den Eintrag darunter („tippe Bibliothek, lande in Partien“).
 *
 * Abhilfe: nach einer Berührung das mouseenter auf Menüeinträgen in der Capture-Phase schlucken und erst beim click
 * nachspielen — dann steht das Ziel des Tipps fest; das Untermenü öffnet, Geschwister-Untermenüs schließen wie gewohnt.
 */
export function installTouchSubmenuGuard(doc: Document, now: () => number = () => performance.now()): () => void {
  let lastTouch = -Infinity;
  let replaying = false;

  const fromTouch = (ev: Event): boolean => {
    const caps = (ev as WithCaps).sourceCapabilities;
    if (caps && typeof caps.firesTouchEvents === 'boolean') return caps.firesTouchEvents;
    return now() - lastTouch < TOUCH_MOUSE_WINDOW_MS;
  };
  const menuItem = (ev: Event): Element | null =>
    ev.target instanceof Element ? ev.target.closest(MENU_ITEM) : null;

  const onTouch = (): void => { lastTouch = now(); };
  const onEnter = (ev: Event): void => {
    if (replaying || !menuItem(ev) || !fromTouch(ev)) return;
    ev.stopImmediatePropagation();
  };
  const onClick = (ev: Event): void => {
    const item = menuItem(ev);
    if (!item || !fromTouch(ev)) return;
    replaying = true;
    try { item.dispatchEvent(new MouseEvent('mouseenter')); } finally { replaying = false; }
  };

  doc.addEventListener('touchstart', onTouch, { capture: true, passive: true });
  doc.addEventListener('mouseenter', onEnter, true);
  doc.addEventListener('click', onClick, true);
  return () => {
    doc.removeEventListener('touchstart', onTouch, true);
    doc.removeEventListener('mouseenter', onEnter, true);
    doc.removeEventListener('click', onClick, true);
  };
}

export function provideTouchSubmenuGuard(): EnvironmentProviders {
  return provideAppInitializer(() => { installTouchSubmenuGuard(inject(DOCUMENT)); });
}
