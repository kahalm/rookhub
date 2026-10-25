/**
 * Eingriffe des Kalkulations-Modus in die App-Hülle (Zerlegung Z20, aus `CalculationComponent`).
 *
 * Klasse am <body>, solange dieser Modus offen ist: die App-Kopfzeile rückt nach rechts, damit
 * die Befehlszeile dieselbe Zeile mitbenutzt (Regeln in `styles.scss`). Bewusst so eng
 * gefasst — `<app-navbar>` ist EINE Instanz in der App-Hülle und damit auf allen Routen
 * dieselbe; ohne die Eingrenzung baute man jede Seite um.
 */
export class CalcPageMode {
  static readonly MODE_CLASS = 'calc-mode';

  /** Beobachtet die Breite der Navigationsleiste — die Befehlszeile hält genau so viel frei. */
  private navResize?: ResizeObserver;
  /** Gemerkte Vollbild-Hülle des Bretts (siehe {@link fsTarget}). */
  private fsHost: HTMLElement | null = null;

  enter(): void {
    if (typeof document === 'undefined') return;
    document.body.classList.add(CalcPageMode.MODE_CLASS);
    const nav = document.querySelector('app-navbar');
    if (!nav || typeof ResizeObserver === 'undefined') return;
    // GEMESSEN statt geraten: die Breite hängt an Sprache, Anmeldezustand und Schriftgröße —
    // ein fester Wert läge in der Hälfte der Fälle daneben und die Zeile liefe unter die Leiste.
    this.navResize = new ResizeObserver(() => this.syncNavWidth(nav));
    this.navResize.observe(nav);
    this.syncNavWidth(nav);
  }

  leave(): void {
    if (typeof document === 'undefined') return;
    this.navResize?.disconnect();
    this.navResize = undefined;
    document.body.classList.remove(CalcPageMode.MODE_CLASS);
    document.body.style.removeProperty('--calc-nav-w');
  }

  /**
   * Element, das der Vollbild-Knopf in der Leiste ins Vollbild schickt. Der Knopf sitzt dort
   * statt im Brett (`allowFullscreen=false`), weil er im Brett eine eigene Zeile DARÜBER belegte
   * und damit Bretthöhe kostete. Die Hülle heißt seit jeher `.board-fs-host` und ist als
   * Vollbild-Element dokumentiert.
   *
   * Bewusst über das Dokument gesucht statt über einen eingespritzten `ElementRef`: die Tests
   * bauen die Komponente direkt mit `new` und haben damit keinen Injektionskontext. Auf der
   * Seite gibt es genau ein Brett, die Suche ist also eindeutig; das Ergebnis wird gemerkt.
   */
  get fsTarget(): HTMLElement | null {
    // `isConnected`: nach einem Ladefehler ersetzt die Zustandsweiche das ganze Layout samt
    // Brett — der Cache zeigte dann auf ein aus dem DOM entferntes Element, requestFullscreen
    // darauf wird still abgelehnt, und der Knopf wäre für den Rest des Besuchs tot.
    if ((!this.fsHost || !this.fsHost.isConnected) && typeof document !== 'undefined') {
      this.fsHost = document.querySelector('app-puzzle-board .board-fs-host');
    }
    return this.fsHost;
  }

  private syncNavWidth(nav: Element): void {
    const breite = Math.ceil(nav.getBoundingClientRect().width);
    document.body.style.setProperty('--calc-nav-w', `${breite}px`);
  }
}
