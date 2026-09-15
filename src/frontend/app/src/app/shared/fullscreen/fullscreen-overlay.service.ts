import { EnvironmentProviders, Injectable, OnDestroy, makeEnvironmentProviders } from '@angular/core';
import { OVERLAY_DEFAULT_CONFIG, OverlayContainer } from '@angular/cdk/overlay';
import { fullscreenElement, isElementFullscreen, onFullscreenChange } from './fullscreen.util';

/**
 * CDK-Overlays OHNE Popover-API — die Voraussetzung dafür, dass Dialoge, Menüs und Snackbars in
 * BEIDEN Vollbild-Arten sichtbar bleiben (app-weit in `app.config.ts`).
 *
 * <p>Die CDK öffnet jedes Overlay seit ihrer Popover-Umstellung als `popover="manual"` in der
 * obersten Ebene (top layer) des Browsers. Zwei Dinge vertragen sich damit nicht, beide 2026-09-15
 * in Chromium UND Firefox nachgestellt (Playwright, Minimalseite):</p>
 * <ol>
 *   <li><b>Umhängen schließt.</b> Dieser Dienst hängt den Overlay-Container beim Brett-Vollbild ins
 *       Vollbild-Element und danach zurück. Ein OFFENES Popover, das im DOM umgehängt wird, schließt
 *       der Browser still: es bleibt im DOM, ist aber unsichtbar, und die CDK weiß davon nichts. Ein
 *       Dialog, der beim Wechsel offen war (etwa die Nachfrage bei langer Lösezeit), verschwand
 *       spurlos — und blockierte als modaler Dialog trotzdem weiter.</li>
 *   <li><b>Die Reihenfolge der obersten Ebene zählt.</b> Ein Overlay, das schon offen ist, BEVOR
 *       `&lt;html&gt;` ins App-Vollbild geht, liegt in Chromium danach UNTER der Seite.</li>
 * </ol>
 * <p>Ein klassisches Overlay (`position: fixed`, z-index 1000 im Container) ist in allen gemessenen
 * Reihenfolgen oben — auch umgehängt ins Brett-Vollbild. Genau dafür ist dieser Dienst gebaut.</p>
 */
export function provideFullscreenSafeOverlays(): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: OVERLAY_DEFAULT_CONFIG, useValue: { usePopover: false } }]);
}

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
    const usable = isElementFullscreen(fsElement);
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
