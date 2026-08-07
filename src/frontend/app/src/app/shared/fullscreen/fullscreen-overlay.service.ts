import { Injectable, OnDestroy } from '@angular/core';
import { OverlayContainer } from '@angular/cdk/overlay';
import { fullscreenElement, onFullscreenChange } from './fullscreen.util';

/**
 * Hängt den CDK-Overlay-Container während des Vollbilds IN das Vollbild-Element um.
 *
 * <p>Im Vollbild rendert der Browser ausschließlich den Teilbaum des Vollbild-Elements. Alles, was
 * Angular Material in den Overlay-Container am `<body>` legt (Dialog, Snackbar, Menü, Tooltip),
 * war dort schlicht unsichtbar — bei einem modalen Dialog mit `disableClose` (z. B. die
 * „Ganz schön lang"-Nachfrage nach dem Lösen) hing die App sogar fest: der Dialog blockiert, ist
 * aber nicht anklickbar. Wandert der Container mit ins Vollbild-Element, erscheinen Overlays
 * wieder normal — `position: fixed` löst dort gegen das Vollbild-Element auf, das den ganzen
 * Bildschirm bedeckt.</p>
 *
 * <p>Beim Verlassen wandert der Container zurück ans `<body>`. Das ist auch der Reparaturpfad,
 * wenn das Vollbild-Element zwischenzeitlich zerstört wurde (Navigation aus dem Vollbild heraus):
 * ein abgehängter Container würde sonst alle künftigen Overlays unsichtbar machen.</p>
 *
 * <p>App-weit einmal instanziiert (AppComponent) — die Brett-Komponenten müssen nichts tun.</p>
 */
@Injectable({ providedIn: 'root' })
export class FullscreenOverlayService implements OnDestroy {
  private readonly off: () => void;
  /** Liegt der Container gerade in einem Vollbild-Element? Nur dann gibt es etwas zurückzuholen. */
  private moved = false;

  constructor(private overlayContainer: OverlayContainer) {
    this.off = onFullscreenChange(() => this.sync(fullscreenElement()));
    if (fullscreenElement()) this.sync(fullscreenElement());   // Vollbild schon beim Start aktiv
  }

  /**
   * Verschiebt den Overlay-Container unter das passende Elternelement.
   * `document`/`<html>`/`<body>` im Vollbild brauchen keinen Umzug — dort ist der `<body>`-Container
   * ohnehin Teil des gerenderten Teilbaums (App-Vollbild).
   */
  sync(fsElement: Element | null): void {
    const usable = fsElement instanceof HTMLElement
      && fsElement !== document.documentElement
      && fsElement !== document.body;
    // Ohne Vollbild und ohne früheren Umzug nichts anfassen: `getContainerElement()` LEGT den
    // Container (samt CDK-Styles) sonst überhaupt erst an — unnötig beim App-Start, und beim
    // Teardown greift es auf einen bereits zerstörten Injector zu (NG0205).
    if (!usable && !this.moved) return;

    let container: HTMLElement;
    try { container = this.overlayContainer.getContainerElement(); } catch { return; }
    const target = usable ? (fsElement as HTMLElement) : document.body;
    if (container.parentElement !== target) target.appendChild(container);
    this.moved = usable;
  }

  ngOnDestroy(): void {
    this.off();
    // Container nie im (womöglich gleich verschwindenden) Vollbild-Element zurücklassen.
    if (this.moved) this.sync(null);
  }
}
