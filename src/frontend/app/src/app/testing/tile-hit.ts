/**
 * Treffertest „ganze Kachel klickbar“ (docs/DESIGN.md 5.2, `src/_tile.scss`): prüft am gerenderten Element, was ein
 * Klick wirklich trifft — `document.elementFromPoint` an Ecken, Kanten, einem Raster über die Fläche und in der Mitte
 * jeder Textstelle. Die globalen Styles laufen im Test mit (angular.json → test.styles), die Kachel muss dafür im
 * Dokument hängen (TestBed-Fixtures tun das).
 *
 * Bewusst kein Blick auf Klassen oder Stilregeln: der Test fällt gegen eine Kachel um, bei der nur der Knopf klickbar
 * ist (dort trifft die Fläche `<mat-card>`, den Titel, den Absatz …), und gegen eine, deren Nebenelement unter der
 * Fläche liegt.
 */
import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';

/** Was in einer Kachel als eigenes Bedienelement zählt (deckt sich mit der Liste in `_tile.scss`). */
export const TILE_INTERACTIVE = 'a, button, input, select, textarea, label, summary, [tabindex]:not([tabindex="-1"]), '
  + '[role="button"], [role="link"], .mat-mdc-checkbox, .mat-mdc-slide-toggle, .mat-mdc-radio-button, '
  + '.mat-mdc-form-field, .mat-button-toggle-group, .mat-mdc-tooltip-trigger, .rh-tile-above';

export interface TileHitOptions {
  /** Nebenelemente; Vorgabe: alles Bedienbare in der Kachel außerhalb des Hauptziels. */
  secondary?: Element[];
  /** Rasterpunkte je Richtung (Vorgabe 7 → 49 Punkte + Ecken und Kantenmitten). */
  grid?: number;
}

interface Point { x: number; y: number; what: string; }

const label = (el: Element | null): string => {
  if (!el) return 'nichts';
  const cls = typeof el.className === 'string' && el.className.trim() ? '.' + el.className.trim().split(/\s+/).slice(0, 3).join('.') : '';
  return `<${el.tagName.toLowerCase()}${cls}>`;
};

/** Liegt der Punkt im eigenen Kasten des Elements? Mit 1 px Rand: der Treffertest rundet auf ganze Pixel. */
function inOwnBox(p: { x: number; y: number }, own: DOMRect): boolean {
  return p.x >= own.left - 1 && p.x <= own.right + 1 && p.y >= own.top - 1 && p.y <= own.bottom + 1;
}

/** Sichtbarer Teil des Elements im Fenster (nach `scrollIntoView`), oder null ohne Fläche. */
function visibleRect(el: Element): DOMRect | null {
  const r = el.getBoundingClientRect();
  const left = Math.max(r.left, 0), top = Math.max(r.top, 0);
  const right = Math.min(r.right, window.innerWidth), bottom = Math.min(r.bottom, window.innerHeight);
  return right - left >= 1 && bottom - top >= 1 ? new DOMRect(left, top, right - left, bottom - top) : null;
}

/** Overlays früherer Specs (offenes Menü, Tooltip) lägen über der Kachel und verfälschten den Treffer. */
function withoutOverlays<T>(fn: () => T): T {
  const overlays = Array.from(document.querySelectorAll<HTMLElement>('.cdk-overlay-container'));
  const before = overlays.map(o => o.style.display);
  overlays.forEach(o => o.style.display = 'none');
  try { return fn(); } finally { overlays.forEach((o, i) => o.style.display = before[i]); }
}

function secondaryOf(tile: Element, target: Element, opts: TileHitOptions): Element[] {
  const all = opts.secondary ?? Array.from(tile.querySelectorAll(TILE_INTERACTIVE));
  return all.filter(e => e !== target && !target.contains(e) && !e.contains(target));
}

