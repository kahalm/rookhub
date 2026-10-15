import { localStore, readRaw, writeRaw, removeKey } from './local-json-store';

/**
 * E-Ink-Modus (0.799.0, docs/DESIGN.md 3.6): für E-Ink-Tablets (Boox Tab X C u. ä.) — keine Animationen, hoher
 * Kontrast, Blättern statt Gleiten, immer hell. JE GERÄT: localStorage `rookhub_eink` = '1' (an) / '0' (aus). Ohne
 * gespeicherte Wahl entscheidet die Media-Query `(update: slow), (monochrome)` (viele E-Ink-Browser melden sie nicht —
 * dafür der Schalter im Profil).
 *
 * Der Zustand lebt HIER (Modul) statt nur im Dienst: `main.ts` setzt die Klasse vor dem ersten Bild (kein Aufblitzen
 * des Dunkelmodus), der ThemeService wird in Tests ohne DI gebaut, und Material liest `animationsDisabled`/`disabled`
 * (app.config) zur Laufzeit über dieselbe Abfrage.
 */
export const EINK_STORAGE_KEY = 'rookhub_eink';
export const EINK_CLASS = 'eink';
export const EINK_MEDIA_QUERY = '(update: slow), (monochrome)';

let active = false;
let initialized = false;
const listeners = new Set<(on: boolean) => void>();

/** Gespeicherte Wahl dieses Geräts: true/false, oder null = keine Wahl (Vorgabe aus der Media-Query). */
export function readEinkChoice(): boolean | null {
  const raw = readRaw(localStore(), EINK_STORAGE_KEY);
  return raw === '1' ? true : raw === '0' ? false : null;
}

/** Wahl speichern (true/false) oder löschen (null). Gibt zurück, ob das Schreiben geklappt hat. */
export function writeEinkChoice(on: boolean | null): boolean {
  const store = localStore();
  if (on === null) { removeKey(store, EINK_STORAGE_KEY); return true; }
  return writeRaw(store, EINK_STORAGE_KEY, on ? '1' : '0');
}

/** Meldet das Gerät von sich aus einen trägen oder einfarbigen Bildschirm? */
export function deviceLooksEink(): boolean {
  try { return !!globalThis.matchMedia?.(EINK_MEDIA_QUERY)?.matches; } catch { return false; }
}

/** Wirksamer Zustand: gespeicherte Wahl, sonst die Media-Query. */
export function resolveEink(): boolean {
  return readEinkChoice() ?? deviceLooksEink();
}

export function isEinkActive(): boolean { return active; }

/** Wurde der Zustand schon gesetzt (main.ts oder Schalter)? Sonst löst der Dienst ihn beim ersten Zugriff auf. */
export function isEinkInitialized(): boolean { return initialized; }

/** Zustand setzen: Klasse an `<html>` und `<body>`, Zuhörer (ThemeService, EinkService) benachrichtigen. */
export function setEinkActive(on: boolean): void {
  active = on;
  initialized = true;
  try {
    document.documentElement.classList.toggle(EINK_CLASS, on);
    document.body?.classList.toggle(EINK_CLASS, on);
  } catch { /* ohne DOM (Tests/SSR) */ }
  for (const fn of listeners) fn(on);
}

/** Zuhören; gibt die Abmeldung zurück. */
export function onEinkChange(fn: (on: boolean) => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

/** Früh beim Start (main.ts, vor bootstrapApplication): Klasse setzen und den Scroll-Wächter einhängen. */
export function applyEinkEarly(): void {
  installSmoothScrollGuard();
  setEinkActive(resolveEink());
}

/** Chessground-Zuganimation: im E-Ink-Modus aus (Zustände springen), sonst die übliche Dauer. */
export function boardAnimation(durationMs: number): { enabled: boolean; duration: number } {
  return active ? { enabled: false, duration: 0 } : { enabled: true, duration: durationMs };
}

/**
 * Blättern statt Gleiten: `scroll-behavior: auto` (_eink.scss) wirkt nur auf CSS, ein ausdrückliches
 * `scrollTo({ behavior: 'smooth' })` aus dem Code nicht. Der Wächter macht daraus im E-Ink-Modus `auto` — eine Stelle
 * statt jedes Aufrufers. Außerhalb des Modus bleibt alles unverändert. Einmalig, idempotent.
 */
let guardInstalled = false;
export function installSmoothScrollGuard(): void {
  if (guardInstalled || typeof Element === 'undefined') return;
  guardInstalled = true;
  const calm = (arg: unknown): unknown => {
    if (!active || !arg || typeof arg !== 'object') return arg;
    const opts = arg as ScrollOptions;
    return opts.behavior === 'smooth' ? { ...opts, behavior: 'auto' } : arg;
  };
  const wrap = <T extends object>(target: T, name: 'scrollTo' | 'scrollBy' | 'scrollIntoView'): void => {
    const orig = (target as Record<string, unknown>)[name];
    if (typeof orig !== 'function') return;
    (target as Record<string, unknown>)[name] = function (this: unknown, ...args: unknown[]) {
      if (args.length > 0) args[0] = calm(args[0]);
      return (orig as (...a: unknown[]) => unknown).apply(this, args);
    };
  };
  wrap(Element.prototype, 'scrollTo');
  wrap(Element.prototype, 'scrollBy');
  wrap(Element.prototype, 'scrollIntoView');
  if (typeof window !== 'undefined') {
    wrap(window, 'scrollTo');
    wrap(window, 'scrollBy');
  }
}
