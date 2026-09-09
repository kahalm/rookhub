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

/** Was diese Marke zusaetzlich zum Kreis kennt. */
export interface MapPinMarkerOptions extends L.CircleMarkerOptions {
  /**
   * Wie viele Turniere auf DIESEM Punkt liegen. Ab 2 traegt der Kopf die Zahl — anders waere
   * nicht zu sehen, dass hinter der Marke mehr als ein Turnier steckt: liegen zwei Pins exakt
   * uebereinander, ist der untere unerreichbar und der Betrachter haelt den oberen fuer alles,
   * was es dort gibt.
   */
  count?: number;
  /**
   * Farbe dieser Zahl. Vorgabe Weiss — das traegt auf allen bunten Fuellungen. Auf einer HELLEN
   * (grau = „nicht eingeordnet") muss sie dunkel sein, sonst steht die Anzahl unlesbar da.
   */
  labelColor?: string;
}

export class MapPinMarker extends L.CircleMarker {
  /** Die Zahl im Kopf; leer, solange der Punkt fuer genau ein Turnier steht. */
  private readonly label: string;
  /** Ihre Schrift — EINMAL beim Anlegen gerechnet, nicht bei jedem Neuzeichnen. */
  private readonly labelFont: string;
  /** Ihre Farbe; siehe `MapPinMarkerOptions.labelColor`. */
  private readonly labelColor: string;

  constructor(latlng: L.LatLngExpression, options: MapPinMarkerOptions = {}) {
    super(latlng, options);
    const count = options.count ?? 1;
    this.label = count > 1 ? countLabel(count) : '';
    this.labelFont = this.label
      ? `bold ${labelSize(options.radius ?? 10, this.label)}px system-ui, sans-serif`
      : '';
    this.labelColor = options.labelColor ?? '#fff';
  }

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

    if (!this.label) return;
    // Weiss auf dem gefuellten Kopf — save/restore, weil der Canvas-Renderer EINE Leinwand fuer
    // alle Marken benutzt und Schrift-/Farbeinstellungen sonst in die naechste hineinlaufen.
    ctx.save();
    ctx.fillStyle = this.labelColor;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.font = this.labelFont;
    ctx.fillText(this.label, point.x, point.y - r * HeadDistance);
    ctx.restore();
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

/** Die Zahl im Kopf — mehr als drei Stellen sind dort nicht mehr lesbar. */
export function countLabel(count: number): string {
  return count > 999 ? '999+' : String(count);
}

/**
 * Kopfradius fuer einen Punkt, auf dem `count` Turniere liegen: je Stelle drei Pixel mehr.
 *
 * <p>Ohne das Wachsen stuende „111" in einem 14 px grossen Kopf. Der groessere Kopf ist dabei
 * nicht nur Platz fuer die Ziffern — er ist das erste, was auffaellt: eine Marke, die anders
 * aussieht als ihre Nachbarn, wird angeklickt.</p>
 */
export function pinRadiusFor(base: number, count: number): number {
  return count < 2 ? base : base + 3 * countLabel(count).length;
}

/**
 * Schriftgroesse, die INNEN passt — begrenzt sowohl durch die Hoehe des Kopfes als auch durch
 * seine Breite (drei Ziffern brauchen mehr Breite als eine, der Kopf bleibt aber rund).
 *
 * <p>Faktor 2,4 und Untergrenze 10: mit 1,7 stand „111" in 9 px und „999+" in 8 px — auf dem
 * Handy nicht mehr lesbar, obwohl die Zahl der einzige Hinweis auf „hier liegen mehrere" ist.
 * Fette system-ui-Ziffern sind ~0,55–0,6 em breit; mit 2,4 belegt die Zahl hoechstens ~72 % des
 * Durchmessers und bleibt im Kopf (13/16/13/11 px fuer 1/2/3/4 Zeichen bei `pinRadiusFor`).</p>
 */
function labelSize(radius: number, label: string): number {
  return Math.max(10, Math.round(Math.min(radius * 1.3, (radius * 2.4) / label.length)));
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