function samplePoints(tile: Element, target: Element, secondary: Element[], grid: number): Point[] {
  const r = visibleRect(tile);
  if (!r) throw new Error(`Kachel ${label(tile)} hat keine sichtbare Fläche`);
  const inset = 2;
  const x0 = r.left + inset, x1 = r.right - inset, y0 = r.top + inset, y1 = r.bottom - inset;
  const points: Point[] = [
    { x: x0, y: y0, what: 'Ecke oben links' }, { x: x1, y: y0, what: 'Ecke oben rechts' },
    { x: x0, y: y1, what: 'Ecke unten links' }, { x: x1, y: y1, what: 'Ecke unten rechts' },
    { x: (x0 + x1) / 2, y: (y0 + y1) / 2, what: 'Mitte' },
  ];
  for (let i = 0; i < grid; i++) {
    for (let j = 0; j < grid; j++) {
      points.push({ x: x0 + (x1 - x0) * (i + 0.5) / grid, y: y0 + (y1 - y0) * (j + 0.5) / grid, what: `Raster ${i + 1}/${j + 1}` });
    }
  }
  // Textbereich: die Mitte jeder Textstelle, die nicht zu einem Bedienelement gehört (Titel, Untertitel, Absatz).
  const walker = document.createTreeWalker(tile, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    const text = (n.textContent ?? '').trim();
    const parent = n.parentElement;
    if (!text || !parent || target.contains(parent) || secondary.some(s => s.contains(parent))) continue;
    const range = document.createRange();
    range.selectNodeContents(n);
    const box = Array.from(range.getClientRects()).find(b => b.width >= 2 && b.height >= 2);
    if (!box) continue;
    const x = box.left + box.width / 2, y = box.top + box.height / 2;
    if (x > r.left && x < r.right && y > r.top && y < r.bottom) points.push({ x, y, what: `Text „${text.slice(0, 30)}“` });
  }
  return points;
}

/**
 * Was der Treffertest an der Kachel auszusetzen hat (leer = die ganze Kachel ist klickbar): jeder Punkt der Fläche muss
 * das Hauptziel oder eines der Nebenelemente treffen — nie die Kachel selbst oder bloßen Inhalt —, Ecken, Mitte und
 * Textstellen das Hauptziel (soweit dort kein Nebenelement sitzt), und die Mitte jedes Nebenelements dieses selbst.
 */
export function tileHitProblems(tile: Element, target: Element, opts: TileHitOptions = {}): string[] {
  if (!tile.contains(target)) return [`Hauptziel ${label(target)} liegt nicht in der Kachel ${label(tile)}`];
  tile.scrollIntoView({ block: 'center', inline: 'center' });
  const secondary = secondaryOf(tile, target, opts);
  return withoutOverlays(() => {
    const problems: string[] = [];
    let outsideOwnBox = 0;
    const own = target.getBoundingClientRect();
    for (const p of samplePoints(tile, target, secondary, opts.grid ?? 7)) {
      const hit = document.elementFromPoint(p.x, p.y);
      if (hit && target.contains(hit)) {
        if (!inOwnBox(p, own)) outsideOwnBox++;
        continue;
      }
      if (hit && secondary.some(s => s.contains(hit))) continue;
      problems.push(`${p.what} (${Math.round(p.x)}/${Math.round(p.y)}) trifft ${label(hit)} statt des Hauptziels ${label(target)}`);
    }
    if (outsideOwnBox === 0) problems.push(`kein Punkt außerhalb des Knopfs/Links trifft das Hauptziel ${label(target)}`);
    for (const s of secondary) {
      const r = visibleRect(s);
      if (!r) continue;   // nicht gerendert (z. B. Menüinhalt)
      const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
      if (!hit || !s.contains(hit)) problems.push(`Mitte des Nebenelements ${label(s)} trifft ${label(hit)}`);
    }
    return problems;
  });
}

