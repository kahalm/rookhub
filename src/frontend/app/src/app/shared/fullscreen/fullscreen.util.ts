/**
 * Dünne Hülle um die Fullscreen-API (echtes Vollbild: über Taskleiste und Browserleiste hinweg,
 * nicht bloß „Fenster maximiert").
 *
 * <p>Warum eine eigene Datei: Safari (Desktop) kennt nur die `webkit`-Varianten, und iOS-Safari kann
 * für normale Elemente überhaupt kein Vollbild (nur `<video>`). Der Aufrufer soll das nicht wissen
 * müssen — er fragt {@link fullscreenSupported} und blendet seinen Knopf sonst aus.</p>
 *
 * <p>Wichtig fürs Layout: im Vollbild rendert der Browser NUR den Teilbaum des Vollbild-Elements.
 * Bedienelemente, die im Vollbild erreichbar bleiben müssen, gehören daher INS Vollbild-Element
 * (Vollbild-Knopf, Solver-Icon-Leiste) — im normalen Seitenfluss stehende Knöpfe sind dort weg.</p>
 *
 * <p>Für CDK-Overlays (Tooltip, Snackbar, Dialog, Menü) erledigt das seit 0.339.0 der
 * {@link FullscreenOverlayService}: er hängt den Overlay-Container fürs Vollbild ins Vollbild-Element
 * um, Overlays erscheinen also normal. (Davor waren sie unsichtbar — ein modaler Dialog mit
 * `disableClose` hing die App sogar fest.) Für Elemente INNERHALB des Vollbild-Elements bleibt das
 * native `title`-Attribut trotzdem die einfachere Wahl.</p>
 */

interface FullscreenCapableElement extends HTMLElement {
  webkitRequestFullscreen?: () => Promise<void> | void;
}

interface FullscreenCapableDocument extends Document {
  webkitFullscreenElement?: Element | null;
  webkitExitFullscreen?: () => Promise<void> | void;
}

function doc(): FullscreenCapableDocument {
  return document as FullscreenCapableDocument;
}

/** Kann dieser Browser normale Elemente ins Vollbild schicken? (iOS-Safari: nein.) */
export function fullscreenSupported(): boolean {
  if (typeof document === 'undefined') return false;
  const d = doc();
  if (d.fullscreenEnabled === false) return false;
  const proto = HTMLElement.prototype as FullscreenCapableElement;
  return typeof proto.requestFullscreen === 'function' || typeof proto.webkitRequestFullscreen === 'function';
}

/** Das aktuell im Vollbild dargestellte Element (oder `null`). */
export function fullscreenElement(): Element | null {
  const d = doc();
  return d.fullscreenElement ?? d.webkitFullscreenElement ?? null;
}

/** Ist genau dieses Element im Vollbild? */
export function isFullscreen(el: Element | null | undefined): boolean {
  return !!el && fullscreenElement() === el;
}

export async function requestFullscreen(el: HTMLElement): Promise<void> {
  const target = el as FullscreenCapableElement;
  // Ein abgelehnter Vollbild-Wunsch (fehlende Nutzer-Interaktion, Berechtigungs-Policy) ist kein
  // Fehler, den der Aufrufer behandeln könnte — schlucken, der Knopf bleibt einfach wirkungslos.
  try {
    if (typeof target.requestFullscreen === 'function') await target.requestFullscreen();
    else if (typeof target.webkitRequestFullscreen === 'function') await target.webkitRequestFullscreen();
  } catch { /* ignoriert */ }
}

export async function exitFullscreen(): Promise<void> {
  const d = doc();
  try {
    if (typeof d.exitFullscreen === 'function') await d.exitFullscreen();
    else if (typeof d.webkitExitFullscreen === 'function') await d.webkitExitFullscreen();
  } catch { /* ignoriert */ }
}

/** Schaltet dieses Element ins Vollbild bzw. wieder heraus. */
export async function toggleFullscreen(el: HTMLElement): Promise<void> {
  if (isFullscreen(el)) await exitFullscreen();
  else await requestFullscreen(el);
}

/**
 * Meldet jeden Vollbild-Wechsel (auch den per Esc oder F11 ausgelösten) und liefert die
 * Abmelde-Funktion zurück. Beide Ereignisnamen, weil Safari nur das `webkit`-Ereignis feuert.
 */
export function onFullscreenChange(cb: () => void): () => void {
  const handler = () => cb();
  document.addEventListener('fullscreenchange', handler);
  document.addEventListener('webkitfullscreenchange', handler);
  return () => {
    document.removeEventListener('fullscreenchange', handler);
    document.removeEventListener('webkitfullscreenchange', handler);
  };
}
