import { localStore, readRaw, writeRaw, removeKey } from './local-json-store';

/**
 * UI-Skalierung JE GERÄT (0.800.x, docs/DESIGN.md 3.7): große Tablets (Boox Tab X C, 13,3", CSS-Viewport ≈ 960 px)
 * zeigen die App sonst winzig. localStorage `rookhub_ui_scale` = Prozent (125/150/175/200); 100 = Schlüssel entfernt.
 *
 * ZWEI WEGE — beide halten die Koordinaten stimmig (Brett, Drag&Drop, Doppelklick im Leser):
 * 1. **Touch-/Mobilbrowser** (`viewport`): Meta-Viewport `width=<Bildschirmbreite / S>, initial-scale=S`. Das Layout
 *    wird schmaler gerechnet und der Browser vergrößert es wie beim Aufziehen mit zwei Fingern — Treffer, clientX,
 *    getBoundingClientRect bleiben alle in denselben CSS-Pixeln (nachgewiesen mit Chromium 151 und 111, isMobile).
 *    `width=device-width, initial-scale=S` allein vergrößert nur die Ansicht über einem unveränderten 960-px-Layout
 *    (seitliches Scrollen) — darum die ausgerechnete Breite. Nach Drehen neu berechnen (`resize`/`orientationchange`).
 *    Kandidaten: primäre Eingabe Touch (`(hover: none) and (pointer: coarse)`) oder Mobil-UA (Android/iOS/iPadOS) —
 *    nur dort werten Browser den Meta-Viewport aus. Ob er greift, wird sofort geprüft (Layoutbreite = Zielbreite);
 *    ein Windows-Touch-Tablet im Desktop-Chrome passt zur Media-Query, ignoriert den Meta-Viewport aber → Weg 2.
 * 2. **Desktop** (`zoom`): CSS `zoom` auf `<html>` (ab Chrome 128 standardisiert: getBoundingClientRect und
 *    Pointer-Koordinaten sind gezoomt und passen zueinander). Chessground rechnet aber Figurenlagen in px aus
 *    getBoundingClientRect und setzt sie als `translate()` INNERHALB des Zooms — doppelt skaliert. Darum hebt
 *    `cg-container` den Zoom wieder auf (Brettgröße kommt weiter vom gezoomten `.cg-wrap`). Die Brett-Koordinaten
 *    (a–h/1–8) bleiben dabei in 100-%-Schrift. CDK-Overlays (Menüs, Tooltips, Dialoge) bekommen dieselbe Behandlung.
 *    Vor Chrome 128 (und in Safari) ist `zoom` nicht standardisiert: getBoundingClientRect bleibt ungezoomt, Klicks
 *    kommen gezoomt an — Treffer würden danebengehen. Das prüft {@link zoomIsStandardized}; dann bleibt es bei 100 %
 *    (`none`) und die Profilkarte sagt es, statt eine kaputte Seite zu liefern.
 */
export const UI_SCALE_STORAGE_KEY = 'rookhub_ui_scale';
export const UI_SCALE_STEPS: readonly number[] = [100, 125, 150, 175, 200];
export const UI_SCALE_DEFAULT = 100;
/** Media-Query „Touch ist die primäre Eingabe" (Handy/Tablet, auch Boox). */
export const UI_SCALE_TOUCH_QUERY = '(hover: none) and (pointer: coarse)';
export const UI_SCALE_ZOOM_CLASS = 'ui-zoom';
const STYLE_ID = 'rh-ui-scale-style';
const DEFAULT_VIEWPORT = 'width=device-width, initial-scale=1';

/** `none` = keiner der Wege ist hier sicher (alter Desktop-Browser ohne standardisiertes `zoom`) → nichts skalieren. */
export type UiScaleMode = 'viewport' | 'zoom' | 'none';

let current = UI_SCALE_DEFAULT;
let mode: UiScaleMode | null = null;
let listenersInstalled = false;
const listeners = new Set<(pct: number) => void>();

