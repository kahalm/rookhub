import * as L from 'leaflet';

/**
 * Die Form eines Karten-Pins: unten spitz, oben rund — die Spitze sitzt GENAU auf dem Ort.
 *
 * <p>Ein Kreis ist als Ortsmarke schlechter, als er aussieht: er behauptet, der Ort liege in
 * seiner MITTE, und die ist bei einem 14 px grossen Punkt schwer zu treffen. Ein Pin zeigt auf
 * einen Punkt, und man sieht auf welchen.</p>
 *
 * <p>Warum das eine eigene Leaflet-Klasse ist und kein `divIcon`: die Karte laeuft mit
 * `preferCanvas`, weil hier bis zu ein paar tausend Marken liegen — als DOM-Knoten bringt das den
 * Browser zum Kriechen. Canvas-Marken kennt Leaflet aber nur als Kreis. Also erbt diese Klasse
 * von `CircleMarker` (Projektion, Klick-Toleranz, Ereignisse, Sichtbarkeitspruefung kommen von
 * dort) und ersetzt genau drei Dinge: die gezeichnete FORM, den Bereich, der als Treffer gilt,
 * und die Ausdehnung fuers Neuzeichnen. Alle drei muessen zusammenpassen — zeichnet man nur die
 * Form um, sitzt der klickbare Bereich unter der Spitze statt im Kopf, und beim Verschieben der
 * Karte werden die oberen zwei Drittel des Pins abgeschnitten.</p>
 */

/** Abstand Spitze → Kopfmitte, in Kopfradien. */
const HeadDistance = 1.55;

/**
 * Wo die Tangente von der Spitze den Kopfkreis beruehrt, als Winkel. Ergibt sich aus
 * <c>HeadDistance</c>: `acos(r / d)` — genau dort geht der runde Kopf in die Flanke des
 * Schwanzes ueber, ohne Knick.
 */
const TangentAngle = Math.acos(1 / HeadDistance);

export class MapPinMarker extends L.CircleMarker {
  // Ohne `override`: die drei Namen sind Leaflet-Interna und stehen nicht in den
  // veroeffentlichten Typen, TypeScript sieht also keine Basis-Deklaration zum Ueberschreiben.
  // Zur Laufzeit ruft der Renderer genau diese auf.
  /**
   * Der Pfad: von der rechten Beruehrung ueber den Kopf herum zur linken, dann auf die Spitze
   * und zu. Ein Bogen ueber `Math.PI - TangentAngle` hinaus statt eines vollen Kreises — sonst
   * lieferte der geschlossene Kopf zwei sich ueberlappende Flaechen und der Rand liefe mitten
   * durch den Pin.
   */
  _updatePath(): void {
    const renderer = (this as unknown as { _renderer?: CanvasRendererLike })._renderer;
    const self = this as unknown as PathInternals;
    if (!renderer?._drawing || self._empty()) return;

    const ctx = renderer._ctx;
    const point = self._point;
    const r = Math.max(Math.round(self._radius), 1);

    ctx.beginPath();
    ctx.arc(point.x, point.y - r * HeadDistance, r, TangentAngle, Math.PI - TangentAngle, true);
    ctx.lineTo(point.x, point.y);
    ctx.closePath();
    renderer._fillStroke(ctx, this);
  }

  /**
   * Treffer im KOPF oder im Schwanz. Der geerbte Kreis um den Ankerpunkt waere hier falsch: er
   * liegt zur Haelfte unter dem Pin im Leeren, und der Kopf — der Teil, auf den man klickt —
   * ragte oben heraus.
   */
  _containsPoint(p: L.Point): boolean {
    const self = this as unknown as PathInternals;
    const r = self._radius;
    const tolerance = self._clickTolerance();
    const tailLength = r * HeadDistance;
    const head = L.point(self._point.x, self._point.y - tailLength);

    if (p.distanceTo(head) <= r + tolerance) return true;

    // Der Schwanz laeuft von der Kopfmitte zur Spitze zusammen — deshalb die mit dem Abstand
    // schrumpfende halbe Breite und nicht ein Rechteck.
    const belowHead = p.y - head.y;
    if (belowHead < 0 || belowHead > tailLength + tolerance) return false;
    return Math.abs(p.x - self._point.x) <= r * (1 - belowHead / tailLength) + tolerance;
  }

  /**
   * Die Ausdehnung fuer den Renderer. Sie reicht nach OBEN ueber Kopf und Schwanz hinaus und nach
   * unten nur bis zur Spitze. Bliebe sie das geerbte Quadrat um den Anker, schnitte der Renderer
   * den halben Pin weg, sobald er am Rand des neu zu zeichnenden Bereichs liegt.
   */
  _updateBounds(): void {
    const self = this as unknown as PathInternals;
    const r = self._radius;
    const w = self._clickTolerance();
    self._pxBounds = new L.Bounds(
      self._point.subtract(L.point(r + w, r * HeadDistance + r + w)),
      self._point.add(L.point(r + w, w)));
  }

  /** Wie hoch der Pin ueber seinem Ort steht — fuer die Ausrichtung von Popup und Hinweis. */
  static heightAbove(radius: number): number {
    return Math.round(radius * (HeadDistance + 1));
  }
}

/** Nur die Teile des Canvas-Renderers, die hier gebraucht werden (Leaflet-Interna). */
interface CanvasRendererLike {
  _drawing: boolean;
  _ctx: CanvasRenderingContext2D;
  _fillStroke(ctx: CanvasRenderingContext2D, layer: L.Path): void;
}

/** Die geerbten Felder von CircleMarker/Path, die Leaflets Typen nicht veroeffentlichen. */
interface PathInternals {
  _point: L.Point;
  _radius: number;
  _pxBounds: L.Bounds;
  _empty(): boolean;
  _clickTolerance(): number;
}
