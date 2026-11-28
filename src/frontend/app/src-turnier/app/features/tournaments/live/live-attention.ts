import { LiveAlert } from './live-vibrate';

/**
 * Aufblitzen und Benachrichtigung der Live-Bretter (1.042.0, Wunsch 2026-11-28: „am PC flashen statt vibrieren“, dazu eine
 * Benachrichtigung). Dieselben Ereignisse wie das Vibrieren ({@link LiveAlertTracker} in `live-vibrate.ts`) — hier steht nur
 * die AUSGABE: Tab-Titel/Favicon im Hintergrund ({@link TabAttention}) und die Notification API ({@link showLiveNotification}).
 * Das Aufblitzen der Karte selbst ist eine CSS-Klasse an der Karte (`lb-flash-result` / `lb-flash-swing`).
 */

/** Aufblitzen an/aus — Gerätevorliebe (`core/local-storage-keys.ts`, Klasse `device`). */
export const LIVE_FLASH_KEY = 'rookhub_live_flash';
/** Benachrichtigung an/aus — Gerätevorliebe; gilt nur, solange der Browser die Erlaubnis hat. */
export const LIVE_NOTIFY_KEY = 'rookhub_live_notify';

/** So lange trägt eine Karte die Blitz-Klasse (Animation 3 × 0,6 s; ohne Bewegung ein ruhiger Rahmen so lange). */
export const LIVE_FLASH_MS = 3000;
/** Takt, in dem der Tab-Titel zwischen Meldung und Originaltitel wechselt. */
export const TAB_BLINK_MS = 1000;
/** Vorsatz des Tab-Titels bei einer Meldung. */
export const TAB_MARK = '● ';

function readFlag(key: string): boolean {
  try { return localStorage.getItem(key) === '1'; } catch { return false; }
}

function writeFlag(key: string, on: boolean): void {
  try { localStorage.setItem(key, on ? '1' : '0'); } catch { /* gesperrter Speicher: gilt bis zum Neuladen */ }
}

export const readLiveFlash = (): boolean => readFlag(LIVE_FLASH_KEY);
export const writeLiveFlash = (on: boolean): void => writeFlag(LIVE_FLASH_KEY, on);
export const readLiveNotify = (): boolean => readFlag(LIVE_NOTIFY_KEY);
export const writeLiveNotify = (on: boolean): void => writeFlag(LIVE_NOTIFY_KEY, on);

/** Favicon mit Punkt, solange nichts Besseres gezeichnet ist (ein Punkt in Warnfarbe auf hellem Grund, beide Themen lesbar). */
export const DOT_FAVICON = 'data:image/svg+xml,' + encodeURIComponent(
  '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><circle cx="16" cy="16" r="15" fill="#fff"/>'
  + '<circle cx="16" cy="16" r="10" fill="#d32f2f"/></svg>');

/**
 * Hinweis im Tab, solange er im Hintergrund liegt: der Titel wechselt sekündlich zwischen „● <Text>“ und dem Originaltitel,
 * jedes Favicon (`link[rel~=icon]`) bekommt einen Punkt. Endet, sobald der Tab wieder sichtbar ist (`visibilitychange`) oder
 * {@link stop} läuft (Verlassen der Seite) — Titel und Favicons stehen dann wieder wie vorher. Eine weitere Meldung im
 * Hintergrund ersetzt nur den Text. Ist der Tab sichtbar, tut {@link start} nichts.
 */
export class TabAttention {
  private originalTitle: string | null = null;
  private icons: { el: HTMLLinkElement; href: string }[] = [];
  private text = '';
  private showText = false;
  private timer: ReturnType<typeof setInterval> | null = null;
  private readonly onVisible = () => { if (!this.doc.hidden) this.stop(); };

  constructor(private readonly doc: Document = document) {}

  get active(): boolean { return this.originalTitle !== null; }

  start(text: string): void {
    if (!this.doc.hidden) return;
    this.text = TAB_MARK + text;
    if (this.active) { this.showText = true; this.doc.title = this.text; return; }
    this.originalTitle = this.doc.title;
    this.icons = Array.from(this.doc.querySelectorAll<HTMLLinkElement>('link[rel~="icon"]')).map(el => ({ el, href: el.getAttribute('href') ?? '' }));
    for (const i of this.icons) i.el.setAttribute('href', DOT_FAVICON);
    this.composeIcon();
    this.showText = true;
    this.doc.title = this.text;
    this.doc.addEventListener('visibilitychange', this.onVisible);
    this.timer = setInterval(() => this.tick(), TAB_BLINK_MS);
  }