/** Nur die Stufen gelten; alles andere (kaputt, alt, fremd) = 100. */
export function normalizeUiScale(value: unknown): number {
  const n = typeof value === 'number' ? value : parseInt(String(value ?? ''), 10);
  return UI_SCALE_STEPS.includes(n) ? n : UI_SCALE_DEFAULT;
}

/** Gespeicherte Stufe dieses Geräts (100 ohne Wahl). */
export function readUiScale(): number {
  return normalizeUiScale(readRaw(localStore(), UI_SCALE_STORAGE_KEY));
}

/** Stufe speichern (100 = Schlüssel löschen). Gibt zurück, ob das Speichern geklappt hat. */
export function writeUiScale(pct: number): boolean {
  const store = localStore();
  const value = normalizeUiScale(pct);
  if (value === UI_SCALE_DEFAULT) {
    if (!store) return false;
    removeKey(store, UI_SCALE_STORAGE_KEY);
    return true;
  }
  return writeRaw(store, UI_SCALE_STORAGE_KEY, String(value));
}

/** Meta-Viewport-Inhalt für eine Stufe bei gegebener Bildschirmbreite (CSS-px bei 100 %). */
export function viewportContentFor(pct: number, screenWidth: number): string {
  const value = normalizeUiScale(pct);
  if (value === UI_SCALE_DEFAULT || !(screenWidth > 0)) return DEFAULT_VIEWPORT;
  const scale = value / 100;
  return `width=${Math.round(screenWidth / scale)}, initial-scale=${scale}`;
}

/**
 * Bildschirmbreite in CSS-px (unabhängig vom Seitenzoom), passend zur aktuellen Lage: Android dreht `screen.width`
 * mit, iOS nicht — darum nach `screen.orientation`/`window.orientation` die kürzere oder längere Seite nehmen.
 */
export function screenCssWidth(): number {
  try {
    const w = screen.width, h = screen.height;
    if (!(w > 0) || !(h > 0)) return 0;
    const type = (screen as Screen & { orientation?: { type?: string } }).orientation?.type ?? '';
    const legacy = (globalThis as { orientation?: number }).orientation;
    const landscape = type ? type.startsWith('landscape') : typeof legacy === 'number' ? Math.abs(legacy) === 90 : w > h;
    return landscape ? Math.max(w, h) : Math.min(w, h);
  } catch { return 0; }
}

/** Wertet dieser Browser vermutlich den Meta-Viewport aus (Touch primär oder Mobil-UA)? */
export function isViewportCandidate(): boolean {
  try {
    if (globalThis.matchMedia?.(UI_SCALE_TOUCH_QUERY)?.matches) return true;
    const nav = globalThis.navigator;
    if (!nav) return false;
    if (/Android|iPhone|iPad|iPod/i.test(nav.userAgent)) return true;
    return /Macintosh/.test(nav.userAgent) && (nav.maxTouchPoints ?? 0) > 1; // iPadOS meldet sich als Mac
  } catch { return false; }
}

export function currentUiScale(): number { return current; }
/** Gewählter Weg (null = noch nie skaliert). */
export function currentUiScaleMode(): UiScaleMode | null { return mode; }

