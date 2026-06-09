import { Injectable, NgZone, inject, signal } from '@angular/core';

/** Das (nicht standardisierte, aber von Chromium implementierte) Install-Prompt-Event. */
interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

declare global {
  interface Window {
    __rookhubInstallPrompt?: BeforeInstallPromptEvent | null;
  }
}

/**
 * Kapselt die PWA-Installierbarkeit fuer die Installationsseite (/install):
 * - faengt `beforeinstallprompt` ab (auch das frueh in index.html abgefangene) und
 *   merkt es vor, um spaeter den nativen Installations-Dialog auszuloesen,
 * - erkennt, ob die App bereits als Standalone-PWA laeuft,
 * - liefert die Plattform-Hinweise (Android/iOS), damit die Seite je System die
 *   passende Variante anbietet bzw. „nicht moeglich" vermerkt.
 *
 * App-weit instanziiert (providedIn root) und im AppComponent angestossen, damit das
 * Event auch dann sicher gefangen wird, wenn es erst nach dem Bootstrap feuert.
 */
@Injectable({ providedIn: 'root' })
export class PwaInstallService {
  private zone = inject(NgZone);
  private deferred: BeforeInstallPromptEvent | null = null;

  /** True, sobald der Browser ein beforeinstallprompt geliefert hat (Chrome/Edge/Android-Chrome). */
  readonly canInstallPwa = signal(false);
  /** True, wenn die App bereits als installierte PWA (Standalone) laeuft. */
  readonly isInstalled = signal(false);

  constructor() {
    // 1) Evtl. vor dem Angular-Bootstrap in index.html abgefangenes Event uebernehmen.
    const early = window.__rookhubInstallPrompt;
    if (early) {
      this.deferred = early;
      this.canInstallPwa.set(true);
    }

    // 2) Spaeter feuernde Events selbst fangen.
    window.addEventListener('beforeinstallprompt', (e: Event) => {
      e.preventDefault();
      this.zone.run(() => {
        this.deferred = e as BeforeInstallPromptEvent;
        this.canInstallPwa.set(true);
      });
    });

    window.addEventListener('appinstalled', () => {
      this.zone.run(() => {
        this.deferred = null;
        this.canInstallPwa.set(false);
        this.isInstalled.set(true);
        window.__rookhubInstallPrompt = null;
      });
    });

    this.isInstalled.set(this.detectStandalone());
  }

  /** Stellt sicher, dass der Service zum App-Start existiert (Aufruf aus AppComponent). */
  init(): void { /* Instanziierung reicht — Listener haengen im Konstruktor. */ }

  get isAndroid(): boolean {
    return /android/i.test(navigator.userAgent);
  }

  get isIOS(): boolean {
    const ua = navigator.userAgent;
    // iPadOS 13+ meldet sich als "Macintosh" mit Touch -> zusaetzlich pruefen.
    return /iphone|ipad|ipod/i.test(ua) || (/macintosh/i.test(ua) && navigator.maxTouchPoints > 1);
  }

  private detectStandalone(): boolean {
    return !!window.matchMedia?.('(display-mode: standalone)').matches
      || (navigator as unknown as { standalone?: boolean }).standalone === true;
  }

  /** Loest den nativen Installations-Dialog aus. Liefert true, wenn der User akzeptiert hat. */
  async promptInstall(): Promise<boolean> {
    const evt = this.deferred;
    if (!evt) return false;
    await evt.prompt();
    const choice = await evt.userChoice;
    // Das Event ist nur einmal verwendbar.
    this.deferred = null;
    this.canInstallPwa.set(false);
    window.__rookhubInstallPrompt = null;
    return choice.outcome === 'accepted';
  }
}