  stop(): void {
    if (this.timer) { clearInterval(this.timer); this.timer = null; }
    this.doc.removeEventListener('visibilitychange', this.onVisible);
    if (!this.active) return;
    this.doc.title = this.originalTitle!;
    for (const i of this.icons) i.el.setAttribute('href', i.href);
    this.icons = [];
    this.originalTitle = null;
  }

  private tick(): void {
    // Hat die Seite den Titel inzwischen selbst geändert, gilt der neue als Original.
    const t = this.doc.title;
    if (t !== this.text && t !== this.originalTitle) this.originalTitle = t;
    this.showText = !this.showText;
    this.doc.title = this.showText ? this.text : this.originalTitle!;
  }

  /** Das eigene Symbol mit Punkt oben rechts (Canvas); klappt das nicht (kein Canvas, Bild fehlt), bleibt der reine Punkt. */
  private composeIcon(): void {
    const src = this.icons.find(i => /\.png($|\?)/.test(i.href))?.href ?? this.icons[0]?.href;
    if (!src || typeof Image === 'undefined') return;
    const img = new Image();
    img.onload = () => {
      if (!this.active) return;
      try {
        const c = this.doc.createElement('canvas');
        c.width = c.height = 64;
        const ctx = c.getContext('2d');
        if (!ctx) return;
        ctx.drawImage(img, 0, 0, 64, 64);
        ctx.beginPath(); ctx.arc(46, 18, 17, 0, 2 * Math.PI); ctx.fillStyle = '#fff'; ctx.fill();
        ctx.beginPath(); ctx.arc(46, 18, 13, 0, 2 * Math.PI); ctx.fillStyle = '#d32f2f'; ctx.fill();
        const url = c.toDataURL('image/png');
        for (const i of this.icons) i.el.setAttribute('href', url);
      } catch { /* verunreinigtes Canvas o. Ä.: der reine Punkt bleibt */ }
    };
    img.src = src;
  }
}

/** Gibt es die Notification API? (Fehlt z. B. in iOS Safari ohne installierte Web-App und in alten WebViews.) */
export function notifySupported(): boolean {
  return typeof window !== 'undefined' && typeof (window as Window & { Notification?: unknown }).Notification === 'function';
}

/**
 * Erlaubnis holen — beim Einschalten, der Klick ist die Nutzergeste, die Browser verlangen. `requestPermission` läuft
 * synchron im Klick (vor dem ersten `await`). Abgelehnt oder ohne API → `false`.
 */
export async function requestNotifyPermission(): Promise<boolean> {
  if (!notifySupported()) return false;
  if (Notification.permission === 'granted') return true;
  if (Notification.permission === 'denied') return false;
  try { return (await Notification.requestPermission()) === 'granted'; } catch { return false; }
}

/** Ist die Seite gerade im Blick? Sichtbarer Tab UND Fenster mit Fokus — sonst lohnt eine Benachrichtigung. */
export function pageAttended(doc: Document = document): boolean {
  return !doc.hidden && doc.hasFocus();
}

/**
 * Benachrichtigung zeigen (nur, solange die Seite offen ist — kein Service-Worker-Push). `tag` je Brett: eine neue Meldung
 * desselben Bretts ersetzt die alte. Android-Chrome kennt `new Notification` nicht (wirft, verlangt den Service Worker) —
 * dann still nichts.
 */
export function showLiveNotification(title: string, body: string, tag: string, onClick: () => void): Notification | null {
  if (!notifySupported() || Notification.permission !== 'granted') return null;
  try {
    const n = new Notification(title, { body, tag, icon: 'icons/icon-192.png' });
    n.onclick = () => { try { window.focus(); } catch { /* egal */ } onClick(); n.close(); };
    return n;
  } catch { return null; }
}

/** Bewertung in Bauern als Text wie im Balken („+1.4“, „−0.3“, „0.0“); die Kappe ±10 steht für Matt. */
export function pawnsText(v: number): string {
  if (Math.abs(v) >= 10) return v > 0 ? '#' : '#−';
  const p = Math.abs(v).toFixed(1);
  return v > 0.05 ? `+${p}` : v < -0.05 ? `−${p}` : '0.0';
}

/** Ergebnis kurz für Titel/Benachrichtigung („1-0“, „½-½“). */
export function resultShort(result: string): string {
  return result === '1/2-1/2' ? '½-½' : result;
}

/** CSS-Klasse der Karte je Meldung. */
export function flashClass(kind: LiveAlert): string {
  return kind === 'result' ? 'lb-flash-result' : 'lb-flash-swing';
}