/** Die ganze Kachel ist klickbar (siehe {@link tileHitProblems}). */
export function expectWholeTileClickable(tile: Element, target: Element, opts: TileHitOptions = {}): void {
  expect(tileHitProblems(tile, target, opts)).withContext(`Treffertest ${label(tile)}`).toEqual([]);
}

/**
 * Die Fläche ist NICHT klickbar (Bearbeiten-Modus, gesperrtes Hauptziel): kein Punkt außerhalb des Knopfs/Links
 * trifft das Hauptziel.
 */
export function expectTileNotClickable(tile: Element, target: Element | null): void {
  tile.scrollIntoView({ block: 'center', inline: 'center' });
  const problems = withoutOverlays(() => {
    const found: string[] = [];
    const own = target?.getBoundingClientRect();
    for (const p of samplePoints(tile, target ?? tile.ownerDocument.createElement('span'), [], 7)) {
      if (own && inOwnBox(p, own)) continue;
      const hit = document.elementFromPoint(p.x, p.y);
      if (target && hit && target.contains(hit)) found.push(`${p.what} (${Math.round(p.x)}/${Math.round(p.y)}) trifft das Hauptziel ${label(target)}`);
    }
    return found;
  });
  expect(problems).withContext(`Fläche von ${label(tile)} soll nicht klickbar sein`).toEqual([]);
}

/**
 * Eine freie Stelle der Kachel — außerhalb des Knopfs/Links selbst —, an der ein Klick das Hauptziel trifft
 * (für „Klick auf die leere Fläche führt zum Ziel“). Wirft, wenn es keine gibt.
 */
export function freeSpot(tile: Element, target: Element): { x: number; y: number } {
  tile.scrollIntoView({ block: 'center', inline: 'center' });
  return withoutOverlays(() => {
    const own = target.getBoundingClientRect();
    for (const p of samplePoints(tile, target, [], 7)) {
      if (inOwnBox(p, own)) continue;
      const hit = document.elementFromPoint(p.x, p.y);
      if (hit && target.contains(hit)) return { x: p.x, y: p.y };
    }
    throw new Error(`keine freie Stelle der Kachel ${label(tile)} trifft das Hauptziel ${label(target)}`);
  });
}

/** Klick wie mit der Maus: trifft, was an der Stelle wirklich oben liegt (gesperrte Elemente lösen nichts aus). Gibt das
 *  getroffene Element zurück. */
export function clickAt(x: number, y: number): Element | null {
  const hit = withoutOverlays(() => document.elementFromPoint(x, y));
  // Ein gesperrtes Bedienelement schluckt den Klick eines Nutzers; dispatchEvent täte das nicht von selbst.
  if (hit?.closest(':disabled')) return hit;
  hit?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: x, clientY: y, view: window }));
  return hit;
}

/** Klick auf die leere Fläche der Kachel (siehe {@link freeSpot}). */
export function clickFreeSpot(tile: Element, target: Element): Element | null {
  const p = freeSpot(tile, target);
  return clickAt(p.x, p.y);
}

/** Klick in die Mitte eines Elements — über den Treffertest, nicht per `el.click()`. */
export function clickCenter(el: Element): Element | null {
  el.scrollIntoView({ block: 'center', inline: 'center' });
  const r = el.getBoundingClientRect();
  return clickAt(r.left + r.width / 2, r.top + r.height / 2);
}

/**
 * Zeichnet auf, wohin Router-Links führen würden, ohne zu navigieren (`Router.navigateByUrl` als Spion) — für „Klick auf
 * die Fläche führt zum Hauptziel“. Gibt eine Funktion zurück, die die bisherigen Ziele als Pfade liefert.
 */
export function recordNavigation(): () => string[] {
  const router = TestBed.inject(Router);
  const spy = spyOn(router, 'navigateByUrl').and.resolveTo(true);
  return () => spy.calls.allArgs().map(a => typeof a[0] === 'string' ? a[0] : router.serializeUrl(a[0] as UrlTree));
}
