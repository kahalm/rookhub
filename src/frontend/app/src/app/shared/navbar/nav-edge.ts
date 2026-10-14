import { DestroyRef, NgZone } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

/**
 * UI-Sweep b1 (b1-rk-kopfleiste-ultrawide-flucht, DESIGN 4.1): ab 2400 px fluchtet der Kopfinhalt (Logo, Symbole) mit der
 * Inhaltskante DER JEWEILIGEN SEITE — nicht fest mit 1 880 px. Statt einer Liste je Seite (die bei jeder neuen Seite
 * vergessen würde) misst die Kopfleiste die Breite des Seiteninhalts in `<main>` und setzt sie als `--nav-edge` auf ihr
 * eigenes Host-Element; die 2400-px-Regel der Navbar liest `var(--nav-edge, var(--page-max-width-wide, 1880px))`.
 * Unter 2400 px läuft nichts (kein Messen, keine Variable) — Laptop/Handy/1920 bleiben pixelgleich.
 */
export const NAV_EDGE_MEDIA = '(min-width: 2400px)';
/** Schmaler gemessene Kästen (Ladekreis, leere Seite im Wechsel) zählen nicht — dann gilt der Rückfall. */
export const NAV_EDGE_MIN = 560;
/**
 * Markierung für randlose Seiten mit eigener Werkzeugleiste (E-Book-Leser, DESIGN 4.5): ein Kasten mit
 * `data-nav-edge="full"` beendet die Suche und liefert die volle Breite von `<main>` — der Kopfinhalt steht dann bei
 * 16 px wie die Leiste der Seite, statt an einem zufällig gefundenen inneren Kasten (c2-eb-reader-ultrawide-kopfkante).
 */
export const NAV_EDGE_FULL_ATTR = 'data-nav-edge';
const MAX_DEPTH = 10;

function inFlow(el: Element): boolean {
  const cs = (el.ownerDocument.defaultView ?? window).getComputedStyle(el);
  return cs.display !== 'none' && cs.position !== 'fixed' && cs.position !== 'absolute' && cs.visibility !== 'hidden';
}

/**
 * Breite eines symmetrisch um den Seiteninhalt gelegten Kastens in px (oder null = Rückfall). Gesucht wird von der
 * Routen-Komponente in `<main>` abwärts, immer im breitesten Kind im Fluss, bis ein Kasten schmaler als `<main>` ist —
 * das ist der Seitencontainer (`max-width: …; margin: 0 auto`, auch `fit-content`-Karten). Nicht mittig stehender
 * Inhalt: der Kasten reicht bis zur äußeren der beiden Kanten. Ein Kasten mit `data-nav-edge="full"` auf dem Weg
 * liefert die volle Breite (randlose Seite).
 */
export function measureContentEdge(main: Element | null): number | null {
  if (!main) return null;
  const outer = main.getBoundingClientRect();
  const full = outer.width;
  if (full <= 0) return null;
  // Routen-Komponente = letztes Element-Kind, das kein <router-outlet> ist.
  let el: Element | null = null;
  for (let i = main.children.length - 1; i >= 0; i--) {
    const c = main.children[i];
    if (c.tagName !== 'ROUTER-OUTLET' && inFlow(c)) { el = c; break; }
  }
  for (let depth = 0; el && depth < MAX_DEPTH; depth++) {
    if (el.getAttribute(NAV_EDGE_FULL_ATTR) === 'full') return Math.round(full);
    const r = el.getBoundingClientRect();
    if (r.width > 0 && r.width < full - 2) {
      const inset = Math.max(0, Math.min(r.left - outer.left, outer.right - r.right));
      const edge = Math.round(full - 2 * inset);
      return edge >= NAV_EDGE_MIN ? edge : null;
    }
    let next: Element | null = null;
    let best = 0;
    for (const c of Array.from(el.children)) {
      if (!inFlow(c)) continue;
      const cr = c.getBoundingClientRect();
      if (cr.height <= 0 || cr.width <= best) continue;
      best = cr.width;
      next = c;
    }
    el = next;
  }
  return null;
}

/**
 * Hält `--nav-edge` auf `host` aktuell: nach jeder Navigation, bei Größenänderungen von `<main>`/Seitencontainer und
 * wenn sich der Inhalt austauscht (Laden → fertig). Nur solange das Fenster ≥ 2400 px breit ist; läuft außerhalb der
 * Angular-Zone (setzt nur eine CSS-Variable, braucht keine Änderungserkennung).
 */
export function trackNavEdge(host: HTMLElement, router: Router, zone: NgZone, destroyRef: DestroyRef): void {
  // Fenster des Host-Elements (nicht global): so lässt es sich im iframe mit echter Breite prüfen.
  const win = host.ownerDocument.defaultView as (Window & typeof globalThis) | null;
  if (!win || !win.matchMedia || typeof win.ResizeObserver === 'undefined') return;
  const mq = win.matchMedia(NAV_EDGE_MEDIA);
  const main = (): Element | null => host.parentElement?.querySelector(':scope > main') ?? host.ownerDocument.querySelector('main');
  let frame = 0;
  let ro: ResizeObserver | null = null;
  let mo: MutationObserver | null = null;

  const apply = () => {
    frame = 0;
    const edge = mq.matches ? measureContentEdge(main()) : null;
    if (edge == null) host.style.removeProperty('--nav-edge');
    else host.style.setProperty('--nav-edge', `${edge}px`);
  };
  const schedule = () => { if (!frame) frame = win.requestAnimationFrame(apply); };

  const start = () => {
    const m = main();
    if (!m || ro) { schedule(); return; }
    ro = new win.ResizeObserver(schedule);
    ro.observe(m);
    mo = new win.MutationObserver(schedule);
    mo.observe(m, { childList: true, subtree: true });
    schedule();
  };
  const stop = () => {
    ro?.disconnect(); mo?.disconnect(); ro = mo = null;
    if (frame) win.cancelAnimationFrame(frame);
    frame = 0;
    host.style.removeProperty('--nav-edge');
  };
  const onMedia = () => (mq.matches ? start() : stop());

  zone.runOutsideAngular(() => {
    mq.addEventListener('change', onMedia);
    const sub = router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => { if (mq.matches) start(); });
    // Erst nach dem ersten Rendern messen — <main> entsteht im selben Durchlauf wie die Navbar.
    win.requestAnimationFrame(onMedia);
    destroyRef.onDestroy(() => { mq.removeEventListener('change', onMedia); sub.unsubscribe(); stop(); });
  });
}