export function onUiScaleChange(fn: (pct: number) => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

function viewportMeta(): HTMLMetaElement | null {
  try {
    let meta = document.querySelector<HTMLMetaElement>('meta[name="viewport"]');
    if (!meta && document.head) {
      meta = document.createElement('meta');
      meta.name = 'viewport';
      document.head.appendChild(meta);
    }
    return meta;
  } catch { return null; }
}

function setViewport(content: string): void {
  const meta = viewportMeta();
  if (meta && meta.content !== content) meta.content = content;
}

/**
 * Rechnet dieser Browser `zoom` nach dem Standard (Chrome ≥ 128, Firefox ≥ 126)? Probe: 10-px-Kind in einem
 * `zoom: 2`-Kasten misst dort 20 px, im alten Modell 10 px (gemessen: Chromium 151 → 20, Chromium 111 → 10).
 */
export function zoomIsStandardized(): boolean {
  try {
    const host = document.body ?? document.documentElement;
    const outer = document.createElement('div');
    outer.style.cssText = 'zoom:2;position:absolute;visibility:hidden;pointer-events:none';
    const inner = document.createElement('div');
    inner.style.cssText = 'width:10px;height:1px';
    outer.appendChild(inner);
    host.appendChild(outer);
    const width = inner.getBoundingClientRect().width;
    outer.remove();
    return Math.abs(width - 20) < 0.5;
  } catch { return false; }
}

/** Greift der Meta-Viewport? Die Layoutbreite folgt ihm in Chromium/WebKit sofort (synchron beim Auslesen). */
function viewportHonored(targetWidth: number): boolean {
  try { return Math.abs(document.documentElement.clientWidth - targetWidth) <= 1; } catch { return false; }
}

function setZoom(pct: number): void {
  try {
    const root = document.documentElement;
    const on = pct !== UI_SCALE_DEFAULT;
    root.style.zoom = on ? String(pct / 100) : '';
    root.style.setProperty('--rh-ui-zoom', on ? String(pct / 100) : '');
    if (!on) root.style.removeProperty('--rh-ui-zoom');
    root.classList.toggle(UI_SCALE_ZOOM_CLASS, on);
    if (on && !document.getElementById(STYLE_ID) && document.head) {
      const style = document.createElement('style');
      style.id = STYLE_ID;
      // Chessground: Figuren-translate() in px aus getBoundingClientRect → innen den Zoom aufheben (siehe oben).
      // CDK-Overlays: Lagen kommen aus getBoundingClientRect (gezoomt) und landen als px im Overlay-Container — dort
      // den Zoom aufheben (Lage stimmt) und für den Inhalt wieder anwenden. Fenster, die CDK selbst per top/left
      // setzt (Tooltips, verschobene Menüs), dürfen den Zoom nicht tragen (ihr top/left würde mitwachsen) → deren
      // Kinder; die übrigen (`position: static` — Menüs/Auswahllisten im Rahmen, Dialoge, Snackbars) das Fenster selbst,
      // damit feste Breiten (Dialog 420px) mitwachsen.
      const z = `html.${UI_SCALE_ZOOM_CLASS}`;
      style.textContent = [
        `${z} cg-container { zoom: calc(1 / var(--rh-ui-zoom, 1)); }`,
        `${z} .cdk-overlay-container { zoom: calc(1 / var(--rh-ui-zoom, 1)); }`,
        `${z} .cdk-overlay-pane[style*="position: static"] { zoom: var(--rh-ui-zoom, 1); }`,
        `${z} .cdk-overlay-pane:not([style*="position: static"]) > * { zoom: var(--rh-ui-zoom, 1); }`,
      ].join('\n');
      document.head.appendChild(style);
    }
  } catch { /* ohne DOM (SSR) */ }
}

/** Meta-Viewport für die aktuelle Lage neu setzen (nach Drehen). */
function refreshViewport(): void {
  if (mode !== 'viewport') return;
  setViewport(viewportContentFor(current, screenCssWidth()));
}

function installListeners(): void {
  if (listenersInstalled || typeof window === 'undefined') return;
  listenersInstalled = true;
  window.addEventListener('orientationchange', refreshViewport);
  window.addEventListener('resize', refreshViewport);
  try { (screen as Screen & { orientation?: EventTarget }).orientation?.addEventListener?.('change', refreshViewport); } catch { /* alt */ }
}

/** Stufe anwenden (sofort, ohne Neuladen). Wählt beim ersten Mal den Weg (Meta-Viewport oder zoom). */
export function applyUiScale(pct: number): void {
  const value = normalizeUiScale(pct);
  current = value;
  if (typeof document === 'undefined') { for (const fn of listeners) fn(value); return; }
  if (value === UI_SCALE_DEFAULT) {
    if (mode === 'viewport') setViewport(DEFAULT_VIEWPORT);
    if (mode === 'zoom') setZoom(UI_SCALE_DEFAULT);
  } else {
    // Touch/Mobil: IMMER erst der Meta-Viewport. Ob er greift, wird NACH ein paar Bildern geprüft (verifyViewport) —
    // echtes Android-Chrome übernimmt die neue Layoutbreite nicht im selben Takt. Die frühere Sofortprüfung schlug am
    // Boox (Chrome 111) deshalb fehl, fiel auf `zoom` zurück, und das ist dort nicht standardisiert → `none`: die
    // Auswahl tat gar nichts (User 15.10., 0.800.2). Headless-Chromium meldet die Breite sofort, darum fiel es im Test
    // nicht auf.
    if (mode === null && isViewportCandidate() && screenCssWidth() > 0) mode = 'viewport';
    if (mode === null) mode = zoomIsStandardized() ? 'zoom' : 'none';
    if (mode === 'viewport') { setViewport(viewportContentFor(value, screenCssWidth())); installListeners(); verifyViewport(); }
    else if (mode === 'zoom') setZoom(value);
  }
  for (const fn of listeners) fn(value);
}

/** Wartezeit bis zur Nachprüfung des Meta-Viewports (zwei Bilder + Puffer für langsame Geräte). */
export const UI_SCALE_VERIFY_MS = 600;
let verifyTimer: ReturnType<typeof setTimeout> | null = null;
/** Ergebnis der letzten Nachprüfung: true = Layoutbreite folgt, false = nicht, null = (noch) nicht geprüft. */
let viewportOk: boolean | null = null;

/**
 * Nachprüfung: Greift der Meta-Viewport nach {@link UI_SCALE_VERIFY_MS} nicht (Windows-Touch-Tablet im Desktop-Chrome),
 * zurück auf device-width und — wenn standardisiert — `zoom`. Sonst bleibt der Meta-Viewport (lieber vielleicht wirksam
 * als sicher wirkungslos); die Profilkarte zeigt den Befund.
 */
function verifyViewport(): void {
  if (verifyTimer !== null) clearTimeout(verifyTimer);
  viewportOk = null;
  verifyTimer = setTimeout(() => {
    verifyTimer = null;
    if (mode !== 'viewport' || current === UI_SCALE_DEFAULT) return;
    viewportOk = viewportHonored(Math.round(screenCssWidth() * 100 / current));
    if (!viewportOk && zoomIsStandardized()) {
      setViewport(DEFAULT_VIEWPORT);
      mode = 'zoom';
      setZoom(current);
    }
    for (const fn of listeners) fn(current);
  }, UI_SCALE_VERIFY_MS);
}

/** Befund fürs Profil (Fehlersuche am Gerät): Weg, Layout- und Bildschirmbreite in CSS-px, Meta-Viewport. */
export interface UiScaleDiagnostics { mode: UiScaleMode | null; layoutWidth: number; screenWidth: number; viewportOk: boolean | null; meta: string; }
export function uiScaleDiagnostics(): UiScaleDiagnostics {
  let layoutWidth = 0;
  try { layoutWidth = document.documentElement.clientWidth; } catch { /* ohne DOM */ }
  return { mode, layoutWidth, screenWidth: screenCssWidth(), viewportOk, meta: viewportMeta()?.content ?? '' };
}

/** Früh beim Start (main.ts, vor bootstrapApplication): gespeicherte Stufe anwenden, damit nichts springt. */
export function applyUiScaleEarly(): void {
  const pct = readUiScale();
  if (pct !== UI_SCALE_DEFAULT) applyUiScale(pct);
}

/** Nur für Tests: Weg und Stufe vergessen, Seite auf 100 % zurück. */
export function resetUiScaleForTests(): void {
  if (verifyTimer !== null) { clearTimeout(verifyTimer); verifyTimer = null; }
  viewportOk = null;
  if (mode === 'viewport') setViewport(DEFAULT_VIEWPORT);
  if (mode === 'zoom') setZoom(UI_SCALE_DEFAULT);
  mode = null;
  current = UI_SCALE_DEFAULT;
}
